using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Inference.Core;
using Inference.Models;
using System.Collections.Generic;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// InferMovementHistory
//
// Identifies OP_MovementHistory.  MovementHistory is a client-to-zone position trail: an
// array of fixed-size records running to the end of the payload, plus a constant trailer.
// The record size is not assumed; it is computed from the field definitions the prior
// patch level stored for the record collection, so the size evidence tests exactly what
// was seen last patch and fails legibly if the record mutates.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferMovementHistory : IInferOpcodes
{
    private const string OpcodeNameConstant = "OP_MovementHistory";
    private const string RecordCollectionName = "OP_MovementHistory";
    private const double LeakProbability = 0.001;        // direction: off-channel per-packet allowance
    private const double MinimumAgreementRate = 0.75;    // discovery gate: peak must reach, background must not
    private const double FundamentalTolerance = 0.9;     // discovery: divisor preference fraction
    private const double StableThreshold = 0.8;          // silhouette: class consistency for a stable offset
    private const double DirectionMaxLogLR = 2.1;        // cap ~9 db: supporting evidence only
    private const int MaxShiftScanned = 256;             // discovery: widest structure discoverable
    private const double StrideLeakProbability = 0.001;  // residue: off-stride per-length allowance
    private const double ResidueMaxLogLR = 1.15;         // cap ~5 db: lengths corroborate, never decide
    private const double AgreeRateIfMatch = 0.9;         // confirmation: chance a true payload demonstrates its stride
    private const int ControlShiftCount = 8;             // confirmation: control shifts sampled per payload
    private const int MaxCapablePayloads = 16;           // confirmation: cost cap, largest payloads first
    private const double ConfirmationMaxLogLR = 5.75;    // cap ~25 db: the deciding evidence
    private const double ReportThreshold = 0.0;          // minimum posterior to emit a proposal

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
    // Scores every unaccepted wire value in the catalog against the MovementHistory
    // hypothesis.  For each candidate the largest payload in its bucket is scanned for a
    // fixed content stride; candidates where no stride stands out are skipped without
    // penalty.  Where discovery succeeds, two evidence features score: silhouette agreement
    // of the bucket's remaining payloads against the discovered stride's printability
    // template (the deciding feature, high cap) and traffic direction (supporting feature,
    // low cap).  The caps encode the valuation of each feature; no stored structure enters
    // the scoring.  The record size the current patch level stores for the opcode is
    // compared against the discovered stride only to annotate the proposal as matching or
    // differing.  Wire values whose posterior meets ReportThreshold are returned as
    // proposals with an itemized deciban ledger.
    //
    // catalog:  The session's cataloged packets.  Prepare must have run.
    //
    // Returns:  Proposals ordered by catalog rank, empty when the catalog is empty or
    //           nothing scores at threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<InferenceProposal> Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferMovementHistory.Infer: starting", LogLevel.Trace);
        List<InferenceProposal> proposals = new List<InferenceProposal>();

        List<OpcodeValue> ranked = catalog.OpcodesByCountDescending();
        if (ranked.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "InferMovementHistory.Infer: catalog empty", LogLevel.Warn);
            return proposals;
        }

        int opcodeCount = ranked.Count;
        ulong totalCount = catalog.TotalPacketCount();
        double priorOdds = 1.0 / opcodeCount;
        Dictionary<SoeConstants.StreamId, int> pureChannelOpcodes = catalog.PureChannelOpcodeCounts();
        Dictionary<int, int> pooledSizes = catalog.PooledSizeHistogram();

        // stored structure, for annotation only; never enters scoring
        uint storedRecordBytes;
        bool storedSizeKnown = TryComputeRecordByteSize(GlassContext.PatchRegistry,
            GlassContext.CurrentPatchLevel, RecordCollectionName, out storedRecordBytes);
        if (storedSizeKnown == false)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferMovementHistory.Infer: no stored record size, proposals will carry no"
                + " structure comparison", LogLevel.Warn);
        }

        for (int rank = 0; rank < ranked.Count; rank++)
        {
            OpcodeValue wireValue = ranked[rank];
            if (AcceptedOpcodes.Instance.IsAccepted(wireValue))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferMovementHistory.Infer: wireValue=" + wireValue + " already accepted as "
                    + AcceptedOpcodes.Instance.GetName(wireValue) + ", skipping", LogLevel.Trace);
                continue;
            }
            OpcodeStats? statsOrNull = catalog.StatsFor(wireValue);
            if (statsOrNull == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferMovementHistory.Infer: no stats for wireValue " + wireValue, LogLevel.Warn);
                continue;
            }
            OpcodeStats stats = statsOrNull.Value;

            // OP_MovementHistory rides Client-to-Zone exclusively; any traffic on another
            // channel disqualifies the candidate outright
            if (stats.ChannelCounts == null || stats.ChannelCounts.Count == 0)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferMovementHistory.Infer: no channel data for wireValue " + wireValue
                    + ", skipping", LogLevel.Warn);
                continue;
            }
            bool offChannel = false;
            foreach (KeyValuePair<SoeConstants.StreamId, uint> channelEntry in stats.ChannelCounts)
            {
                if ((channelEntry.Key != SoeConstants.StreamId.StreamClientToZone)
                    && (channelEntry.Value > 0))
                {
                    offChannel = true;
                    break;
                }
            }

            if (offChannel)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferMovementHistory.Infer: wireValue=" + wireValue
                    + " has traffic off Client-to-Zone, disqualified", LogLevel.Trace);
                continue;
            }
            CatalogedPacket? largestOrNull = catalog.PacketAt(stats.LargestPacketIndex);
            if (largestOrNull == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferMovementHistory.Infer: largest packet index " + stats.LargestPacketIndex
                    + " unresolved for wireValue " + wireValue + ", skipping", LogLevel.Error);
                continue;
            }
            CatalogedPacket largest = largestOrNull.Value;

            int discoveredStride;
            int shiftsScanned;
            if (EvidenceScoring.TryDiscoverStride(largest.Payload.AsReadOnlySpan(),
                MinimumAgreementRate, FundamentalTolerance, StableThreshold, MaxShiftScanned,
                out discoveredStride, out shiftsScanned) == false)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferMovementHistory.Infer: wireValue=" + wireValue
                    + " no stride discovered in largest payload ("
                    + largest.Payload.Length + " bytes), skipping", LogLevel.Trace);
                continue;
            }

            // confirmation: every payload long enough to demonstrate the stride votes
            List<CatalogedPacket> bucket = catalog.PacketsFor(wireValue, null, null, int.MaxValue);
            int capableCount;
            int confirmedCount;
            double confirmationLogLR = EvidenceScoring.StrideConfirmationLogLR(bucket,
                largest.PacketIndex, discoveredStride, MinimumAgreementRate, AgreeRateIfMatch,
                ControlShiftCount, MaxCapablePayloads, ConfirmationMaxLogLR,
                out capableCount, out confirmedCount);

            // the expected residue comes from the discovery payload itself
            int residue = largest.Payload.Length % discoveredStride;

            double residueLogLR = EvidenceScoring.SizeStrideLogLR(stats.SizeHistogram,
                discoveredStride, residue, pooledSizes, StrideLeakProbability, ResidueMaxLogLR);
            double directionLogLR = EvidenceScoring.DirectionLogLR(stats.ChannelCounts,
                SoeConstants.StreamId.StreamClientToZone, pureChannelOpcodes, opcodeCount,
                LeakProbability, DirectionMaxLogLR);

            double totalLogLR = confirmationLogLR + residueLogLR + directionLogLR;
            double posterior = EvidenceScoring.CombinePosterior(totalLogLR, priorOdds);

            DebugLog.Write(LogChannel.InferenceDebug,
                            "InferMovementHistory.Infer: rank " + rank + " wireValue=" + wireValue
                            + " stride=" + discoveredStride + " residue=" + residue
                            + " confirmed=" + confirmedCount + "/" + capableCount
                            + " conf=" + EvidenceScoring.ToDecibans(confirmationLogLR).ToString("F1")
                            + "db res=" + EvidenceScoring.ToDecibans(residueLogLR).ToString("F1")
                            + "db dir=" + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1")
                            + "db total=" + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1")
                            + "db posterior=" + posterior.ToString("F3"), LogLevel.Trace);

            if (posterior < ReportThreshold)
            {
                continue;
            }

            string structureNote;
            if (storedSizeKnown == false)
            {
                structureNote = "stored structure unavailable";
            }
            else if (storedRecordBytes == (uint)discoveredStride)
            {
                structureNote = "matches stored " + storedRecordBytes;
            }
            else
            {
                structureNote = "differs from stored " + storedRecordBytes + " - mutated?";
            }

            InferenceProposal proposal = new InferenceProposal();
            proposal.Label = Label;
            proposal.Opcode = new PatchOpcode(GlassContext.CurrentPatchLevel, wireValue);
            proposal.Confidence = posterior;
            proposal.Evidence =
                stats.Count + "/" + totalCount + " messages, "
                + stats.DistinctSizeCount + " sizes, min " + stats.MinSize
                + " | stride " + discoveredStride + ", confirmed " + confirmedCount + "/"
                + capableCount + " capable, residue " + residue
                + ", " + structureNote
                + " | conf " + EvidenceScoring.ToDecibans(confirmationLogLR).ToString("F1")
                + " db, res " + EvidenceScoring.ToDecibans(residueLogLR).ToString("F1")
                + " db, dir " + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1")
                + " db | total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1")
                + " db, prior 1/" + opcodeCount
                + ", posterior " + posterior.ToString("F3");
            proposals.Add(proposal);

        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferMovementHistory.Infer: " + proposals.Count + " proposals", LogLevel.Trace);
        return proposals;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TryComputeRecordByteSize
    //
    // Computes the byte size of one record of the named collection from the field
    // definitions stored in the given patch level.  The size is the largest field extent
    // (BitOffset plus the field's bit length, where a blob field's length is its
    // BlobByteCount) rounded up to whole bytes.
    //
    // The computation is only valid for a fixed-size record, so any field that makes the
    // record variable — a string encoding, a presence predicate, a nested gate, or an
    // offset relative to another slot — fails the computation rather than producing a
    // wrong size.
    //
    // registry:        The patch registry holding the loaded patch level.
    // patchLevel:      The patch level whose stored definitions are read.
    // collectionName:  The record collection's name.
    // recordBytes:     Receives the record size in bytes on success; zero on failure.
    //
    // Returns:  True when the collection resolved and every field is fixed-size; false
    //           otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool TryComputeRecordByteSize(PatchRegistry registry, PatchLevel patchLevel,
        string collectionName, out uint recordBytes)
    {
        recordBytes = 0;

        CollectionHandle collectionHandle = registry.GetCollectionHandle(patchLevel, collectionName);
        if (collectionHandle.Exists == false)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferMovementHistory.TryComputeRecordByteSize: collection '" + collectionName
                + "' not found in patch " + patchLevel, LogLevel.Warn);
            return false;
        }

        FieldDefinition[]? fields = registry.GetFields(collectionHandle);
        if (fields == null || fields.Length == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferMovementHistory.TryComputeRecordByteSize: collection '" + collectionName
                + "' has no fields in patch " + patchLevel, LogLevel.Warn);
            return false;
        }

        uint maxExtentBits = 0;
        for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
        {
            FieldDefinition field = fields[fieldIndex];

            if (field.Gate.Exists)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferMovementHistory.TryComputeRecordByteSize: field '" + field.Name
                    + "' is a gate reference, record is not fixed-size", LogLevel.Warn);
                return false;
            }
            if (field.Predicate.Op != PredicateOp.None)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferMovementHistory.TryComputeRecordByteSize: field '" + field.Name
                    + "' is predicated, record is not fixed-size", LogLevel.Warn);
                return false;
            }
            if ((field.Encoding == FieldEncoding.StringNullTerminated)
                || (field.Encoding == FieldEncoding.StringLengthPrefixed))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferMovementHistory.TryComputeRecordByteSize: field '" + field.Name
                    + "' is string-encoded, record is not fixed-size", LogLevel.Warn);
                return false;
            }
            if (field.RelativeToSlot != null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferMovementHistory.TryComputeRecordByteSize: field '" + field.Name
                    + "' has a relative offset, extent cannot be computed", LogLevel.Warn);
                return false;
            }

            uint extentBits = field.BitOffset + field.BitLength + (field.BlobByteCount * 8);
            if (extentBits > maxExtentBits)
            {
                maxExtentBits = extentBits;
            }
        }

        if ((maxExtentBits % 8) != 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferMovementHistory.TryComputeRecordByteSize: extent " + maxExtentBits
                + " bits is not whole bytes, rounding up", LogLevel.Warn);
        }
        recordBytes = (maxExtentBits + 7) / 8;

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferMovementHistory.TryComputeRecordByteSize: collection '" + collectionName
            + "' record size " + recordBytes + " bytes from " + fields.Length + " fields",
            LogLevel.Trace);
        return true;
    }
}