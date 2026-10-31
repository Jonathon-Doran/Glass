using Glass.Core;
using Glass.Core.Logging;
using Glass.Core.Signals;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Client;
using Glass.Network.Protocol;
using Glass.World;
using Inference.Core;
using Inference.Models;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// InferManaUpdate
//
// Identifies OP_ManaUpdate.  Payloads seen so far are 10 bytes, zone-to-client, beginning
// with the 16-bit spawn id of a player character, and every session receives reports about
// its own character.  Across the reports one session receives about one character, some
// nonzero 32-bit value after the spawn id stays the same.
//
// Only packets on zone sessions after each session's last ZoneEntry naming its own
// character are examined.  Every application wire value with such packets is examined so
// that each background covers all zone traffic; wire values already accepted are kept in
// the backgrounds but not proposed.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferManaUpdate : IInferOpcodes
{
    private const string OpcodeNameConstant = "OP_ManaUpdate";
    private const double LeakProbability = 0.001;          // per-packet allowance for a failed test
    private const int ExpectedLength = 10;                 // current OP_ManaUpdate payload length
    private const double LengthLeakProbability = 0.01;     // per distinct length other than ExpectedLength
    private const double LengthDistancePenalty = 0.5;      // log-LR per byte away from ExpectedLength
    private const double MaxAbsLogLR = 12.0;               // clamp per feature, roughly +/- 52 decibans
    private const double ReportThreshold = 0.00;           // minimum posterior to emit a proposal
    private const double CharacterLeakProbability = 0.05;  // per-packet allowance for a report naming a non-character spawn
    private const double SelfLeakProbability = 0.05;       // per-session allowance for an eligible session with no self report
    private const double RepeatLeakProbability = 0.05;     // per-stream allowance for reports sharing no repeated value
    private const int SpawnIdLength = 2;                   // bytes of the leading spawn id, excluded from the repeated value test
    private const string ZoneEntryOpcodeName = "OP_ZoneEntry_Z2C";  // accepted name of the cutoff anchor

    ///////////////////////////////////////////////////////////////////////////////////////////
    // StreamFacts
    //
    // The reports one session received about one character.  Reports counts them.  Values
    // holds the nonzero 32-bit values, read after the spawn id, that every report so far has
    // contained; it is meaningful only once Reports is at least one.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class StreamFacts
    {
        public int Reports = 0;
        public HashSet<uint> Values = new HashSet<uint>();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // WireFacts
    //
    // One wire value's packets on zone sessions after their cutoff.  Packets counts them and
    // ToClient how many are zone-to-client.  CharacterPass counts how many begin with the
    // spawn id of a player character in the session's zone.  BeforeCutoff counts packets
    // skipped because they arrived at or before their session's cutoff.  SelfSessions holds
    // each session with a cutoff that received at least one zone-to-client packet about its
    // own character.  Streams holds, per session and per character the packets concern, the
    // facts of that stream of reports.  Lengths holds the packet count per length.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class WireFacts
    {
        public OpcodeValue WireValue;
        public int Packets = 0;
        public int ToClient = 0;
        public int CharacterPass = 0;
        public int BeforeCutoff = 0;
        public HashSet<Session> SelfSessions = new HashSet<Session>();
        public Dictionary<Session, Dictionary<Character, StreamFacts>> Streams =
            new Dictionary<Session, Dictionary<Character, StreamFacts>>();

        public Dictionary<int, int> Lengths = new Dictionary<int, int>();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RepeatCounts
    //
    // One wire value's results for the repeated value test.  Tested counts the streams with
    // at least two reports, and Passed how many of those still share a nonzero value.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class RepeatCounts
    {
        public int Tested = 0;
        public int Passed = 0;
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
    // Examines every application wire value, totals each test's results across all of
    // them, then scores each unaccepted wire value.  Sessions with a cutoff are the sessions
    // eligible for self reports; a wire value covers them when every one received a self
    // report.  No wire value is examined when OP_ZoneEntry_Z2C is not accepted.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  Proposals ordered by posterior descending, empty when OP_ZoneEntry_Z2C is not
    //           accepted, no wire value has packets on a zone session, or nothing scores at
    //           threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<InferenceProposal> Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferManaUpdate.Infer: starting", LogLevel.Trace);
        List<InferenceProposal> proposals = new List<InferenceProposal>();

        OpcodeValue zoneEntryWireValue = AcceptedOpcodes.Instance.WireValueFor(ZoneEntryOpcodeName);
        if (!zoneEntryWireValue.Exists)
        {
            DebugLog.Write(LogChannel.Inference,
                "InferManaUpdate.Infer: " + ZoneEntryOpcodeName
                + " not accepted this session, no proposals", LogLevel.Warn);
            GlassContext.SignalBus.Publish(new SignalInferenceStatus(
                OpcodeNameConstant + " skipped: accept " + ZoneEntryOpcodeName
                + " first, then run identification again"));
            return proposals;
        }

        Dictionary<Session, uint> cutoffs = BuildSelfZoneEntryCutoffs(catalog, zoneEntryWireValue);
        int eligibleSessions = cutoffs.Count;

        List<WireFacts> facts = new List<WireFacts>();
        OpcodeValue[] known = catalog.KnownOpcodes();

        for (int i = 0; i < known.Length; i++)
        {
            if (SoeConstants.IsNetOpcode(known[i].Value))
            {
                continue;
            }
            WireFacts wire = ExamineWireValue(catalog, known[i], cutoffs);
            if (wire.Packets > 0)
            {
                facts.Add(wire);
            }
        }

        if (facts.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferManaUpdate.Infer: no packets on zone sessions, no proposals", LogLevel.Warn);
            return proposals;
        }

        int allToClient = 0;
        int allCharacter = 0;
        int allSelf = 0;
        int allRepeat = 0;
        for (int i = 0; i < facts.Count; i++)
        {
            if (facts[i].ToClient == facts[i].Packets)
            {
                allToClient++;
            }
            if (facts[i].CharacterPass == facts[i].Packets)
            {
                allCharacter++;
            }
            if (eligibleSessions > 0 && facts[i].SelfSessions.Count == eligibleSessions)
            {
                allSelf++;
            }

            RepeatCounts repeat = CountRepeatStreams(facts[i]);
            if (repeat.Tested > 0 && repeat.Passed == repeat.Tested)
            {
                allRepeat++;
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferManaUpdate.Infer: backgrounds over " + facts.Count + " wire values, all Z2C "
            + allToClient + ", all character " + allCharacter + ", all " + eligibleSessions
            + " sessions self " + allSelf + ", all streams repeat " + allRepeat, LogLevel.Trace);

        Dictionary<int, int> pooledSizes = catalog.PooledSizeHistogram();
        for (int i = 0; i < facts.Count; i++)
        {
            if (AcceptedOpcodes.Instance.IsAccepted(facts[i].WireValue))
            {
                continue;
            }
            InferenceProposal? proposal = ScoreCandidate(facts[i], allToClient, allCharacter,
                allSelf, allRepeat, eligibleSessions, facts.Count, pooledSizes);
            if (proposal != null)
            {
                proposals.Add(proposal);
            }
        }

        proposals.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferManaUpdate.Infer: " + facts.Count + " wire values examined, "
            + proposals.Count + " proposals", LogLevel.Trace);
        return proposals;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // IsZonePacket
    //
    // Tests whether a packet belongs to a zone session.
    //
    // packet:  The packet.
    //
    // Returns:  True when the packet's Session is a zone session.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool IsZonePacket(CatalogedPacket packet)
    {
        Session session = packet.Metadata.Session;
        return session != null && session.ServerType == ServerType.Zone;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ResolveCharacter
    //
    // Reads the leading 16-bit spawn id of a payload and looks up the player character
    // placed at that spawn id in the zone.
    //
    // payload:  The bytes to read.
    // zoneId:   The zone of the packet's session.
    //
    // Returns:  The character, or null when the payload is shorter than 2 bytes or no
    //           character is placed at the spawn id.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static Character? ResolveCharacter(ReadOnlySpan<byte> payload, ZoneId zoneId)
    {
        if (payload.Length < 2)
        {
            return null;
        }
        SpawnId spawnId = (SpawnId)(uint)BinaryPrimitives.ReadUInt16LittleEndian(payload);
        return CharacterRepository.Instance.GetBySpawnId(zoneId, spawnId);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LeadingNameEquals
    //
    // Tests whether a payload begins with the given name as a null-terminated string.
    //
    // payload:  The bytes to test.
    // name:     The name to compare against.
    //
    // Returns:  True when the first name.Length bytes equal the name's characters and the
    //           byte after them is zero.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool LeadingNameEquals(ReadOnlySpan<byte> payload, string name)
    {
        if (payload.Length <= name.Length)
        {
            return false;
        }

        for (int i = 0; i < name.Length; i++)
        {
            if (payload[i] != (byte)name[i])
            {
                return false;
            }
        }

        return payload[name.Length] == 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BuildSelfZoneEntryCutoffs
    //
    // Finds, for each zone session, the PacketIndex of the last zone-to-client ZoneEntry whose
    // leading name is the session's own character.
    //
    // catalog:             The session's cataloged packets.
    // zoneEntryWireValue:  The wire value of OP_ZoneEntry_Z2C.
    //
    // Returns:  The cutoff PacketIndex per session.  A session with no character, or with no
    //           ZoneEntry naming its character, has no entry.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static Dictionary<Session, uint> BuildSelfZoneEntryCutoffs(PacketCatalog catalog,
        OpcodeValue zoneEntryWireValue)
    {
        Dictionary<Session, uint> cutoffs = new Dictionary<Session, uint>();

        if (!zoneEntryWireValue.Exists)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferManaUpdate.BuildSelfZoneEntryCutoffs: " + ZoneEntryOpcodeName
                + " not accepted this session, no cutoffs", LogLevel.Warn);
            return cutoffs;
        }

        List<CatalogedPacket> bucket = catalog.PacketsFor(zoneEntryWireValue, null, null, int.MaxValue);
        uint selfEntries = 0;
        uint noCharacter = 0;

        for (int i = 0; i < bucket.Count; i++)
        {
            if (!IsZonePacket(bucket[i]))
            {
                continue;
            }
            if (bucket[i].Metadata.ToServer)
            {
                continue;
            }

            Session session = bucket[i].Metadata.Session;
            Character? character = session.Character;
            if (character == null)
            {
                noCharacter++;
                continue;
            }

            ReadOnlySpan<byte> payload = bucket[i].Payload.AsReadOnlySpan();
            if (!LeadingNameEquals(payload, character.Name))
            {
                continue;
            }
            selfEntries++;

            uint existing;
            if (!cutoffs.TryGetValue(session, out existing) || bucket[i].PacketIndex > existing)
            {
                cutoffs[session] = bucket[i].PacketIndex;
            }
        }

        foreach (KeyValuePair<Session, uint> entry in cutoffs)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferManaUpdate.BuildSelfZoneEntryCutoffs: character " + entry.Key.Character!.Name
                + " zoneId=" + entry.Key.ZoneId + " cutoff at message " + entry.Value, LogLevel.Trace);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferManaUpdate.BuildSelfZoneEntryCutoffs: " + bucket.Count + " ZoneEntry packets, "
            + selfEntries + " name the session's character, " + noCharacter
            + " on sessions with no character, " + cutoffs.Count + " sessions with a cutoff",
            LogLevel.Trace);
        return cutoffs;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddStreamReport
    //
    // Records one report in a stream.  The report's nonzero little-endian 32-bit values are
    // read at every offset from SpawnIdLength on.  For the stream's first report they become
    // the stream's Values; for each later report Values keeps only the values this report
    // also contains.  Once Values is empty, later reports are counted without being read.
    //
    // stream:   The stream the report belongs to.
    // payload:  The report's bytes.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void AddStreamReport(StreamFacts stream, ReadOnlySpan<byte> payload)
    {
        bool firstReport = stream.Reports == 0;
        stream.Reports++;

        if (!firstReport && stream.Values.Count == 0)
        {
            return;
        }

        HashSet<uint> reportValues = new HashSet<uint>();
        for (int offset = SpawnIdLength; offset + 4 <= payload.Length; offset++)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset));
            if (value != 0)
            {
                reportValues.Add(value);
            }
        }

        if (firstReport)
        {
            stream.Values = reportValues;
            return;
        }

        stream.Values.IntersectWith(reportValues);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CountRepeatStreams
    //
    // Counts one wire value's streams for the repeated value test.  A stream with at least
    // two reports is tested, and it passes when its Values still holds a nonzero value.
    //
    // wire:  The wire value's facts.
    //
    // Returns:  The tested and passing stream counts.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static RepeatCounts CountRepeatStreams(WireFacts wire)
    {
        RepeatCounts counts = new RepeatCounts();

        foreach (Dictionary<Character, StreamFacts> sessionStreams in wire.Streams.Values)
        {
            foreach (StreamFacts stream in sessionStreams.Values)
            {
                if (stream.Reports < 2)
                {
                    continue;
                }

                counts.Tested++;
                if (stream.Values.Count > 0)
                {
                    counts.Passed++;
                }
            }
        }

        return counts;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ExamineWireValue
    //
    // Walks one wire value's packets once, keeping only packets on zone sessions, and
    // tallies direction, length and the character test.  A zone-to-client packet whose
    // character resolves is recorded in the stream of its session and character, and, when
    // that character is the session's own and the session has a cutoff, adds the session to
    // SelfSessions.
    //
    // Packets on a session with a cutoff are skipped entirely when their PacketIndex is at or
    // before that cutoff, and are counted in BeforeCutoff only.
    //
    // catalog:    The session's cataloged packets.
    // wireValue:  The wire value.
    // cutoffs:    The cutoff PacketIndex per session.
    //
    // Returns:  The wire value's facts.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static WireFacts ExamineWireValue(PacketCatalog catalog, OpcodeValue wireValue,
        Dictionary<Session, uint> cutoffs)
    {
        WireFacts wire = new WireFacts();
        wire.WireValue = wireValue;

        List<CatalogedPacket> bucket = catalog.PacketsFor(wireValue, null, null, int.MaxValue);
        for (int i = 0; i < bucket.Count; i++)
        {
            if (!IsZonePacket(bucket[i]))
            {
                continue;
            }

            Session session = bucket[i].Metadata.Session;

            uint cutoff;
            if (cutoffs.TryGetValue(session, out cutoff) && bucket[i].PacketIndex <= cutoff)
            {
                wire.BeforeCutoff++;
                continue;
            }

            ReadOnlySpan<byte> payload = bucket[i].Payload.AsReadOnlySpan();

            wire.Packets++;

            int seen;
            if (wire.Lengths.TryGetValue(payload.Length, out seen))
            {
                wire.Lengths[payload.Length] = seen + 1;
            }
            else
            {
                wire.Lengths[payload.Length] = 1;
            }

            if (bucket[i].Metadata.ToServer)
            {
                continue;
            }
            wire.ToClient++;

            Character? character = ResolveCharacter(payload, session.ZoneId);
            if (character == null)
            {
                continue;
            }
            wire.CharacterPass++;

            if (ReferenceEquals(character, session.Character) && cutoffs.ContainsKey(session))
            {
                wire.SelfSessions.Add(session);
            }

            Dictionary<Character, StreamFacts>? sessionStreams;
            if (!wire.Streams.TryGetValue(session, out sessionStreams))
            {
                sessionStreams = new Dictionary<Character, StreamFacts>(ReferenceEqualityComparer.Instance);
                wire.Streams[session] = sessionStreams;
            }

            StreamFacts? stream;
            if (!sessionStreams.TryGetValue(character, out stream))
            {
                stream = new StreamFacts();
                sessionStreams[character] = stream;
            }

            AddStreamReport(stream, payload);
        }

        if (wire.Packets > 0 && wire.CharacterPass == wire.Packets)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferManaUpdate.ExamineWireValue: wireValue=" + wireValue + " " + wire.Packets
                + " packets, " + wire.BeforeCutoff + " before cutoff, " + wire.ToClient
                + " Z2C, character " + wire.CharacterPass + ", self sessions " + wire.SelfSessions.Count
                + ", streams on " + wire.Streams.Count + " sessions", LogLevel.Trace);
        }
        return wire;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScoreCandidate
    //
    // Scores the direction and character tests once for the wire value: passing on every
    // packet is compared with how many other examined wire values do the same, and each
    // failing packet is charged the leak.  The self coverage test counts eligible sessions in
    // place of packets, charges each uncovered session the self leak, and abstains when there
    // are no eligible sessions.  The repeated value test counts tested streams in place of
    // packets, charges each failing stream the repeat leak, and abstains when no stream was
    // tested.  Length is scored against ExpectedLength.  Sums the log likelihood ratios and
    // converts the total to a posterior with prior odds of one over the number of examined
    // wire values.  Builds a proposal with an itemized deciban ledger when the posterior
    // reaches ReportThreshold.
    //
    // candidate:         The candidate's facts.
    // allToClient:       Examined wire values whose every packet is zone-to-client.
    // allCharacter:      Examined wire values whose every packet passes the character test.
    // allSelf:           Examined wire values whose self reports cover every eligible session.
    // allRepeat:         Examined wire values with tested streams that all pass the repeated
    //                    value test.
    // eligibleSessions:  Number of sessions eligible for self reports.
    // examinedCount:     Number of examined wire values.
    // pooledSizes:       Message count per length across the capture.
    //
    // Returns:  The proposal, or null when the posterior is below threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private InferenceProposal? ScoreCandidate(WireFacts candidate, int allToClient, int allCharacter,
        int allSelf, int allRepeat, int eligibleSessions, int examinedCount, Dictionary<int, int> pooledSizes)
    {
        int others = examinedCount - 1;

        int othersToClient = allToClient - (candidate.ToClient == candidate.Packets ? 1 : 0);
        double directionLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.ToClient,
            candidate.Packets, othersToClient, others, LeakProbability, MaxAbsLogLR);

        double lengthLogLR = EvidenceScoring.SizeRangeLogLR(candidate.Lengths, ExpectedLength,
            ExpectedLength, pooledSizes, LengthLeakProbability, LengthDistancePenalty, MaxAbsLogLR);

        int othersCharacter = allCharacter - (candidate.CharacterPass == candidate.Packets ? 1 : 0);
        double characterLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.CharacterPass,
            candidate.Packets, othersCharacter, others, CharacterLeakProbability, MaxAbsLogLR);

        int selfCount = candidate.SelfSessions.Count;
        double selfLogLR = 0.0;
        if (eligibleSessions > 0)
        {
            int othersSelf = allSelf - (selfCount == eligibleSessions ? 1 : 0);
            selfLogLR = EvidenceScoring.PayloadPredicateLogLR(selfCount, eligibleSessions,
                othersSelf, others, SelfLeakProbability, MaxAbsLogLR);
        }
        else
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferManaUpdate.ScoreCandidate: wireValue=" + candidate.WireValue
                + " no eligible sessions, self coverage abstains", LogLevel.Trace);
        }

        RepeatCounts repeat = CountRepeatStreams(candidate);
        double repeatLogLR = 0.0;
        if (repeat.Tested > 0)
        {
            int othersRepeat = allRepeat - (repeat.Passed == repeat.Tested ? 1 : 0);
            repeatLogLR = EvidenceScoring.PayloadPredicateLogLR(repeat.Passed, repeat.Tested,
                othersRepeat, others, RepeatLeakProbability, MaxAbsLogLR);
        }
        else
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferManaUpdate.ScoreCandidate: wireValue=" + candidate.WireValue
                + " no stream with two reports, repeated value test abstains", LogLevel.Trace);
        }

        double totalLogLR = directionLogLR + lengthLogLR + characterLogLR + selfLogLR + repeatLogLR;
        double posterior = EvidenceScoring.CombinePosterior(totalLogLR, 1.0 / examinedCount);

        string ledger =
            candidate.Packets + " packets, " + candidate.ToClient + " Z2C, character "
            + candidate.CharacterPass + ", self " + selfCount + " of " + eligibleSessions
            + " sessions, repeat " + repeat.Passed + " of " + repeat.Tested + " streams | "
            + "direction " + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1") + " db, "
            + "length " + EvidenceScoring.ToDecibans(lengthLogLR).ToString("F1") + " db, "
            + "character " + EvidenceScoring.ToDecibans(characterLogLR).ToString("F1") + " db, "
            + "self " + EvidenceScoring.ToDecibans(selfLogLR).ToString("F1") + " db, "
            + "repeat " + EvidenceScoring.ToDecibans(repeatLogLR).ToString("F1") + " db | "
            + "total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + " db, "
            + "prior 1/" + examinedCount + ", posterior " + posterior.ToString("F3");

        if (posterior < ReportThreshold)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferManaUpdate.ScoreCandidate: wireValue=" + candidate.WireValue
                + " below threshold, " + ledger, LogLevel.Trace);
            return null;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferManaUpdate.ScoreCandidate: wireValue=" + candidate.WireValue + " " + ledger,
            LogLevel.Trace);

        InferenceProposal proposal = new InferenceProposal();
        proposal.Label = Label;
        proposal.Opcode = new PatchOpcode(GlassContext.CurrentPatchLevel, candidate.WireValue);
        proposal.Confidence = posterior;
        proposal.Evidence = ledger;
        return proposal;
    }
}