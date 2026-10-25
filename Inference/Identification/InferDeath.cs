using Glass.Core;
using Glass.Core.Logging;
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
// InferDeath
//
// Identifies OP_Death.  The zone server sends it to the client; it is fixed length,
// currently 40 bytes, and begins with two 32-bit spawn ids (the spawn that died and its
// killer).
//
// Only packets on zone sessions whose Session has a ZoneId are examined.  Every application
// wire value with such packets is examined so that each background covers all zone
// traffic; wire values already accepted are kept in the backgrounds but not proposed.  Each
// candidate is scored on direction, length, and spawn id validity.  Spawn ids are checked
// against the zone repository as it stands when inference runs, so spawns removed earlier
// in the capture, or ids the server reused, do not validate.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferDeath : IInferOpcodes
{
    private const string OpcodeNameConstant = "OP_Death";
    private const int ExpectedLength = 40;                 // current OP_Death payload length
    private const double LeakProbability = 0.001;          // per-packet allowance for a client-to-zone packet
    private const double LengthLeakProbability = 0.01;     // per distinct length other than ExpectedLength
    private const double LengthDistancePenalty = 0.5;      // log-LR per byte away from ExpectedLength
    private const double SpawnValidRateIfDeath = 0.9;      // assumed share of OP_Death packets whose ids validate
    private const double MaxAbsLogLR = 12.0;               // clamp per feature, roughly +/- 52 decibans
    private const double ReportThreshold = 0.40;           // minimum posterior to emit a proposal

    ///////////////////////////////////////////////////////////////////////////////////////////
    // WireFacts
    //
    // One wire value's packets on zone sessions with a ZoneId.  Payloads counts them,
    // ToClient how many are zone-to-client, and ValidSpawns how many begin with two spawn
    // ids known in the session's zone.  Lengths holds the packet count per length.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class WireFacts
    {
        public OpcodeValue WireValue;
        public int Payloads = 0;
        public int ToClient = 0;
        public int ValidSpawns = 0;
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
    // Examines every application wire value, totals the spawn id results across all of
    // them, then scores each unaccepted wire value.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  Proposals ordered by posterior descending, empty when no wire value has
    //           packets on a zone session with a ZoneId or nothing scores at threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<InferenceProposal> Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferDeath.Infer: starting", LogLevel.Trace);
        List<InferenceProposal> proposals = new List<InferenceProposal>();

        List<WireFacts> facts = new List<WireFacts>();
        OpcodeValue[] known = catalog.KnownOpcodes();
        for (int i = 0; i < known.Length; i++)
        {
            if (SoeConstants.IsNetOpcode(known[i].Value))
            {
                continue;
            }
            WireFacts wire = ExamineWireValue(catalog, known[i]);
            if (wire.Payloads > 0)
            {
                facts.Add(wire);
            }
        }

        if (facts.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferDeath.Infer: no packets on zone sessions with a zone, no proposals", LogLevel.Warn);
            return proposals;
        }

        int totalPayloads = 0;
        int totalValid = 0;
        for (int i = 0; i < facts.Count; i++)
        {
            totalPayloads += facts[i].Payloads;
            totalValid += facts[i].ValidSpawns;
        }

        Dictionary<int, int> pooledSizes = catalog.PooledSizeHistogram();
        for (int i = 0; i < facts.Count; i++)
        {
            if (AcceptedOpcodes.Instance.IsAccepted(facts[i].WireValue))
            {
                continue;
            }
            InferenceProposal? proposal = ScoreCandidate(facts, i, totalPayloads, totalValid, pooledSizes);
            if (proposal != null)
            {
                proposals.Add(proposal);
            }
        }

        proposals.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferDeath.Infer: " + facts.Count + " wire values examined, "
            + proposals.Count + " proposals", LogLevel.Info);
        return proposals;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ZoneOf
    //
    // Finds the zone of a packet's session.
    //
    // packet:  The packet.
    // zoneId:  Receives the session's zone, or none when the packet has no Session, the
    //          Session is not a zone session, or it has no zone.
    //
    // Returns:  True when the packet belongs to a zone session with a zone.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool ZoneOf(CatalogedPacket packet, out Id<ZoneTag> zoneId)
    {
        zoneId = Id<ZoneTag>.None;
        Session session = packet.Metadata.Session;
        if (session == null || session.ServerType != ServerType.Zone || !session.ZoneId.Exists)
        {
            return false;
        }
        zoneId = session.ZoneId;
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HasKnownSpawns
    //
    // Tests whether a payload begins with two little-endian 32-bit spawn ids that are both
    // known in the given zone.
    //
    // payload:  The bytes to test.
    // zoneId:   The zone the ids would be valid in.
    //
    // Returns:  True when both ids are known spawns in the zone.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool HasKnownSpawns(ReadOnlySpan<byte> payload, Id<ZoneTag> zoneId)
    {
        if (payload.Length < 8)
        {
            return false;
        }

        Id<SpawnTag> first = (Id<SpawnTag>)BinaryPrimitives.ReadUInt32LittleEndian(payload);
        Id<SpawnTag> second = (Id<SpawnTag>)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4));

        return ZoneRepository.Instance.IsKnownSpawn(zoneId, first)
            && ZoneRepository.Instance.IsKnownSpawn(zoneId, second);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ExamineWireValue
    //
    // Walks one wire value's packets once, keeping only packets on zone sessions with a
    // zone, and tallies direction, length, and spawn id validity.
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
            CatalogedPacket packet = bucket[i];
            Id<ZoneTag> zoneId;
            if (!ZoneOf(packet, out zoneId))
            {
                continue;
            }

            ReadOnlySpan<byte> payload = packet.Payload.AsReadOnlySpan();
            wire.Payloads++;
            if (!packet.Metadata.ToServer)
            {
                wire.ToClient++;
            }
            if (HasKnownSpawns(payload, zoneId))
            {
                wire.ValidSpawns++;
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
        }

        if (wire.Payloads > 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferDeath.ExamineWireValue: wireValue=" + wireValue + " " + wire.Payloads
                + " packets, " + wire.ToClient + " Z2C, " + wire.ValidSpawns + " valid spawn ids, "
                + wire.Lengths.Count + " lengths", LogLevel.Trace);
        }
        return wire;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScoreCandidate
    //
    // Sums the direction, length, and spawn id log likelihood ratios for one wire value and
    // converts the total to a posterior with prior odds of one over the number of examined
    // wire values.  Builds a proposal with an itemized deciban ledger when the posterior
    // reaches ReportThreshold.
    //
    // facts:          Every examined wire value's facts.
    // candidateIndex: Position of the candidate being scored in facts.
    // totalPayloads:  Packets across all examined wire values.
    // totalValid:     Packets with valid spawn ids across all examined wire values.
    // pooledSizes:    Message count per length across the capture.
    //
    // Returns:  The proposal, or null when the posterior is below threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private InferenceProposal? ScoreCandidate(List<WireFacts> facts, int candidateIndex,
        int totalPayloads, int totalValid, Dictionary<int, int> pooledSizes)
    {
        WireFacts candidate = facts[candidateIndex];

        double directionLogLR = ToClientLogLR(facts, candidateIndex);
        double lengthLogLR = EvidenceScoring.SizeRangeLogLR(candidate.Lengths, ExpectedLength,
            ExpectedLength, pooledSizes, LengthLeakProbability, LengthDistancePenalty, MaxAbsLogLR);
        double spawnLogLR = SpawnLogLR(candidate, totalPayloads, totalValid);
        double totalLogLR = directionLogLR + lengthLogLR + spawnLogLR;
        double posterior = EvidenceScoring.CombinePosterior(totalLogLR, 1.0 / facts.Count);

        string ledger =
            candidate.Payloads + " packets, " + candidate.ToClient + " Z2C, "
            + candidate.ValidSpawns + " valid spawn ids, " + candidate.Lengths.Count + " lengths | "
            + "direction " + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1") + " db, "
            + "length " + EvidenceScoring.ToDecibans(lengthLogLR).ToString("F1") + " db, "
            + "spawn " + EvidenceScoring.ToDecibans(spawnLogLR).ToString("F1") + " db | "
            + "total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + " db, "
            + "prior 1/" + facts.Count + ", posterior " + posterior.ToString("F3");

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferDeath.ScoreCandidate: wireValue=" + candidate.WireValue + " " + ledger,
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

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ToClientLogLR
    //
    // Log likelihood ratio from how many of the candidate's packets are zone-to-client,
    // against the other examined wire values all of whose packets are.
    //
    // facts:           Every examined wire value's facts.
    // candidateIndex:  Position of the candidate being scored in facts.
    //
    // Returns:  Natural-log likelihood ratio from direction, clamped.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double ToClientLogLR(List<WireFacts> facts, int candidateIndex)
    {
        int othersAllToClient = 0;
        for (int other = 0; other < facts.Count; other++)
        {
            if (other != candidateIndex && facts[other].ToClient == facts[other].Payloads)
            {
                othersAllToClient++;
            }
        }

        WireFacts candidate = facts[candidateIndex];
        return EvidenceScoring.PayloadPredicateLogLR(candidate.ToClient, candidate.Payloads,
            othersAllToClient, facts.Count - 1, LeakProbability, MaxAbsLogLR);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SpawnLogLR
    //
    // Log likelihood ratio from how many of the candidate's packets begin with two valid
    // spawn ids.  Under the hypothesis each packet validates with SpawnValidRateIfDeath;
    // otherwise with q, the smoothed rate across the other examined wire values' packets:
    //
    //   log LR = k * (log(p) - log(q)) + (n - k) * (log(1 - p) - log(1 - q))
    //
    // with n the candidate's packets and k those that validate.  Packets arrive in
    // correlated bursts, so the total is clamped to +/- MaxAbsLogLR.
    //
    // candidate:      The candidate's facts.
    // totalPayloads:  Packets across all examined wire values.
    // totalValid:     Packets with valid spawn ids across all examined wire values.
    //
    // Returns:  Natural-log likelihood ratio from spawn id validity, clamped.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double SpawnLogLR(WireFacts candidate, int totalPayloads, int totalValid)
    {
        int otherPayloads = totalPayloads - candidate.Payloads;
        int otherValid = totalValid - candidate.ValidSpawns;
        double q = (otherValid + 1.0) / (otherPayloads + 2.0);
        double p = SpawnValidRateIfDeath;

        int n = candidate.Payloads;
        int k = candidate.ValidSpawns;
        double logLR = k * (Math.Log(p) - Math.Log(q))
            + (n - k) * (Math.Log(1.0 - p) - Math.Log(1.0 - q));

        if (logLR > MaxAbsLogLR)
        {
            logLR = MaxAbsLogLR;
        }
        if (logLR < -MaxAbsLogLR)
        {
            logLR = -MaxAbsLogLR;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferDeath.SpawnLogLR: wireValue=" + candidate.WireValue + " valid=" + k + "/" + n
            + " q=" + q.ToString("F4") + " logLR=" + logLR.ToString("F3"), LogLevel.Trace);
        return logLR;
    }
}
