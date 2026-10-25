using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Client;
using Glass.Network.Protocol;
using Inference.Core;
using Inference.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// InferCommonMessage
//
// Identifies OP_CommonMessage.  The message is bidirectional and variable length, and every
// payload begins with a null-terminated string.  On a zone session the client may send
// messages whose leading string is exactly the session's character name; these may contain
// "[join" when the client auto-joins channels.  Zone-to-client messages carry other senders'
// names, so their leading string is not compared with anything.
//
// Only zone sessions whose Session has a Character are examined.  Candidates are the
// unaccepted wire values with at least one client-to-zone payload beginning with that
// session's character name.  Each candidate is scored on the character name, the autojoin
// marker, and the leading string.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferCommonMessage : IInferOpcodes
{
    private const string OpcodeNameConstant = "OP_CommonMessage";
    private const double LeakProbability = 0.001;          // per-item allowance for a failed predicate
    private const double MaxAbsLogLR = 12.0;               // clamp per feature, roughly +/- 52 decibans
    private const double ReportThreshold = 0.40;           // minimum posterior to emit a proposal
    private const int MinTextLength = 4;                   // printable bytes required in the message text
    private const double TextMajorityShare = 0.5;          // share of payloads that must carry message text
    private const double JoinPseudoCount = 1e-8;           // smoothing for the join background; the marker is not expected elsewhere
    private const double JoinMaxAbsLogLR = 23.0;           // clamp for the join alone, roughly 100 decibans

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PayloadTest
    //
    // A test applied to one payload.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private delegate bool PayloadTest(ReadOnlySpan<byte> payload);

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CandidateFacts
    //
    // One candidate's traffic in zone sessions with a character.  ToZone counts its
    // client-to-zone payloads and ToZoneWithName how many of those begin with the session's
    // character name.  HasJoin is true when any client-to-zone payload contains the autojoin
    // marker.  Payloads counts its payloads in both directions, PayloadsWithString how many
    // of those begin with a null-terminated printable string, and PayloadsWithText how many
    // also contain message text after it.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class CandidateFacts
    {
        public OpcodeValue WireValue;
        public int ToZone = 0;
        public int ToZoneWithName = 0;
        public bool HasJoin = false;
        public int Payloads = 0;
        public int PayloadsWithString = 0;
        public int PayloadsWithText = 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // JoinMarker
    //
    // The autojoin marker searched for anywhere in a client-to-zone payload.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static ReadOnlySpan<byte> JoinMarker
    {
        get { return "[join"u8; }
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
    // Finds the zone sessions with a character and their names, selects the candidates,
    // gathers the capture-wide backgrounds, examines each candidate, then scores each one.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  Proposals ordered by posterior descending, empty when there are no sessions
    //           with a character, no candidates, or nothing scores at threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<InferenceProposal> Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferCommonMessage.Infer: starting", LogLevel.Trace);
        List<InferenceProposal> proposals = new List<InferenceProposal>();

        List<Session> sessions = FindCharacterSessions(catalog);
        if (sessions.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferCommonMessage.Infer: no zone sessions with a character, no proposals",
                LogLevel.Warn);
            return proposals;
        }

        byte[][] names = SessionNames(sessions);
        List<OpcodeValue> candidates = SelectCandidates(catalog, sessions, names);
        if (candidates.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferCommonMessage.Infer: no candidates, no proposals", LogLevel.Warn);
            return proposals;
        }

        List<OpcodeValue> joinValues = FindJoinValues(catalog, sessions);
        int opcodesAllString = CountOpcodesPassing(catalog, sessions, BeginsWithString, 1.0, "string");
        int opcodesMostlyText = CountOpcodesPassing(catalog, sessions, HasTextAfterLead,
            TextMajorityShare, "text");
        int opcodeCount = catalog.KnownOpcodes().Length;

        List<CandidateFacts> facts = new List<CandidateFacts>();
        for (int i = 0; i < candidates.Count; i++)
        {
            facts.Add(ExamineCandidate(catalog, candidates[i], sessions, names));
        }

        for (int i = 0; i < facts.Count; i++)
        {
            InferenceProposal? proposal = ScoreCandidate(facts, i, joinValues, opcodesAllString,
                opcodesMostlyText, opcodeCount);
            if (proposal != null)
            {
                proposals.Add(proposal);
            }
        }

        proposals.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferCommonMessage.Infer: " + proposals.Count + " proposals", LogLevel.Info);
        return proposals;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindCharacterSessions
    //
    // Walks the catalog in arrival order and collects each distinct zone Session that has a
    // Character, in order of first appearance.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  The sessions, empty when none qualify.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static List<Session> FindCharacterSessions(PacketCatalog catalog)
    {
        List<Session> sessions = new List<Session>();
        Session? lastSession = null;
        uint totalCount = (uint)catalog.TotalPacketCount();

        for (uint i = 0; i < totalCount; i++)
        {
            CatalogedPacket? packetOrNull = catalog.PacketAt(i);
            if (packetOrNull == null)
            {
                DebugLog.Write(LogChannel.Inference,
                    "InferCommonMessage.FindCharacterSessions: null packet at index " + i, LogLevel.Error);
                continue;
            }

            Session session = packetOrNull.Value.Metadata.Session;
            if (session == null || ReferenceEquals(session, lastSession))
            {
                continue;
            }
            lastSession = session;

            if (session.ServerType != ServerType.Zone || session.Character == null)
            {
                continue;
            }
            if (!sessions.Contains(session))
            {
                sessions.Add(session);
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferCommonMessage.FindCharacterSessions: " + sessions.Count
            + " zone sessions with a character", LogLevel.Trace);
        return sessions;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SessionNames
    //
    // Encodes each session's character name as ASCII bytes for comparison with payloads.
    //
    // sessions:  Zone sessions with a character.
    //
    // Returns:  One byte array per session, parallel to sessions.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static byte[][] SessionNames(List<Session> sessions)
    {
        byte[][] names = new byte[sessions.Count][];
        for (int i = 0; i < sessions.Count; i++)
        {
            string name = sessions[i].Character == null ? string.Empty : sessions[i].Character!.Name;
            names[i] = Encoding.ASCII.GetBytes(name);
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferCommonMessage.SessionNames: session " + i + " character " + name, LogLevel.Trace);
        }
        return names;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SessionIndexOf
    //
    // Finds the position of a packet's Session in the session list.
    //
    // packet:    The packet.
    // sessions:  Zone sessions with a character.
    //
    // Returns:  The position, or -1 when the packet has no Session or its Session is not
    //           in the list.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static int SessionIndexOf(CatalogedPacket packet, List<Session> sessions)
    {
        Session session = packet.Metadata.Session;
        if (session == null)
        {
            return -1;
        }
        return sessions.IndexOf(session);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BeginsWithName
    //
    // Tests whether a payload begins with exactly the given name followed by a 0x00
    // terminator.
    //
    // payload:  The bytes to test.
    // name:     The character name as ASCII bytes.
    //
    // Returns:  True when the payload begins with the name and its terminator.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool BeginsWithName(ReadOnlySpan<byte> payload, byte[] name)
    {
        if (name.Length == 0 || payload.Length <= name.Length)
        {
            return false;
        }
        return payload.Slice(0, name.Length).SequenceEqual(name) && payload[name.Length] == 0x00;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LeadingStringLength
    //
    // Measures the null-terminated printable ASCII string (bytes 0x20 to 0x7E) at the start
    // of a payload.
    //
    // payload:  The bytes to measure.
    //
    // Returns:  The string's length without its terminator, or -1 when the payload does not
    //           begin with one or more printable bytes followed by a 0x00 inside the payload.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static int LeadingStringLength(ReadOnlySpan<byte> payload)
    {
        int length = 0;
        while (length < payload.Length && payload[length] >= 0x20 && payload[length] <= 0x7E)
        {
            length++;
        }
        if (length == 0 || length >= payload.Length || payload[length] != 0x00)
        {
            return -1;
        }
        return length;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BeginsWithString
    //
    // Tests whether a payload begins with a null-terminated printable string.
    //
    // payload:  The bytes to test.
    //
    // Returns:  True when the leading string is present.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool BeginsWithString(ReadOnlySpan<byte> payload)
    {
        return LeadingStringLength(payload) > 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HasTextAfterLead
    //
    // Tests whether a payload begins with a null-terminated printable string and, anywhere
    // after its terminator, contains a run of at least MinTextLength printable bytes.
    //
    // payload:  The bytes to test.
    //
    // Returns:  True when message text follows the leading string.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool HasTextAfterLead(ReadOnlySpan<byte> payload)
    {
        int leadLength = LeadingStringLength(payload);
        if (leadLength <= 0)
        {
            return false;
        }

        int run = 0;
        for (int i = leadLength + 1; i < payload.Length; i++)
        {
            if (payload[i] >= 0x20 && payload[i] <= 0x7E)
            {
                run++;
                if (run >= MinTextLength)
                {
                    return true;
                }
            }
            else
            {
                run = 0;
            }
        }
        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ContainsJoin
    //
    // Tests whether a payload contains the autojoin marker anywhere.
    //
    // payload:  The bytes to test.
    //
    // Returns:  True when the marker is present.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool ContainsJoin(ReadOnlySpan<byte> payload)
    {
        return payload.IndexOf(JoinMarker) >= 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SelectCandidates
    //
    // Walks the catalog once and collects the distinct unaccepted application wire values
    // with a client-to-zone payload, in a session with a character, that begins with that
    // session's character name.
    //
    // catalog:   The session's cataloged packets.
    // sessions:  Zone sessions with a character.
    // names:     Character names, parallel to sessions.
    //
    // Returns:  The candidate wire values, in order of first appearance.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static List<OpcodeValue> SelectCandidates(PacketCatalog catalog, List<Session> sessions,
        byte[][] names)
    {
        List<OpcodeValue> candidates = new List<OpcodeValue>();
        int skippedAccepted = 0;
        uint totalCount = (uint)catalog.TotalPacketCount();

        for (uint i = 0; i < totalCount; i++)
        {
            CatalogedPacket? packetOrNull = catalog.PacketAt(i);
            if (packetOrNull == null)
            {
                continue;
            }
            CatalogedPacket packet = packetOrNull.Value;
            OpcodeValue wireValue = packet.Metadata.WireValue;

            if (!packet.Metadata.ToServer
                || SoeConstants.IsNetOpcode(wireValue.Value)
                || candidates.Contains(wireValue))
            {
                continue;
            }
            int sessionIndex = SessionIndexOf(packet, sessions);
            if (sessionIndex < 0 || !BeginsWithName(packet.Payload.AsReadOnlySpan(), names[sessionIndex]))
            {
                continue;
            }
            if (AcceptedOpcodes.Instance.IsAccepted(wireValue))
            {
                skippedAccepted++;
                continue;
            }
            candidates.Add(wireValue);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferCommonMessage.SelectCandidates: " + candidates.Count + " candidates, "
            + skippedAccepted + " name-leading packets of accepted wire values skipped",
            LogLevel.Trace);
        return candidates;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindJoinValues
    //
    // Walks the catalog once and collects the distinct application wire values with a
    // client-to-zone payload, in a session with a character, that contains the autojoin
    // marker.
    //
    // catalog:   The session's cataloged packets.
    // sessions:  Zone sessions with a character.
    //
    // Returns:  The wire values carrying the marker.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static List<OpcodeValue> FindJoinValues(PacketCatalog catalog, List<Session> sessions)
    {
        List<OpcodeValue> joinValues = new List<OpcodeValue>();
        uint totalCount = (uint)catalog.TotalPacketCount();

        for (uint i = 0; i < totalCount; i++)
        {
            CatalogedPacket? packetOrNull = catalog.PacketAt(i);
            if (packetOrNull == null)
            {
                continue;
            }
            CatalogedPacket packet = packetOrNull.Value;
            OpcodeValue wireValue = packet.Metadata.WireValue;

            if (!packet.Metadata.ToServer
                || SoeConstants.IsNetOpcode(wireValue.Value)
                || joinValues.Contains(wireValue))
            {
                continue;
            }
            if (SessionIndexOf(packet, sessions) < 0 || !ContainsJoin(packet.Payload.AsReadOnlySpan()))
            {
                continue;
            }
            joinValues.Add(wireValue);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferCommonMessage.FindJoinValues: " + joinValues.Count
            + " wire values carry the autojoin marker", LogLevel.Trace);
        return joinValues;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CountOpcodesPassing
    //
    // Counts the application wire values in the capture for which at least minimumShare of
    // their payloads, in sessions with a character, pass the given test.  A minimumShare of
    // 1.0 requires every payload to pass.  Wire values with no payloads in those sessions
    // are not counted.
    //
    // catalog:       The session's cataloged packets.
    // sessions:      Zone sessions with a character.
    // test:          The payload test.
    // minimumShare:  Fraction of payloads that must pass, from 0.0 to 1.0.
    // testName:      Name of the test, for the log.
    //
    // Returns:  The count.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static int CountOpcodesPassing(PacketCatalog catalog, List<Session> sessions,
        PayloadTest test, double minimumShare, string testName)
    {
        OpcodeValue[] known = catalog.KnownOpcodes();
        int count = 0;

        for (int k = 0; k < known.Length; k++)
        {
            if (SoeConstants.IsNetOpcode(known[k].Value))
            {
                continue;
            }

            List<CatalogedPacket> bucket = catalog.PacketsFor(known[k], null, null, int.MaxValue);
            int payloads = 0;
            int passing = 0;
            for (int i = 0; i < bucket.Count; i++)
            {
                if (SessionIndexOf(bucket[i], sessions) < 0)
                {
                    continue;
                }
                payloads++;
                if (test(bucket[i].Payload.AsReadOnlySpan()))
                {
                    passing++;
                }
            }

            if (payloads > 0 && passing >= minimumShare * payloads)
            {
                count++;
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferCommonMessage.CountOpcodesPassing: " + count + "/" + known.Length
            + " wire values pass the " + testName + " test on at least "
            + minimumShare.ToString("F2") + " of payloads", LogLevel.Trace);
        return count;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ExamineCandidate
    //
    // Walks one candidate's packets once, keeping only packets of sessions with a
    // character, and tallies the name, autojoin, and leading string tests.
    //
    // catalog:    The session's cataloged packets.
    // wireValue:  The candidate wire value.
    // sessions:   Zone sessions with a character.
    // names:      Character names, parallel to sessions.
    //
    // Returns:  The candidate's facts.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static CandidateFacts ExamineCandidate(PacketCatalog catalog, OpcodeValue wireValue,
        List<Session> sessions, byte[][] names)
    {
        CandidateFacts candidate = new CandidateFacts();
        candidate.WireValue = wireValue;

        List<CatalogedPacket> bucket = catalog.PacketsFor(wireValue, null, null, int.MaxValue);
        for (int i = 0; i < bucket.Count; i++)
        {
            CatalogedPacket packet = bucket[i];
            int sessionIndex = SessionIndexOf(packet, sessions);
            if (sessionIndex < 0)
            {
                continue;
            }

            ReadOnlySpan<byte> payload = packet.Payload.AsReadOnlySpan();
            candidate.Payloads++;
            if (BeginsWithString(payload))
            {
                candidate.PayloadsWithString++;
            }
            if (HasTextAfterLead(payload))
            {
                candidate.PayloadsWithText++;
            }

            if (packet.Metadata.ToServer)
            {
                candidate.ToZone++;
                if (BeginsWithName(payload, names[sessionIndex]))
                {
                    candidate.ToZoneWithName++;
                }
                if (ContainsJoin(payload))
                {
                    candidate.HasJoin = true;
                }
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferCommonMessage.ExamineCandidate: wireValue=" + wireValue
            + " C2Z " + candidate.ToZoneWithName + "/" + candidate.ToZone + " with name"
            + ", join " + candidate.HasJoin
            + ", string " + candidate.PayloadsWithString + "/" + candidate.Payloads, LogLevel.Trace);
        return candidate;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScoreCandidate
    //
    // Sums the name, autojoin, leading string, and message text log likelihood ratios for one
    // candidate and converts the total to a posterior with prior odds of one over the number
    // of wire values in the capture.  Builds a proposal with an itemized deciban ledger when
    // the posterior reaches ReportThreshold.
    //
    // facts:              Every candidate's facts.
    // candidateIndex:     Position of the candidate being scored in facts.
    // joinValues:         Wire values carrying the autojoin marker.
    // opcodesAllString:   Wire values whose payloads always begin with a string.
    // opcodesMostlyText:  Wire values whose payloads mostly carry message text.
    // opcodeCount:        Number of distinct wire values in the capture.
    //
    // Returns:  The proposal, or null when the posterior is below threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private InferenceProposal? ScoreCandidate(List<CandidateFacts> facts, int candidateIndex,
        List<OpcodeValue> joinValues, int opcodesAllString, int opcodesMostlyText, int opcodeCount)
    {
        CandidateFacts candidate = facts[candidateIndex];

        double nameLogLR = NameLogLR(facts, candidateIndex, opcodeCount);
        double joinLogLR = JoinLogLR(candidate, joinValues, opcodeCount);
        double stringLogLR = AllPassingLogLR(candidate.PayloadsWithString, candidate.Payloads,
            opcodesAllString, opcodeCount);
        double textLogLR = TextLogLR(candidate, opcodesMostlyText, opcodeCount);
        double totalLogLR = nameLogLR + joinLogLR + stringLogLR + textLogLR;
        double posterior = EvidenceScoring.CombinePosterior(totalLogLR, 1.0 / opcodeCount);

        string ledger =
            candidate.ToZoneWithName + "/" + candidate.ToZone + " C2Z with name, join "
            + (candidate.HasJoin ? "yes" : "no") + ", "
            + candidate.PayloadsWithString + "/" + candidate.Payloads + " with string, "
            + candidate.PayloadsWithText + "/" + candidate.Payloads + " with text | "
            + "name " + EvidenceScoring.ToDecibans(nameLogLR).ToString("F1") + " db, "
            + "join " + EvidenceScoring.ToDecibans(joinLogLR).ToString("F1") + " db, "
            + "string " + EvidenceScoring.ToDecibans(stringLogLR).ToString("F1") + " db, "
            + "text " + EvidenceScoring.ToDecibans(textLogLR).ToString("F1") + " db | "
            + "total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + " db, "
            + "prior 1/" + opcodeCount + ", posterior " + posterior.ToString("F3");

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferCommonMessage.ScoreCandidate: wireValue=" + candidate.WireValue + " " + ledger,
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
    // NameLogLR
    //
    // Log likelihood ratio from how many of the candidate's client-to-zone payloads begin
    // with the session's character name, against the other candidates all of whose
    // client-to-zone payloads do.  Wire values with no such payload cannot satisfy this,
    // so the other candidates are the whole background.
    //
    // facts:           Every candidate's facts.
    // candidateIndex:  Position of the candidate being scored in facts.
    // opcodeCount:     Number of distinct wire values in the capture.
    //
    // Returns:  Natural-log likelihood ratio from the name, clamped.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double NameLogLR(List<CandidateFacts> facts, int candidateIndex, int opcodeCount)
    {
        int othersAllName = 0;
        for (int other = 0; other < facts.Count; other++)
        {
            if (other != candidateIndex
                && facts[other].ToZone > 0
                && facts[other].ToZoneWithName == facts[other].ToZone)
            {
                othersAllName++;
            }
        }

        CandidateFacts candidate = facts[candidateIndex];
        return EvidenceScoring.PayloadPredicateLogLR(candidate.ToZoneWithName, candidate.ToZone,
            othersAllName, opcodeCount - 1, LeakProbability, MaxAbsLogLR);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // JoinLogLR
    //
    // Log likelihood ratio from the autojoin marker.  Presence scores -log(q), where q is
    // the fraction of the other wire values in the capture carrying the marker, smoothed by
    // JoinPseudoCount.  The marker is not expected outside OP_CommonMessage, so with no
    // other carrier q is very small and the score reaches JoinMaxAbsLogLR.  Absence scores
    // zero, since a session need not auto-join.
    //
    // candidate:    The candidate's facts.
    // joinValues:   Wire values carrying the autojoin marker.
    // opcodeCount:  Number of distinct wire values in the capture.
    //
    // Returns:  Natural-log likelihood ratio from the marker, zero or positive.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double JoinLogLR(CandidateFacts candidate, List<OpcodeValue> joinValues,
        int opcodeCount)
    {
        if (!candidate.HasJoin)
        {
            return 0.0;
        }

        int joinOthers = joinValues.Count - (joinValues.Contains(candidate.WireValue) ? 1 : 0);
        double q = (joinOthers + JoinPseudoCount) / ((opcodeCount - 1) + 2.0 * JoinPseudoCount);
        double logLR = -Math.Log(q);
        if (logLR > JoinMaxAbsLogLR)
        {
            logLR = JoinMaxAbsLogLR;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferCommonMessage.JoinLogLR: wireValue=" + candidate.WireValue
            + " others with marker=" + joinOthers + " logLR=" + logLR.ToString("F3"), LogLevel.Trace);
        return logLR;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AllPassingLogLR
    //
    // Log likelihood ratio from how many of the candidate's payloads pass a test, against
    // the other wire values all of whose payloads pass it.
    //
    // passing:            The candidate's payloads that pass.
    // payloads:           The candidate's payloads.
    // opcodesAllPassing:  Wire values, the candidate included, whose payloads always pass.
    // opcodeCount:        Number of distinct wire values in the capture.
    //
    // Returns:  Natural-log likelihood ratio from the test, clamped.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double AllPassingLogLR(int passing, int payloads, int opcodesAllPassing,
        int opcodeCount)
    {
        bool candidateAll = payloads > 0 && passing == payloads;
        int othersAll = opcodesAllPassing - (candidateAll ? 1 : 0);

        return EvidenceScoring.PayloadPredicateLogLR(passing, payloads, othersAll, opcodeCount - 1,
            LeakProbability, MaxAbsLogLR);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TextLogLR
    //
    // Log likelihood ratio from whether most of the candidate's payloads carry message text,
    // scored once for the wire value.  A wire value whose share of payloads with text
    // reaches TextMajorityShare passes; payloads without text below that point cost nothing.
    // The pass is compared with q, the smoothed fraction of the other wire values that also
    // pass:
    //
    //   pass:  -log(q)
    //   fail:  log(leak) - log(1 - q)
    //
    // Clamped to +/- MaxAbsLogLR.
    //
    // candidate:          The candidate's facts.
    // opcodesMostlyText:  Wire values, the candidate included, that pass.
    // opcodeCount:        Number of distinct wire values in the capture.
    //
    // Returns:  Natural-log likelihood ratio from message text, clamped.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double TextLogLR(CandidateFacts candidate, int opcodesMostlyText, int opcodeCount)
    {
        bool mostly = candidate.Payloads > 0
            && candidate.PayloadsWithText >= TextMajorityShare * candidate.Payloads;
        int othersMostly = opcodesMostlyText - (mostly ? 1 : 0);
        double q = (othersMostly + 1.0) / ((opcodeCount - 1) + 2.0);

        double logLR;
        if (mostly)
        {
            logLR = -Math.Log(q);
        }
        else
        {
            logLR = Math.Log(LeakProbability) - Math.Log(1.0 - q);
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
            "InferCommonMessage.TextLogLR: wireValue=" + candidate.WireValue
            + " text " + candidate.PayloadsWithText + "/" + candidate.Payloads
            + " others passing=" + othersMostly + " logLR=" + logLR.ToString("F3"), LogLevel.Trace);
        return logLR;
    }
}
