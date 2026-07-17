using Glass.Core.Logging;
using Inference.Models;
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
    // SizeStrideLogLR
    //
    // Log likelihood ratio from whether the candidate's observed lengths fall on a fixed
    // stride, for a hypothesis that the opcode is a repeating array of fixed-size records
    // with a constant number of bytes outside the records.  A conforming length satisfies
    //
    //   length % stride == residue
    //
    // The unit of observation is the distinct length, not the message.  Message counts
    // within one length arrive in correlated bursts and overstate the independent evidence;
    // each distinct length is one honest draw from the length distribution, so a candidate
    // showing many different lengths that all conform earns support that scales with that
    // variety.
    //
    //   per conforming distinct length:
    //     P(conforms | IS)  ~ 1
    //     P(conforms | NOT) = q     smoothed fraction of other opcodes' distinct lengths
    //                               that land on this residue by chance
    //     log LR increment = -log(q)
    //
    //   per violating distinct length:
    //     P(violation | IS)  = leakProbability
    //     P(violation | NOT) = 1 - q
    //     log LR increment = log(leakProbability) - log(1 - q)
    //
    // q is estimated from the pooled histogram with the candidate's own contribution
    // removed: a length is counted only if some other opcode also produced it, so a
    // high-volume candidate cannot dilute its own background.  q is Laplace-smoothed so
    // neither it nor its complement is ever zero.  The total is clamped to +/- maxAbsLogLR.
    //
    // This feature should be used when the hypothesis predicts a length
    // family rather than a fixed set of lengths; per the statistic-selection rule a handler
    // uses at most one length measurement.
    //
    // observedHistogram:  Message count per length for the candidate, from OpcodeStats.
    // stride:             The record size the hypothesis predicts.  Must be positive.
    // residue:            The expected length modulo stride.
    // pooledHistogram:    Message count per length across all opcodes including the
    //                     candidate, from the catalog.  The candidate's own counts are
    //                     subtracted here.
    // leakProbability:    Per-distinct-length probability the hypothesis allows off the
    //                     stride.  Small and nonzero.
    // maxAbsLogLR:        Clamp magnitude for the returned value.
    //
    // Returns:  Natural-log likelihood ratio from stride conformance, clamped.  Zero when
    //           observedHistogram is null or empty, or stride is not positive.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static double SizeStrideLogLR(Dictionary<int, int>? observedHistogram, int stride,
        int residue, Dictionary<int, int>? pooledHistogram, double leakProbability,
        double maxAbsLogLR)
    {
        if (observedHistogram == null || observedHistogram.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.SizeStrideLogLR: missing observed histogram, returning 0",
                LogLevel.Warn);
            return 0.0;
        }
        if (stride <= 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.SizeStrideLogLR: stride " + stride + " is not positive, returning 0",
                LogLevel.Error);
            return 0.0;
        }

        int conformingLengths = 0;
        int violatingLengths = 0;
        foreach (KeyValuePair<int, int> entry in observedHistogram)
        {
            if ((entry.Key % stride) == residue)
            {
                conformingLengths++;
            }
            else
            {
                violatingLengths++;
            }
        }

        int conformingOthers = 0;
        int totalOthers = 0;
        if (pooledHistogram != null)
        {
            foreach (KeyValuePair<int, int> entry in pooledHistogram)
            {
                int observedCount;
                observedHistogram.TryGetValue(entry.Key, out observedCount);
                if ((entry.Value - observedCount) <= 0)
                {
                    // the candidate is this length's only source; it is not background
                    continue;
                }
                totalOthers++;
                if ((entry.Key % stride) == residue)
                {
                    conformingOthers++;
                }
            }
        }
        else
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.SizeStrideLogLR: missing pooled histogram, using uninformative background",
                LogLevel.Warn);
        }
        double q = (conformingOthers + 1.0) / (totalOthers + 2.0);

        double logLR = (conformingLengths * -Math.Log(q))
            + (violatingLengths * (Math.Log(leakProbability) - Math.Log(1.0 - q)));

        if (logLR > maxAbsLogLR)
        {
            logLR = maxAbsLogLR;
        }
        if (logLR < -maxAbsLogLR)
        {
            logLR = -maxAbsLogLR;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "EvidenceScoring.SizeStrideLogLR: stride=" + stride + " residue=" + residue
            + " conforming=" + conformingLengths + " violating=" + violatingLengths
            + " q=" + q.ToString("F3") + " logLR=" + logLR.ToString("F3"), LogLevel.Trace);
        return logLR;
    }


    ///////////////////////////////////////////////////////////////////////////////////////////
    // StrideSilhouetteLogLR
    //
    // Log likelihood ratio from how well a bucket's payloads match a per-offset printability
    // template, for a hypothesis that every payload of the opcode is built from the same
    // fixed-size structure.  The template (silhouette) is built from one designated payload:
    // bytes are classed printable (0x20 through 0x7E) or non-printable, and each offset
    // within the stride whose class is consistent across that payload's repetitions becomes
    // a stable template position; inconsistent offsets are excluded from all scoring.  The
    // designated payload itself is never scored — the template is fitted to it, so its
    // agreement carries no information.
    //
    // Every other payload in the bucket is scored position by position at stable offsets:
    //
    //   P(class match | IS)  = matchRateIfPeriodic
    //   P(class match | NOT) = q, that payload's mean agreement against the template at
    //                          every misalignment (the template rotated by 1 to stride-1),
    //                          so a payload's own class composition supplies its null
    //
    //   per payload:  k*(log(p) - log(q)) + (n-k)*(log(1-p) - log(1-q))
    //
    // with n the stable positions compared and k the matches.  Payload contributions sum,
    // then log(shiftsScanned) is subtracted once — the charge for having selected the
    // stride from that many scanned shifts.  Positions agree in field-sized runs rather
    // than independently, so the nominal count overstates the independent evidence; the
    // clamp bounds the total.
    //
    // A payload is skipped when it has no stable-offset positions to compare or when its
    // misalignment agreement reaches the hypothesis rate (nothing to discriminate).
    // Abstains (returns zero) when the bucket holds no scorable payload besides the
    // designated one, or when no template offset is stable.
    //
    // bucket:               All cataloged packets for the candidate opcode.
    // silhouetteIndex:      Position in bucket of the payload the template is built from.
    // stride:               The structure size in bytes.  Must be at least 2.
    // matchRateIfPeriodic:  Expected class agreement at stable offsets, between zero and
    //                       one exclusive.
    // stableThreshold:      Minimum class-consistency fraction for a template offset to be
    //                       stable, between one half and one.
    // shiftsScanned:        Number of shifts examined by the discovery that produced the
    //                       stride; charged once as model selection.  At least 1.
    // misalignmentSamples:  Largest number of misaligned rotations used to estimate q.
    //                       Bounds the cost of the null on wide strides.  Must be at
    //                       least 1.
    // maxAbsLogLR:          Clamp magnitude for the returned value.
    //
    // Returns:  Natural-log likelihood ratio from silhouette agreement, clamped.  Zero on
    //           abstention or invalid arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static double StrideSilhouetteLogLR(List<CatalogedPacket> bucket, int silhouetteIndex,
            int stride, double matchRateIfPeriodic, double stableThreshold, int shiftsScanned,
            int misalignmentSamples, double maxAbsLogLR)
    {
        if (bucket == null || bucket.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.StrideSilhouetteLogLR: empty bucket, returning 0", LogLevel.Warn);
            return 0.0;
        }
        if (silhouetteIndex < 0 || silhouetteIndex >= bucket.Count)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.StrideSilhouetteLogLR: silhouette index " + silhouetteIndex
                + " outside bucket of " + bucket.Count + ", returning 0", LogLevel.Error);
            return 0.0;
        }
        if (stride < 2)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.StrideSilhouetteLogLR: stride " + stride
                + " below 2, returning 0", LogLevel.Error);
            return 0.0;
        }
        if (misalignmentSamples < 1)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.StrideSilhouetteLogLR: misalignmentSamples "
                + misalignmentSamples + " below 1, returning 0", LogLevel.Error);
            return 0.0;
        }

        // build the template: class-consistency per offset within the stride
        ReadOnlySpan<byte> template = bucket[silhouetteIndex].Payload.AsReadOnlySpan();
        int[] silhouette = new int[stride];
        int stableOffsets = 0;
        for (int offset = 0; offset < stride; offset++)
        {
            int printableCount = 0;
            int total = 0;
            for (int i = offset; i < template.Length; i += stride)
            {
                if ((template[i] >= 0x20) && (template[i] <= 0x7E))
                {
                    printableCount++;
                }
                total++;
            }
            double printableFraction = (total > 0) ? ((double)printableCount / total) : 0.0;
            if (total > 0 && printableFraction >= stableThreshold)
            {
                silhouette[offset] = 1;
                stableOffsets++;
            }
            else if (total > 0 && printableFraction <= (1.0 - stableThreshold))
            {
                silhouette[offset] = 0;
                stableOffsets++;
            }
            else
            {
                silhouette[offset] = -1;
            }
        }
        if (stableOffsets == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.StrideSilhouetteLogLR: no stable offsets in template, abstaining",
                LogLevel.Warn);
            return 0.0;
        }

        int sampleCount = stride - 1;
        if (sampleCount > misalignmentSamples)
        {
            sampleCount = misalignmentSamples;
        }
        int[] rotations = new int[sampleCount];
        double rotationStep = (double)(stride - 1) / sampleCount;
        for (int sample = 0; sample < sampleCount; sample++)
        {
            rotations[sample] = 1 + (int)(sample * rotationStep);
        }

        double totalLogLR = 0.0;
        int scoredPayloads = 0;
        int skippedPayloads = 0;
        for (int packetIndex = 0; packetIndex < bucket.Count; packetIndex++)
        {
            if (packetIndex == silhouetteIndex)
            {
                continue;
            }
            ReadOnlySpan<byte> payload = bucket[packetIndex].Payload.AsReadOnlySpan();

            // aligned agreement, and mean agreement over every misalignment
            int alignedMatches = 0;
            int alignedTotal = 0;
            long rotatedMatches = 0;
            long rotatedTotal = 0;

            for (int i = 0; i < payload.Length; i++)
            {
                bool isPrintable = (payload[i] >= 0x20) && (payload[i] <= 0x7E);
                int alignedExpected = silhouette[i % stride];
                if (alignedExpected != -1)
                {
                    alignedTotal++;
                    if ((alignedExpected == 1) == isPrintable)
                    {
                        alignedMatches++;
                    }
                }
                for (int sample = 0; sample < rotations.Length; sample++)
                {
                    int expected = silhouette[(i + rotations[sample]) % stride];
                    if (expected == -1)
                    {
                        continue;
                    }
                    rotatedTotal++;
                    if ((expected == 1) == isPrintable)
                    {
                        rotatedMatches++;
                    }
                }
            }

            if (alignedTotal == 0 || rotatedTotal == 0)
            {
                skippedPayloads++;
                continue;
            }

            double q = (double)rotatedMatches / rotatedTotal;
            if (q < (1.0 / (alignedTotal + 2.0)))
            {
                q = 1.0 / (alignedTotal + 2.0);
            }
            if (q >= matchRateIfPeriodic)
            {
                skippedPayloads++;
                continue;
            }

            totalLogLR += (alignedMatches * (Math.Log(matchRateIfPeriodic) - Math.Log(q)))
                + ((alignedTotal - alignedMatches)
                    * (Math.Log(1.0 - matchRateIfPeriodic) - Math.Log(1.0 - q)));
            scoredPayloads++;
        }

        if (scoredPayloads == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.StrideSilhouetteLogLR: no scorable payloads ("
                + skippedPayloads + " skipped), abstaining", LogLevel.Warn);
            return 0.0;
        }

        if (shiftsScanned >= 1)
        {
            totalLogLR -= Math.Log(shiftsScanned);
        }

        if (totalLogLR > maxAbsLogLR)
        {
            totalLogLR = maxAbsLogLR;
        }
        if (totalLogLR < -maxAbsLogLR)
        {
            totalLogLR = -maxAbsLogLR;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "EvidenceScoring.StrideSilhouetteLogLR: stride=" + stride
            + " stableOffsets=" + stableOffsets + " scored=" + scoredPayloads
            + " skipped=" + skippedPayloads + " logLR=" + totalLogLR.ToString("F3"),
            LogLevel.Trace);
        return totalLogLR;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TryDiscoverStride
    //
    // Discovers the fixed stride at which a payload's content repeats, from the payload
    // alone.  Contributes no evidence: the return value is a gate, not a score.
    //
    // Bytes are reduced to two classes, printable (0x20 through 0x7E) and non-printable,
    // so values that drift while staying in a similar range keep the same class.  Class
    // agreement is measured at every shift from 2 up to a quarter of the payload's size or
    // maxShiftCap, whichever is smaller; the best-agreeing shift, reduced to its smallest
    // nearly-equal divisor, is the discovered stride.
    //
    // A stride qualifies only when its period carries both classes consistently — at least
    // one offset stably printable and at least one stably non-printable across the
    // repetitions.  A payload of one class throughout, unbroken text or dense binary,
    // contains no strides.
    //
    // The background rate is the mean agreement over the remaining shifts (multiples of
    // the candidate excluded), so a payload whose classes agree at every shift supplies
    // its own null.  Discovery succeeds only when the agreement at the discovered stride
    // reaches minimumAgreementRate and the background stays below it — the peak must both
    // be high and stand out.  Fails when the payload is too small to scan, when the class
    // mixture is absent, when no background shifts exist, or when either rate condition is
    // not met.
    //
    // payload:               The payload bytes of one message.
    // minimumAgreementRate:  Class agreement the discovered stride must reach, and the
    //                        background must stay below, between zero and one exclusive.
    // fundamentalTolerance:  Fraction of the best agreement a divisor shift must reach to
    //                        be preferred as the fundamental stride, between zero and one.
    // stableThreshold:       Minimum class-consistency fraction for an offset to count as
    //                        stable in the mixture test, between one half and one.
    // maxShiftCap:           Largest shift scanned, in bytes.  Bounds the cost of discovery
    //                        on large payloads, which would otherwise scan a quarter of
    //                        their own size.  A repeating structure wider than this cap
    //                        cannot be discovered.  Must be at least 2.
    // discoveredStride:      Receives the discovered stride in bytes; zero on failure.
    // shiftsScanned:         Receives the number of shifts examined; zero when the payload
    //                        was too small to scan.
    //
    // Returns:  True when a stride was discovered, carried both classes, and passed both
    //           rate conditions; false otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static bool TryDiscoverStride(ReadOnlySpan<byte> payload, double minimumAgreementRate,
        double fundamentalTolerance, double stableThreshold, int maxShiftCap,
        out int discoveredStride, out int shiftsScanned)
    {
        discoveredStride = 0;
        shiftsScanned = 0;

        int minShift = 2;
        if (maxShiftCap < minShift)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.TryDiscoverStride: maxShiftCap " + maxShiftCap
                + " below " + minShift + ", failing discovery", LogLevel.Error);
            return false;
        }
        int maxShift = payload.Length / 4;
        if (maxShift > maxShiftCap)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.TryDiscoverStride: scan capped at " + maxShiftCap
                + " shifts for payload of " + payload.Length + " bytes", LogLevel.Trace);
            maxShift = maxShiftCap;
        }
        if (maxShift < minShift)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.TryDiscoverStride: payload " + payload.Length
                + " bytes too small to scan, failing discovery", LogLevel.Trace);
            return false;
        }
        shiftsScanned = maxShift - minShift + 1;

        bool[] printable = new bool[payload.Length];
        for (int i = 0; i < payload.Length; i++)
        {
            printable[i] = (payload[i] >= 0x20) && (payload[i] <= 0x7E);
        }

        double[] rates = new double[maxShift + 1];
        int bestShift = 0;
        double bestRate = -1.0;
        for (int shift = minShift; shift <= maxShift; shift++)
        {
            int positions = payload.Length - shift;
            int matches = 0;
            for (int i = 0; i < positions; i++)
            {
                if (printable[i] == printable[i + shift])
                {
                    matches++;
                }
            }
            rates[shift] = (double)matches / positions;
            if (rates[shift] > bestRate)
            {
                bestRate = rates[shift];
                bestShift = shift;
            }
        }

        int stride = bestShift;
        for (int divisor = minShift; divisor < bestShift; divisor++)
        {
            if (((bestShift % divisor) == 0) && (rates[divisor] >= (bestRate * fundamentalTolerance)))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "EvidenceScoring.TryDiscoverStride: divisor " + divisor + " of best shift "
                    + bestShift + " agrees nearly as well, preferring it", LogLevel.Trace);
                stride = divisor;
                break;
            }
        }

        int stablePrintable = 0;
        int stableNonPrintable = 0;
        for (int offset = 0; offset < stride; offset++)
        {
            int printableCount = 0;
            int total = 0;
            for (int i = offset; i < payload.Length; i += stride)
            {
                if (printable[i])
                {
                    printableCount++;
                }
                total++;
            }
            if (total == 0)
            {
                continue;
            }
            double printableFraction = (double)printableCount / total;
            if (printableFraction >= stableThreshold)
            {
                stablePrintable++;
            }
            else if (printableFraction <= (1.0 - stableThreshold))
            {
                stableNonPrintable++;
            }
        }
        if (stablePrintable == 0 || stableNonPrintable == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.TryDiscoverStride: stride " + stride
                + " lacks class mixture (stable printable=" + stablePrintable
                + ", stable non-printable=" + stableNonPrintable
                + "), failing discovery", LogLevel.Trace);
            return false;
        }

        double backgroundSum = 0.0;
        int backgroundShifts = 0;
        for (int shift = minShift; shift <= maxShift; shift++)
        {
            if ((shift % stride) == 0)
            {
                continue;
            }
            backgroundSum += rates[shift];
            backgroundShifts++;
        }
        if (backgroundShifts == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.TryDiscoverStride: no background shifts outside stride "
                + stride + ", failing discovery", LogLevel.Warn);
            return false;
        }
        double background = backgroundSum / backgroundShifts;

        if (rates[stride] < minimumAgreementRate)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.TryDiscoverStride: peak " + rates[stride].ToString("F3")
                + " below required " + minimumAgreementRate.ToString("F3")
                + ", failing discovery", LogLevel.Trace);
            return false;
        }
        if (background >= minimumAgreementRate)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.TryDiscoverStride: background " + background.ToString("F3")
                + " reaches required " + minimumAgreementRate.ToString("F3")
                + ", nothing stands out, failing discovery", LogLevel.Trace);
            return false;
        }

        discoveredStride = stride;
        DebugLog.Write(LogChannel.InferenceDebug,
            "EvidenceScoring.TryDiscoverStride: stride=" + stride
            + " peak=" + rates[stride].ToString("F3") + " background=" + background.ToString("F3")
            + " stablePrintable=" + stablePrintable + " stableNonPrintable=" + stableNonPrintable
            + " shiftsScanned=" + shiftsScanned, LogLevel.Trace);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // StrideConfirmationLogLR
    //
    // Log likelihood ratio from how many of a bucket's capable payloads demonstrate a given
    // stride, for a hypothesis that every payload of the opcode is built from fixed-size
    // structures of that stride.  A payload is capable when it is long enough to hold more
    // than two repetitions (length > 2 * stride) — the minimum that can demonstrate a
    // stride.  One designated payload is excluded: it selected the stride and does not get
    // to vote for it.
    //
    // Each capable payload is tested, not re-scanned: bytes are reduced to printable
    // (0x20 through 0x7E) and non-printable classes, class agreement is measured at the
    // stride and at a deterministic, evenly spaced sample of control shifts (multiples of
    // the stride excluded), and the payload demonstrates the stride when its agreement at
    // the stride reaches minimumAgreementRate and exceeds the agreement at every control.
    // By exchangeability, a payload with no stride tops m controls with probability
    // 1 / (m + 1), which supplies the null exactly:
    //
    //   demonstrates:      log LR += log(agreeRateIfMatch) - log(1 / (m + 1))
    //   fails:             log LR += log(1 - agreeRateIfMatch) - log(m / (m + 1))
    //
    // Contributions sum over capable payloads and the total is clamped.  When more capable
    // payloads exist than the cost cap allows, the largest are scored and the truncation is
    // logged — the reported counts are of scored payloads only.
    //
    // bucket:                All cataloged packets for the candidate opcode.
    // excludePacketIndex:    PacketIndex of the payload that selected the stride.
    // stride:                The structure size in bytes being confirmed.  At least 2.
    // minimumAgreementRate:  Class agreement the stride must reach in a payload for it to
    //                        count as demonstrating, between zero and one exclusive.
    // agreeRateIfMatch:      Probability a true payload demonstrates its stride, between
    //                        zero and one exclusive.
    // controlShiftCount:     Number of control shifts sampled per payload.  At least 1.
    // maxCapablePayloads:    Largest number of capable payloads scored, largest first.
    //                        Bounds cost.  At least 1.
    // maxAbsLogLR:           Clamp magnitude for the returned value.
    // capableCount:          Receives the number of capable payloads scored.
    // confirmedCount:        Receives how many of those demonstrated the stride.
    //
    // Returns:  Natural-log likelihood ratio from stride demonstration, clamped.  Zero
    //           when no capable payload exists or the arguments are invalid.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static double StrideConfirmationLogLR(List<CatalogedPacket> bucket,
        uint excludePacketIndex, int stride, double minimumAgreementRate,
        double agreeRateIfMatch, int controlShiftCount, int maxCapablePayloads,
        double maxAbsLogLR, out int capableCount, out int confirmedCount)
    {
        capableCount = 0;
        confirmedCount = 0;

        if (bucket == null || bucket.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.StrideConfirmationLogLR: empty bucket, returning 0", LogLevel.Warn);
            return 0.0;
        }
        if (stride < 2)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.StrideConfirmationLogLR: stride " + stride
                + " below 2, returning 0", LogLevel.Error);
            return 0.0;
        }
        if (controlShiftCount < 1 || maxCapablePayloads < 1)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.StrideConfirmationLogLR: controlShiftCount "
                + controlShiftCount + " or maxCapablePayloads " + maxCapablePayloads
                + " below 1, returning 0", LogLevel.Error);
            return 0.0;
        }

        List<int> capable = new List<int>();
        for (int packetIndex = 0; packetIndex < bucket.Count; packetIndex++)
        {
            if (bucket[packetIndex].PacketIndex == excludePacketIndex)
            {
                continue;
            }
            if (bucket[packetIndex].Payload.Length > (2 * stride))
            {
                capable.Add(packetIndex);
            }
        }
        if (capable.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.StrideConfirmationLogLR: no capable payloads, abstaining",
                LogLevel.Trace);
            return 0.0;
        }
        if (capable.Count > maxCapablePayloads)
        {
            capable.Sort((a, b) => bucket[b].Payload.Length.CompareTo(bucket[a].Payload.Length));
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.StrideConfirmationLogLR: " + capable.Count
                + " capable payloads truncated to " + maxCapablePayloads
                + " largest", LogLevel.Warn);
            capable.RemoveRange(maxCapablePayloads, capable.Count - maxCapablePayloads);
        }

        double totalLogLR = 0.0;
        for (int capableIndex = 0; capableIndex < capable.Count; capableIndex++)
        {
            ReadOnlySpan<byte> payload = bucket[capable[capableIndex]].Payload.AsReadOnlySpan();

            bool[] printable = new bool[payload.Length];
            for (int i = 0; i < payload.Length; i++)
            {
                printable[i] = (payload[i] >= 0x20) && (payload[i] <= 0x7E);
            }

            double strideRate = AgreementRateAt(printable, stride);

            // the control set is enumerated before any comparison: the null probability
            // depends on the size of the comparison set, not on where a beating control sits
            int controlRange = payload.Length / 2;
            double controlStep = (double)(controlRange - 2) / controlShiftCount;
            if (controlStep < 1.0)
            {
                controlStep = 1.0;
            }
            List<int> controls = new List<int>();
            for (int sample = 0; sample < controlShiftCount; sample++)
            {
                int controlShift = 2 + (int)(sample * controlStep);
                if (controlShift >= controlRange)
                {
                    break;
                }
                if ((controlShift % stride) == 0)
                {
                    continue;
                }
                controls.Add(controlShift);
            }
            if (controls.Count == 0)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "EvidenceScoring.StrideConfirmationLogLR: no valid controls for payload of "
                    + payload.Length + " bytes, payload skipped", LogLevel.Trace);
                continue;
            }
            capableCount++;

            double chanceTop = 1.0 / (controls.Count + 1.0);
            bool beatenByControl = false;
            for (int controlIndex = 0; controlIndex < controls.Count; controlIndex++)
            {
                if (AgreementRateAt(printable, controls[controlIndex]) >= strideRate)
                {
                    beatenByControl = true;
                    break;
                }
            }

            bool demonstrates = (strideRate >= minimumAgreementRate) && (beatenByControl == false);
            if (demonstrates)
            {
                confirmedCount++;
                totalLogLR += Math.Log(agreeRateIfMatch) - Math.Log(chanceTop);
            }
            else
            {
                totalLogLR += Math.Log(1.0 - agreeRateIfMatch) - Math.Log(1.0 - chanceTop);
            }
        }

        if (totalLogLR > maxAbsLogLR)
        {
            totalLogLR = maxAbsLogLR;
        }
        if (totalLogLR < -maxAbsLogLR)
        {
            totalLogLR = -maxAbsLogLR;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "EvidenceScoring.StrideConfirmationLogLR: stride=" + stride
            + " confirmed=" + confirmedCount + "/" + capableCount
            + " logLR=" + totalLogLR.ToString("F3"), LogLevel.Trace);
        return totalLogLR;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SizeFitLogLR
    //
    // Log likelihood ratio from how well a candidate's length support fits a baseline
    // length distribution.  This feature can only refute or stand aside; it never
    // supports.  Only presence and absence of lengths are scored — the proportions of a
    // length mix are behavior-dependent and deliberately contribute nothing, so a capture
    // where the mix drifts costs the true opcode nothing as long as the lengths appear.
    //
    //   missing length:  a baseline length absent from the candidate.  Under the
    //                    hypothesis each message had chance p of taking it, so absence in
    //                    nEff effective messages costs
    //                        nEff * log(1 - p),  p = (1 - escapeMass) * baseline share
    //                    A dominant length missing is damning; a rare one, negligible.
    //
    //   novel length:    an observed length the baseline never saw costs log(escapeMass)
    //                    once per distinct length — message counts within a length are
    //                    burst-correlated and add no independent evidence.
    //
    //   shared length:   present in both, contributes zero.
    //
    // escapeMass is estimated from the baseline by Good-Turing (the fraction of baseline
    // messages whose length appeared exactly once), floored at escapeFloor and capped at
    // one half.  nEff is the message count capped at effectiveSampleCap, the
    // effective-sample-size correction for burst correlation.  The result is never
    // positive and is clamped at -maxAbsLogLR.
    //
    // observedHistogram:   Message count per length for the candidate, from OpcodeStats.
    // baselineHistogram:   Message count per length for the opcode at prior patch levels.
    // escapeFloor:         Minimum escape mass.  Small and nonzero.
    // effectiveSampleCap:  Largest message count credited as independent evidence.  At
    //                      least 1.
    // maxAbsLogLR:         Clamp magnitude for the returned value.
    //
    // Returns:  Natural-log likelihood ratio from length support fit, at most zero,
    //           clamped.  Zero when either histogram is null or empty or arguments are
    //           invalid.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static double SizeFitLogLR(Dictionary<int, int>? observedHistogram,
        Dictionary<int, int>? baselineHistogram, double escapeFloor, int effectiveSampleCap,
        double maxAbsLogLR)
    {
        if (observedHistogram == null || observedHistogram.Count == 0
            || baselineHistogram == null || baselineHistogram.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.SizeFitLogLR: missing histogram data, returning 0",
                LogLevel.Trace);
            return 0.0;
        }
        if (effectiveSampleCap < 1)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "EvidenceScoring.SizeFitLogLR: effectiveSampleCap " + effectiveSampleCap
                + " below 1, returning 0", LogLevel.Error);
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
                "EvidenceScoring.SizeFitLogLR: escape mass " + escapeMass.ToString("F3")
                + " capped at 0.5, baseline is mostly singletons", LogLevel.Warn);
            escapeMass = 0.5;
        }

        double observedTotal = 0.0;
        foreach (KeyValuePair<int, int> entry in observedHistogram)
        {
            observedTotal += entry.Value;
        }
        double effectiveSamples = observedTotal;
        if (effectiveSamples > effectiveSampleCap)
        {
            effectiveSamples = effectiveSampleCap;
        }

        double logLR = 0.0;
        int missingLengths = 0;
        foreach (KeyValuePair<int, int> baselineEntry in baselineHistogram)
        {
            if (observedHistogram.ContainsKey(baselineEntry.Key))
            {
                continue;
            }
            missingLengths++;
            double presenceChance = (1.0 - escapeMass) * (baselineEntry.Value / baselineTotal);
            logLR += effectiveSamples * Math.Log(1.0 - presenceChance);
        }

        int novelLengths = 0;
        foreach (KeyValuePair<int, int> entry in observedHistogram)
        {
            if (baselineHistogram.ContainsKey(entry.Key))
            {
                continue;
            }
            novelLengths++;
            logLR += Math.Log(escapeMass);
        }

        if (logLR > 0.0)
        {
            logLR = 0.0;
        }
        if (logLR < -maxAbsLogLR)
        {
            logLR = -maxAbsLogLR;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "EvidenceScoring.SizeFitLogLR: messages=" + observedTotal
            + " nEff=" + effectiveSamples + " escape=" + escapeMass.ToString("F4")
            + " missing=" + missingLengths + " novel=" + novelLengths
            + " logLR=" + logLR.ToString("F3"), LogLevel.Trace);
        return logLR;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AgreementRateAt
    //
    // Computes the fraction of positions at which a class sequence agrees with itself at
    // the given shift.
    //
    // classes:  Per-byte class values.
    // shift:    The self-comparison shift, in elements.  Must be positive and smaller than
    //           the sequence length.
    //
    // Returns:  Matching positions divided by positions compared.  Zero when the shift
    //           leaves nothing to compare.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double AgreementRateAt(bool[] classes, int shift)
    {
        int positions = classes.Length - shift;
        if (positions <= 0)
        {
            return 0.0;
        }
        int matches = 0;
        for (int i = 0; i < positions; i++)
        {
            if (classes[i] == classes[i + shift])
            {
                matches++;
            }
        }
        return (double)matches / positions;
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