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
// InferCastResolution
//
// Identifies OP_Cast_Resolution.  Payloads seen so far are 20 bytes, zone-to-client, with
// the caster's new mana at offset 0, a 32-bit spell id at offset 8, and a 32-bit resolution
// code at offset 16 that is most often 0xFFFFFFFF.
//
// Only packets on zone sessions are examined.  Every application wire value with such
// packets is examined so that each background covers all zone traffic; wire values already
// accepted are kept in the backgrounds but not proposed.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferCastResolution : IInferOpcodes
{
    private const string OpcodeNameConstant = "OP_Cast_Resolution";
    private const int    SpellIdOffset = 8;                   // offset of the 32-bit spell id
    private const int    ResolutionOffset = 16;               // offset of the 32-bit resolution code
    private const uint   ResolutionCommon = 0xFFFFFFFF;      // most frequent resolution code
    private const double LeakProbability = 0.001;          // per-packet allowance for a failed test
    private const int    ExpectedLength = 20;                 // current OP_Cast_Resolution payload length
    private const double LengthLeakProbability = 0.01;     // per distinct length other than ExpectedLength
    private const double LengthDistancePenalty = 0.5;      // log-LR per byte away from ExpectedLength
    private const double MaxAbsLogLR = 12.0;               // clamp per feature, roughly +/- 52 decibans
    private const double ReportThreshold = 0.40;           // minimum posterior to emit a proposal
    private const string ManaUpdateOpcodeName = "OP_ManaUpdate";  // accepted name of the mana report
    private const double ManaLeakProbability = 0.1;        // per-packet allowance for a failed mana comparison
    private const double ManaMaxAbsLogLR = 4.0;            // clamp for the mana test, roughly +/- 17 decibans
 
    ///////////////////////////////////////////////////////////////////////////////////////////
    // ValueMatch
    //
    // The outcome of comparing the 32-bit values of two payloads.  Match means a nonzero value
    // appears in both; Mismatch means both contain nonzero values but none is shared; NoValue
    // means at least one payload contains no nonzero value, so nothing could be compared.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private enum ValueMatch
    {
        Match,
        Mismatch,
        NoValue
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // WireFacts
    //
    // One wire value's packets on zone sessions.  Packets counts them and ToClient how many
    // are zone-to-client; SpellPass counts how many carry a known spell id, and TrailerPass
    // how many carry ResolutionCommon.  ManaTested counts the zone-to-client packets that
    // could be compared with the next mana update their session received about its own
    // character, and ManaPass how many of those share a nonzero 32-bit value with it.
    // Lengths holds the packet count per length.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class WireFacts
    {
        public OpcodeValue WireValue;
        public int Packets = 0;
        public int ToClient = 0;
        public int SpellPass = 0;
        public int TrailerPass = 0;
        public int ManaTested = 0;
        public int ManaPass = 0;
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
    // them, then scores each unaccepted wire value.  The mana test uses the mana updates of
    // the accepted OP_ManaUpdate wire value and abstains when none is accepted.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  Proposals ordered by posterior descending, empty when no wire value has
    //           packets on a zone session or nothing scores at threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<InferenceProposal> Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferCastResolution.Infer: starting", LogLevel.Trace);
        List<InferenceProposal> proposals = new List<InferenceProposal>();

        bool spellsLoaded = SpellCatalog.Instance.Count > 0;
        if (!spellsLoaded)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferCastResolution.Infer: spell catalog is empty, spell id test abstains", LogLevel.Warn);
        }

        OpcodeValue manaWireValue = AcceptedOpcodes.Instance.WireValueFor(ManaUpdateOpcodeName);
        if (!manaWireValue.Exists)
        {
            DebugLog.Write(LogChannel.Inference,
                "InferCastResolution.Infer: " + ManaUpdateOpcodeName
                + " not accepted this session, no proposals", LogLevel.Warn);
            GlassContext.SignalBus.Publish(new SignalInferenceStatus(
                OpcodeNameConstant + " skipped: accept " + ManaUpdateOpcodeName
                + " first, then run identification again"));
            return proposals;
        }

        Dictionary<Session, List<CatalogedPacket>> selfManaUpdates = BuildSelfManaUpdates(catalog, manaWireValue);


        List<WireFacts> facts = new List<WireFacts>();
        OpcodeValue[] known = catalog.KnownOpcodes();
        for (int i = 0; i < known.Length; i++)
        {
            if (SoeConstants.IsNetOpcode(known[i].Value))
            {
                continue;
            }
            WireFacts wire = ExamineWireValue(catalog, known[i], selfManaUpdates);
            if (wire.Packets > 0)
            {
                facts.Add(wire);
            }
        }

        if (facts.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferCastResolution.Infer: no packets on zone sessions, no proposals", LogLevel.Warn);
            return proposals;
        }

        int allToClient = 0;
        int allSpell = 0;
        int allMana = 0;
        double trailerRateSum = 0.0;
        for (int i = 0; i < facts.Count; i++)
        {
            if (facts[i].ToClient == facts[i].Packets)
            {
                allToClient++;
            }
            if (facts[i].SpellPass == facts[i].Packets)
            {
                allSpell++;
            }
            if (facts[i].ManaTested > 0 && facts[i].ManaPass == facts[i].ManaTested)
            {
                allMana++;
            }
            trailerRateSum += (double)facts[i].TrailerPass / facts[i].Packets;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferCastResolution.Infer: backgrounds over " + facts.Count + " wire values, all Z2C "
            + allToClient + ", all spell " + allSpell + ", all mana " + allMana + ", trailer rate sum "
            + trailerRateSum.ToString("F3"), LogLevel.Trace);

        Dictionary<int, int> pooledSizes = catalog.PooledSizeHistogram();
        for (int i = 0; i < facts.Count; i++)
        {
            if (AcceptedOpcodes.Instance.IsAccepted(facts[i].WireValue))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferCastResolution.Infer: wireValue=" + facts[i].WireValue + " already accepted as "
                    + AcceptedOpcodes.Instance.GetName(facts[i].WireValue) + ", skipping", LogLevel.Trace);
                continue;
            }
            InferenceProposal? proposal = ScoreCandidate(facts[i], allToClient, allSpell, allMana,
                trailerRateSum, facts.Count, spellsLoaded, pooledSizes);
            if (proposal != null)
            {
                proposals.Add(proposal);
            }
        }

        proposals.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferCastResolution.Infer: " + facts.Count + " wire values examined, "
            + proposals.Count + " proposals", LogLevel.Info);
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
    // PassesSpell
    //
    // Tests whether the little-endian 32-bit value at SpellIdOffset is a spell in the spell
    // catalog.
    //
    // payload:  The bytes to test.
    //
    // Returns:  True when the spell id is in the catalog.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool PassesSpell(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SpellIdOffset + 4)
        {
            return false;
        }
        Id<SpellTag> spellId = (Id<SpellTag>)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(SpellIdOffset));
        return SpellCatalog.Instance.TryGet(spellId, out _);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PassesTrailer
    //
    // Tests whether the little-endian 32-bit value at ResolutionOffset is ResolutionCommon.
    //
    // payload:  The bytes to test.
    //
    // Returns:  True when the resolution code is ResolutionCommon.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool PassesTrailer(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < ResolutionOffset + 4)
        {
            return false;
        }
        return BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(ResolutionOffset)) == ResolutionCommon;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ExamineWireValue
    //
    // Walks one wire value's packets once, keeping only packets on zone sessions, and
    // tallies direction, length, and the spell id, trailer and mana tests.  For the mana
    // test, each zone-to-client packet is compared with the first mana update its session
    // received about its own character after the packet; a packet with no such update, or
    // whose comparison finds no nonzero value, is not tested.
    //
    // catalog:            The session's cataloged packets.
    // wireValue:          The wire value.
    // selfManaUpdates:    Per session, the mana updates about the session's own character,
    //                     in arrival order.
    //
    // Returns:  The wire value's facts.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static WireFacts ExamineWireValue(PacketCatalog catalog, OpcodeValue wireValue,
        Dictionary<Session, List<CatalogedPacket>> selfManaUpdates)
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

            if (PassesSpell(payload))
            {
                wire.SpellPass++;
            }
            if (PassesTrailer(payload))
            {
                wire.TrailerPass++;
            }

            if (bucket[i].Metadata.ToServer)
            {
                continue;
            }
            wire.ToClient++;

            List<CatalogedPacket>? updates;
            if (!selfManaUpdates.TryGetValue(bucket[i].Metadata.Session, out updates))
            {
                continue;
            }

            // first update whose PacketIndex is after the candidate's
            int low = 0;
            int high = updates.Count;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                if (updates[middle].PacketIndex <= bucket[i].PacketIndex)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            if (low >= updates.Count)
            {
                continue;
            }

            ValueMatch result = CompareNonzeroValues(payload, updates[low].Payload.AsReadOnlySpan());
            if (result == ValueMatch.NoValue)
            {
                continue;
            }

            wire.ManaTested++;
            if (result == ValueMatch.Match)
            {
                wire.ManaPass++;
            }
        }

        if (wire.Packets > 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferCastResolution.ExamineWireValue: wireValue=" + wireValue + " " + wire.Packets
                + " packets, " + wire.ToClient + " Z2C, spell " + wire.SpellPass
                + ", trailer " + wire.TrailerPass + ", mana " + wire.ManaPass + " of "
                + wire.ManaTested, LogLevel.Trace);
        }
        return wire;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BuildSelfManaUpdates
    //
    // Collects, for each zone session with a Character, the zone-to-client mana updates whose
    // leading 16-bit spawn id resolves in the session's zone to the session's own Character.
    // Each session's list is in arrival order.
    //
    // catalog:          The session's cataloged packets.
    // manaWireValue:    The accepted wire value of OP_ManaUpdate.
    //
    // Returns:  The self mana updates per session.  A session with none has no entry.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static Dictionary<Session, List<CatalogedPacket>> BuildSelfManaUpdates(PacketCatalog catalog,
        OpcodeValue manaWireValue)
    {
        Dictionary<Session, List<CatalogedPacket>> updates = new Dictionary<Session, List<CatalogedPacket>>();

        List<CatalogedPacket> bucket = catalog.PacketsFor(manaWireValue, null, null, int.MaxValue);
        uint selfCount = 0;
        uint otherCount = 0;
        uint unresolvedCount = 0;

        for (int i = 0; i < bucket.Count; i++)
        {
            if (!IsZonePacket(bucket[i]) || bucket[i].Metadata.ToServer)
            {
                continue;
            }

            Session session = bucket[i].Metadata.Session;
            if (session.Character == null)
            {
                continue;
            }

            ReadOnlySpan<byte> payload = bucket[i].Payload.AsReadOnlySpan();
            if (payload.Length < 2)
            {
                unresolvedCount++;
                continue;
            }

            SpawnId spawnId = (SpawnId)(uint)BinaryPrimitives.ReadUInt16LittleEndian(payload);
            Character? subject = CharacterRepository.Instance.GetBySpawnId(session.ZoneId, spawnId);
            if (subject == null)
            {
                unresolvedCount++;
                continue;
            }

            if (!ReferenceEquals(subject, session.Character))
            {
                otherCount++;
                continue;
            }

            List<CatalogedPacket>? list;
            if (!updates.TryGetValue(session, out list))
            {
                list = new List<CatalogedPacket>();
                updates[session] = list;
            }
            list.Add(bucket[i]);
            selfCount++;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferCastResolution.BuildSelfManaUpdates: " + bucket.Count + " mana updates, " + selfCount
            + " about the session's own character on " + updates.Count + " sessions, " + otherCount
            + " about other characters, " + unresolvedCount + " unresolved", LogLevel.Trace);
        return updates;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SharesNonzeroValue
    //
    // Tests whether two payloads contain the same nonzero little-endian 32-bit value, each at
    // any offset.  Zero is never treated as a match.
    //
    // first:   The first payload.
    // second:  The second payload.
    //
    // Returns:  True when some nonzero 32-bit value in first also appears in second.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool SharesNonzeroValue(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        for (int firstOffset = 0; firstOffset + 4 <= first.Length; firstOffset++)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(first.Slice(firstOffset));
            if (value == 0)
            {
                continue;
            }

            for (int secondOffset = 0; secondOffset + 4 <= second.Length; secondOffset++)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(second.Slice(secondOffset)) == value)
                {
                    return true;
                }
            }
        }
        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CompareNonzeroValues
    //
    // Compares the little-endian 32-bit values of two payloads, each read at every offset.
    // Zero is never treated as a match.
    //
    // first:   The first payload.
    // second:  The second payload.
    //
    // Returns:  Match when some nonzero value in first also appears in second; Mismatch when
    //           both payloads contain nonzero values but none is shared; NoValue when either
    //           payload contains no nonzero value.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static ValueMatch CompareNonzeroValues(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        bool secondHasNonzero = false;
        for (int secondOffset = 0; secondOffset + 4 <= second.Length; secondOffset++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(second.Slice(secondOffset)) != 0)
            {
                secondHasNonzero = true;
                break;
            }
        }

        if (!secondHasNonzero)
        {
            return ValueMatch.NoValue;
        }

        bool firstHasNonzero = false;
        for (int firstOffset = 0; firstOffset + 4 <= first.Length; firstOffset++)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(first.Slice(firstOffset));
            if (value == 0)
            {
                continue;
            }
            firstHasNonzero = true;

            for (int secondOffset = 0; secondOffset + 4 <= second.Length; secondOffset++)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(second.Slice(secondOffset)) == value)
                {
                    return ValueMatch.Match;
                }
            }
        }

        if (!firstHasNonzero)
        {
            return ValueMatch.NoValue;
        }

        return ValueMatch.Mismatch;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScoreCandidate
    //
    // Scores the direction and spell id tests once for the wire value: passing on every
    // packet is compared with how many other examined wire values do the same, and each
    // failing packet is charged the leak.  Length is scored against ExpectedLength.  The
    // trailer is additive only: when the candidate's trailer rate exceeds the smoothed mean
    // trailer rate of the other wire values, the log of the ratio is added; otherwise it
    // contributes nothing.  The mana test counts only tested packets, is compared with how
    // many other examined wire values pass it on every tested packet, charges each failing
    // packet the mana leak, is clamped at ManaMaxAbsLogLR, and abstains when no packet was
    // tested.  Sums the log likelihood ratios and converts the total to a posterior with prior
    // odds of one over the number of examined wire values.  Builds a proposal with an itemized
    // deciban ledger when the posterior reaches ReportThreshold.
    //
    // candidate:         The candidate's facts.
    // allToClient:       Examined wire values whose every packet is zone-to-client.
    // allSpell:          Examined wire values whose every packet passes the spell id test.
    // allMana:           Examined wire values with tested packets that all pass the mana test.
    // trailerRateSum:    Sum over examined wire values of each one's trailer rate.
    // examinedCount:     Number of examined wire values.
    // spellsLoaded:      Whether the spell catalog holds any spells.
    // pooledSizes:       Message count per length across the capture.
    //
    // Returns:  The proposal, or null when the posterior is below threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private InferenceProposal? ScoreCandidate(WireFacts candidate, int allToClient, int allSpell,
        int allMana, double trailerRateSum, int examinedCount, bool spellsLoaded,
        Dictionary<int, int> pooledSizes)
    {
        int others = examinedCount - 1;

        int othersToClient = allToClient - (candidate.ToClient == candidate.Packets ? 1 : 0);
        double directionLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.ToClient,
            candidate.Packets, othersToClient, others, LeakProbability, MaxAbsLogLR);

        double lengthLogLR = EvidenceScoring.SizeRangeLogLR(candidate.Lengths, ExpectedLength,
            ExpectedLength, pooledSizes, LengthLeakProbability, LengthDistancePenalty, MaxAbsLogLR);

        double spellLogLR = 0.0;
        if (spellsLoaded)
        {
            int othersSpell = allSpell - (candidate.SpellPass == candidate.Packets ? 1 : 0);
            spellLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.SpellPass, candidate.Packets,
                othersSpell, others, LeakProbability, MaxAbsLogLR);
        }

        double candidateTrailerRate = (double)candidate.TrailerPass / candidate.Packets;
        double backgroundTrailerRate = (trailerRateSum - candidateTrailerRate + 1.0) / (others + 2.0);
        double trailerLogLR = 0.0;
        if (candidateTrailerRate > backgroundTrailerRate)
        {
            trailerLogLR = Math.Log(candidateTrailerRate / backgroundTrailerRate);
            if (trailerLogLR > MaxAbsLogLR)
            {
                trailerLogLR = MaxAbsLogLR;
            }
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferCastResolution.ScoreCandidate: wireValue=" + candidate.WireValue
                + " trailer rate " + candidateTrailerRate.ToString("F3") + " above background "
                + backgroundTrailerRate.ToString("F3") + ", adding support", LogLevel.Trace);
        }
        else
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferCastResolution.ScoreCandidate: wireValue=" + candidate.WireValue
                + " trailer rate " + candidateTrailerRate.ToString("F3") + " not above background "
                + backgroundTrailerRate.ToString("F3") + ", no trailer evidence", LogLevel.Trace);
        }

        double manaLogLR = 0.0;
        if (candidate.ManaTested > 0)
        {
            int othersMana = allMana - (candidate.ManaPass == candidate.ManaTested ? 1 : 0);
            manaLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.ManaPass, candidate.ManaTested,
                othersMana, others, ManaLeakProbability, ManaMaxAbsLogLR);
        }
        else
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferCastResolution.ScoreCandidate: wireValue=" + candidate.WireValue
                + " no packets tested against a mana update, mana test abstains", LogLevel.Trace);
        }

        double totalLogLR = directionLogLR + lengthLogLR + spellLogLR + trailerLogLR + manaLogLR;
        double posterior = EvidenceScoring.CombinePosterior(totalLogLR, 1.0 / examinedCount);

        string ledger =
            candidate.Packets + " packets, " + candidate.ToClient + " Z2C, spell "
            + candidate.SpellPass + ", trailer " + candidate.TrailerPass + ", mana "
            + candidate.ManaPass + " of " + candidate.ManaTested + " | "
            + "direction " + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1") + " db, "
            + "length " + EvidenceScoring.ToDecibans(lengthLogLR).ToString("F1") + " db, "
            + "spell " + EvidenceScoring.ToDecibans(spellLogLR).ToString("F1") + " db, "
            + "trailer " + EvidenceScoring.ToDecibans(trailerLogLR).ToString("F1") + " db, "
            + "mana " + EvidenceScoring.ToDecibans(manaLogLR).ToString("F1") + " db | "
            + "total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + " db, "
            + "prior 1/" + examinedCount + ", posterior " + posterior.ToString("F3");

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferCastResolution.ScoreCandidate: wireValue=" + candidate.WireValue + " " + ledger,
            LogLevel.Trace);

        if (posterior < ReportThreshold)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferCastResolution.ScoreCandidate: wireValue=" + candidate.WireValue
                + " below threshold, no proposal", LogLevel.Trace);
            return null;
        }

        InferenceProposal proposal = new InferenceProposal();
        proposal.Label = Label;
        proposal.Opcode = new PatchOpcode(GlassContext.CurrentPatchLevel, candidate.WireValue);
        proposal.Confidence = posterior;
        proposal.Evidence = ledger;
        return proposal;
    }
}