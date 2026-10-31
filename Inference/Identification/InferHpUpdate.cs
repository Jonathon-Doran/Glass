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
// InferHpUpdate
//
// Identifies OP_HpUpdate.  Payloads seen so far are 18 bytes, zone-to-client, beginning
// with the 16-bit spawn id of a player character, and are sent only for the receiving
// character and the players grouped with it.  The payload carries the character's maximum
// HP as a 32-bit value: without the worn HP bonus when the packet concerns the receiving
// character, and with it when the packet concerns another character.
//
// Only packets on zone sessions are examined.  Every application wire value with such
// packets is examined so that each background covers all zone traffic; wire values already
// accepted are kept in the backgrounds but not proposed.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferHpUpdate : IInferOpcodes
{
    private const string OpcodeNameConstant = "OP_HpUpdate";
    private const double LeakProbability = 0.001;          // per-packet allowance for a failed test
    private const int ExpectedLength = 18;                 // current OP_HpUpdate payload length
    private const double LengthLeakProbability = 0.01;     // per distinct length other than ExpectedLength
    private const double LengthDistancePenalty = 0.5;      // log-LR per byte away from ExpectedLength
    private const double MaxAbsLogLR = 12.0;               // clamp per feature, roughly +/- 52 decibans
    private const double ReportThreshold = 0.40;           // minimum posterior to emit a proposal
    private const double CharacterLeakProbability = 0.05;  // per-packet allowance for an update naming a non-character spawn
    private const double SelfLeakProbability = 0.05;       // per-session allowance for an eligible session with no self report
    private const string ZoneEntryOpcodeName = "OP_ZoneEntry_Z2C";  // accepted name of the cutoff anchor

    // temp test
    private static bool correctOpcode = false;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // WireFacts
    //
    // One wire value's packets on zone sessions.  Packets counts them and ToClient how many
    // are zone-to-client.  CharacterPass counts how many begin with the spawn id of a player
    // character in the session's zone.  MaxHpTested counts the packets whose character has a
    // MaxHP value, and MaxHpPass how many of those carry the expected maximum HP.
    // BeforeCutoff counts packets skipped because they arrived at or before their session's
    // cutoff.  SelfSessions holds each session that received at least one zone-to-client
    // packet whose spawn id is the session's own character.  Lengths holds the packet count
    // per length.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class WireFacts
    {
        public OpcodeValue WireValue;
        public int Packets = 0;
        public int ToClient = 0;
        public int CharacterPass = 0;
        public int MaxHpTested = 0;
        public int MaxHpPass = 0;
        public int BeforeCutoff = 0;
        public HashSet<Session> SelfSessions = new HashSet<Session>();

        public Dictionary<int, int> Lengths = new Dictionary<int, int>();
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
    // report.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  Proposals ordered by posterior descending, empty when no wire value has
    //           packets on a zone session or nothing scores at threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<InferenceProposal> Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferHpUpdate.Infer: starting", LogLevel.Trace);
        List<InferenceProposal> proposals = new List<InferenceProposal>();

        OpcodeValue zoneEntryWireValue = AcceptedOpcodes.Instance.WireValueFor(ZoneEntryOpcodeName);
        if (!zoneEntryWireValue.Exists)
        {
            DebugLog.Write(LogChannel.Inference,
                "InferHpUpdate.Infer: " + ZoneEntryOpcodeName
                + " not accepted this session, no proposals", LogLevel.Warn);
            GlassContext.SignalBus.Publish(new SignalInferenceStatus(
                OpcodeNameConstant + " skipped: accept " + ZoneEntryOpcodeName
                + " first, then run identification again"));
            return proposals;
        }

        Dictionary<Session, uint> cutoffs = BuildSelfZoneEntryCutoffs(catalog, zoneEntryWireValue);
        int eligibleSessions = cutoffs.Count;
        Dictionary<Character, long> wornHpByCharacter =
            new Dictionary<Character, long>(ReferenceEqualityComparer.Instance);

        List<WireFacts> facts = new List<WireFacts>();
        OpcodeValue[] known = catalog.KnownOpcodes();

        for (int i = 0; i < known.Length; i++)
        {
            if (SoeConstants.IsNetOpcode(known[i].Value))
            {
                continue;
            }
            WireFacts wire = ExamineWireValue(catalog, known[i], cutoffs, wornHpByCharacter);
            if (wire.Packets > 0)
            {
                facts.Add(wire);
            }
        }

        if (facts.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferHpUpdate.Infer: no packets on zone sessions, no proposals", LogLevel.Warn);
            return proposals;
        }

        int allToClient = 0;
        int allCharacter = 0;
        int allMaxHp = 0;
        int allSelf = 0;
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
            if (facts[i].MaxHpTested > 0 && facts[i].MaxHpPass == facts[i].MaxHpTested)
            {
                allMaxHp++;
            }
            if (eligibleSessions > 0 && facts[i].SelfSessions.Count == eligibleSessions)
            {
                allSelf++;
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferHpUpdate.Infer: backgrounds over " + facts.Count + " wire values, all Z2C "
            + allToClient + ", all character " + allCharacter + ", all max HP " + allMaxHp
            + ", all " + eligibleSessions + " sessions self " + allSelf, LogLevel.Trace);

        Dictionary<int, int> pooledSizes = catalog.PooledSizeHistogram();
        for (int i = 0; i < facts.Count; i++)
        {
            if (AcceptedOpcodes.Instance.IsAccepted(facts[i].WireValue))
            {
                continue;
            }
            InferenceProposal? proposal = ScoreCandidate(facts[i], allToClient, allCharacter,
                allMaxHp, allSelf, eligibleSessions, facts.Count, pooledSizes);
            if (proposal != null)
            {
                proposals.Add(proposal);
            }
        }

        proposals.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferHpUpdate.Infer: " + facts.Count + " wire values examined, "
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
    // PassesMaxHp
    //
    // Tests whether any little-endian 32-bit value in the payload equals the character's
    // stored MaxHP plus its worn HP bonus.  The worn bonus is taken from the supplied
    // dictionary; when the character has no entry yet, its worn bonus is summed once and
    // stored there for later packets.
    //
    // payload:             The bytes to test.
    // character:           The character the update concerns; its MaxHP must have a value.
    // wornHpByCharacter:   Worn HP bonus per character, filled as characters are first seen.
    //
    // Returns:  True when a payload value equals MaxHP plus the worn HP bonus.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool PassesMaxHp(ReadOnlySpan<byte> payload, Character character,
        Dictionary<Character, long> wornHpByCharacter)
    {
        long wornHp;
        if (!wornHpByCharacter.TryGetValue(character, out wornHp))
        {
            wornHp = character.SumWornBonuses().HP;
            wornHpByCharacter[character] = wornHp;

            DebugLog.Write(LogChannel.InferenceDebug,
                "InferHpUpdate.PassesMaxHp: character " + character.Name + " worn HP bonus "
                + wornHp + ", stored MaxHP " + character.MaxHP!.Value, LogLevel.Trace);
        }

        long expectedMaxHp = character.MaxHP!.Value + wornHp;

        for (int offset = 0; offset + 4 <= payload.Length; offset++)
        {
            long value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset));
            if (value == expectedMaxHp)
            {
                return true;
            }
        }
        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ExamineWireValue
    //
    // Walks one wire value's packets once, keeping only packets on zone sessions, and
    // tallies direction, length, and the character and maximum HP tests.  The maximum HP
    // test is applied only to packets whose character resolves and has a MaxHP value.  A
    // zone-to-client packet whose character is the session's own character adds that session
    // to SelfSessions.
    //
    // Packets on a session with a cutoff are skipped entirely when their PacketIndex is at or
    // before that cutoff, and are counted in BeforeCutoff only.
    //
    // catalog:             The session's cataloged packets.
    // wireValue:           The wire value.
    // cutoffs:             The cutoff PacketIndex per session.
    // wornHpByCharacter:   Worn HP bonus per character, filled as characters are first seen.
    //
    // Returns:  The wire value's facts.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static WireFacts ExamineWireValue(PacketCatalog catalog, OpcodeValue wireValue,
        Dictionary<Session, uint> cutoffs, Dictionary<Character, long> wornHpByCharacter)
    {
        WireFacts wire = new WireFacts();
        wire.WireValue = wireValue;

        if (wireValue.Value == 0x80ca)
        {
            int x = 1;
            correctOpcode = true;
        }
        else
        {
            correctOpcode = false;
        }

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

            // skip packets that are going the wrong direction
            if (bucket[i].Metadata.ToServer)
            {
                continue;
            }
            wire.ToClient++;

            Character? character = ResolveCharacter(payload, session.ZoneId);
            if (character == null)
            {
                if (correctOpcode)
                {
                    int x = 1;
                }
                continue;
            }
            wire.CharacterPass++;

            if (ReferenceEquals(character, session.Character) && cutoffs.ContainsKey(session))
            {
                wire.SelfSessions.Add(session);
            }

            if (character.MaxHP.HasValue == false)
            {
                continue;
            }
            wire.MaxHpTested++;

            if (correctOpcode)
            {
                int x = 1;
            }

            if (PassesMaxHp(payload, character, wornHpByCharacter))
            {
                wire.MaxHpPass++;
            }
        }

        if (wire.Packets > 0 && wire.MaxHpTested > 0 && wire.MaxHpPass == wire.MaxHpTested)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferHpUpdate.ExamineWireValue: wireValue=" + wireValue + " " + wire.Packets
                + " packets, " + wire.BeforeCutoff + " before cutoff, " + wire.ToClient
                + " Z2C, character " + wire.CharacterPass + ", max HP " + wire.MaxHpPass
                + " of " + wire.MaxHpTested + ", self sessions " + wire.SelfSessions.Count,
                LogLevel.Trace);
        }
        return wire;
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
                "InferHpUpdate.BuildSelfZoneEntryCutoffs: " + ZoneEntryOpcodeName
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
                "InferHpUpdate.BuildSelfZoneEntryCutoffs: character " + entry.Key.Character!.Name
                + " zoneId=" + entry.Key.ZoneId + " cutoff at message " + entry.Value, LogLevel.Trace);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferHpUpdate.BuildSelfZoneEntryCutoffs: " + bucket.Count + " ZoneEntry packets, "
            + selfEntries + " name the session's character, " + noCharacter
            + " on sessions with no character, " + cutoffs.Count + " sessions with a cutoff",
            LogLevel.Trace);
        return cutoffs;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScoreCandidate
    //
    // Scores the direction, character and maximum HP tests once for the wire value: passing
    // on every packet is compared with how many other examined wire values do the same, and
    // each failing packet is charged the leak.  The maximum HP test counts only packets whose
    // character has a MaxHP value, and abstains when there are none.  The self coverage test
    // counts eligible sessions in place of packets: passing means every eligible session
    // received a self report, it is compared with how many other examined wire values cover
    // every eligible session, each uncovered session is charged the self leak, and the test
    // abstains when there are no eligible sessions.  Length is scored against ExpectedLength.
    // Sums the log likelihood ratios and converts the total to a posterior with prior odds of
    // one over the number of examined wire values.  Builds a proposal with an itemized deciban
    // ledger when the posterior reaches ReportThreshold.
    //
    // candidate:         The candidate's facts.
    // allToClient:       Examined wire values whose every packet is zone-to-client.
    // allCharacter:      Examined wire values whose every packet passes the character test.
    // allMaxHp:          Examined wire values with tested packets that all pass the maximum HP
    //                    test.
    // allSelf:           Examined wire values whose self reports cover every eligible session.
    // eligibleSessions:  Number of sessions eligible for self reports.
    // examinedCount:     Number of examined wire values.
    // pooledSizes:       Message count per length across the capture.
    //
    // Returns:  The proposal, or null when the posterior is below threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private InferenceProposal? ScoreCandidate(WireFacts candidate, int allToClient, int allCharacter,
        int allMaxHp, int allSelf, int eligibleSessions, int examinedCount, Dictionary<int, int> pooledSizes)
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

        double maxHpLogLR = 0.0;
        if (candidate.MaxHpTested > 0)
        {
            int othersMaxHp = allMaxHp - (candidate.MaxHpPass == candidate.MaxHpTested ? 1 : 0);
            maxHpLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.MaxHpPass,
                candidate.MaxHpTested, othersMaxHp, others, LeakProbability, MaxAbsLogLR);
        }

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
                "InferHpUpdate.ScoreCandidate: wireValue=" + candidate.WireValue
                + " no eligible sessions, self coverage abstains", LogLevel.Trace);
        }

        double totalLogLR = directionLogLR + lengthLogLR + characterLogLR + maxHpLogLR + selfLogLR;
        double posterior = EvidenceScoring.CombinePosterior(totalLogLR, 1.0 / examinedCount);

        string ledger =
            candidate.Packets + " packets, " + candidate.ToClient + " Z2C, character "
            + candidate.CharacterPass + ", max HP " + candidate.MaxHpPass + " of "
            + candidate.MaxHpTested + ", self " + selfCount + " of " + eligibleSessions
            + " sessions | "
            + "direction " + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1") + " db, "
            + "length " + EvidenceScoring.ToDecibans(lengthLogLR).ToString("F1") + " db, "
            + "character " + EvidenceScoring.ToDecibans(characterLogLR).ToString("F1") + " db, "
            + "max HP " + EvidenceScoring.ToDecibans(maxHpLogLR).ToString("F1") + " db, "
            + "self " + EvidenceScoring.ToDecibans(selfLogLR).ToString("F1") + " db | "
            + "total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + " db, "
            + "prior 1/" + examinedCount + ", posterior " + posterior.ToString("F3");

        if (posterior < ReportThreshold)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferHpUpdate.ScoreCandidate: wireValue=" + candidate.WireValue
                + " below threshold, " + ledger, LogLevel.Trace);
            return null;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferHpUpdate.ScoreCandidate: wireValue=" + candidate.WireValue + " " + ledger,
            LogLevel.Trace);

        InferenceProposal proposal = new InferenceProposal();
        proposal.Label = Label;
        proposal.Opcode = new PatchOpcode(GlassContext.CurrentPatchLevel, candidate.WireValue);
        proposal.Confidence = posterior;
        proposal.Evidence = ledger;
        return proposal;
    }
}
