using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Client;
using Glass.Network.Protocol;
using Inference.Core;
using Inference.Models;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// InferInventory
//
// Identifies OP_Inventory.  Within one zone session the server is expected to send one
// filled inventory (zone-to-client, large and variable length) and the client to answer
// with one empty inventory (client-to-zone, zero length).  A filled inventory begins with
// a 32-bit item count followed at offset 4 by a 16-character printable string and a null
// terminator.
//
// Candidates are the unaccepted wire values with the largest observed payloads.  Only zone
// sessions are examined; other sessions are not scored.  A zone session is scored for a
// candidate only when it carries the accepted OP_PlayerProfile or at least one packet of
// the candidate.  Each candidate is scored on per-session pairing, the filled-payload
// header, and the filled-payload size range.  The catalog's length statistics must be
// prepared before this handler runs.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferInventory : IInferOpcodes
{
    private const string OpcodeNameConstant = "OP_Inventory";
    private const string ProfileOpcodeName = "OP_PlayerProfile";   // qualifies a zone session
    private const int CandidateCount = 10;                 // wire values examined, largest first
    private const double LeakProbability = 0.001;          // per-item allowance for a failed predicate
    private const double MaxAbsLogLR = 12.0;               // clamp per feature, roughly +/- 52 decibans
    private const int RangeLow = 100000;                   // smallest filled inventory length accepted
    private const int RangeHigh = 1000000;                 // largest filled inventory length accepted
    private const double RangeDistancePenalty = 0.0001;    // log-LR per byte outside the range
    private const uint MaxItemCount = 1000;                // item count at offset 0 must be below this
    private const int NameOffset = 4;                      // offset of the 16-character string
    private const int NameLength = 16;                     // printable characters before the null
    private const double ReportThreshold = 0.40;           // minimum posterior to emit a proposal

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SessionFacts
    //
    // One candidate's traffic in one zone session.  FilledToClient counts non-empty
    // zone-to-client payloads, EmptyToZone counts zero-length client-to-zone payloads, and
    // Other counts everything else (empty zone-to-client, non-empty client-to-zone).
    ///////////////////////////////////////////////////////////////////////////////////////////
    private struct SessionFacts
    {
        public int FilledToClient;
        public int EmptyToZone;
        public int Other;

        public bool HasFilled
        {
            get { return FilledToClient > 0; }
        }

        public bool HasEmpty
        {
            get { return EmptyToZone > 0; }
        }

        public bool HasExtra
        {
            get { return FilledToClient > 1 || EmptyToZone > 1 || Other > 0; }
        }

        public bool IsPresent
        {
            get { return FilledToClient > 0 || EmptyToZone > 0 || Other > 0; }
        }

        public bool IsExactPair
        {
            get { return HasFilled && HasEmpty && !HasExtra; }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CandidateFacts
    //
    // One candidate's traffic across all zone sessions.  Sessions is parallel to the zone
    // session list.  FilledLengths holds the length of every non-empty zone-to-client
    // payload; HeaderTotal counts how many of those carried the inventory header.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class CandidateFacts
    {
        public OpcodeValue WireValue;
        public SessionFacts[] Sessions = Array.Empty<SessionFacts>();
        public List<int> FilledLengths = new List<int>();
        public int HeaderTotal = 0;
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
    // Selects the candidates, finds the zone sessions and which of them carry the profile,
    // examines each candidate's traffic per session, then scores each candidate.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  Proposals ordered by posterior descending, empty when there are no
    //           candidates, no zone sessions, or nothing scores at threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<InferenceProposal> Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferInventory.Infer: starting", LogLevel.Trace);
        List<InferenceProposal> proposals = new List<InferenceProposal>();

        List<OpcodeValue> candidates = SelectCandidates(catalog);
        List<Session> zoneSessions = FindZoneSessions(catalog);
        if (candidates.Count == 0 || zoneSessions.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferInventory.Infer: " + candidates.Count + " candidates, "
                + zoneSessions.Count + " zone sessions, no proposals", LogLevel.Warn);
            return proposals;
        }

        bool[] profileSessions = FindProfileSessions(catalog, zoneSessions);

        List<CandidateFacts> facts = new List<CandidateFacts>();
        for (int i = 0; i < candidates.Count; i++)
        {
            facts.Add(ExamineCandidate(catalog, candidates[i], zoneSessions));
        }

        Dictionary<int, int> pooledSizes = catalog.PooledSizeHistogram();
        for (int i = 0; i < facts.Count; i++)
        {
            InferenceProposal? proposal = ScoreCandidate(facts, i, profileSessions, pooledSizes);
            if (proposal != null)
            {
                proposals.Add(proposal);
            }
        }

        proposals.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferInventory.Infer: " + proposals.Count + " proposals", LogLevel.Info);
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
                    "InferInventory.SelectCandidates: wireValue=" + wireValue + " already accepted as "
                    + AcceptedOpcodes.Instance.GetName(wireValue) + ", skipping", LogLevel.Trace);
                continue;
            }
            candidates.Add(wireValue);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferInventory.SelectCandidates: " + candidates.Count + " candidates", LogLevel.Trace);
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
                    "InferInventory.FindZoneSessions: null packet at index " + i, LogLevel.Error);
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
            "InferInventory.FindZoneSessions: " + zoneSessions.Count + " zone sessions",
            LogLevel.Trace);
        return zoneSessions;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindProfileSessions
    //
    // Marks which zone sessions carry a packet with the accepted OP_PlayerProfile wire
    // value.  When OP_PlayerProfile has not been accepted, no session is marked, and a
    // session then qualifies for a candidate only by carrying that candidate.
    //
    // catalog:       The session's cataloged packets.
    // zoneSessions:  The zone sessions.
    //
    // Returns:  One flag per zone session, parallel to zoneSessions.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool[] FindProfileSessions(PacketCatalog catalog, List<Session> zoneSessions)
    {
        bool[] hasProfile = new bool[zoneSessions.Count];

        OpcodeValue profileWireValue = AcceptedOpcodes.Instance.WireValueFor(ProfileOpcodeName);
        if (!profileWireValue.Exists)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferInventory.FindProfileSessions: " + ProfileOpcodeName
                + " not accepted, sessions qualify only by carrying the candidate", LogLevel.Warn);
            return hasProfile;
        }

        int marked = 0;
        List<CatalogedPacket> profiles = catalog.PacketsFor(profileWireValue, null, null, int.MaxValue);
        for (int i = 0; i < profiles.Count; i++)
        {
            int sessionIndex = zoneSessions.IndexOf(profiles[i].Metadata.Session);
            if (sessionIndex >= 0 && !hasProfile[sessionIndex])
            {
                hasProfile[sessionIndex] = true;
                marked++;
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferInventory.FindProfileSessions: " + marked + "/" + zoneSessions.Count
            + " zone sessions carry " + ProfileOpcodeName, LogLevel.Trace);
        return hasProfile;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ExamineCandidate
    //
    // Walks one candidate's packets once and tallies them into the zone session each
    // belongs to.  Packets outside the zone sessions are ignored.  Non-empty
    // zone-to-client payloads have their lengths recorded and are tested for the
    // inventory header.
    //
    // catalog:       The session's cataloged packets.
    // wireValue:     The candidate wire value.
    // zoneSessions:  The zone sessions.
    //
    // Returns:  The candidate's facts.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static CandidateFacts ExamineCandidate(PacketCatalog catalog, OpcodeValue wireValue,
        List<Session> zoneSessions)
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

            bool toClient = packet.Metadata.Channel == SoeConstants.StreamId.StreamZoneToClient;
            ReadOnlySpan<byte> payload = packet.Payload.AsReadOnlySpan();

            if (toClient && payload.Length > 0)
            {
                candidate.Sessions[sessionIndex].FilledToClient++;
                candidate.FilledLengths.Add(payload.Length);
                if (HasInventoryHeader(payload))
                {
                    candidate.HeaderTotal++;
                }
            }
            else if (!toClient && payload.Length == 0)
            {
                candidate.Sessions[sessionIndex].EmptyToZone++;
            }
            else
            {
                candidate.Sessions[sessionIndex].Other++;
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferInventory.ExamineCandidate: wireValue=" + wireValue + " "
            + bucket.Count + " packets, " + candidate.FilledLengths.Count + " filled Z2C, "
            + candidate.HeaderTotal + " with header", LogLevel.Trace);
        return candidate;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HasInventoryHeader
    //
    // Tests whether a payload begins with the filled inventory header: a little-endian
    // 32-bit count below MaxItemCount at offset 0, then NameLength printable ASCII bytes
    // (0x20 to 0x7E) starting at NameOffset, then a 0x00 terminator.
    //
    // payload:  The bytes to test.
    //
    // Returns:  True when the header is present.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool HasInventoryHeader(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < NameOffset + NameLength + 1)
        {
            return false;
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        if (count >= MaxItemCount)
        {
            return false;
        }

        for (int i = NameOffset; i < NameOffset + NameLength; i++)
        {
            if (payload[i] < 0x20 || payload[i] > 0x7E)
            {
                return false;
            }
        }

        return payload[NameOffset + NameLength] == 0x00;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScoreCandidate
    //
    // Sums the pairing, header, and size range log likelihood ratios for one candidate and
    // converts the total to a posterior with prior odds of one over the candidate count.
    // Builds a proposal with an itemized deciban ledger when the posterior reaches
    // ReportThreshold.
    //
    // facts:            Every candidate's facts.
    // candidateIndex:   Position of the candidate being scored in facts.
    // profileSessions:  Per zone session, whether it carries the profile.
    // pooledSizes:      Message count per length across the capture.
    //
    // Returns:  The proposal, or null when no session qualified or the posterior is below
    //           threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private InferenceProposal? ScoreCandidate(List<CandidateFacts> facts, int candidateIndex,
        bool[] profileSessions, Dictionary<int, int> pooledSizes)
    {
        CandidateFacts candidate = facts[candidateIndex];

        int qualifyingSessions;
        int pairedSessions;
        double pairingLogLR = PairingLogLR(facts, candidateIndex, profileSessions,
            out qualifyingSessions, out pairedSessions);
        if (qualifyingSessions == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferInventory.ScoreCandidate: wireValue=" + candidate.WireValue
                + " has no qualifying zone sessions, skipping", LogLevel.Trace);
            return null;
        }

        double headerLogLR = HeaderLogLR(facts, candidateIndex);
        double rangeLogLR = RangeLogLR(candidate, pooledSizes);
        double totalLogLR = pairingLogLR + headerLogLR + rangeLogLR;
        double posterior = EvidenceScoring.CombinePosterior(totalLogLR, 1.0 / facts.Count);

        string ledger =
            pairedSessions + "/" + qualifyingSessions + " zone sessions paired, "
            + candidate.FilledLengths.Count + " filled Z2C, " + candidate.HeaderTotal + " with header | "
            + "pair " + EvidenceScoring.ToDecibans(pairingLogLR).ToString("F1") + " db, "
            + "header " + EvidenceScoring.ToDecibans(headerLogLR).ToString("F1") + " db, "
            + "range " + EvidenceScoring.ToDecibans(rangeLogLR).ToString("F1") + " db | "
            + "total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + " db, "
            + "prior 1/" + facts.Count + ", posterior " + posterior.ToString("F3");

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferInventory.ScoreCandidate: wireValue=" + candidate.WireValue + " " + ledger,
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
    // PairingLogLR
    //
    // Log likelihood ratio from the candidate's per-session behavior.  A zone session
    // qualifies when it carries the profile or the candidate.  In each qualifying session
    // the exact pair (one filled zone-to-client, one empty client-to-zone, no extra
    // packets) is compared against q, the smoothed fraction of the other candidates that
    // show the exact pair in that session:
    //
    //   exact pair:     log(1 - leak) - log(q)
    //   anything else:  log(leak) - log(1 - q)
    //
    // Anything else covers a missing filled payload, a missing empty payload, and extra
    // packets.  The total is clamped to +/- MaxAbsLogLR.
    //
    // facts:               Every candidate's facts.
    // candidateIndex:      Position of the candidate being scored in facts.
    // profileSessions:     Per zone session, whether it carries the profile.
    // qualifyingSessions:  Receives the number of sessions scored.
    // pairedSessions:      Receives how many of those showed the exact pair.
    //
    // Returns:  Natural-log likelihood ratio from pairing, clamped.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double PairingLogLR(List<CandidateFacts> facts, int candidateIndex,
        bool[] profileSessions, out int qualifyingSessions, out int pairedSessions)
    {
        qualifyingSessions = 0;
        pairedSessions = 0;
        CandidateFacts candidate = facts[candidateIndex];
        double logLR = 0.0;

        for (int sessionIndex = 0; sessionIndex < candidate.Sessions.Length; sessionIndex++)
        {
            SessionFacts session = candidate.Sessions[sessionIndex];
            if (!session.IsPresent && !profileSessions[sessionIndex])
            {
                continue;
            }
            qualifyingSessions++;

            int pairedOthers = 0;
            for (int other = 0; other < facts.Count; other++)
            {
                if (other != candidateIndex && facts[other].Sessions[sessionIndex].IsExactPair)
                {
                    pairedOthers++;
                }
            }
            double q = (pairedOthers + 1.0) / ((facts.Count - 1) + 2.0);

            if (session.IsExactPair)
            {
                pairedSessions++;
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
            "InferInventory.PairingLogLR: wireValue=" + candidate.WireValue
            + " paired=" + pairedSessions + "/" + qualifyingSessions
            + " logLR=" + logLR.ToString("F3"), LogLevel.Trace);
        return logLR;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HeaderLogLR
    //
    // Log likelihood ratio from how many of the candidate's filled zone-to-client payloads
    // carry the inventory header, against the other candidates all of whose filled
    // payloads carry it.
    //
    // facts:           Every candidate's facts.
    // candidateIndex:  Position of the candidate being scored in facts.
    //
    // Returns:  Natural-log likelihood ratio from the header, clamped.  Zero when the
    //           candidate has no filled payloads.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double HeaderLogLR(List<CandidateFacts> facts, int candidateIndex)
    {
        int othersAllHeader = 0;
        for (int other = 0; other < facts.Count; other++)
        {
            if (other == candidateIndex)
            {
                continue;
            }
            int filled = facts[other].FilledLengths.Count;
            if (filled > 0 && facts[other].HeaderTotal == filled)
            {
                othersAllHeader++;
            }
        }

        CandidateFacts candidate = facts[candidateIndex];
        return EvidenceScoring.PayloadPredicateLogLR(candidate.HeaderTotal,
            candidate.FilledLengths.Count, othersAllHeader, facts.Count - 1,
            LeakProbability, MaxAbsLogLR);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RangeLogLR
    //
    // Log likelihood ratio from whether the distinct lengths of the candidate's filled
    // zone-to-client payloads fall between RangeLow and RangeHigh.  The lengths are
    // gathered into a histogram because the range scorer takes one.
    //
    // candidate:    The candidate's facts.
    // pooledSizes:  Message count per length across the capture.
    //
    // Returns:  Natural-log likelihood ratio from the size range, clamped.  Zero when the
    //           candidate has no filled payloads.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double RangeLogLR(CandidateFacts candidate, Dictionary<int, int> pooledSizes)
    {
        Dictionary<int, int> observed = new Dictionary<int, int>();
        for (int i = 0; i < candidate.FilledLengths.Count; i++)
        {
            int length = candidate.FilledLengths[i];
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