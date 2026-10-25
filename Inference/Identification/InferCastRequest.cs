using Glass.Core;
using Glass.Core.Logging;
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
// InferCastRequest
//
// Identifies OP_CastRequest.  Payloads seen so far begin with a 32-bit spell gem number, a
// small integer, followed by a 32-bit spell id and then FillLength bytes of 0xFF.
//
// Only packets on zone sessions are examined.  Every application wire value with such
// packets is examined so that each background covers all zone traffic; wire values already
// accepted are kept in the backgrounds but not proposed.  Each candidate is scored on the
// gem range, the spell id, and the 0xFF fill, each as a per-packet pass rate.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferCastRequest : IInferOpcodes
{
    private const string OpcodeNameConstant = "OP_CastRequest";
    private const int GemOffset = 0;                       // offset of the 32-bit gem number
    private const uint GemLimit = 30;                      // gem number must be below this
    private const int SpellIdOffset = 4;                   // offset of the 32-bit spell id
    private const int FillOffset = 8;                      // offset of the 0xFF fill
    private const int FillLength = 10;                     // bytes of 0xFF in the fill
    private const double LeakProbability = 0.001;          // per-packet allowance for a failed test
    private const int ExpectedLength = 39;                 // current OP_CastRequest payload length
    private const double LengthLeakProbability = 0.01;     // per distinct length other than ExpectedLength
    private const double LengthDistancePenalty = 0.5;      // log-LR per byte away from ExpectedLength
    private const double MaxAbsLogLR = 12.0;               // clamp per feature, roughly +/- 52 decibans
    private const double ReportThreshold = 0.40;           // minimum posterior to emit a proposal

    ///////////////////////////////////////////////////////////////////////////////////////////
    // WireFacts
    //
    // One wire value's packets on zone sessions.  Packets counts them and ToZone how many
    // are client-to-zone; GemPass, SpellPass, and FillPass count how many pass the gem
    // range, spell id, and 0xFF fill tests.  Lengths holds the packet count per length.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class WireFacts
    {
        public OpcodeValue WireValue;
        public int Packets = 0;
        public int ToZone = 0;
        public int GemPass = 0;
        public int SpellPass = 0;
        public int FillPass = 0;
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
    // them, then scores each unaccepted wire value.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  Proposals ordered by posterior descending, empty when no wire value has
    //           packets on a zone session or nothing scores at threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<InferenceProposal> Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferCastRequest.Infer: starting", LogLevel.Trace);
        List<InferenceProposal> proposals = new List<InferenceProposal>();

        bool spellsLoaded = SpellCatalog.Instance.Count > 0;
        if (!spellsLoaded)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferCastRequest.Infer: spell catalog is empty, spell id test abstains", LogLevel.Warn);
        }

        List<WireFacts> facts = new List<WireFacts>();
        OpcodeValue[] known = catalog.KnownOpcodes();
        for (int i = 0; i < known.Length; i++)
        {
            if (SoeConstants.IsNetOpcode(known[i].Value))
            {
                continue;
            }
            WireFacts wire = ExamineWireValue(catalog, known[i]);
            if (wire.Packets > 0)
            {
                facts.Add(wire);
            }
        }

        if (facts.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferCastRequest.Infer: no packets on zone sessions, no proposals", LogLevel.Warn);
            return proposals;
        }

        int allToZone = 0;
        int allGem = 0;
        int allSpell = 0;
        int allFill = 0;
        for (int i = 0; i < facts.Count; i++)
        {
            if (facts[i].ToZone == facts[i].Packets)
            {
                allToZone++;
            }
            if (facts[i].GemPass == facts[i].Packets)
            {
                allGem++;
            }
            if (facts[i].SpellPass == facts[i].Packets)
            {
                allSpell++;
            }
            if (facts[i].FillPass == facts[i].Packets)
            {
                allFill++;
            }
        }

        Dictionary<int, int> pooledSizes = catalog.PooledSizeHistogram();
        for (int i = 0; i < facts.Count; i++)
        {
            if (AcceptedOpcodes.Instance.IsAccepted(facts[i].WireValue))
            {
                continue;
            }
            InferenceProposal? proposal = ScoreCandidate(facts[i], allToZone, allGem, allSpell,
                allFill, facts.Count, spellsLoaded, pooledSizes);
            if (proposal != null)
            {
                proposals.Add(proposal);
            }
        }


        proposals.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferCastRequest.Infer: " + facts.Count + " wire values examined, "
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
    // PassesGem
    //
    // Tests whether the little-endian 32-bit value at GemOffset is below GemLimit.
    //
    // payload:  The bytes to test.
    //
    // Returns:  True when the gem number is in range.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool PassesGem(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < GemOffset + 4)
        {
            return false;
        }
        return BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(GemOffset)) < GemLimit;
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
    // PassesFill
    //
    // Tests whether FillLength bytes starting at FillOffset are all 0xFF.
    //
    // payload:  The bytes to test.
    //
    // Returns:  True when the fill is present.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool PassesFill(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < FillOffset + FillLength)
        {
            return false;
        }
        for (int i = FillOffset; i < FillOffset + FillLength; i++)
        {
            if (payload[i] != 0xFF)
            {
                return false;
            }
        }
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ExamineWireValue
    //
    // Walks one wire value's packets once, keeping only packets on zone sessions, and
    // tallies the three tests.
    //
    // catalog:    The session's cataloged packets.
    // wireValue:  The wire value.
    //
    // Returns:  The wire value's facts.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static WireFacts ExamineWireValue(PacketCatalog catalog, OpcodeValue wireValue)
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
            if (bucket[i].Metadata.ToServer)
            {
                wire.ToZone++;
            }
            int seen;
            if (wire.Lengths.TryGetValue(payload.Length, out seen))
            {
                wire.Lengths[payload.Length] = seen + 1;
            }
            else
            {
                wire.Lengths[payload.Length] = 1;
            }

            if (PassesGem(payload))
            {
                wire.GemPass++;
            }
            if (PassesSpell(payload))
            {
                wire.SpellPass++;
            }
            if (PassesFill(payload))
            {
                wire.FillPass++;
            }
        }

        if (wire.Packets > 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferCastRequest.ExamineWireValue: wireValue=" + wireValue + " " + wire.Packets
                + " packets, gem " + wire.GemPass + ", spell " + wire.SpellPass
                + ", fill " + wire.FillPass, LogLevel.Trace);
        }
        return wire;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScoreCandidate
    //
    // Scores each of the direction, gem, spell id, and fill tests once for the wire value:
    // passing on every packet is compared with how many other examined wire values do the
    // same, and each failing packet is charged the leak.  Length is scored against
    // ExpectedLength.  Sums the five log likelihood ratios and
    // converts the total to a posterior with prior odds of one over the number of examined
    // wire values.  Builds a proposal with an itemized deciban ledger when the posterior
    // reaches ReportThreshold.
    //
    // candidate:      The candidate's facts.
    // allToZone:      Examined wire values whose every packet is client-to-zone.
    // allGem:         Examined wire values whose every packet passes the gem test.
    // allSpell:       Examined wire values whose every packet passes the spell id test.
    // allFill:        Examined wire values whose every packet passes the fill test.
    // examinedCount:  Number of examined wire values.
    // spellsLoaded:   Whether the spell catalog holds any spells.
    // pooledSizes:    Message count per length across the capture.
    //
    // Returns:  The proposal, or null when the posterior is below threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private InferenceProposal? ScoreCandidate(WireFacts candidate, int allToZone, int allGem,
        int allSpell, int allFill, int examinedCount, bool spellsLoaded,
        Dictionary<int, int> pooledSizes)
    {
        int others = examinedCount - 1;

        int othersGem = allGem - (candidate.GemPass == candidate.Packets ? 1 : 0);
        double gemLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.GemPass, candidate.Packets,
            othersGem, others, LeakProbability, MaxAbsLogLR);

        double spellLogLR = 0.0;
        if (spellsLoaded)
        {
            int othersSpell = allSpell - (candidate.SpellPass == candidate.Packets ? 1 : 0);
            spellLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.SpellPass, candidate.Packets,
                othersSpell, others, LeakProbability, MaxAbsLogLR);
        }

        int othersFill = allFill - (candidate.FillPass == candidate.Packets ? 1 : 0);
        double fillLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.FillPass, candidate.Packets,
            othersFill, others, LeakProbability, MaxAbsLogLR);

        int othersToZone = allToZone - (candidate.ToZone == candidate.Packets ? 1 : 0);
        double directionLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.ToZone, candidate.Packets,
            othersToZone, others, LeakProbability, MaxAbsLogLR);
        double lengthLogLR = EvidenceScoring.SizeRangeLogLR(candidate.Lengths, ExpectedLength,
            ExpectedLength, pooledSizes, LengthLeakProbability, LengthDistancePenalty, MaxAbsLogLR);

        double totalLogLR = directionLogLR + lengthLogLR + gemLogLR + spellLogLR + fillLogLR;


        double posterior = EvidenceScoring.CombinePosterior(totalLogLR, 1.0 / examinedCount);

        string ledger =
            +candidate.Packets + " packets, " + candidate.ToZone + " C2Z, gem " + candidate.GemPass
            + ", spell " + candidate.SpellPass + ", fill " + candidate.FillPass + " | "
            + "direction " + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1") + " db, "
            + "length " + EvidenceScoring.ToDecibans(lengthLogLR).ToString("F1") + " db, "
            + "gem " + EvidenceScoring.ToDecibans(gemLogLR).ToString("F1") + " db, "
            + "spell " + EvidenceScoring.ToDecibans(spellLogLR).ToString("F1") + " db, "
            + "fill " + EvidenceScoring.ToDecibans(fillLogLR).ToString("F1") + " db | "
            + "total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + " db, "
            + "prior 1/" + examinedCount + ", posterior " + posterior.ToString("F3");

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferCastRequest.ScoreCandidate: wireValue=" + candidate.WireValue + " " + ledger,
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
}
