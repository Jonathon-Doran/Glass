using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Repositories;
using Glass.Network.Client;
using Glass.Network.Protocol;
using Inference.Core;
using Inference.Models;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// InferNpcMoveUpdate
//
// Identifies OP_NpcMoveUpdate.  Every unaccepted wire value is scored on direction, share of
// the capture, closeness of its lengths to the baseline length range, a minimum length, and
// how many of its packets begin with the spawn id of a known mob, read high byte first.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferNpcMoveUpdate : IInferOpcodes
{
    private const string OpcodeNameConstant = "OP_NpcMoveUpdate";
    private const double LeakProbability = 0.001;          // DirectionLogLR: off-channel per-packet allowance
    private const double MaxAbsLogLR = 12.0;               // clamp per feature, roughly +/- 52 decibans
    private const double ShareMinimum = 0.10;              // ShareThresholdLogLR: fraction of the capture required
    private const double ShareBudgetLogLR = 1.1513;        // ShareThresholdLogLR: 5 decibans at or above ShareMinimum
    private const int ExpectedFloor = 12;                  // smallest physically possible payload
    private const double FloorShortfallPenalty = 2.0;      // SizeFloorLogLR: log-LR per byte below floor
    private const double SizeMinimumShare = 0.10;          // SizeFitLogLR: abstain below this fraction of the capture
    private const double SizeBudgetLogLR = 1.1513;         // SizeFitLogLR: 5 decibans when every message is in range
    private const int SizeFalloffBytes = 8;                // SizeFitLogLR: weight reaches 0 this far outside the range
    private const double SpawnBudgetLogLR = 4.6052;        // KnownMobSpawnLogLR: 20 decibans when every packet names a known mob
    private const double ReportThreshold = 0.4;            // minimum posterior to emit a proposal

    // Measured length-to-count table, merged from five captures (May-July 2026), 58364
    // messages.  The smallest and largest lengths set the range the size test scores against.
    private static readonly Dictionary<int, int> BaselineSizeHistogram = new Dictionary<int, int>
    {
        { 15, 1703 },
        { 17, 4735 },
        { 18, 51915 },
        { 19, 11 },
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
    // Infer
    //
    // Scores every unaccepted wire value in the catalog against the NpcMoveUpdate hypothesis
    // using five evidence features (direction, traffic share, size histogram, size floor,
    // known mob spawn id), sums their log likelihood ratios, and converts the total to a
    // posterior with prior odds of one over the number of unaccepted opcodes.  Accepted wire
    // values are removed before scoring, so the prior counts only unaccepted values.  Wire
    // values whose posterior meets ReportThreshold are returned as proposals with an
    // itemized deciban ledger.
    //
    // catalog:  The session's cataloged packets.  Prepare must have run.
    //
    // Returns:  Proposals in descending message count, empty when the catalog is empty,
    //           every value is accepted, or nothing scores at threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<InferenceProposal> Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferNpcMoveUpdate.Infer: starting", LogLevel.Trace);
        List<InferenceProposal> proposals = new List<InferenceProposal>();
        List<OpcodeValue> allRanked = catalog.OpcodesByCountDescending();
        if (allRanked.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "InferNpcMoveUpdate.Infer: catalog empty", LogLevel.Warn);
            return proposals;
        }

        List<OpcodeValue> ranked = new List<OpcodeValue>();
        for (int i = 0; i < allRanked.Count; i++)
        {
            if (AcceptedOpcodes.Instance.IsAccepted(allRanked[i]))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferNpcMoveUpdate.Infer: wireValue=" + allRanked[i] + " already accepted as "
                    + AcceptedOpcodes.Instance.GetName(allRanked[i]) + ", removed from scoring", LogLevel.Trace);
                continue;
            }
            ranked.Add(allRanked[i]);
        }
        if (ranked.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferNpcMoveUpdate.Infer: every wire value is accepted, no candidates", LogLevel.Warn);
            return proposals;
        }

        int opcodeCount = ranked.Count;
        ulong totalCount = catalog.TotalPacketCount();
        double priorOdds = 1.0 / opcodeCount;
        Dictionary<SoeConstants.StreamId, int> pureChannelOpcodes = catalog.PureChannelOpcodeCounts();

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferNpcMoveUpdate.Infer: " + opcodeCount + " unaccepted of " + allRanked.Count
            + " wire values scored", LogLevel.Trace);

        for (int rank = 0; rank < ranked.Count; rank++)
        {
            OpcodeValue wireValue = ranked[rank];
            OpcodeStats? statsOrNull = catalog.StatsFor(wireValue);
            if (statsOrNull == null)
            {
                continue;
            }
            OpcodeStats stats = statsOrNull.Value;
            double directionLogLR = EvidenceScoring.DirectionLogLR(stats.ChannelCounts,
                SoeConstants.StreamId.StreamZoneToClient, pureChannelOpcodes, opcodeCount,
                LeakProbability, MaxAbsLogLR);
            double shareLogLR = EvidenceScoring.ShareThresholdLogLR(stats.Count, totalCount,
                ShareMinimum, ShareBudgetLogLR);
            double histogramLogLR = EvidenceScoring.SizeFitLogLR(stats.SizeHistogram,
                            BaselineSizeHistogram, totalCount, SizeMinimumShare,
                            SizeBudgetLogLR, SizeFalloffBytes);
            double floorLogLR = EvidenceScoring.SizeFloorLogLR(stats.MinSize, ExpectedFloor,
                FloorShortfallPenalty);
            double spawnLogLR = KnownMobSpawnLogLR(catalog, wireValue);
            double totalLogLR = directionLogLR + shareLogLR + histogramLogLR + floorLogLR + spawnLogLR;
            double posterior = EvidenceScoring.CombinePosterior(totalLogLR, priorOdds);
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferNpcMoveUpdate.Infer: rank " + rank + " wireValue=" + wireValue
                + " dir=" + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1")
                + "db share=" + EvidenceScoring.ToDecibans(shareLogLR).ToString("F1")
                + "db hist=" + EvidenceScoring.ToDecibans(histogramLogLR).ToString("F1")
                + "db floor=" + EvidenceScoring.ToDecibans(floorLogLR).ToString("F1")
                + "db spawn=" + EvidenceScoring.ToDecibans(spawnLogLR).ToString("F1")
                + "db posterior=" + posterior.ToString("F3"), LogLevel.Trace);
            if (posterior < ReportThreshold)
            {
                continue;
            }
            InferenceProposal proposal = new InferenceProposal();
            proposal.Label = Label;
            proposal.Opcode = new PatchOpcode(GlassContext.CurrentPatchLevel, wireValue);
            proposal.Confidence = posterior;
            proposal.Evidence =
                stats.Count + "/" + totalCount + " messages, "
                + stats.DistinctSizeCount + " sizes, min " + stats.MinSize
                + " | dir " + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1") + " db,"
                + " share " + EvidenceScoring.ToDecibans(shareLogLR).ToString("F1") + " db,"
                + " hist " + EvidenceScoring.ToDecibans(histogramLogLR).ToString("F1") + " db,"
                + " floor " + EvidenceScoring.ToDecibans(floorLogLR).ToString("F1") + " db,"
                + " spawn " + EvidenceScoring.ToDecibans(spawnLogLR).ToString("F1") + " db,"
                + " | total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + " db,"
                + " prior 1/" + opcodeCount + ", posterior " + posterior.ToString("F3");
            proposals.Add(proposal);
        }
        DebugLog.Write(LogChannel.InferenceDebug,
            "InferNpcMoveUpdate.Infer: " + proposals.Count + " proposals", LogLevel.Trace);
        return proposals;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // KnownMobSpawnLogLR
    //
    // Log likelihood ratio from how many of a wire value's zone-to-client packets begin with
    // the spawn id of a mob known in the packet's zone.  The first two payload bytes are read
    // high byte first.  The budget is divided evenly across the packets examined, and each
    // packet whose id is a known mob spawn earns its share, so the result runs from 0 to
    // SpawnBudgetLogLR regardless of packet count.  Packets shorter than two bytes count as
    // failing.  Packets on a session with no zone are not examined.
    //
    // catalog:    The session's cataloged packets.
    // wireValue:  The wire value whose packets are examined.
    //
    // Returns:  Natural-log likelihood ratio from 0 to SpawnBudgetLogLR.  Zero when no packet
    //           was examined.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double KnownMobSpawnLogLR(PacketCatalog catalog, OpcodeValue wireValue)
    {
        List<CatalogedPacket> bucket = catalog.PacketsFor(wireValue,
            SoeConstants.StreamId.StreamZoneToClient, null, int.MaxValue);

        uint examined = 0;
        uint matched = 0;
        uint noZone = 0;
        uint tooShort = 0;

        for (int i = 0; i < bucket.Count; i++)
        {
            Session session = bucket[i].Metadata.Session;
            if (session == null || !session.ZoneId.Exists)
            {
                noZone++;
                continue;
            }

            examined++;
            ReadOnlySpan<byte> payload = bucket[i].Payload.AsReadOnlySpan();
            if (payload.Length < 2)
            {
                tooShort++;
                continue;
            }

            ushort rawSpawnId = BinaryPrimitives.ReadUInt16BigEndian(payload);
            SpawnId spawnId = (SpawnId)(uint)rawSpawnId;
            if (MobRepository.Instance.TryGetBySpawnId(session.ZoneId, spawnId, out Spawn? spawn))
            {
                matched++;
            }
        }

        if (examined == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferNpcMoveUpdate.KnownMobSpawnLogLR: wireValue=" + wireValue + " no packets with a zone ("
                + noZone + " without), abstaining", LogLevel.Trace);
            return 0.0;
        }

        double fraction = (double)matched / examined;
        double logLR = SpawnBudgetLogLR * fraction;

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferNpcMoveUpdate.KnownMobSpawnLogLR: wireValue=" + wireValue + " examined " + examined
            + ", known mob " + matched + ", too short " + tooShort + ", without zone " + noZone
            + ", fraction " + fraction.ToString("F3") + ", logLR " + logLR.ToString("F3"),
            LogLevel.Trace);
        return logLR;
    }

}