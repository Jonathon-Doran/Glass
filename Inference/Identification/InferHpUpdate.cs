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
    private const double SelfLeakProbability = 0.05;       // per-session allowance for an eligible session with no self report
    private const string ZoneEntryOpcodeName = "OP_ZoneEntry_Z2C";  // accepted name of the cutoff anchor
    private const LogLevel defaultLoglevel = LogLevel.Trace;

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
    // Infer
    //
    // Requires OP_ZoneEntry_Z2C to be accepted this session, and builds each session's cutoff
    // from it; when it is not accepted, no proposals are made and a message saying so is added
    // to the result.  Examines every application wire value with packets on zone sessions,
    // builds the backgrounds for direction, maximum HP matches and self coverage over all of
    // them, and scores each wire value not already accepted.  The maximum HP background counts
    // the wire values with at least one matching packet.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  The result, with proposals ordered by posterior descending; no proposals
    //           when OP_ZoneEntry_Z2C is not accepted, no wire value has packets on a zone
    //           session, or nothing scores at threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IdentificationResult Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferHpUpdate.Infer: starting", defaultLoglevel);
        IdentificationResult result = IdentificationResult.ForOpcode(OpcodeNameConstant);

        OpcodeValue zoneEntryWireValue = AcceptedOpcodes.Instance.WireValueFor(ZoneEntryOpcodeName);
        if (!zoneEntryWireValue.Exists)
        {
            DebugLog.Write(LogChannel.Inference,
                "InferHpUpdate.Infer: " + ZoneEntryOpcodeName
                + " not accepted this session, no proposals", LogLevel.Warn);
            result.Messages.Add(OpcodeNameConstant + " skipped: accept " + ZoneEntryOpcodeName
                + " first, then run identification again");
            return result;
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
                "InferHpUpdate.Infer: no packets on zone sessions, no proposals", LogLevel.Warn);
            return result;
        }

        int allToClient = 0;
        int anyMaxHp = 0;
        int allSelf = 0;
        for (int i = 0; i < facts.Count; i++)
        {
            if (facts[i].ToClient == facts[i].Packets)
            {
                allToClient++;
            }
            if (facts[i].MaxHpPass > 0)
            {
                anyMaxHp++;
            }
            if (eligibleSessions > 0 && facts[i].SelfSessions.Count == eligibleSessions)
            {
                allSelf++;
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferHpUpdate.Infer: backgrounds over " + facts.Count + " wire values, all Z2C "
            + allToClient + ", any max HP match " + anyMaxHp + ", all " + eligibleSessions
            + " sessions self " + allSelf, defaultLoglevel);

        Dictionary<int, int> pooledSizes = catalog.PooledSizeHistogram();
        for (int i = 0; i < facts.Count; i++)
        {
            if (AcceptedOpcodes.Instance.IsAccepted(facts[i].WireValue))
            {
                continue;
            }
            InferenceProposal? proposal = ScoreCandidate(facts[i], allToClient, anyMaxHp, allSelf,
                eligibleSessions, facts.Count, pooledSizes);
            if (proposal != null)
            {
                result.Proposals.Add(proposal);
            }
        }

        result.Proposals.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferHpUpdate.Infer: " + facts.Count + " wire values examined, "
            + result.Proposals.Count + " proposals", defaultLoglevel);
        return result;
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
    // stored MaxHP.
    //
    // payload:    The bytes to test.
    // character:  The character the update concerns; its MaxHP must have a value.
    //
    // Returns:  True when a payload value equals the stored MaxHP.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool PassesMaxHp(ReadOnlySpan<byte> payload, Character character)
    {
        long expectedMaxHp = character.MaxHP!.Value;

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
    // to SelfSessions.  For wire value 0x787D only, each zone-to-client packet that fails the
    // character test or the maximum HP test is written to the log with its PacketIndex.
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

        if (wireValue.Value == 0x787d)
        {
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
                    string spawnIdText = "none (payload " + payload.Length + " bytes)";
                    if (payload.Length >= 2)
                    {
                        spawnIdText = "0x" + BinaryPrimitives.ReadUInt16LittleEndian(payload).ToString("X4");
                    }

                    DebugLog.Write(LogChannel.InferenceDebug,
                        "InferHpUpdate.ExamineWireValue: wireValue=" + wireValue + " packet "
                        + bucket[i].PacketIndex + " session " + session.SessionId + " zoneId=" + session.ZoneId
                        + " fails character test, spawn id " + spawnIdText, defaultLoglevel);
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

            if (PassesMaxHp(payload, character))
            {
                wire.MaxHpPass++;
            }
            else
            {
                if (correctOpcode)
                {
                    DebugLog.Write(LogChannel.InferenceDebug,
                        "InferHpUpdate.ExamineWireValue: wireValue=" + wireValue + " packet "
                        + bucket[i].PacketIndex + " session " + session.SessionId + " character "
                        + character.Name + " fails max HP test, expected stored " + character.MaxHP.Value,
                        defaultLoglevel);
                }
            }
        }

        if (wire.Packets > 0 && wire.MaxHpTested > 0 && wire.MaxHpPass == wire.MaxHpTested)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferHpUpdate.ExamineWireValue: wireValue=" + wireValue + " " + wire.Packets
                + " packets, " + wire.BeforeCutoff + " before cutoff, " + wire.ToClient
                + " Z2C, character " + wire.CharacterPass + ", max HP " + wire.MaxHpPass
                + " of " + wire.MaxHpTested + ", self sessions " + wire.SelfSessions.Count,
                defaultLoglevel);
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
                + " zoneId=" + entry.Key.ZoneId + " cutoff at message " + entry.Value, defaultLoglevel);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferHpUpdate.BuildSelfZoneEntryCutoffs: " + bucket.Count + " ZoneEntry packets, "
            + selfEntries + " name the session's character, " + noCharacter
            + " on sessions with no character, " + cutoffs.Count + " sessions with a cutoff",
            defaultLoglevel);
        return cutoffs;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScoreCandidate
    //
    // Scores one wire value from direction, length, maximum HP matches and self coverage, and
    // builds a proposal when the posterior reaches the report threshold.  The maximum HP
    // feature is computed from matching packets only: a wire value with at least one packet
    // carrying its character's stored MaxHP is scored against the share of other wire values
    // with at least one match, and packets that do not match are not charged.  A wire value
    // with no match abstains on that feature.
    //
    // candidate:         The wire value's facts.
    // allToClient:       Number of examined wire values whose packets are all zone-to-client.
    // anyMaxHp:          Number of examined wire values with at least one maximum HP match.
    // allSelf:           Number of examined wire values seen as self reports on every
    //                    eligible session.
    // eligibleSessions:  Number of sessions eligible for self reports.
    // examinedCount:     Number of examined wire values.
    // pooledSizes:       Message count per length across the capture.
    //
    // Returns:  The proposal, or null when the posterior is below threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private InferenceProposal? ScoreCandidate(WireFacts candidate, int allToClient, int anyMaxHp,
        int allSelf, int eligibleSessions, int examinedCount, Dictionary<int, int> pooledSizes)
    {
        int others = examinedCount - 1;

        int othersToClient = allToClient - (candidate.ToClient == candidate.Packets ? 1 : 0);
        double directionLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.ToClient,
            candidate.Packets, othersToClient, others, LeakProbability, MaxAbsLogLR);

        double lengthLogLR = EvidenceScoring.SizeRangeLogLR(candidate.Lengths, ExpectedLength,
            ExpectedLength, pooledSizes, LengthLeakProbability, LengthDistancePenalty, MaxAbsLogLR);

        double maxHpLogLR = 0.0;
        if (candidate.MaxHpPass > 0)
        {
            int othersMaxHp = anyMaxHp - 1;
            maxHpLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.MaxHpPass,
                candidate.MaxHpPass, othersMaxHp, others, LeakProbability, MaxAbsLogLR);
        }
        else
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferHpUpdate.ScoreCandidate: wireValue=" + candidate.WireValue
                + " no max HP matches, max HP abstains", defaultLoglevel);
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
                + " no eligible sessions, self coverage abstains", defaultLoglevel);
        }

        double totalLogLR = directionLogLR + lengthLogLR + maxHpLogLR + selfLogLR;
        double posterior = EvidenceScoring.CombinePosterior(totalLogLR, 1.0 / examinedCount);

        string ledger =
            candidate.Packets + " packets, " + candidate.ToClient + " Z2C, max HP "
            + candidate.MaxHpPass + " matches, self " + selfCount + " of " + eligibleSessions
            + " sessions | "
            + "direction " + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1") + " db, "
            + "length " + EvidenceScoring.ToDecibans(lengthLogLR).ToString("F1") + " db, "
            + "max HP " + EvidenceScoring.ToDecibans(maxHpLogLR).ToString("F1") + " db, "
            + "self " + EvidenceScoring.ToDecibans(selfLogLR).ToString("F1") + " db | "
            + "total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + " db, "
            + "prior 1/" + examinedCount + ", posterior " + posterior.ToString("F3");

        if (posterior < ReportThreshold)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferHpUpdate.ScoreCandidate: wireValue=" + candidate.WireValue
                + " below threshold, " + ledger, defaultLoglevel);
            return null;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferHpUpdate.ScoreCandidate: wireValue=" + candidate.WireValue + " " + ledger,
            defaultLoglevel);

        return InferenceProposal.ForOpcode(OpcodeNameConstant,
            new PatchOpcode(GlassContext.CurrentPatchLevel, candidate.WireValue), posterior, ledger);
    }
}
