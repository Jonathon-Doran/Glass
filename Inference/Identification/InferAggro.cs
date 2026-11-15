using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Client;
using Glass.Network.Protocol;
using Inference.Core;
using Inference.Models;
using System;
using System.Collections.Generic;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// InferAggro
//
// Identifies OP_Aggro.  The zone server sends it to a client while a mob that client has
// targeted or on extended target is engaged.  The payload begins with a flag byte, 0 or 1;
// a flag of 1 is followed by a 32-bit spawn id.  Messages to one client arrive on a cadence
// of about one second, and an engagement ends with a flag 1 message carrying spawn id 0,
// sent to every engaged client within a fraction of a second.  Every unaccepted wire value
// is scored on four features: direction, flag head, cadence, and reset burst.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferAggro : IInferOpcodes
{
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    private const string OpcodeNameConstant = "OP_Aggro";
    private const double LeakProbability = 0.001;              // per-item allowance for a failed predicate
    private const double MaxAbsLogLR = 12.0;                   // clamp per feature, roughly +/- 52 decibans
    private const byte FlagNoSpawn = 0;                        // flag value with no spawn id following
    private const byte FlagSpawn = 1;                          // flag value with a 32-bit spawn id following
    private const int NoSpawnHeadLength = 2;                   // flag plus count
    private const int SpawnHeadLength = 6;                     // flag, 32-bit spawn id, count
    private const double CadencePeriodSeconds = 1.0;           // send period per client
    private const double CadenceToleranceSeconds = 0.3;        // a gap conforms within this of a whole period
    private const double CadenceMinimumGapSeconds = 0.5;       // shorter gaps are paired or change-driven sends
    private const double CadenceMaximumGapSeconds = 3.5;       // longer gaps separate engagements
    private const double CadenceConformRateIfAggro = 0.85;     // expected conforming fraction for OP_Aggro
    private const double CadenceMaxAbsLogLR = 3.0;             // clamp for cadence, roughly +/- 13 decibans
    private const double ResetWindowSeconds = 0.5;             // reset messages to all sessions arrive within this
    private const int ResetMinimumSessions = 2;                // sessions that must receive one reset burst
    private const double ReportThreshold = 0.40;               // minimum posterior to emit a proposal

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Infer
    //
    // Groups the unaccepted zone-to-client messages by wire value, then scores every
    // unaccepted wire value on direction, flag head, cadence and reset burst.  The flag head
    // and reset burst backgrounds are the fractions of other unaccepted zone-to-client wire
    // values showing the same property.  A missing reset burst is neutral.  The log
    // likelihood ratios are summed and converted to a posterior with prior odds of one over
    // the number of observed opcodes; wire values at or above ReportThreshold are returned
    // with an itemized deciban ledger.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  The result, with proposals ordered by posterior, highest first; no proposals
    //           when the catalog is empty or nothing scores at threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IdentificationResult Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferAggro.Infer: starting", defaultLoglevel);
        IdentificationResult result = IdentificationResult.ForOpcode(OpcodeNameConstant);

        List<OpcodeValue> ranked = catalog.OpcodesByCountDescending();
        if (ranked.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "InferAggro.Infer: catalog empty", LogLevel.Warn);
            return result;
        }

        // group the unaccepted zone-to-client messages by wire value
        List<CatalogedPacket> zoneToClient = InferSupport.BuildZoneToClientList(catalog);
        Dictionary<OpcodeValue, List<CatalogedPacket>> buckets = new Dictionary<OpcodeValue, List<CatalogedPacket>>();
        foreach (CatalogedPacket packet in zoneToClient)
        {
            List<CatalogedPacket>? bucketOrNull;
            if (!buckets.TryGetValue(packet.Metadata.WireValue, out bucketOrNull))
            {
                bucketOrNull = new List<CatalogedPacket>();
                buckets[packet.Metadata.WireValue] = bucketOrNull;
            }
            bucketOrNull.Add(packet);
        }

        // per wire value flag head counts and reset bursts, with background totals
        Dictionary<OpcodeValue, int> headCounts = new Dictionary<OpcodeValue, int>();
        HashSet<OpcodeValue> withBurst = new HashSet<OpcodeValue>();
        int opcodesAllHead = 0;
        foreach (KeyValuePair<OpcodeValue, List<CatalogedPacket>> entry in buckets)
        {
            int heads = 0;
            foreach (CatalogedPacket packet in entry.Value)
            {
                if (HasAggroHead(packet.Payload.AsReadOnlySpan()))
                {
                    heads++;
                }
            }
            headCounts[entry.Key] = heads;
            if (heads == entry.Value.Count)
            {
                opcodesAllHead++;
            }
            if (HasResetBurst(entry.Value))
            {
                withBurst.Add(entry.Key);
            }
        }

        int otherOpcodeCount = buckets.Count - 1;
        if (otherOpcodeCount < 0)
        {
            otherOpcodeCount = 0;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferAggro.Infer: " + ranked.Count + " opcodes, " + buckets.Count
            + " unaccepted zone-to-client, " + opcodesAllHead + " all flag head, "
            + withBurst.Count + " with reset burst", defaultLoglevel);

        int opcodeCount = ranked.Count;
        double priorOdds = 1.0 / opcodeCount;
        Dictionary<SoeConstants.StreamId, int> pureChannelOpcodes = catalog.PureChannelOpcodeCounts();

        for (int rank = 0; rank < ranked.Count; rank++)
        {
            OpcodeValue wireValue = ranked[rank];
            if (AcceptedOpcodes.Instance.IsAccepted(wireValue))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferAggro.Infer: wireValue=" + wireValue + " already accepted as "
                    + AcceptedOpcodes.Instance.GetName(wireValue) + ", skipping", defaultLoglevel);
                continue;
            }
            OpcodeStats? statsOrNull = catalog.StatsFor(wireValue);
            if (statsOrNull == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferAggro.Infer: no stats for wireValue " + wireValue, LogLevel.Warn);
                continue;
            }
            OpcodeStats stats = statsOrNull.Value;

            List<CatalogedPacket>? bucket;
            if (!buckets.TryGetValue(wireValue, out bucket))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferAggro.Infer: wireValue=" + wireValue
                    + " has no zone-to-client messages, skipping", defaultLoglevel);
                continue;
            }

            int payloads = bucket.Count;
            int heads = headCounts[wireValue];
            int othersAllHead = opcodesAllHead - ((heads == payloads) ? 1 : 0);

            double directionLogLR = EvidenceScoring.DirectionLogLR(stats.ChannelCounts,
                SoeConstants.StreamId.StreamZoneToClient, pureChannelOpcodes, opcodeCount,
                LeakProbability, MaxAbsLogLR);
            double headLogLR = EvidenceScoring.PayloadPredicateLogLR(heads, payloads,
                othersAllHead, otherOpcodeCount, LeakProbability, MaxAbsLogLR);

            List<double> gapSeconds = CollectGaps(bucket);
            int scoredGaps;
            int conformingGaps;
            double cadenceLogLR = CadenceLogLR(gapSeconds, out scoredGaps, out conformingGaps);

            double resetLogLR = 0.0;
            bool burst = withBurst.Contains(wireValue);
            if (burst)
            {
                int othersWithBurst = withBurst.Count - 1;
                double q = (othersWithBurst + 1.0) / (otherOpcodeCount + 2.0);
                resetLogLR = -Math.Log(q);
                if (resetLogLR > MaxAbsLogLR)
                {
                    resetLogLR = MaxAbsLogLR;
                }
            }
            else
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferAggro.Infer: wireValue=" + wireValue + " has no reset burst, neutral",
                    defaultLoglevel);
            }

            double totalLogLR = directionLogLR + headLogLR + cadenceLogLR + resetLogLR;
            double posterior = EvidenceScoring.CombinePosterior(totalLogLR, priorOdds);

            DebugLog.Write(LogChannel.InferenceDebug,
                "InferAggro.Infer: rank " + rank + " wireValue=" + wireValue + " "
                + "dir=" + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1") + "db "
                + "head=" + EvidenceScoring.ToDecibans(headLogLR).ToString("F1") + "db "
                + "cadence=" + EvidenceScoring.ToDecibans(cadenceLogLR).ToString("F1") + "db "
                + "reset=" + EvidenceScoring.ToDecibans(resetLogLR).ToString("F1") + "db "
                + "total=" + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + "db "
                + "posterior=" + posterior.ToString("F3"), defaultLoglevel);

            if (posterior < ReportThreshold)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferAggro.Infer: wireValue=" + wireValue + " below threshold, skipping",
                    defaultLoglevel);
                continue;
            }

            string ledger =
                stats.Count + "/" + catalog.TotalPacketCount() + " messages, "
                + heads + "/" + payloads + " flag head, "
                + conformingGaps + "/" + scoredGaps + " gaps on cadence, "
                + (burst ? "reset burst" : "no reset burst") + " | "
                + "dir " + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1") + " db, "
                + "head " + EvidenceScoring.ToDecibans(headLogLR).ToString("F1") + " db, "
                + "cadence " + EvidenceScoring.ToDecibans(cadenceLogLR).ToString("F1") + " db, "
                + "reset " + EvidenceScoring.ToDecibans(resetLogLR).ToString("F1") + " db | "
                + "total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + " db, "
                + "prior 1/" + opcodeCount + ", "
                + "posterior " + posterior.ToString("F3");
            result.Proposals.Add(InferenceProposal.ForOpcode(OpcodeNameConstant,
                new PatchOpcode(GlassContext.CurrentPatchLevel, wireValue), posterior, ledger));
        }

        result.Proposals.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferAggro.Infer: " + result.Proposals.Count + " proposals", defaultLoglevel);
        return result;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HasAggroHead
    //
    // Tests whether a payload begins with a valid flag byte: 0 with room for the flag and
    // count, or 1 with room for the flag, a 32-bit spawn id and the count.
    //
    // payload:  The bytes to test.
    //
    // Returns:  True when the flag is 0 or 1 and the payload is long enough for its head.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool HasAggroHead(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < NoSpawnHeadLength)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferAggro.HasAggroHead: payload of " + payload.Length + " bytes too short for a head",
                defaultLoglevel);
            return false;
        }

        byte flag = payload[0];
        if (flag == FlagNoSpawn)
        {
            return true;
        }
        if (flag == FlagSpawn)
        {
            if (payload.Length < SpawnHeadLength)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferAggro.HasAggroHead: flag 1 with payload of " + payload.Length
                    + " bytes, too short for a spawn id", defaultLoglevel);
                return false;
            }
            return true;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferAggro.HasAggroHead: flag " + flag + " is not 0 or 1", defaultLoglevel);
        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CadenceLogLR
    //
    // Log likelihood ratio from how closely a candidate's per-session message gaps fall on
    // whole multiples of the send period.  Gaps shorter than CadenceMinimumGapSeconds or
    // longer than CadenceMaximumGapSeconds are not scored.  A scored gap conforms when it
    // lies within CadenceToleranceSeconds of a whole multiple of CadencePeriodSeconds.
    //
    //   P(conforms | IS)  = CadenceConformRateIfAggro
    //   P(conforms | NOT) = q = 2 * tolerance / period     a gap of random phase
    //
    //   log LR = k * (log p - log q) + (n - k) * (log(1 - p) - log(1 - q))
    //
    // with n the scored gaps and k the conforming ones.  The total is clamped to
    // +/- CadenceMaxAbsLogLR so the feature stays suggestive.
    //
    // gapSeconds:       Gaps between consecutive candidate messages on one session, in
    //                   seconds, from every session.
    // scoredGaps:       Receives the number of gaps scored.
    // conformingGaps:   Receives the number of scored gaps that conform.
    //
    // Returns:  Natural-log likelihood ratio from cadence, clamped.  Zero when no gap is
    //           scored or the tolerance leaves no chance rate below one.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double CadenceLogLR(List<double> gapSeconds, out int scoredGaps,
        out int conformingGaps)
    {
        scoredGaps = 0;
        conformingGaps = 0;

        double q = (2.0 * CadenceToleranceSeconds) / CadencePeriodSeconds;
        if (q <= 0.0 || q >= 1.0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferAggro.CadenceLogLR: chance rate " + q.ToString("F3")
                + " outside 0 to 1, returning 0", LogLevel.Error);
            return 0.0;
        }

        foreach (double gap in gapSeconds)
        {
            if (gap < CadenceMinimumGapSeconds || gap > CadenceMaximumGapSeconds)
            {
                continue;
            }

            scoredGaps++;
            double nearestMultiple = Math.Round(gap / CadencePeriodSeconds) * CadencePeriodSeconds;
            double distance = Math.Abs(gap - nearestMultiple);
            if (distance <= CadenceToleranceSeconds)
            {
                conformingGaps++;
            }
        }

        if (scoredGaps == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferAggro.CadenceLogLR: no gaps in range of " + gapSeconds.Count + ", returning 0",
                defaultLoglevel);
            return 0.0;
        }

        double p = CadenceConformRateIfAggro;
        double logLR = (conformingGaps * (Math.Log(p) - Math.Log(q)))
            + ((scoredGaps - conformingGaps) * (Math.Log(1.0 - p) - Math.Log(1.0 - q)));

        if (logLR > CadenceMaxAbsLogLR)
        {
            logLR = CadenceMaxAbsLogLR;
        }
        if (logLR < -CadenceMaxAbsLogLR)
        {
            logLR = -CadenceMaxAbsLogLR;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferAggro.CadenceLogLR: gaps=" + gapSeconds.Count + " scored=" + scoredGaps
            + " conforming=" + conformingGaps + " q=" + q.ToString("F3")
            + " logLR=" + logLR.ToString("F3"), defaultLoglevel);
        return logLR;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HasResetBurst
    //
    // Tests whether a candidate's messages contain a reset burst: reset messages reaching at
    // least ResetMinimumSessions distinct zone sessions within ResetWindowSeconds of the
    // first of them.  A reset message has flag 1, a payload long enough for the spawn id,
    // and a spawn id of 0.
    //
    // packets:  The candidate's zone-to-client messages, in arrival order.
    //
    // Returns:  True when at least one reset burst is found.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool HasResetBurst(List<CatalogedPacket> packets)
    {
        List<CatalogedPacket> resets = new List<CatalogedPacket>();

        foreach (CatalogedPacket packet in packets)
        {
            ReadOnlySpan<byte> payload = packet.Payload.AsReadOnlySpan();
            if (payload.Length < SpawnHeadLength || payload[0] != FlagSpawn)
            {
                continue;
            }
            uint spawnId = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(1, 4));
            if (spawnId != 0)
            {
                continue;
            }
            if (packet.Metadata.Session == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferAggro.HasResetBurst: reset message " + packet.Metadata.MessageId
                    + " has no session, skipped", LogLevel.Warn);
                continue;
            }
            resets.Add(packet);
        }

        if (resets.Count < ResetMinimumSessions)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferAggro.HasResetBurst: " + resets.Count + " reset messages, too few for a burst",
                defaultLoglevel);
            return false;
        }

        for (int first = 0; first < resets.Count; first++)
        {
            DateTime windowStart = resets[first].Metadata.Timestamp;
            HashSet<Session> sessions = new HashSet<Session>();

            for (int next = first; next < resets.Count; next++)
            {
                double elapsed = (resets[next].Metadata.Timestamp - windowStart).TotalSeconds;
                if (elapsed > ResetWindowSeconds)
                {
                    break;
                }
                sessions.Add(resets[next].Metadata.Session);
            }

            if (sessions.Count >= ResetMinimumSessions)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferAggro.HasResetBurst: burst of " + sessions.Count + " sessions starting at message "
                    + resets[first].Metadata.MessageId, defaultLoglevel);
                return true;
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferAggro.HasResetBurst: " + resets.Count + " reset messages, no burst across "
            + ResetMinimumSessions + " sessions", defaultLoglevel);
        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CollectGaps
    //
    // Computes the time between consecutive messages of a candidate on each session.  The
    // first message on a session starts its sequence and produces no gap.
    //
    // packets:  The candidate's zone-to-client messages, in arrival order.
    //
    // Returns:  The gaps in seconds, from every session.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static List<double> CollectGaps(List<CatalogedPacket> packets)
    {
        List<double> gapSeconds = new List<double>();
        Dictionary<Session, DateTime> lastSeen = new Dictionary<Session, DateTime>();

        foreach (CatalogedPacket packet in packets)
        {
            Session session = packet.Metadata.Session;
            if (session == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferAggro.CollectGaps: message " + packet.Metadata.MessageId
                    + " has no session, skipped", LogLevel.Warn);
                continue;
            }

            DateTime timestamp = packet.Metadata.Timestamp;
            DateTime previous;
            if (lastSeen.TryGetValue(session, out previous))
            {
                double gap = (timestamp - previous).TotalSeconds;
                if (gap < 0.0)
                {
                    DebugLog.Write(LogChannel.InferenceDebug,
                        "InferAggro.CollectGaps: message " + packet.Metadata.MessageId
                        + " is " + (-gap).ToString("F3") + " s earlier than the previous on session "
                        + session.SessionId + ", gap skipped", LogLevel.Warn);
                }
                else
                {
                    gapSeconds.Add(gap);
                }
            }

            lastSeen[session] = timestamp;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferAggro.CollectGaps: " + gapSeconds.Count + " gaps from " + packets.Count
            + " messages on " + lastSeen.Count + " sessions", defaultLoglevel);
        return gapSeconds;
    }
}