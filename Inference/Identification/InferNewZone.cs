using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Inference.Core;
using Inference.Models;
using System;
using System.Collections.Generic;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// InferNewZone
//
// Identifies OP_NewZone.  The zone server sends it to the client, and its payload begins
// with two consecutive null-terminated printable strings (the zone's short and long names)
// followed by a fixed block of zone parameters.  Every unaccepted wire value is scored on
// three features: direction, the two-string head, and the size floor.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferNewZone : IInferOpcodes
{
    private const string OpcodeNameConstant = "OP_NewZone";
    private const double LeakProbability = 0.001;          // per-item allowance for a failed predicate
    private const double MaxAbsLogLR = 12.0;               // clamp per feature, roughly +/- 52 decibans
    private const int RangeLow = 310;                      // SizeRangeLogLR: smallest NewZone length seen across captures
    private const int RangeHigh = 370;                     // SizeRangeLogLR: largest NewZone length seen across captures
    private const double RangeDistancePenalty = 0.05;       // SizeRangeLogLR: log-LR per byte outside the range
    private const double ReportThreshold = 0.40;            // minimum posterior to emit a proposal
    private static readonly Dictionary<int, int> BaselineSizeHistogram = new Dictionary<int, int>
    {
        { 310, 1 },
        { 324, 1 },
        { 325, 2 },
        { 328, 1 },
        { 333, 1 },
        { 335, 2 },
        { 339, 2 },
        { 346, 7 },
    };
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
    // BeginsWithTwoStrings
    //
    // Tests whether a payload starts with two consecutive null-terminated strings of
    // printable ASCII (bytes 0x20 to 0x7E), each at least one character long, with the
    // second terminator inside the payload.
    //
    // payload:  The bytes to test.
    //
    // Returns:  True when both strings are present at offset 0.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool BeginsWithTwoStrings(ReadOnlySpan<byte> payload)
    {
        int offset = 0;

        for (int stringIndex = 0; stringIndex < 2; stringIndex++)
        {
            int start = offset;

            while (offset < payload.Length && payload[offset] >= 0x20 && payload[offset] <= 0x7E)
            {
                offset++;
            }

            if (offset == start || offset >= payload.Length || payload[offset] != 0x00)
            {
                return false;
            }

            offset++;
        }

        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Increment
    //
    // Adds one to a wire value's count in a dictionary, inserting it at one when absent.
    //
    // counts:     The dictionary to update.
    // wireValue:  The key to increment.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void Increment(Dictionary<OpcodeValue, int> counts, OpcodeValue wireValue)
    {
        int seen;
        if (counts.TryGetValue(wireValue, out seen))
        {
            counts[wireValue] = seen + 1;
        }
        else
        {
            counts[wireValue] = 1;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Count
    //
    // Reads a wire value's count from a dictionary, treating an absent key as zero.
    //
    // counts:     The dictionary to read.
    // wireValue:  The key to read.
    //
    // Returns:  The stored count, or zero.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static int Count(Dictionary<OpcodeValue, int> counts, OpcodeValue wireValue)
    {
        int seen;
        if (counts.TryGetValue(wireValue, out seen))
        {
            return seen;
        }
        return 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AllPayloadsSatisfy
    //
    // Tests whether every payload of a wire value satisfied a predicate, from the per-opcode
    // satisfying and total counts.
    //
    // satisfying:  Satisfying payload count per wire value.
    // totals:      Total payload count per wire value.
    // wireValue:   The wire value to test.
    //
    // Returns:  True when the wire value has at least one payload and all of them satisfied.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool AllPayloadsSatisfy(Dictionary<OpcodeValue, int> satisfying,
        Dictionary<OpcodeValue, int> totals, OpcodeValue wireValue)
    {
        int total = Count(totals, wireValue);
        return total > 0 && Count(satisfying, wireValue) == total;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Infer
    //
    // Walks the catalog once to collect, per wire value, the payload total and how many
    // payloads begin with two strings, then scores every unaccepted wire value on direction,
    // two-string head, and size floor.  The log likelihood ratios are summed and converted to
    // a posterior with prior odds of one over the number of observed opcodes; wire values at
    // or above ReportThreshold are returned with an itemized deciban ledger.
    //
    // catalog:  The session's cataloged packets.  Prepare must have run.
    //
    // Returns:  Proposals ordered by catalog rank, empty when the catalog is empty or nothing
    //           scores at threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<InferenceProposal> Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferNewZone.Infer: starting", LogLevel.Trace);
        List<InferenceProposal> proposals = new List<InferenceProposal>();

        OpcodeValue dumpWireValue = new OpcodeValue(0xa923);
        catalog.LogSizeHistogram(dumpWireValue);

        List<OpcodeValue> ranked = catalog.OpcodesByCountDescending();
        if (ranked.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "InferNewZone.Infer: catalog empty", LogLevel.Warn);
            return proposals;
        }

        // pass 1: per wire value, payload total and two-string head count
        Dictionary<OpcodeValue, int> payloadsByOpcode = new Dictionary<OpcodeValue, int>();
        Dictionary<OpcodeValue, int> headByOpcode = new Dictionary<OpcodeValue, int>();

        uint totalCount = (uint)catalog.TotalPacketCount();

        for (uint i = 0; i < totalCount; i++)
        {
            CatalogedPacket? packetOrNull = catalog.PacketAt(i);
            if (packetOrNull == null)
            {
                DebugLog.Write(LogChannel.Inference,
                    "InferNewZone.Infer: null packet at index " + i, LogLevel.Error);
                continue;
            }
            CatalogedPacket packet = packetOrNull.Value;
            OpcodeValue wireValue = packet.Metadata.WireValue;

            Increment(payloadsByOpcode, wireValue);
            if (BeginsWithTwoStrings(packet.Payload.AsReadOnlySpan()))
            {
                Increment(headByOpcode, wireValue);
            }
        }

        // background count over all opcodes; the candidate's own contribution is removed
        // when it is scored
        int opcodesAllHead = 0;
        foreach (OpcodeValue wireValue in ranked)
        {
            if (AllPayloadsSatisfy(headByOpcode, payloadsByOpcode, wireValue))
            {
                opcodesAllHead++;
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferNewZone.Infer: " + ranked.Count + " opcodes, " + opcodesAllHead
            + " all two-string head", LogLevel.Trace);

        int opcodeCount = ranked.Count;
        double priorOdds = 1.0 / opcodeCount;
        Dictionary<SoeConstants.StreamId, int> pureChannelOpcodes = catalog.PureChannelOpcodeCounts();
        Dictionary<int, int> pooledSizes = catalog.PooledSizeHistogram();

        for (int rank = 0; rank < ranked.Count; rank++)
        {
            OpcodeValue wireValue = ranked[rank];
            if (AcceptedOpcodes.Instance.IsAccepted(wireValue))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferNewZone.Infer: wireValue=" + wireValue + " already accepted as "
                    + AcceptedOpcodes.Instance.GetName(wireValue) + ", skipping", LogLevel.Trace);
                continue;
            }
            OpcodeStats? statsOrNull = catalog.StatsFor(wireValue);
            if (statsOrNull == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferNewZone.Infer: no stats for wireValue " + wireValue, LogLevel.Warn);
                continue;
            }
            OpcodeStats stats = statsOrNull.Value;

            int payloads = Count(payloadsByOpcode, wireValue);
            int heads = Count(headByOpcode, wireValue);
            int othersAllHead = opcodesAllHead
                - (AllPayloadsSatisfy(headByOpcode, payloadsByOpcode, wireValue) ? 1 : 0);

            double directionLogLR = EvidenceScoring.DirectionLogLR(stats.ChannelCounts,
                SoeConstants.StreamId.StreamZoneToClient, pureChannelOpcodes, opcodeCount,
                LeakProbability, MaxAbsLogLR);
            double headLogLR = EvidenceScoring.PayloadPredicateLogLR(heads, payloads,
                othersAllHead, opcodeCount - 1, LeakProbability, MaxAbsLogLR);
            double histogramLogLR = EvidenceScoring.SizeRangeLogLR(stats.SizeHistogram, RangeLow,
                RangeHigh, pooledSizes, LeakProbability, RangeDistancePenalty, MaxAbsLogLR);
            double totalLogLR = directionLogLR + headLogLR + histogramLogLR;
            double posterior = EvidenceScoring.CombinePosterior(totalLogLR, priorOdds);

            DebugLog.Write(LogChannel.InferenceDebug,
                "InferNewZone.Infer: rank " + rank + " wireValue=" + wireValue + " "
                + "dir=" + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1") + "db "
                + "head=" + EvidenceScoring.ToDecibans(headLogLR).ToString("F1") + "db "
                + "range=" + EvidenceScoring.ToDecibans(histogramLogLR).ToString("F1") + "db "
                + "total=" + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + "db "
                + "posterior=" + posterior.ToString("F3"), LogLevel.Trace);

            if (posterior < ReportThreshold)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferNewZone.Infer: wireValue=" + wireValue + " below threshold, skipping",
                    LogLevel.Trace);
                continue;
            }

            InferenceProposal proposal = new InferenceProposal();
            proposal.Label = Label;
            proposal.Opcode = new PatchOpcode(GlassContext.CurrentPatchLevel, wireValue);
            proposal.Confidence = posterior;
            proposal.Evidence =
                stats.Count + "/" + catalog.TotalPacketCount() + " messages, "
                + stats.DistinctSizeCount + " sizes, min " + stats.MinSize + " | "
                + "dir " + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1") + " db, "
                + "head " + EvidenceScoring.ToDecibans(headLogLR).ToString("F1") + " db, "
                + "range " + EvidenceScoring.ToDecibans(histogramLogLR).ToString("F1") + " db | "
                + "total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + " db, "
                + "prior 1/" + opcodeCount + ", "
                + "posterior " + posterior.ToString("F3");
            proposals.Add(proposal);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferNewZone.Infer: " + proposals.Count + " proposals", LogLevel.Trace);
        return proposals;
    }
}