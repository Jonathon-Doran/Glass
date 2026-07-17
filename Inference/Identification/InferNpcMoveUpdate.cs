using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Inference.Core;
using Inference.Models;
using System.Collections.Generic;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// InferNpcMoveUpdate
//
// Identifies OP_NpcMoveUpdate by frequency.  NpcMoveUpdate is the highest-volume
// server-to-client opcode in normal play, so the two most frequent wire values are proposed
// as candidates, most frequent first.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferNpcMoveUpdate : IInferOpcodes
{
    private const string OpcodeNameConstant = "OP_NpcMoveUpdate";
    private const double LeakProbability = 0.001;          // DirectionLogLR: off-channel per-packet allowance
    private const double EscapeFloor = 0.001;              // SizeHistogramLogLR: minimum escape mass for unseen lengths
    private const double MaxAbsLogLR = 12.0;               // clamp per feature, roughly +/- 52 decibans
    private const int CapturesObserved = 10;               // TODO: adequate captures in the stability record
    private const int CapturesAtRankOne = 9;               // TODO: of those, how many had NpcMoveUpdate at rank 0
    private const int AdequateMinimumMessages = 200;     // TODO: below this the rank feature abstains (~30 min)
    private const int ExpectedFloor = 12;                  // smallest physically possible payload
    private const double FloorShortfallPenalty = 2.0;      // SizeFloorLogLR: log-LR per byte below floor
    private const int EffectiveSampleCap = 100;            // fit: largest message count credited as independent
    private const double ReportThreshold = 0.1;            // minimum posterior to emit a proposal

    // Measured length-to-count table, merged from five captures (May-July 2026), 58364
    // messages.  No singleton lengths: the Good-Turing escape mass floors at EscapeFloor,
    // so lengths outside this support charge the full escape penalty.
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
    // using four evidence features (direction, traffic proportion, size histogram, size
    // floor), sums their log likelihood ratios, and converts the total to a posterior with
    // prior odds of one over the number of observed opcodes.  Wire values whose posterior
    // meets ReportThreshold are returned as proposals with an itemized deciban ledger.
    //
    // catalog:  The session's cataloged packets.  Prepare must have run.
    //
    // Returns:  Proposals ordered by catalog rank, empty when the catalog is empty or
    //           nothing scores at threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<InferenceProposal> Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferNpcMoveUpdate.Infer: starting", LogLevel.Trace);
        List<InferenceProposal> proposals = new List<InferenceProposal>();
        List<OpcodeValue> ranked = catalog.OpcodesByCountDescending();
        if (ranked.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "InferNpcMoveUpdate.Infer: catalog empty", LogLevel.Warn);
            return proposals;
        }
        int opcodeCount = ranked.Count;
        ulong totalCount = catalog.TotalPacketCount();
        double priorOdds = 1.0 / opcodeCount;
        Dictionary<SoeConstants.StreamId, int> pureChannelOpcodes = catalog.PureChannelOpcodeCounts();

        for (int rank = 0; rank < ranked.Count; rank++)
        {
            OpcodeValue wireValue = ranked[rank];
            if (AcceptedOpcodes.Instance.IsAccepted(wireValue))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferNpcMoveUpdate.Infer: wireValue=" + wireValue + " already accepted as "
                    + AcceptedOpcodes.Instance.GetName(wireValue) + ", skipping", LogLevel.Trace);
                continue;
            }
            OpcodeStats? statsOrNull = catalog.StatsFor(wireValue);
            if (statsOrNull == null)
            {
                continue;
            }
            OpcodeStats stats = statsOrNull.Value;
            double directionLogLR = EvidenceScoring.DirectionLogLR(stats.ChannelCounts,
                SoeConstants.StreamId.StreamZoneToClient, pureChannelOpcodes, opcodeCount,
                LeakProbability, MaxAbsLogLR);
            double rankLogLR;
            if (totalCount >= (ulong)AdequateMinimumMessages)
            {
                rankLogLR = EvidenceScoring.DominantRankLogLR(rank, CapturesAtRankOne,
                    CapturesObserved, opcodeCount, MaxAbsLogLR);
            }
            else
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferNpcMoveUpdate.Infer: capture below adequacy bar ("
                    + totalCount + " messages), rank feature abstains", LogLevel.Trace);
                rankLogLR = 0.0;
            }
            double histogramLogLR = EvidenceScoring.SizeFitLogLR(stats.SizeHistogram,
                            BaselineSizeHistogram, EscapeFloor, EffectiveSampleCap, MaxAbsLogLR);
            double floorLogLR = EvidenceScoring.SizeFloorLogLR(stats.MinSize, ExpectedFloor,
                FloorShortfallPenalty);
            double totalLogLR = directionLogLR + rankLogLR + histogramLogLR + floorLogLR;
            double posterior = EvidenceScoring.CombinePosterior(totalLogLR, priorOdds);
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferNpcMoveUpdate.Infer: rank " + rank + " wireValue=" + wireValue
                + " dir=" + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1")
                + "db rank=" + EvidenceScoring.ToDecibans(rankLogLR).ToString("F1")
                + "db hist=" + EvidenceScoring.ToDecibans(histogramLogLR).ToString("F1")
                + "db floor=" + EvidenceScoring.ToDecibans(floorLogLR).ToString("F1")
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
                + " | dir " + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1")
                + " db, rank " + EvidenceScoring.ToDecibans(rankLogLR).ToString("F1")
                + " db, hist " + EvidenceScoring.ToDecibans(histogramLogLR).ToString("F1")
                + " db, floor " + EvidenceScoring.ToDecibans(floorLogLR).ToString("F1")
                + " db | total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1")
                + " db, prior 1/" + opcodeCount
                + ", posterior " + posterior.ToString("F3");
            proposals.Add(proposal);
        }
        DebugLog.Write(LogChannel.InferenceDebug,
            "InferNpcMoveUpdate.Infer: " + proposals.Count + " proposals", LogLevel.Trace);
        return proposals;
    }
}