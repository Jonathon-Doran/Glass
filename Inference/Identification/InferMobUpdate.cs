using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Inference.Core;
using Inference.Models;
using System.Collections.Generic;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// InferMobUpdate
//
// Identifies OP_MobUpdate.  MobUpdate is a periodic mob-position snapshot from the zone
// server: inbound, fixed-shape, and the second-most-frequent mob opcode after NpcMoveUpdate.
// Candidates are drawn from observed opcodes and scored on frequency position and fixed-size
// shape; those meeting the reporting threshold are proposed.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferMobUpdate : IInferOpcodes
{
    private const string OpcodeNameConstant = "OP_MobUpdate";
    private const double LeakProbability = 0.001;          // DirectionLogLR: off-channel per-packet allowance
    private const double EscapeFloor = 0.001;              // SizeHistogramLogLR: minimum escape mass for unseen lengths
    private const double MaxAbsLogLR = 12.0;               // clamp per feature, roughly +/- 52 decibans
    private const double BaselineShare = 0.15;             // TODO: measured share from prior-patch capture
    private const double BaselineStrength = 20.0;          // pseudo-count trust in the one-capture baseline
    private const double BackgroundStrength = 2.0;         // breadth of the arbitrary-opcode share model
    private const int ExpectedFloor = 12;                  // TODO: smallest physically possible payload
    private const double FloorShortfallPenalty = 2.0;      // SizeFloorLogLR: log-LR per byte below floor
    private const double ReportThreshold = 0.5;            // minimum posterior to emit a proposal

    // Fixed 14-byte payload in all prior captures.  The count encodes how well-sampled the
    // baseline is (it drives the Good-Turing escape mass); a large single-length count says
    // "thoroughly observed, never any other size."
    private static readonly Dictionary<int, int> BaselineSizeHistogram = new Dictionary<int, int>
    {
        { 14, 7457 },
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
    // Scores every unaccepted wire value in the catalog against the MobUpdate hypothesis
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
        DebugLog.Write(LogChannel.InferenceDebug, "InferMobUpdate.Infer: starting", LogLevel.Trace);
        List<InferenceProposal> proposals = new List<InferenceProposal>();
        List<OpcodeValue> ranked = catalog.OpcodesByCountDescending();
        if (ranked.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "InferMobUpdate.Infer: catalog empty", LogLevel.Warn);
            return proposals;
        }
        int opcodeCount = ranked.Count;
        ulong totalCount = catalog.TotalPacketCount();
        double priorOdds = 1.0 / opcodeCount;
        double backgroundShare = 1.0 / opcodeCount;
        Dictionary<SoeConstants.StreamId, int> pureChannelOpcodes = catalog.PureChannelOpcodeCounts();
        Dictionary<int, int> pooledSizes = catalog.PooledSizeHistogram();
        for (int rank = 0; rank < ranked.Count; rank++)
        {
            OpcodeValue wireValue = ranked[rank];
            if (AcceptedOpcodes.Instance.IsAccepted(wireValue))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferMobUpdate.Infer: wireValue=" + wireValue + " already accepted as "
                    + AcceptedOpcodes.Instance.GetName(wireValue) + ", skipping", LogLevel.Info);
                continue;
            }
            OpcodeStats? statsOrNull = catalog.StatsFor(wireValue);
            if (statsOrNull == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferMobUpdate:  No stats for wirevalue " + wireValue, LogLevel.Warn);
                continue;
            }
            OpcodeStats stats = statsOrNull.Value;
            double directionLogLR = EvidenceScoring.DirectionLogLR(stats.ChannelCounts,
                SoeConstants.StreamId.StreamZoneToClient, pureChannelOpcodes, opcodeCount,
                LeakProbability, MaxAbsLogLR);
            double proportionLogLR = EvidenceScoring.ProportionLogLR(stats.Count, totalCount,
                BaselineShare, BaselineStrength, backgroundShare, BackgroundStrength,
                MaxAbsLogLR);
            double histogramLogLR = EvidenceScoring.SizeHistogramLogLR(stats.SizeHistogram,
                BaselineSizeHistogram, pooledSizes, EscapeFloor, MaxAbsLogLR);
            double floorLogLR = EvidenceScoring.SizeFloorLogLR(stats.MinSize, ExpectedFloor,
                FloorShortfallPenalty);
            double totalLogLR = directionLogLR + proportionLogLR + histogramLogLR + floorLogLR;
            double posterior = EvidenceScoring.CombinePosterior(totalLogLR, priorOdds);
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferMobUpdate.Infer: rank " + rank + " wireValue=" + wireValue
                + " dir=" + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1")
                + "db prop=" + EvidenceScoring.ToDecibans(proportionLogLR).ToString("F1")
                + "db hist=" + EvidenceScoring.ToDecibans(histogramLogLR).ToString("F1")
                + "db floor=" + EvidenceScoring.ToDecibans(floorLogLR).ToString("F1")
                + "db total=" + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1")
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
                + " db, prop " + EvidenceScoring.ToDecibans(proportionLogLR).ToString("F1")
                + " db, hist " + EvidenceScoring.ToDecibans(histogramLogLR).ToString("F1")
                + " db, floor " + EvidenceScoring.ToDecibans(floorLogLR).ToString("F1")
                + " db | total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1")
                + " db, prior 1/" + opcodeCount
                + ", posterior " + posterior.ToString("F3");
            proposals.Add(proposal);
        }
        DebugLog.Write(LogChannel.InferenceDebug,
            "InferMobUpdate.Infer: " + proposals.Count + " proposals", LogLevel.Info);
        return proposals;
    }
}