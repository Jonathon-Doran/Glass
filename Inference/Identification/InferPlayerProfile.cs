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
// InferPlayerProfile
//
// Identifies OP_PlayerProfile.  The zone server sends exactly one profile per zone session,
// and it is the first zone-to-client message on that session at or above MinimumLargeSize.
// The profile is variable length.
//
// Candidates are the unaccepted wire values with the largest observed payloads.  Only zone
// sessions are examined; other sessions are not scored.  A zone session is scored for a
// candidate only when it carries the accepted OP_Inventory or at least one packet of the
// candidate.  Each candidate is scored on per-session behavior.  The
// catalog's length statistics must be prepared before this handler runs.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferPlayerProfile : IInferOpcodes
{
    private const string OpcodeNameConstant = "OP_PlayerProfile";
    private const string AnchorOpcodeName = "OP_Inventory";        // qualifies a zone session
    private const int CandidateCount = 10;                 // wire values examined, largest first
    private const int MinimumLargeSize = 10000;            // smallest Z2C payload counted as large
    private const double LeakProbability = 0.001;          // per-item allowance for a failed predicate
    private const double MaxAbsLogLR = 12.0;               // clamp per feature, roughly +/- 52 decibans
    private const double ReportThreshold = 0.40;           // minimum posterior to emit a proposal

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SessionFacts
    //
    // One candidate's traffic in one zone session.  ToClient counts zone-to-client payloads
    // and Other counts client-to-zone payloads.  IsFirstLarge is true when the session's
    // first zone-to-client payload of at least MinimumLargeSize carried the candidate.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private struct SessionFacts
    {
        public int ToClient;
        public bool IsFirstLarge;

        public bool IsPresent
        {
            get { return ToClient > 0; }
        }

        public bool IsMatch
        {
            get { return ToClient == 1 && IsFirstLarge; }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CandidateFacts
    //
    // One candidate's traffic across all zone sessions.  Sessions is parallel to the zone
    // session list. Occurrences counts the candidate's zone-to-client packets that belong
    // to a zone session.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class CandidateFacts
    {
        public OpcodeValue WireValue;
        public SessionFacts[] Sessions = Array.Empty<SessionFacts>();
        public int Occurrences = 0;
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
    // Selects the candidates, finds the zone sessions, which of them carry the anchor, and
    // which wire value was each session's first large zone-to-client message, examines each
    // candidate's traffic per session, then scores each candidate.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  Proposals ordered by posterior descending, empty when there are no
    //           candidates, no zone sessions, or nothing scores at threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<InferenceProposal> Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferPlayerProfile.Infer: starting", LogLevel.Trace);
        List<InferenceProposal> proposals = new List<InferenceProposal>();

        List<OpcodeValue> candidates = SelectCandidates(catalog);
        List<Session> zoneSessions = FindZoneSessions(catalog);
        if (candidates.Count == 0 || zoneSessions.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferPlayerProfile.Infer: " + candidates.Count + " candidates, "
                + zoneSessions.Count + " zone sessions, no proposals", LogLevel.Warn);
            return proposals;
        }

        bool[] anchorSessions = FindAnchorSessions(catalog, zoneSessions);
        OpcodeValue[] firstLarge = FindFirstLarge(catalog, zoneSessions);

        List<CandidateFacts> facts = new List<CandidateFacts>();
        for (int i = 0; i < candidates.Count; i++)
        {
            facts.Add(ExamineCandidate(catalog, candidates[i], zoneSessions, firstLarge));
        }

        Dictionary<int, int> pooledSizes = catalog.PooledSizeHistogram();
        for (int i = 0; i < facts.Count; i++)
        {
            InferenceProposal? proposal = ScoreCandidate(facts, i, anchorSessions);
            if (proposal != null)
            {
                proposals.Add(proposal);
            }
        }

        proposals.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferPlayerProfile.Infer: " + proposals.Count + " proposals", LogLevel.Info);
        return proposals;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SelectCandidates
    //
    // Takes wire values in order of largest observed payload, skipping net opcodes and
    // wire values already accepted this session, until CandidateCount are collected.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  Up to CandidateCount wire values, largest first.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static List<OpcodeValue> SelectCandidates(PacketCatalog catalog)
    {
        List<OpcodeValue> bySize = catalog.OpcodesByMaxSizeDescending();
        List<OpcodeValue> candidates = new List<OpcodeValue>();

        for (int i = 0; i < bySize.Count && candidates.Count < CandidateCount; i++)
        {
            OpcodeValue wireValue = bySize[i];
            if (SoeConstants.IsNetOpcode(wireValue.Value))
            {
                continue;
            }
            if (AcceptedOpcodes.Instance.IsAccepted(wireValue))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferPlayerProfile.SelectCandidates: wireValue=" + wireValue + " already accepted as "
                    + AcceptedOpcodes.Instance.GetName(wireValue) + ", skipping", LogLevel.Trace);
                continue;
            }
            candidates.Add(wireValue);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferPlayerProfile.SelectCandidates: " + candidates.Count + " candidates", LogLevel.Trace);
        return candidates;
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
                    "InferPlayerProfile.FindZoneSessions: null packet at index " + i, LogLevel.Error);
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
            "InferPlayerProfile.FindZoneSessions: " + zoneSessions.Count + " zone sessions",
            LogLevel.Trace);
        return zoneSessions;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindAnchorSessions
    //
    // Marks which zone sessions carry a packet with the accepted OP_Inventory wire value.
    // When OP_Inventory has not been accepted, no session is marked, and a session then
    // qualifies for a candidate only by carrying that candidate.
    //
    // catalog:       The session's cataloged packets.
    // zoneSessions:  The zone sessions.
    //
    // Returns:  One flag per zone session, parallel to zoneSessions.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool[] FindAnchorSessions(PacketCatalog catalog, List<Session> zoneSessions)
    {
        bool[] hasAnchor = new bool[zoneSessions.Count];

        OpcodeValue anchorWireValue = AcceptedOpcodes.Instance.WireValueFor(AnchorOpcodeName);
        if (!anchorWireValue.Exists)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferPlayerProfile.FindAnchorSessions: " + AnchorOpcodeName
                + " not accepted, sessions qualify only by carrying the candidate", LogLevel.Warn);
            return hasAnchor;
        }

        int marked = 0;
        List<CatalogedPacket> anchors = catalog.PacketsFor(anchorWireValue, null, null, int.MaxValue);
        for (int i = 0; i < anchors.Count; i++)
        {
            int sessionIndex = zoneSessions.IndexOf(anchors[i].Metadata.Session);
            if (sessionIndex >= 0 && !hasAnchor[sessionIndex])
            {
                hasAnchor[sessionIndex] = true;
                marked++;
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferPlayerProfile.FindAnchorSessions: " + marked + "/" + zoneSessions.Count
            + " zone sessions carry " + AnchorOpcodeName, LogLevel.Trace);
        return hasAnchor;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindFirstLarge
    //
    // Walks the catalog in arrival order and records, for each zone session, the wire value
    // of its first zone-to-client application payload of at least MinimumLargeSize.  The
    // walk stops early once every zone session has one.
    //
    // catalog:       The session's cataloged packets.
    // zoneSessions:  The zone sessions.
    //
    // Returns:  One wire value per zone session, parallel to zoneSessions; OpcodeValue.None
    //           for a session with no large zone-to-client payload.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static OpcodeValue[] FindFirstLarge(PacketCatalog catalog, List<Session> zoneSessions)
    {
        OpcodeValue[] firstLarge = new OpcodeValue[zoneSessions.Count];
        for (int i = 0; i < firstLarge.Length; i++)
        {
            firstLarge[i] = OpcodeValue.None;
        }

        int found = 0;
        uint totalCount = (uint)catalog.TotalPacketCount();
        for (uint i = 0; i < totalCount && found < firstLarge.Length; i++)
        {
            CatalogedPacket? packetOrNull = catalog.PacketAt(i);
            if (packetOrNull == null)
            {
                continue;
            }
            CatalogedPacket packet = packetOrNull.Value;

            Session session = packet.Metadata.Session;
            if (session == null
                || session.ServerType != ServerType.Zone
                || packet.Metadata.ToServer
                || packet.Payload.Length < MinimumLargeSize
                || SoeConstants.IsNetOpcode(packet.Metadata.WireValue.Value))
            {
                continue;
            }

            int sessionIndex = zoneSessions.IndexOf(session);
            if (sessionIndex < 0 || firstLarge[sessionIndex].Exists)
            {
                continue;
            }

            firstLarge[sessionIndex] = packet.Metadata.WireValue;
            found++;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferPlayerProfile.FindFirstLarge: " + found + "/" + firstLarge.Length
            + " zone sessions have a large Z2C payload", LogLevel.Trace);
        return firstLarge;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ExamineCandidate
    //
    // Walks one candidate's packets once and tallies them into the zone session each
    // belongs to, recording every length.  Packets outside the zone sessions are ignored.
    // Each session is then marked when its first large zone-to-client payload carried the
    // candidate.
    //
    // catalog:       The session's cataloged packets.
    // wireValue:     The candidate wire value.
    // zoneSessions:  The zone sessions.
    // firstLarge:    Per zone session, the wire value of its first large Z2C payload.
    //
    // Returns:  The candidate's facts.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static CandidateFacts ExamineCandidate(PacketCatalog catalog, OpcodeValue wireValue,
        List<Session> zoneSessions, OpcodeValue[] firstLarge)
    {
        CandidateFacts candidate = new CandidateFacts();
        candidate.WireValue = wireValue;
        candidate.Sessions = new SessionFacts[zoneSessions.Count];

        List<CatalogedPacket> bucket = catalog.PacketsFor(wireValue, null, null, int.MaxValue);
        for (int i = 0; i < bucket.Count; i++)
        {
            CatalogedPacket packet = bucket[i];
            if (packet.Metadata.Session == null)
            {
                continue;
            }
            int sessionIndex = zoneSessions.IndexOf(packet.Metadata.Session);
            if (sessionIndex < 0)
            {
                continue;
            }

            if (!packet.Metadata.ToServer)
            {
                candidate.Occurrences++;
                candidate.Sessions[sessionIndex].ToClient++;
            }
        }

        for (int sessionIndex = 0; sessionIndex < firstLarge.Length; sessionIndex++)
        {
            candidate.Sessions[sessionIndex].IsFirstLarge = firstLarge[sessionIndex].Equals(wireValue);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferPlayerProfile.ExamineCandidate: wireValue=" + wireValue + " "
            + bucket.Count + " packets, " + candidate.Occurrences + " Z2C in zone sessions",
            LogLevel.Trace);
        return candidate;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScoreCandidate
    //
    // Converts the session log likelihood ratio for one candidate to a posterior with
    // prior odds of one over the candidate count.  Builds a proposal with an itemized
    // deciban ledger when the posterior reaches ReportThreshold.
    //
    // facts:           Every candidate's facts.
    // candidateIndex:  Position of the candidate being scored in facts.
    // anchorSessions:  Per zone session, whether it carries the anchor.
    //
    // Returns:  The proposal, or null when no session qualified or the posterior is below
    //           threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private InferenceProposal? ScoreCandidate(List<CandidateFacts> facts, int candidateIndex,
        bool[] anchorSessions)
    {
        CandidateFacts candidate = facts[candidateIndex];

        int qualifyingSessions;
        int matchedSessions;
        double sessionLogLR = SessionLogLR(facts, candidateIndex, anchorSessions,
            out qualifyingSessions, out matchedSessions);
        if (qualifyingSessions == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferPlayerProfile.ScoreCandidate: wireValue=" + candidate.WireValue
                + " has no qualifying zone sessions, skipping", LogLevel.Trace);
            return null;
        }

        double totalLogLR = sessionLogLR;
        double posterior = EvidenceScoring.CombinePosterior(totalLogLR, 1.0 / facts.Count);

        string ledger =
            matchedSessions + "/" + qualifyingSessions + " zone sessions matched, "
            + candidate.Occurrences + " Z2C occurrences in zone sessions | "
            + "session " + EvidenceScoring.ToDecibans(sessionLogLR).ToString("F1") + " db | "
            + "total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + " db, "
            + "prior 1/" + facts.Count + ", posterior " + posterior.ToString("F3");

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferPlayerProfile.ScoreCandidate: wireValue=" + candidate.WireValue + " " + ledger,
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
    // SessionLogLR
    //
    // Log likelihood ratio from the candidate's per-session behavior.  A zone session
    // qualifies when it carries the anchor or the candidate.  In each qualifying session
    // the match (exactly one candidate packet, zone-to-client, and it is the session's
    // first large zone-to-client payload) is compared against q, the smoothed fraction of
    // the other candidates that match in that session:
    //
    //   match:          log(1 - leak) - log(q)
    //   anything else:  log(leak) - log(1 - q)
    //
    // Anything else covers absence, more than one packet, a client-to-zone packet, and not
    // being first.  The total is clamped to +/- MaxAbsLogLR.
    //
    // facts:               Every candidate's facts.
    // candidateIndex:      Position of the candidate being scored in facts.
    // anchorSessions:      Per zone session, whether it carries the anchor.
    // qualifyingSessions:  Receives the number of sessions scored.
    // matchedSessions:     Receives how many of those matched.
    //
    // Returns:  Natural-log likelihood ratio from session behavior, clamped.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double SessionLogLR(List<CandidateFacts> facts, int candidateIndex,
        bool[] anchorSessions, out int qualifyingSessions, out int matchedSessions)
    {
        qualifyingSessions = 0;
        matchedSessions = 0;
        CandidateFacts candidate = facts[candidateIndex];
        double logLR = 0.0;

        for (int sessionIndex = 0; sessionIndex < candidate.Sessions.Length; sessionIndex++)
        {
            SessionFacts session = candidate.Sessions[sessionIndex];
            if (!session.IsPresent && !anchorSessions[sessionIndex])
            {
                continue;
            }
            qualifyingSessions++;

            int matchedOthers = 0;
            for (int other = 0; other < facts.Count; other++)
            {
                if (other != candidateIndex && facts[other].Sessions[sessionIndex].IsMatch)
                {
                    matchedOthers++;
                }
            }
            double q = (matchedOthers + 1.0) / ((facts.Count - 1) + 2.0);

            if (session.IsMatch)
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
            "InferPlayerProfile.SessionLogLR: wireValue=" + candidate.WireValue
            + " matched=" + matchedSessions + "/" + qualifyingSessions
            + " logLR=" + logLR.ToString("F3"), LogLevel.Trace);
        return logLR;
    }
}