using Glass.Core.Logging;
using System;
using System.Collections.Generic;
using static Glass.Network.Protocol.SoeConstants;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// EvidenceScoring
//
// Shared scoring primitives for opcode identification.  Each evidence function returns a
// natural-log likelihood ratio for one observation under a specific opcode hypothesis:
//
//   log LR = log P(observation | candidate IS the opcode) - log P(observation | is NOT)
//
// Handlers compute a log-LR per evidence piece, sum them (independent evidence combines by
// multiplying ratios, i.e. adding logs), and convert the total to a posterior probability
// with CombinePosterior.  Positive log-LR supports the hypothesis, negative refutes, zero
// is neutral.
///////////////////////////////////////////////////////////////////////////////////////////////
public static class EvidenceScoring
{
    ///////////////////////////////////////////////////////////////////////////////////////////
    // CombinePosterior
    //
    // Converts a summed log likelihood ratio to a posterior probability, given prior odds.
    //
    //   posterior odds = priorOdds * exp(totalLogLR)
    //   posterior probability = odds / (1 + odds)
    //
    // priorOdds is P(IS) / P(NOT) before evidence.  With priorOdds = 1 the posterior is
    // driven entirely by the evidence.  The result is clamped to [0, 1].
    //
    // totalLogLR:  Sum of per-evidence natural-log likelihood ratios.
    // priorOdds:   Prior odds in favor of the hypothesis before evidence.
    //
    // Returns:  Posterior probability in the range 0.0 to 1.0.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static double CombinePosterior(double totalLogLR, double priorOdds)
    {
        double posteriorOdds = priorOdds * Math.Exp(totalLogLR);
        double posterior = posteriorOdds / (1.0 + posteriorOdds);

        if (posterior < 0.0)
        {
            posterior = 0.0;
        }
        if (posterior > 1.0)
        {
            posterior = 1.0;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "EvidenceScoring.CombinePosterior: totalLogLR=" + totalLogLR.ToString("F3")
            + " posterior=" + posterior.ToString("F3"), LogLevel.Trace);
        return posterior;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SizeFloorLogLR
    //
    // Log likelihood ratio from where a candidate's minimum observed length falls relative to
    // the smallest length the opcode could physically take, for a hypothesis constrained by a
    // known minimum payload size.
    //
    // The floor is near-hard but not absolute: shrinking a packet below the space its required
    // fields occupy is very hard for the publisher, who moves fields far more often than
    // shrinks structures and enlarges far more readily than shrinks.  So a minimum below the
    // floor is scored as strongly implausible, decaying steeply with the shortfall; at or
    // above the floor there is no penalty from this piece.
    //
    //   observedMin >= floor:  log LR = 0                        no floor violation
    //   observedMin <  floor:  log LR = -(floor - observedMin) * shortfallPenalty
    //
    // The penalty grows linearly (in log space, so exponentially in likelihood) with how many
    // bytes short of the floor the observation falls, so a one-byte shortfall is mild and a
    // several-byte shortfall approaches disqualifying.
    //
    // observedMin:       Smallest observed length for the candidate.
    // floor:             Smallest length the opcode could physically take.
    // shortfallPenalty:  Log-LR penalty per byte below the floor.
    //
    // Returns:  Natural-log likelihood ratio from the size floor.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static double SizeFloorLogLR(int observedMin, int floor, double shortfallPenalty)
    {
        double logLR;

        if (observedMin >= floor)
        {
            logLR = 0.0;
        }
        else
        {
            logLR = -(floor - observedMin) * shortfallPenalty;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "EvidenceScoring.SizeFloorLogLR: observedMin=" + observedMin + " floor=" + floor
            + " logLR=" + logLR.ToString("F3"), LogLevel.Trace);
        return logLR;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // DirectionLogLR
    //
    // Log likelihood ratio from the candidate's observed traffic direction, for a hypothesis
    // that the opcode travels on one expected channel.
    //
    // Direction is one observation per collection, not one per packet: every packet of an
    // opcode shares its channel by construction, so packet counts add no independent
    // direction evidence.  The collection is scored as a single categorical observation
    // against an opcode-level background:
    //
    //   consistent (no off-channel packets):
    //     P(consistent | IS)  ~ 1
    //     P(consistent | NOT) = q     smoothed fraction of observed opcodes pure on the
    //                                 expected channel
    //     log LR = -log(q)            small positive support
    //
    //   inconsistent (off-channel packets present):
    //     P(off packets | IS)  = leakProbability per off-channel packet
    //     P(off packets | NOT) = 1 - q     mixed or other-channel traffic is unremarkable
    //     log LR = offCount * log(leakProbability) - log(1 - q)
    //
    // q is Laplace-smoothed so neither it nor its complement is ever zero.  Consistency can
    // never earn more than a few decibans; violation charges per off-channel packet and goes
    // strongly negative fast, bounded by the clamp.
    //
    // channelCounts:            Packet count per channel for the candidate, from OpcodeStats.
    // expectedChannel:          The channel the hypothesis says this opcode travels on.
    // pureChannelOpcodeCounts:  Per channel, the number of opcodes pure on it, from the
    //                           catalog.
    // opcodeCount:              Number of distinct opcodes observed in the capture.
    // leakProbability:          Per-packet probability the hypothesis allows off the expected
    //                           channel.  Small and nonzero.
    // maxAbsLogLR:              Clamp magnitude for the returned value.
    //
    // Returns:  Natural-log likelihood ratio from traffic direction, clamped.  Zero when
    //           the candidate's channel counts are missing.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static double DirectionLogLR(Dictionary<StreamId, uint>? channelCounts,
        StreamId expectedChannel, Dictionary<StreamId, int>? pureChannelOpcodeCounts,
        int opcodeCount, double leakProbability, double maxAbsLogLR)
    {
        if (channelCounts == null || channelCounts.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.DirectionLogLR: missing channel data, returning 0", LogLevel.Warn);
            return 0.0;
        }
        long offCount = 0;
        foreach (KeyValuePair<StreamId, uint> entry in channelCounts)
        {
            if (entry.Key != expectedChannel)
            {
                offCount += entry.Value;
            }
        }
        int pureOnExpected = 0;
        if (pureChannelOpcodeCounts != null)
        {
            pureChannelOpcodeCounts.TryGetValue(expectedChannel, out pureOnExpected);
        }
        double q = (pureOnExpected + 1.0) / (opcodeCount + 2.0);
        double logLR;
        if (offCount == 0)
        {
            logLR = -Math.Log(q);
        }
        else
        {
            logLR = offCount * Math.Log(leakProbability) - Math.Log(1.0 - q);
        }
        if (logLR > maxAbsLogLR)
        {
            logLR = maxAbsLogLR;
        }
        if (logLR < -maxAbsLogLR)
        {
            logLR = -maxAbsLogLR;
        }
        DebugLog.Write(LogChannel.InferenceDebug,
            "EvidenceScoring.DirectionLogLR: expected=" + expectedChannel
            + " offCount=" + offCount + " q=" + q.ToString("F3")
            + " logLR=" + logLR.ToString("F3"), LogLevel.Trace);
        return logLR;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SizeHistogramLogLR
    //
    // Log likelihood ratio from the candidate's observed length distribution, for a
    // hypothesis with a known baseline length distribution from prior patch levels.
    //
    // Each message contributes its own increment, summed per distinct length:
    //
    //   P(length | IS)  = (1 - escapeMass) * baseline share of that length
    //                     when the baseline has seen the length
    //                   = escapeMass * pooled share of that length
    //                     when it has not (back-off: the escape mass scales the background
    //                     rather than replacing it)
    //   P(length | NOT) = pooled share of that length      background mix across all opcodes
    //
    //   log LR = sum over lengths of count_l * (log P(l | IS) - log P(l | NOT))
    //
    // For a length the baseline has seen, the score compares baseline share to background
    // share.  For a novel length the pooled share cancels and every message costs exactly
    // log(escapeMass), so novel lengths always count against the hypothesis, by the same
    // amount whether the length is common or exotic.  This is what makes a mutated message
    // (fields reordered, field appended) legible: sizes shift outside the baseline support
    // and this feature goes strongly negative while every other feature still matches, the
    // signature of the identified-but-mutated verdict.
    //
    // escapeMass is the probability the hypothesis reserves for lengths the baseline never
    // saw, estimated from the baseline itself by Good-Turing: the fraction of baseline
    // messages whose length appeared exactly once.  A baseline with ragged singleton tails
    // (its size support undersampled) forgives novel lengths readily; one that saw every
    // length many times stays strict.  The estimate is floored at escapeFloor so it is never
    // zero, and capped at one half so a degenerate all-singleton baseline cannot invert the
    // model.
    //
    // The total is clamped to +/- maxAbsLogLR, both for the finite-cap agreement and because
    // messages arrive in correlated bursts, so the nominal count overstates the independent
    // evidence.
    //
    // observedHistogram:  Message count per length for the candidate, from OpcodeStats.
    // baselineHistogram:  Message count per length for the opcode at prior patch levels.
    // pooledHistogram:    Message count per length across all opcodes, from the catalog.
    // escapeFloor:        Minimum escape mass, used when the baseline has no singleton
    //                     lengths.  Small and nonzero.
    // maxAbsLogLR:        Clamp magnitude for the returned value.
    //
    // Returns:  Natural-log likelihood ratio from the length distribution, clamped.  Zero
    //           when any histogram is null or empty.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static double SizeHistogramLogLR(Dictionary<int, int>? observedHistogram,
        Dictionary<int, int>? baselineHistogram, Dictionary<int, int>? pooledHistogram,
        double escapeFloor, double maxAbsLogLR)
    {
        if (observedHistogram == null || observedHistogram.Count == 0
            || baselineHistogram == null || baselineHistogram.Count == 0
            || pooledHistogram == null || pooledHistogram.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.SizeHistogramLogLR: missing histogram data, returning 0",
                LogLevel.Trace);
            return 0.0;
        }
        double baselineTotal = 0.0;
        int singletonCount = 0;
        foreach (KeyValuePair<int, int> baselineEntry in baselineHistogram)
        {
            baselineTotal += baselineEntry.Value;
            if (baselineEntry.Value == 1)
            {
                singletonCount++;
            }
        }
        double escapeMass = singletonCount / baselineTotal;
        if (escapeMass < escapeFloor)
        {
            escapeMass = escapeFloor;
        }
        if (escapeMass > 0.5)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.SizeHistogramLogLR: escape mass " + escapeMass.ToString("F3")
                + " capped at 0.5, baseline is mostly singletons", LogLevel.Warn);
            escapeMass = 0.5;
        }
        double pooledTotal = 0.0;
        foreach (KeyValuePair<int, int> pooledEntry in pooledHistogram)
        {
            pooledTotal += pooledEntry.Value;
        }
        double logLR = 0.0;
        foreach (KeyValuePair<int, int> entry in observedHistogram)
        {
            int length = entry.Key;
            int count = entry.Value;
            int pooledCount;
            if (!pooledHistogram.TryGetValue(length, out pooledCount) || pooledCount == 0)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "EvidenceScoring.SizeHistogramLogLR: length " + length
                    + " absent from pooled histogram, skipping " + count + " messages",
                    LogLevel.Trace);
                continue;
            }
            double pIfNot = pooledCount / pooledTotal;
            int baselineCount;
            double pIfIs;
            if (baselineHistogram.TryGetValue(length, out baselineCount))
            {
                pIfIs = (1.0 - escapeMass) * (baselineCount / baselineTotal);
            }
            else
            {
                // back-off: the escape mass scales the background rather than replacing it,
                // so a novel length always scores log(escapeMass) against the hypothesis
                pIfIs = escapeMass * pIfNot;
            }
            logLR += count * (Math.Log(pIfIs) - Math.Log(pIfNot));
        }
        if (logLR > maxAbsLogLR)
        {
            logLR = maxAbsLogLR;
        }
        if (logLR < -maxAbsLogLR)
        {
            logLR = -maxAbsLogLR;
        }
        DebugLog.Write(LogChannel.InferenceDebug,
            "EvidenceScoring.SizeHistogramLogLR: lengths=" + observedHistogram.Count
            + " escape=" + escapeMass.ToString("F4")
            + " logLR=" + logLR.ToString("F3"), LogLevel.Trace);
        return logLR;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ProportionLogLR
    //
    // Log likelihood ratio from the candidate's share of total capture traffic, for a
    // hypothesis with a known baseline share from prior patch levels.
    //
    // The baseline share is treated as uncertain rather than exact: the hypothesis is a Beta
    // distribution centered on baselineShare whose spread is set by baselineStrength (the
    // pseudo-count weight of the baseline; larger trusts the baseline more).  The alternative
    // is a broad Beta centered on backgroundShare, the share an arbitrary opcode would hold.
    // Both sides then predict the observed count through a Beta-binomial, and the binomial
    // coefficient cancels in the ratio:
    //
    //   aH = baselineShare * baselineStrength        bH = (1 - baselineShare) * baselineStrength
    //   aN = backgroundShare * backgroundStrength    bN = (1 - backgroundShare) * backgroundStrength
    //
    //   log LR = [ lnB(count + aH, total - count + bH) - lnB(aH, bH) ]
    //          - [ lnB(count + aN, total - count + bN) - lnB(aN, bN) ]
    //
    // Evidence scales with the observation automatically: a small capture leaves the two
    // predictions overlapping and the ratio modest; a large one separates them.  The total
    // is clamped to +/- maxAbsLogLR because packets arrive in correlated bursts, so the
    // nominal count overstates the independent evidence.
    //
    // count:               The candidate's packet count.
    // totalCount:          Total packets across all opcodes in the capture.
    // baselineShare:       The opcode's traffic share at prior patch levels, 0 to 1 exclusive.
    // baselineStrength:    Pseudo-count weight of the baseline estimate.
    // backgroundShare:     Typical share of an arbitrary opcode (e.g. 1 / opcode count),
    //                      0 to 1 exclusive.
    // backgroundStrength:  Pseudo-count weight of the background model.  Small keeps it broad.
    // maxAbsLogLR:         Clamp magnitude for the returned value.
    //
    // Returns:  Natural-log likelihood ratio from traffic share, clamped.  Zero when
    //           totalCount is zero.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static double ProportionLogLR(uint count, ulong totalCount, double baselineShare,
        double baselineStrength, double backgroundShare, double backgroundStrength,
        double maxAbsLogLR)
    {
        if (totalCount == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.ProportionLogLR: empty capture, returning 0", LogLevel.Warn);
            return 0.0;
        }
        double n = totalCount;
        double k = count;
        double aH = baselineShare * baselineStrength;
        double bH = (1.0 - baselineShare) * baselineStrength;
        double aN = backgroundShare * backgroundStrength;
        double bN = (1.0 - backgroundShare) * backgroundStrength;
        double logPIfIs = LogBeta(k + aH, n - k + bH) - LogBeta(aH, bH);
        double logPIfNot = LogBeta(k + aN, n - k + bN) - LogBeta(aN, bN);
        double logLR = logPIfIs - logPIfNot;
        if (logLR > maxAbsLogLR)
        {
            logLR = maxAbsLogLR;
        }
        if (logLR < -maxAbsLogLR)
        {
            logLR = -maxAbsLogLR;
        }
        DebugLog.Write(LogChannel.InferenceDebug,
            "EvidenceScoring.ProportionLogLR: count=" + count + " total=" + totalCount
            + " baseline=" + baselineShare.ToString("F4") + " logLR=" + logLR.ToString("F3"),
            LogLevel.Trace);
        return logLR;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // DominantRankLogLR
    //
    // Log likelihood ratio from whether the candidate holds rank 0 (the highest message
    // count) in the capture, for a hypothesis that the opcode is the dominant one.  The
    // likelihoods are parameterized by the operator's capture record rather than invented:
    //
    //   pTop = (capturesAtRankOne + 1) / (capturesObserved + 2)     Laplace-smoothed record
    //
    //   candidate holds rank 0:
    //     P(rank 0 | IS)  = pTop
    //     P(rank 0 | NOT) = (1 - pTop) / (opcodeCount - 1)
    //                       the dominant opcode was dethroned AND this particular wire value,
    //                       exchangeable among the others a priori, is the usurper
    //     log LR = log(pTop) - log((1 - pTop) / (opcodeCount - 1))
    //
    //   candidate does not hold rank 0:
    //     P(not rank 0 | IS)  = 1 - pTop
    //     P(not rank 0 | NOT) = 1 - (1 - pTop) / (opcodeCount - 1)
    //     log LR = log(1 - pTop) - log(1 - (1 - pTop) / (opcodeCount - 1))
    //
    // Rank is a single observation per capture: it does not scale with message count, so the
    // capture record's conditioning matters.  The record counts only adequate captures, and
    // this feature must not be applied to captures below that adequacy bar; the caller
    // enforces the bar and skips the feature when the capture is too small.
    //
    // This feature and a traffic-share feature are two statistics of the same measurement.
    // Per the statistic-selection rule a handler uses one or the other, never both.
    //
    // observedRank:       The candidate's zero-based position in count-descending order.
    // capturesAtRankOne:  How many adequate captures in the record had this opcode at rank 0.
    // capturesObserved:   How many adequate captures are in the record.
    // opcodeCount:        Number of distinct opcodes observed in this capture.
    // maxAbsLogLR:        Clamp magnitude for the returned value.
    //
    // Returns:  Natural-log likelihood ratio from dominance, clamped.  Zero when the record
    //           is empty or the capture holds fewer than two opcodes.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static double DominantRankLogLR(int observedRank, int capturesAtRankOne,
        int capturesObserved, int opcodeCount, double maxAbsLogLR)
    {
        if (capturesObserved <= 0 || opcodeCount < 2)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.DominantRankLogLR: empty record or degenerate capture, returning 0",
                LogLevel.Warn);
            return 0.0;
        }
        double pTop = (capturesAtRankOne + 1.0) / (capturesObserved + 2.0);
        double pUsurper = (1.0 - pTop) / (opcodeCount - 1.0);
        double logLR;
        if (observedRank == 0)
        {
            logLR = Math.Log(pTop) - Math.Log(pUsurper);
        }
        else
        {
            logLR = Math.Log(1.0 - pTop) - Math.Log(1.0 - pUsurper);
        }
        if (logLR > maxAbsLogLR)
        {
            logLR = maxAbsLogLR;
        }
        if (logLR < -maxAbsLogLR)
        {
            logLR = -maxAbsLogLR;
        }
        DebugLog.Write(LogChannel.InferenceDebug,
            "EvidenceScoring.DominantRankLogLR: rank=" + observedRank
            + " pTop=" + pTop.ToString("F3") + " logLR=" + logLR.ToString("F3"), LogLevel.Trace);
        return logLR;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LogGamma
    //
    // Natural log of the Gamma function, by the Lanczos approximation (g = 7, n = 9
    // coefficients).  Accurate to around 15 significant digits for positive arguments,
    // which covers every use here: arguments are counts and pseudo-counts, always positive.
    //
    // x:  Argument, must be positive.
    //
    // Returns:  ln(Gamma(x)).
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double LogGamma(double x)
    {
        double[] coefficients =
        {
            0.99999999999980993, 676.5203681218851, -1259.1392167224028,
            771.32342877765313, -176.61502916214059, 12.507343278686905,
            -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
        };
        if (x < 0.5)
        {
            // reflection formula for small arguments
            return Math.Log(Math.PI / Math.Sin(Math.PI * x)) - LogGamma(1.0 - x);
        }
        double shifted = x - 1.0;
        double sum = coefficients[0];
        for (int i = 1; i < coefficients.Length; i++)
        {
            sum += coefficients[i] / (shifted + i);
        }
        double t = shifted + 7.5;
        return 0.5 * Math.Log(2.0 * Math.PI) + (shifted + 0.5) * Math.Log(t) - t + Math.Log(sum);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LogBeta
    //
    // Natural log of the Beta function, from LogGamma.
    //
    // a:  First parameter, must be positive.
    // b:  Second parameter, must be positive.
    //
    // Returns:  ln(B(a, b)).
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double LogBeta(double a, double b)
    {
        return LogGamma(a) + LogGamma(b) - LogGamma(a + b);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ToDecibans
    //
    // Converts a natural-log likelihood ratio to decibans for display and logging.
    // 10 db is 10:1 odds, 20 db is 100:1.
    //
    // logLR:  Natural-log likelihood ratio.
    //
    // Returns:  The same evidence weight expressed in decibans.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static double ToDecibans(double logLR)
    {
        return logLR * (10.0 / Math.Log(10.0));
    }
}