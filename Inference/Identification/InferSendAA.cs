using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Client;
using Glass.Network.Protocol;
using Inference.Core;
using Inference.Models;
using System;
using System.Collections.Generic;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// InferSendAA
//
// Identifies OP_Send_AA.  The zone server is expected to send the whole AA table as a run
// of hundreds of consecutive OP_Send_AA messages on the zone-to-client stream, near the
// start of the session and before the player profile.  Payloads are expected to be over
// 100 bytes and rarely over 2000.
//
// A run is more than RunLengthThreshold consecutive zone-to-client application messages on
// one session carrying one wire value.  Each zone session is walked once in arrival order,
// counting zone-to-client application messages, to record its runs and the position of its
// first accepted OP_PlayerProfile.  A zone session is scored only when it carries the
// accepted OP_PlayerProfile; with no accepted profile, nothing is scored.  Candidates are
// the unaccepted wire values with a run before the profile in at least one such session.
// Each candidate is scored on the run, its proximity to the session start, and the size
// range.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferSendAA : IInferOpcodes
{
    private const string OpcodeNameConstant = "OP_Send_AA";
    private const string ProfileOpcodeName = "OP_PlayerProfile";   // qualifies a session, orders the run
    private const int RunLengthThreshold = 100;            // a run is more than this many consecutive messages
    private const double LeakProbability = 0.001;          // per-item allowance for a failed predicate
    private const double MaxAbsLogLR = 12.0;               // clamp per feature, roughly +/- 52 decibans
    private const int ProximityAllowance = 20;             // Z2C messages a run may start after, uncharged
    private const double ProximityPenalty = 0.1;           // log-LR per Z2C message past the allowance
    private const int RangeLow = 100;                      // smallest expected payload length
    private const int RangeHigh = 2000;                    // largest expected payload length
    private const double RangeDistancePenalty = 0.01;      // log-LR per byte outside the range
    private const double ReportThreshold = 0.40;           // minimum posterior to emit a proposal

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RunRecord
    //
    // One run in one zone session: more than RunLengthThreshold consecutive zone-to-client
    // application messages carrying one wire value.  Start and End are the positions of its
    // first and last messages, counted in zone-to-client application messages.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private struct RunRecord
    {
        public OpcodeValue WireValue;
        public int Start;
        public int End;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SessionRuns
    //
    // Run state for one zone session, counted in zone-to-client application messages.
    // Position is the number of such messages seen so far.  The Current fields describe the
    // sequence of one wire value in progress.  Runs holds every completed run.
    // ProfilePosition is the position of the first accepted OP_PlayerProfile, or -1 when
    // none was seen.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private struct SessionRuns
    {
        public int Position;
        public OpcodeValue CurrentValue;
        public int CurrentLength;
        public int CurrentStart;
        public int ProfilePosition;
        public List<RunRecord> Runs;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CandidateFacts
    //
    // One candidate's zone-to-client traffic in zone sessions that carry the profile.
    // Lengths holds the length of every such message.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class CandidateFacts
    {
        public OpcodeValue WireValue;
        public List<int> Lengths = new List<int>();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeName
    //
    // The logical name of the opcode this handler identifies.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string OpcodeName
    {
        get { return OpcodeNameConstant; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Label
    //
    // The printable group identifier for this handler's proposals.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string Label
    {
        get { return OpcodeNameConstant; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Infer
    //
    // Finds the zone sessions and their runs, selects the candidates from the runs,
    // examines each candidate's traffic, then scores each candidate.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  Proposals ordered by posterior descending, empty when there are no zone
    //           sessions, no candidates, or nothing scores at threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<InferenceProposal> Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferSendAA.Infer: starting", LogLevel.Trace);
        List<InferenceProposal> proposals = new List<InferenceProposal>();

        List<Session> zoneSessions = FindZoneSessions(catalog);
        if (zoneSessions.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferSendAA.Infer: no zone sessions, no proposals", LogLevel.Warn);
            return proposals;
        }

        SessionRuns[] runs = FindRuns(catalog, zoneSessions);
        List<OpcodeValue> candidates = SelectCandidates(runs);
        if (candidates.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferSendAA.Infer: no candidates, no proposals", LogLevel.Warn);
            return proposals;
        }

        int opcodeCount = catalog.KnownOpcodes().Length;
        Dictionary<int, int> pooledSizes = catalog.PooledSizeHistogram();
        for (int i = 0; i < candidates.Count; i++)
        {
            CandidateFacts candidate = ExamineCandidate(catalog, candidates[i], zoneSessions, runs);
            InferenceProposal? proposal = ScoreCandidate(candidate, runs, opcodeCount, pooledSizes);
            if (proposal != null)
            {
                proposals.Add(proposal);
            }
        }

        proposals.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferSendAA.Infer: " + proposals.Count + " proposals", LogLevel.Info);
        return proposals;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindZoneSessions
    //
    // Walks the catalog in arrival order and collects each distinct Session whose server
    // type is Zone, in order of first appearance.  Packets with no Session are skipped.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  The zone sessions, empty when none were seen.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static List<Session> FindZoneSessions(PacketCatalog catalog)
    {
        List<Session> zoneSessions = new List<Session>();
        Session? lastSession = null;
        uint totalCount = (uint)catalog.TotalPacketCount();

        for (uint i = 0; i < totalCount; i++)
        {
            CatalogedPacket? packetOrNull = catalog.PacketAt(i);
            if (packetOrNull == null)
            {
                DebugLog.Write(LogChannel.Inference,
                    "InferSendAA.FindZoneSessions: null packet at index " + i, LogLevel.Error);
                continue;
            }

            Session session = packetOrNull.Value.Metadata.Session;
            if (session == null || ReferenceEquals(session, lastSession))
            {
                continue;
            }
            lastSession = session;

            if (session.ServerType != ServerType.Zone)
            {
                continue;
            }
            if (!zoneSessions.Contains(session))
            {
                zoneSessions.Add(session);
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferSendAA.FindZoneSessions: " + zoneSessions.Count + " zone sessions", LogLevel.Trace);
        return zoneSessions;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindRuns
    //
    // Walks the catalog once in arrival order.  For each zone-to-client application message
    // in a zone session, extends that session's current sequence when the wire value is
    // unchanged, or closes it and starts a new one when it changes.  Records the position of
    // the first accepted OP_PlayerProfile.  Sequences still open when the walk ends are
    // closed.  When OP_PlayerProfile has not been accepted, no profile position is recorded,
    // so no session is scored.
    //
    // catalog:       The session's cataloged packets.
    // zoneSessions:  The zone sessions.
    //
    // Returns:  One run record set per zone session, parallel to zoneSessions.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static SessionRuns[] FindRuns(PacketCatalog catalog, List<Session> zoneSessions)
    {
        SessionRuns[] runs = new SessionRuns[zoneSessions.Count];
        for (int i = 0; i < runs.Length; i++)
        {
            runs[i].CurrentValue = OpcodeValue.None;
            runs[i].ProfilePosition = -1;
            runs[i].Runs = new List<RunRecord>();
        }

        OpcodeValue profileWireValue = AcceptedOpcodes.Instance.WireValueFor(ProfileOpcodeName);
        if (!profileWireValue.Exists)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferSendAA.FindRuns: " + ProfileOpcodeName
                + " not accepted, no session can be scored", LogLevel.Warn);
        }

        Session? lastSession = null;
        int lastIndex = -1;
        uint totalCount = (uint)catalog.TotalPacketCount();

        for (uint i = 0; i < totalCount; i++)
        {
            CatalogedPacket? packetOrNull = catalog.PacketAt(i);
            if (packetOrNull == null)
            {
                continue;
            }
            CatalogedPacket packet = packetOrNull.Value;

            Session session = packet.Metadata.Session;
            OpcodeValue wireValue = packet.Metadata.WireValue;
            if (session == null
                || session.ServerType != ServerType.Zone
                || packet.Metadata.ToServer
                || SoeConstants.IsNetOpcode(wireValue.Value))
            {
                continue;
            }

            if (!ReferenceEquals(session, lastSession))
            {
                lastSession = session;
                lastIndex = zoneSessions.IndexOf(session);
            }
            if (lastIndex < 0)
            {
                continue;
            }

            ref SessionRuns run = ref runs[lastIndex];

            if (profileWireValue.Exists && wireValue.Equals(profileWireValue) && run.ProfilePosition < 0)
            {
                run.ProfilePosition = run.Position;
            }

            if (wireValue.Equals(run.CurrentValue))
            {
                run.CurrentLength++;
            }
            else
            {
                CloseRun(ref run);
                run.CurrentValue = wireValue;
                run.CurrentLength = 1;
                run.CurrentStart = run.Position;
            }

            run.Position++;
        }

        for (int i = 0; i < runs.Length; i++)
        {
            CloseRun(ref runs[i]);

            DebugLog.Write(LogChannel.InferenceDebug,
                "InferSendAA.FindRuns: zone session " + i + " has " + runs[i].Runs.Count
                + " runs, profile at " + runs[i].ProfilePosition
                + ", " + runs[i].Position + " Z2C messages", LogLevel.Trace);
            for (int r = 0; r < runs[i].Runs.Count; r++)
            {
                RunRecord record = runs[i].Runs[r];
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferSendAA.FindRuns: zone session " + i + " run " + record.WireValue
                    + " at " + record.Start + ".." + record.End, LogLevel.Trace);
            }
        }
        return runs;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CloseRun
    //
    // Ends a session's current sequence.  When it holds more than RunLengthThreshold
    // messages, it is added to the session's runs.  The current sequence is then cleared.
    //
    // run:  The session's run state.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void CloseRun(ref SessionRuns run)
    {
        if (run.CurrentValue.Exists && run.CurrentLength > RunLengthThreshold)
        {
            RunRecord record = new RunRecord();
            record.WireValue = run.CurrentValue;
            record.Start = run.CurrentStart;
            record.End = run.CurrentStart + run.CurrentLength - 1;
            run.Runs.Add(record);
        }

        run.CurrentValue = OpcodeValue.None;
        run.CurrentLength = 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SelectCandidates
    //
    // Collects the distinct wire values with a run before the profile in at least one zone
    // session, skipping wire values already accepted this session.
    //
    // runs:  Per zone session run state.
    //
    // Returns:  The candidate wire values, in order of first appearance.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static List<OpcodeValue> SelectCandidates(SessionRuns[] runs)
    {
        List<OpcodeValue> candidates = new List<OpcodeValue>();

        for (int i = 0; i < runs.Length; i++)
        {
            for (int r = 0; r < runs[i].Runs.Count; r++)
            {
                OpcodeValue wireValue = runs[i].Runs[r].WireValue;
                int start;
                if (candidates.Contains(wireValue) || !HasRunBeforeProfile(runs[i], wireValue, out start))
                {
                    continue;
                }
                if (AcceptedOpcodes.Instance.IsAccepted(wireValue))
                {
                    DebugLog.Write(LogChannel.InferenceDebug,
                        "InferSendAA.SelectCandidates: wireValue=" + wireValue + " already accepted as "
                        + AcceptedOpcodes.Instance.GetName(wireValue) + ", skipping", LogLevel.Trace);
                    continue;
                }
                candidates.Add(wireValue);
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferSendAA.SelectCandidates: " + candidates.Count + " candidates", LogLevel.Trace);
        return candidates;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ExamineCandidate
    //
    // Walks one candidate's packets once, keeping only zone-to-client packets of zone
    // sessions that carry the profile, and records each length.
    //
    // catalog:       The session's cataloged packets.
    // wireValue:     The candidate wire value.
    // zoneSessions:  The zone sessions.
    // runs:          Per zone session run records.
    //
    // Returns:  The candidate's facts.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static CandidateFacts ExamineCandidate(PacketCatalog catalog, OpcodeValue wireValue,
        List<Session> zoneSessions, SessionRuns[] runs)
    {
        CandidateFacts candidate = new CandidateFacts();
        candidate.WireValue = wireValue;

        List<CatalogedPacket> bucket = catalog.PacketsFor(wireValue, null, null, int.MaxValue);
        for (int i = 0; i < bucket.Count; i++)
        {
            CatalogedPacket packet = bucket[i];
            Session session = packet.Metadata.Session;
            if (session == null || session.ServerType != ServerType.Zone || packet.Metadata.ToServer)
            {
                continue;
            }
            int sessionIndex = zoneSessions.IndexOf(session);
            if (sessionIndex < 0 || runs[sessionIndex].ProfilePosition < 0)
            {
                continue;
            }

            candidate.Lengths.Add(packet.Payload.Length);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferSendAA.ExamineCandidate: wireValue=" + wireValue + " "
            + bucket.Count + " packets, " + candidate.Lengths.Count + " Z2C in profile sessions",
            LogLevel.Trace);
        return candidate;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HasRunBeforeProfile
    //
    // Tests whether a session has a run of the given wire value that ends before the
    // session's profile.  A session with no recorded profile position has no such run.
    //
    // run:        The session's run state.
    // wireValue:  The wire value to test.
    // start:      Receives the position of the first message of the earliest such run, or
    //             -1 when there is none.
    //
    // Returns:  True when such a run exists.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool HasRunBeforeProfile(SessionRuns run, OpcodeValue wireValue, out int start)
    {
        start = -1;
        if (run.ProfilePosition < 0)
        {
            return false;
        }

        for (int i = 0; i < run.Runs.Count; i++)
        {
            RunRecord record = run.Runs[i];
            if (record.WireValue.Equals(wireValue) && record.End < run.ProfilePosition)
            {
                start = record.Start;
                return true;
            }
        }
        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScoreCandidate
    //
    // Sums the run, proximity, and size range log likelihood ratios for one candidate and
    // converts the total to a posterior with prior odds of one over the number of wire
    // values in the capture.  Builds a proposal with an itemized deciban ledger when the
    // posterior reaches ReportThreshold.
    //
    // candidate:    The candidate's facts.
    // runs:         Per zone session run records.
    // opcodeCount:  Number of distinct wire values in the capture.
    // pooledSizes:  Message count per length across the capture.
    //
    // Returns:  The proposal, or null when no session qualified or the posterior is below
    //           threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private InferenceProposal? ScoreCandidate(CandidateFacts candidate, SessionRuns[] runs,
        int opcodeCount, Dictionary<int, int> pooledSizes)
    {
        int qualifyingSessions;
        int matchedSessions;
        double runLogLR = RunLogLR(candidate, runs, opcodeCount,
            out qualifyingSessions, out matchedSessions);
        if (qualifyingSessions == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferSendAA.ScoreCandidate: wireValue=" + candidate.WireValue
                + " has no qualifying zone sessions, skipping", LogLevel.Trace);
            return null;
        }

        double proximityLogLR = ProximityLogLR(candidate, runs);
        double rangeLogLR = RangeLogLR(candidate, pooledSizes);
        double totalLogLR = runLogLR + proximityLogLR + rangeLogLR;
        double posterior = EvidenceScoring.CombinePosterior(totalLogLR, 1.0 / opcodeCount);

        string ledger =
            matchedSessions + "/" + qualifyingSessions + " zone sessions with the run, "
            + candidate.Lengths.Count + " Z2C occurrences in zone sessions | "
            + "run " + EvidenceScoring.ToDecibans(runLogLR).ToString("F1") + " db, "
            + "proximity " + EvidenceScoring.ToDecibans(proximityLogLR).ToString("F1") + " db, "
            + "range " + EvidenceScoring.ToDecibans(rangeLogLR).ToString("F1") + " db | "
            + "total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + " db, "
            + "prior 1/" + opcodeCount + ", posterior " + posterior.ToString("F3");

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferSendAA.ScoreCandidate: wireValue=" + candidate.WireValue + " " + ledger,
            LogLevel.Trace);

        if (posterior < ReportThreshold)
        {
            return null;
        }

        InferenceProposal proposal = new InferenceProposal();
        proposal.Label = Label;
        proposal.Opcode = new PatchOpcode(GlassContext.CurrentPatchLevel, candidate.WireValue);
        proposal.Confidence = posterior;
        proposal.Evidence = ledger;
        return proposal;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RunLogLR
    //
    // Log likelihood ratio from the session runs.  A zone session qualifies only when it
    // has a recorded profile position.  In each qualifying session, whether the candidate
    // has a run before the profile is compared against q, the smoothed fraction of the
    // other wire values in the capture with a run before the profile in that session:
    //
    //   run:     log(1 - leak) - log(q)
    //   no run:  log(leak) - log(1 - q)
    //
    // The total is clamped to +/- MaxAbsLogLR.
    //
    // candidate:           The candidate's facts.
    // runs:                Per zone session run state.
    // opcodeCount:         Number of distinct wire values in the capture.
    // qualifyingSessions:  Receives the number of sessions scored.
    // matchedSessions:     Receives how many of those had the candidate's run.
    //
    // Returns:  Natural-log likelihood ratio from the runs, clamped.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double RunLogLR(CandidateFacts candidate, SessionRuns[] runs, int opcodeCount,
        out int qualifyingSessions, out int matchedSessions)
    {
        qualifyingSessions = 0;
        matchedSessions = 0;
        double logLR = 0.0;
        int otherCount = opcodeCount - 1;

        for (int sessionIndex = 0; sessionIndex < runs.Length; sessionIndex++)
        {
            SessionRuns run = runs[sessionIndex];
            if (run.ProfilePosition < 0)
            {
                continue;
            }
            qualifyingSessions++;

            int start;
            bool matched = HasRunBeforeProfile(run, candidate.WireValue, out start);

            // distinct other wire values with a run before the profile in this session
            int matchedOthers = 0;
            for (int r = 0; r < run.Runs.Count; r++)
            {
                OpcodeValue other = run.Runs[r].WireValue;
                if (other.Equals(candidate.WireValue) || !HasRunBeforeProfile(run, other, out start))
                {
                    continue;
                }
                bool counted = false;
                for (int earlier = 0; earlier < r; earlier++)
                {
                    if (run.Runs[earlier].WireValue.Equals(other))
                    {
                        counted = true;
                        break;
                    }
                }
                if (!counted)
                {
                    matchedOthers++;
                }
            }
            double q = (matchedOthers + 1.0) / (otherCount + 2.0);

            if (matched)
            {
                matchedSessions++;
                logLR += Math.Log(1.0 - LeakProbability) - Math.Log(q);
            }
            else
            {
                logLR += Math.Log(LeakProbability) - Math.Log(1.0 - q);
            }
        }

        if (logLR > MaxAbsLogLR)
        {
            logLR = MaxAbsLogLR;
        }
        if (logLR < -MaxAbsLogLR)
        {
            logLR = -MaxAbsLogLR;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferSendAA.RunLogLR: wireValue=" + candidate.WireValue
            + " matched=" + matchedSessions + "/" + qualifyingSessions
            + " logLR=" + logLR.ToString("F3"), LogLevel.Trace);
        return logLR;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ProximityLogLR
    //
    // Log likelihood ratio from how near the session start the candidate's run begins, in
    // each session where the candidate has a run before the profile.  A run starting at or
    // before ProximityAllowance Z2C messages costs nothing; each message past it costs
    // ProximityPenalty.  This feature only refutes; it never supports.  The total is
    // clamped at -MaxAbsLogLR.
    //
    // candidate:  The candidate's facts.
    // runs:       Per zone session run state.
    //
    // Returns:  Natural-log likelihood ratio from proximity, at most zero, clamped.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double ProximityLogLR(CandidateFacts candidate, SessionRuns[] runs)
    {
        double logLR = 0.0;

        for (int sessionIndex = 0; sessionIndex < runs.Length; sessionIndex++)
        {
            int start;
            if (!HasRunBeforeProfile(runs[sessionIndex], candidate.WireValue, out start))
            {
                continue;
            }
            int excess = start - ProximityAllowance;
            if (excess > 0)
            {
                logLR -= excess * ProximityPenalty;
            }
        }

        if (logLR < -MaxAbsLogLR)
        {
            logLR = -MaxAbsLogLR;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferSendAA.ProximityLogLR: wireValue=" + candidate.WireValue
            + " logLR=" + logLR.ToString("F3"), LogLevel.Trace);
        return logLR;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RangeLogLR
    //
    // Log likelihood ratio from whether the distinct lengths of the candidate's
    // zone-to-client payloads fall between RangeLow and RangeHigh, charged steeply per
    // byte outside.  The lengths are gathered into a histogram because the range scorer
    // takes one.
    //
    // candidate:    The candidate's facts.
    // pooledSizes:  Message count per length across the capture.
    //
    // Returns:  Natural-log likelihood ratio from the size range, clamped.  Zero when the
    //           candidate has no zone-to-client payloads.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double RangeLogLR(CandidateFacts candidate, Dictionary<int, int> pooledSizes)
    {
        Dictionary<int, int> observed = new Dictionary<int, int>();
        for (int i = 0; i < candidate.Lengths.Count; i++)
        {
            int length = candidate.Lengths[i];
            int seen;
            if (observed.TryGetValue(length, out seen))
            {
                observed[length] = seen + 1;
            }
            else
            {
                observed[length] = 1;
            }
        }

        return EvidenceScoring.SizeRangeLogLR(observed, RangeLow, RangeHigh, pooledSizes,
            LeakProbability, RangeDistancePenalty, MaxAbsLogLR);
    }
}
