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
// its own character.  The payload carries the character's maximum mana as a 32-bit value,
// equal to the character's stored MaxMana.
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
    private const double MaxManaMissPenalty = 12.0;         // log-LR charged when no tested packet carries max mana, scaled by the fail fraction
    private const double ReportThreshold = 0.40;           // minimum posterior to emit a proposal
    private const double SelfLeakProbability = 0.05;       // per-session allowance for an eligible session with no self report
    private const string ZoneEntryOpcodeName = "OP_ZoneEntry_Z2C";  // accepted name of the cutoff anchor
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // WireFacts
    //
    // One wire value's packets on zone sessions after their cutoff.  Packets counts them and
    // ToClient how many are zone-to-client.  CharacterPass counts how many begin with the
    // spawn id of a player character in the session's zone.  MaxManaTested counts the packets
    // whose character has a nonzero MaxMana, and MaxManaPass how many of those carry it.
    // BeforeCutoff counts packets skipped because they arrived at or before their session's
    // cutoff.  SelfSessions holds each session with a cutoff that received at least one
    // zone-to-client packet about its own character.  Lengths holds the packet count per
    // length.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class WireFacts
    {
        public OpcodeValue WireValue;
        public int Packets = 0;
        public int ToClient = 0;
        public int CharacterPass = 0;
        public int MaxManaTested = 0;
        public int MaxManaPass = 0;
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
    // builds the backgrounds for direction, maximum mana matches and self coverage over all of
    // them, and scores each wire value not already accepted.  The maximum mana background
    // counts the wire values with at least one matching packet.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  The result, with proposals ordered by posterior descending; no proposals
    //           when OP_ZoneEntry_Z2C is not accepted, no wire value has packets on a zone
    //           session, or nothing scores at threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IdentificationResult Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferManaUpdate.Infer: starting", defaultLoglevel);
        IdentificationResult result = IdentificationResult.ForOpcode(OpcodeNameConstant);

        OpcodeValue zoneEntryWireValue = AcceptedOpcodes.Instance.WireValueFor(ZoneEntryOpcodeName);
        if (!zoneEntryWireValue.Exists)
        {
            DebugLog.Write(LogChannel.Inference,
                "InferManaUpdate.Infer: " + ZoneEntryOpcodeName
                + " not accepted this session, no proposals", LogLevel.Warn);
            result.Messages.Add(OpcodeNameConstant + " skipped: accept " + ZoneEntryOpcodeName
                + " first, then run identification again");
            return result;
        }

        Dictionary<Session, uint> cutoffs = BuildSelfZoneEntryCutoffs(catalog, zoneEntryWireValue);
        LogCharacterMana(cutoffs);
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
            return result;
        }

        int allToClient = 0;
        int anyMaxMana = 0;
        int allSelf = 0;
        for (int i = 0; i < facts.Count; i++)
        {
            if (facts[i].ToClient == facts[i].Packets)
            {
                allToClient++;
            }
            if (facts[i].MaxManaPass > 0)
            {
                anyMaxMana++;
            }
            if (eligibleSessions > 0 && facts[i].SelfSessions.Count == eligibleSessions)
            {
                allSelf++;
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferManaUpdate.Infer: backgrounds over " + facts.Count + " wire values, all Z2C "
            + allToClient + ", any max mana match " + anyMaxMana + ", all " + eligibleSessions
            + " sessions self " + allSelf, defaultLoglevel);

        Dictionary<int, int> pooledSizes = catalog.PooledSizeHistogram();
        for (int i = 0; i < facts.Count; i++)
        {
            if (AcceptedOpcodes.Instance.IsAccepted(facts[i].WireValue))
            {
                continue;
            }
            InferenceProposal? proposal = ScoreCandidate(facts[i], allToClient, anyMaxMana, allSelf,
                eligibleSessions, facts.Count, pooledSizes);
            if (proposal != null)
            {
                result.Proposals.Add(proposal);
            }
        }

        result.Proposals.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferManaUpdate.Infer: " + facts.Count + " wire values examined, "
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
    // LogCharacterMana
    //
    // Logs, once per character, the character's worn mana bonus and its max mana as loaded
    // from the database.  Characters are taken from the sessions that have a cutoff.
    //
    // cutoffs:  The cutoff PacketIndex per session.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void LogCharacterMana(Dictionary<Session, uint> cutoffs)
    {
        HashSet<Character> logged = new HashSet<Character>(ReferenceEqualityComparer.Instance);

        foreach (Session session in cutoffs.Keys)
        {
            Character? character = session.Character;
            if (character == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug, "InferManaUpdate.LogCharacterMana: session "
                    + session.SessionId + " has no character", LogLevel.Warn);
                continue;
            }

            if (logged.Add(character) == false)
            {
                continue;
            }

            StatBonuses wornBonuses = character.SumWornBonuses();
            string maxManaText;
            if (character.MaxMana.HasValue)
            {
                maxManaText = character.MaxMana.Value.ToString();
            }
            else
            {
                maxManaText = "null";
            }

            DebugLog.Write(LogChannel.InferenceDebug, "InferManaUpdate.LogCharacterMana: '" + character.Name
                + "' worn mana " + wornBonuses.Mana + ", database max mana " + maxManaText, defaultLoglevel);
        }
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
                + " zoneId=" + entry.Key.ZoneId + " cutoff at message " + entry.Value, defaultLoglevel);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferManaUpdate.BuildSelfZoneEntryCutoffs: " + bucket.Count + " ZoneEntry packets, "
            + selfEntries + " name the session's character, " + noCharacter
            + " on sessions with no character, " + cutoffs.Count + " sessions with a cutoff",
            defaultLoglevel);
        return cutoffs;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PassesMaxMana
    //
    // Tests whether any little-endian 32-bit value in the payload equals the character's
    // stored MaxMana.
    //
    // payload:    The bytes to test.
    // character:  The character the update concerns; its MaxMana must have a nonzero value.
    //
    // Returns:  True when a payload value equals the stored MaxMana.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool PassesMaxMana(ReadOnlySpan<byte> payload, Character character)
    {
        long expectedMaxMana = character.MaxMana!.Value;

        for (int offset = 0; offset + 4 <= payload.Length; offset++)
        {
            long value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset));
            if (value == expectedMaxMana)
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
    // tallies direction, length, and the character and maximum mana tests.  A packet whose
    // spawn id is not a character is not tested further.  The maximum mana test is applied
    // only to packets whose character has a nonzero MaxMana, and each packet that fails it
    // is logged.  A zone-to-client packet whose character is the session's own character
    // adds that session to SelfSessions when the session has a cutoff.  For wire value
    // 0xAC0B only, every packet that leaves the loop without being tested is logged with
    // the reason.
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

        // temporary diagnostic for 0xAC0B
        bool diagnose = wireValue.Value == 0xAC0B;

        List<CatalogedPacket> bucket = catalog.PacketsFor(wireValue, null, null, int.MaxValue);
        for (int i = 0; i < bucket.Count; i++)
        {
            if (!IsZonePacket(bucket[i]))
            {
                if (diagnose)
                {
                    DebugLog.Write(LogChannel.InferenceDebug, "InferManaUpdate.ExamineWireValue: wireValue="
                        + wireValue + " packet " + bucket[i].PacketIndex + " not on a zone session",
                        defaultLoglevel);
                }
                continue;
            }

            Session session = bucket[i].Metadata.Session;

            uint cutoff;
            if (cutoffs.TryGetValue(session, out cutoff) && bucket[i].PacketIndex <= cutoff)
            {
                if (diagnose)
                {
                    DebugLog.Write(LogChannel.InferenceDebug, "InferManaUpdate.ExamineWireValue: wireValue="
                        + wireValue + " packet " + bucket[i].PacketIndex + " session " + session.SessionId
                        + " at or before cutoff " + cutoff, defaultLoglevel);
                }
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
                if (diagnose)
                {
                    DebugLog.Write(LogChannel.InferenceDebug, "InferManaUpdate.ExamineWireValue: wireValue="
                        + wireValue + " packet " + bucket[i].PacketIndex + " session " + session.SessionId
                        + " is client to zone", defaultLoglevel);
                }
                continue;
            }
            wire.ToClient++;

            // mobs and other non-characters are not evidence either way
            Character? character = ResolveCharacter(payload, session.ZoneId);
            if (character == null)
            {
                if (diagnose)
                {
                    DebugLog.Write(LogChannel.InferenceDebug, "InferManaUpdate.ExamineWireValue: wireValue="
                        + wireValue + " packet " + bucket[i].PacketIndex + " session " + session.SessionId
                        + " zoneId=" + session.ZoneId + " spawn id 0x"
                        + BinaryPrimitives.ReadUInt16LittleEndian(payload).ToString("X4")
                        + " does not resolve to a character", defaultLoglevel);
                }
                continue;
            }
            wire.CharacterPass++;

            if (ReferenceEquals(character, session.Character) && cutoffs.ContainsKey(session))
            {
                wire.SelfSessions.Add(session);
            }

            // a zero max mana is not evidence
            if ((character.MaxMana.HasValue == false) || (character.MaxMana.Value == 0))
            {
                if (diagnose)
                {
                    string maxManaText = character.MaxMana.HasValue ? character.MaxMana.Value.ToString() : "null";
                    DebugLog.Write(LogChannel.InferenceDebug, "InferManaUpdate.ExamineWireValue: wireValue="
                        + wireValue + " packet " + bucket[i].PacketIndex + " session " + session.SessionId
                        + " character " + character.Name + " max mana " + maxManaText + ", not tested",
                        defaultLoglevel);
                }
                continue;
            }
            wire.MaxManaTested++;

            if (PassesMaxMana(payload, character))
            {
                wire.MaxManaPass++;
            }
            else
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferManaUpdate.ExamineWireValue: wireValue=" + wireValue + " packet "
                    + bucket[i].PacketIndex + " session " + session.SessionId + " character "
                    + character.Name + " fails max mana test, expected stored " + character.MaxMana.Value,
                    defaultLoglevel);
            }
        }

        if (wire.Packets > 0 && wire.MaxManaTested > 0 && wire.MaxManaPass == wire.MaxManaTested)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferManaUpdate.ExamineWireValue: wireValue=" + wireValue + " " + wire.Packets
                + " packets, " + wire.BeforeCutoff + " before cutoff, " + wire.ToClient
                + " Z2C, character " + wire.CharacterPass + ", max mana " + wire.MaxManaPass
                + " of " + wire.MaxManaTested + ", self sessions " + wire.SelfSessions.Count,
                defaultLoglevel);
        }
        return wire;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScoreCandidate
    //
    // Scores one wire value from direction, length, maximum mana matches and self coverage,
    // and builds a proposal when the posterior reaches the report threshold.  The maximum
    // mana feature abstains when no packet was tested.  Otherwise it is the support from
    // matching packets, scored against the share of other wire values with at least one
    // match and scaled by the fraction of tested packets that match, less MaxManaMissPenalty
    // scaled by the fraction of tested packets that do not match.
    //
    // candidate:         The wire value's facts.
    // allToClient:       Number of examined wire values whose packets are all zone-to-client.
    // anyMaxMana:        Number of examined wire values with at least one maximum mana match.
    // allSelf:           Number of examined wire values seen as self reports on every
    //                    eligible session.
    // eligibleSessions:  Number of sessions eligible for self reports.
    // examinedCount:     Number of examined wire values.
    // pooledSizes:       Message count per length across the capture.
    //
    // Returns:  The proposal, or null when the posterior is below threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private InferenceProposal? ScoreCandidate(WireFacts candidate, int allToClient, int anyMaxMana,
        int allSelf, int eligibleSessions, int examinedCount, Dictionary<int, int> pooledSizes)
    {
        int others = examinedCount - 1;

        int othersToClient = allToClient - (candidate.ToClient == candidate.Packets ? 1 : 0);
        double directionLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.ToClient,
            candidate.Packets, othersToClient, others, LeakProbability, MaxAbsLogLR);

        double lengthLogLR = EvidenceScoring.SizeRangeLogLR(candidate.Lengths, ExpectedLength,
            ExpectedLength, pooledSizes, LengthLeakProbability, LengthDistancePenalty, MaxAbsLogLR);

        double maxManaLogLR = 0.0;
        if (candidate.MaxManaTested == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferManaUpdate.ScoreCandidate: wireValue=" + candidate.WireValue
                + " no packets tested, max mana abstains", defaultLoglevel);
        }
        else
        {
            double passFraction = (double)candidate.MaxManaPass / candidate.MaxManaTested;
            double failFraction = 1.0 - passFraction;

            double supportLogLR = 0.0;
            if (candidate.MaxManaPass > 0)
            {
                int othersMaxMana = anyMaxMana - 1;
                supportLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.MaxManaPass,
                    candidate.MaxManaPass, othersMaxMana, others, LeakProbability, MaxAbsLogLR);
            }
            else
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferManaUpdate.ScoreCandidate: wireValue=" + candidate.WireValue + " none of "
                    + candidate.MaxManaTested + " tested packets carry max mana", defaultLoglevel);
            }

            maxManaLogLR = (passFraction * supportLogLR) - (failFraction * MaxManaMissPenalty);
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
                "InferManaUpdate.ScoreCandidate: wireValue=" + candidate.WireValue
                + " no eligible sessions, self coverage abstains", defaultLoglevel);
        }

        double totalLogLR = directionLogLR + lengthLogLR + maxManaLogLR + selfLogLR;
        double posterior = EvidenceScoring.CombinePosterior(totalLogLR, 1.0 / examinedCount);

        string ledger =
            candidate.Packets + " packets, " + candidate.ToClient + " Z2C, character "
            + candidate.CharacterPass + ", max mana " + candidate.MaxManaPass + " of "
            + candidate.MaxManaTested + " tested, self " + selfCount + " of " + eligibleSessions
            + " sessions | "
            + "direction " + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1") + " db, "
            + "length " + EvidenceScoring.ToDecibans(lengthLogLR).ToString("F1") + " db, "
            + "max mana " + EvidenceScoring.ToDecibans(maxManaLogLR).ToString("F1") + " db, "
            + "self " + EvidenceScoring.ToDecibans(selfLogLR).ToString("F1") + " db | "
            + "total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + " db, "
            + "prior 1/" + examinedCount + ", posterior " + posterior.ToString("F3");

        if (posterior < ReportThreshold)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferManaUpdate.ScoreCandidate: wireValue=" + candidate.WireValue
                + " below threshold, " + ledger, defaultLoglevel);
            return null;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferManaUpdate.ScoreCandidate: wireValue=" + candidate.WireValue + " " + ledger,
            defaultLoglevel);

        return InferenceProposal.ForOpcode(OpcodeNameConstant,
            new PatchOpcode(GlassContext.CurrentPatchLevel, candidate.WireValue), posterior, ledger);
    }
}