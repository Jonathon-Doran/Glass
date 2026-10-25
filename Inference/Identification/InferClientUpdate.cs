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
// InferClientUpdate
//
// Identifies OP_ClientUpdate.  The message is frequent and bidirectional, fixed length per
// direction.  Client-to-zone payloads are currently 46 bytes and carry the sending
// character's own spawn id at offset 2.  Zone-to-client payloads are currently 24 bytes and
// carry the spawn id of another spawn in the zone at offset 0; they may be absent.
//
// Only packets on zone sessions whose Session has a ZoneId are examined.  Every application
// wire value with such packets is examined so that each background covers all zone traffic.
// Only the unaccepted wire values among the CandidateCount highest by packet count are
// scored; that selection is a gate and contributes no evidence.  Each candidate is scored on
// the client-to-zone self id and length and the zone-to-client spawn id and length.  Spawn
// ids are checked against the repositories as they stand when inference runs, so spawns
// that moved or were removed earlier in the capture do not validate.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferClientUpdate : IInferOpcodes
{
    private const string OpcodeNameConstant = "OP_ClientUpdate";
    private const int CandidateCount = 10;                 // wire values scored, highest count first
    private const int ToZoneLength = 46;                   // current client-to-zone payload length
    private const int ToClientLength = 24;                 // current zone-to-client payload length
    private const int SelfIdOffset = 2;                    // client-to-zone offset of the sender's spawn id
    private const int SpawnIdOffset = 0;                   // zone-to-client offset of the spawn id
    private const double LengthLeakProbability = 0.01;     // per distinct length other than the expected one
    private const double LengthDistancePenalty = 0.5;      // log-LR per byte away from the expected length
    private const double ValidRateIfClientUpdate = 0.9;    // assumed share of packets whose id validates
    private const double MaxAbsLogLR = 12.0;               // clamp per feature, roughly +/- 52 decibans
    private const double ReportThreshold = 0.40;           // minimum posterior to emit a proposal

    ///////////////////////////////////////////////////////////////////////////////////////////
    // WireFacts
    //
    // One wire value's packets on zone sessions with a ZoneId.  ToZone counts client-to-zone
    // packets and ToZoneSelfId how many carry the session character's own spawn id at
    // SelfIdOffset.  ToClient counts zone-to-client packets and ToClientKnown how many carry
    // a known spawn id at SpawnIdOffset.  Each direction has its own packet count per length.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class WireFacts
    {
        public OpcodeValue WireValue;
        public int ToZone = 0;
        public int ToZoneSelfId = 0;
        public Dictionary<int, int> ToZoneLengths = new Dictionary<int, int>();
        public int ToClient = 0;
        public int ToClientKnown = 0;
        public Dictionary<int, int> ToClientLengths = new Dictionary<int, int>();
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
    // Examines every application wire value, selects the candidates by count, totals each
    // direction's spawn id results across all examined wire values, then scores each
    // candidate.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  Proposals ordered by posterior descending, empty when nothing was examined,
    //           no candidate was examined, or nothing scores at threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<InferenceProposal> Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferClientUpdate.Infer: starting", LogLevel.Trace);
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
            if (wire.ToZone + wire.ToClient > 0)
            {
                facts.Add(wire);
            }
        }

        if (facts.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferClientUpdate.Infer: no packets on zone sessions with a zone, no proposals",
                LogLevel.Warn);
            return proposals;
        }

        List<OpcodeValue> candidates = SelectCandidates(catalog);

        int totalToZone = 0;
        int totalSelfId = 0;
        int totalToClient = 0;
        int totalKnown = 0;
        for (int i = 0; i < facts.Count; i++)
        {
            totalToZone += facts[i].ToZone;
            totalSelfId += facts[i].ToZoneSelfId;
            totalToClient += facts[i].ToClient;
            totalKnown += facts[i].ToClientKnown;
        }

        Dictionary<int, int> pooledSizes = catalog.PooledSizeHistogram();
        for (int i = 0; i < facts.Count; i++)
        {
            if (!candidates.Contains(facts[i].WireValue))
            {
                continue;
            }
            InferenceProposal? proposal = ScoreCandidate(facts[i], facts.Count, totalToZone,
                totalSelfId, totalToClient, totalKnown, pooledSizes);
            if (proposal != null)
            {
                proposals.Add(proposal);
            }
        }

        proposals.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferClientUpdate.Infer: " + facts.Count + " wire values examined, "
            + proposals.Count + " proposals", LogLevel.Info);
        return proposals;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SelectCandidates
    //
    // Takes wire values in order of packet count, skipping net opcodes and wire values
    // already accepted this session, until CandidateCount are collected.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  Up to CandidateCount wire values, highest count first.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static List<OpcodeValue> SelectCandidates(PacketCatalog catalog)
    {
        List<OpcodeValue> byCount = catalog.OpcodesByCountDescending();
        List<OpcodeValue> candidates = new List<OpcodeValue>();

        for (int i = 0; i < byCount.Count && candidates.Count < CandidateCount; i++)
        {
            OpcodeValue wireValue = byCount[i];
            if (SoeConstants.IsNetOpcode(wireValue.Value))
            {
                continue;
            }
            if (AcceptedOpcodes.Instance.IsAccepted(wireValue))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferClientUpdate.SelectCandidates: wireValue=" + wireValue + " already accepted as "
                    + AcceptedOpcodes.Instance.GetName(wireValue) + ", skipping", LogLevel.Trace);
                continue;
            }
            candidates.Add(wireValue);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferClientUpdate.SelectCandidates: " + candidates.Count + " candidates", LogLevel.Trace);
        return candidates;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ZoneSessionOf
    //
    // Finds a packet's session when it is a zone session with a zone.
    //
    // packet:   The packet.
    // session:  Receives the packet's Session, or null when it does not qualify.
    //
    // Returns:  True when the packet belongs to a zone session with a zone.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool ZoneSessionOf(CatalogedPacket packet, out Session? session)
    {
        session = packet.Metadata.Session;
        if (session == null || session.ServerType != ServerType.Zone || !session.ZoneId.Exists)
        {
            session = null;
            return false;
        }
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ReadSpawnId
    //
    // Reads a little-endian 32-bit spawn id at an offset.
    //
    // payload:  The bytes to read.
    // offset:   Position of the id.
    // spawnId:  Receives the id.
    //
    // Returns:  True when the payload is long enough to hold the id.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool ReadSpawnId(ReadOnlySpan<byte> payload, int offset, out Id<SpawnTag> spawnId)
    {
        spawnId = Id<SpawnTag>.None;
        if (payload.Length < offset + 4)
        {
            return false;
        }
        spawnId = (Id<SpawnTag>)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset));
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // IsSelfId
    //
    // Tests whether a payload carries, at SelfIdOffset, the spawn id of the session's own
    // character in the session's zone.
    //
    // payload:  The bytes to test.
    // session:  The packet's zone session.
    //
    // Returns:  True when the id resolves to the session's own character.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool IsSelfId(ReadOnlySpan<byte> payload, Session session)
    {
        Id<SpawnTag> spawnId;
        if (session.Character == null || !ReadSpawnId(payload, SelfIdOffset, out spawnId))
        {
            return false;
        }
        var character = CharacterRepository.Instance.GetBySpawnId(session.ZoneId, spawnId);
        return character != null && ReferenceEquals(character, session.Character);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // IsKnownSpawnId
    //
    // Tests whether a payload carries, at SpawnIdOffset, a spawn id known in the session's
    // zone.
    //
    // payload:  The bytes to test.
    // session:  The packet's zone session.
    //
    // Returns:  True when the id is a known spawn in the zone.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool IsKnownSpawnId(ReadOnlySpan<byte> payload, Session session)
    {
        Id<SpawnTag> spawnId;
        if (!ReadSpawnId(payload, SpawnIdOffset, out spawnId))
        {
            return false;
        }
        return ZoneRepository.Instance.IsKnownSpawn(session.ZoneId, spawnId);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddLength
    //
    // Adds one packet of the given length to a length histogram.
    //
    // lengths:  The histogram to update.
    // length:   The payload length.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void AddLength(Dictionary<int, int> lengths, int length)
    {
        int seen;
        if (lengths.TryGetValue(length, out seen))
        {
            lengths[length] = seen + 1;
        }
        else
        {
            lengths[length] = 1;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ExamineWireValue
    //
    // Walks one wire value's packets once, keeping only packets on zone sessions with a
    // zone, and tallies each direction's spawn id test and lengths.
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
            Session? session;
            if (!ZoneSessionOf(packet, out session) || session == null)
            {
                continue;
            }

            ReadOnlySpan<byte> payload = packet.Payload.AsReadOnlySpan();
            if (packet.Metadata.ToServer)
            {
                wire.ToZone++;
                AddLength(wire.ToZoneLengths, payload.Length);
                if (IsSelfId(payload, session))
                {
                    wire.ToZoneSelfId++;
                }
            }
            else
            {
                wire.ToClient++;
                AddLength(wire.ToClientLengths, payload.Length);
                if (IsKnownSpawnId(payload, session))
                {
                    wire.ToClientKnown++;
                }
            }
        }

        if (wire.ToZone + wire.ToClient > 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferClientUpdate.ExamineWireValue: wireValue=" + wireValue
                + " C2Z " + wire.ToZoneSelfId + "/" + wire.ToZone + " self id"
                + ", Z2C " + wire.ToClientKnown + "/" + wire.ToClient + " known id", LogLevel.Trace);
        }
        return wire;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScoreCandidate
    //
    // Sums the self id, client-to-zone length, spawn id, and zone-to-client length log
    // likelihood ratios for one candidate and converts the total to a posterior with prior
    // odds of one over the number of examined wire values.  Builds a proposal with an
    // itemized deciban ledger when the posterior reaches ReportThreshold.
    //
    // candidate:      The candidate's facts.
    // examinedCount:  Number of examined wire values.
    // totalToZone:    Client-to-zone packets across all examined wire values.
    // totalSelfId:    Of those, packets carrying the sender's own id.
    // totalToClient:  Zone-to-client packets across all examined wire values.
    // totalKnown:     Of those, packets carrying a known spawn id.
    // pooledSizes:    Message count per length across the capture.
    //
    // Returns:  The proposal, or null when the posterior is below threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private InferenceProposal? ScoreCandidate(WireFacts candidate, int examinedCount,
        int totalToZone, int totalSelfId, int totalToClient, int totalKnown,
        Dictionary<int, int> pooledSizes)
    {
        double selfIdLogLR = RateLogLR(candidate.ToZoneSelfId, candidate.ToZone,
            totalSelfId - candidate.ToZoneSelfId, totalToZone - candidate.ToZone);
        double toZoneLengthLogLR = EvidenceScoring.SizeRangeLogLR(candidate.ToZoneLengths,
            ToZoneLength, ToZoneLength, pooledSizes, LengthLeakProbability, LengthDistancePenalty,
            MaxAbsLogLR);
        double spawnIdLogLR = RateLogLR(candidate.ToClientKnown, candidate.ToClient,
            totalKnown - candidate.ToClientKnown, totalToClient - candidate.ToClient);
        double toClientLengthLogLR = EvidenceScoring.SizeRangeLogLR(candidate.ToClientLengths,
            ToClientLength, ToClientLength, pooledSizes, LengthLeakProbability, LengthDistancePenalty,
            MaxAbsLogLR);

        double totalLogLR = selfIdLogLR + toZoneLengthLogLR + spawnIdLogLR + toClientLengthLogLR;
        double posterior = EvidenceScoring.CombinePosterior(totalLogLR, 1.0 / examinedCount);

        string ledger =
            "C2Z " + candidate.ToZoneSelfId + "/" + candidate.ToZone + " self id, "
            + "Z2C " + candidate.ToClientKnown + "/" + candidate.ToClient + " known id | "
            + "self id " + EvidenceScoring.ToDecibans(selfIdLogLR).ToString("F1") + " db, "
            + "C2Z length " + EvidenceScoring.ToDecibans(toZoneLengthLogLR).ToString("F1") + " db, "
            + "spawn id " + EvidenceScoring.ToDecibans(spawnIdLogLR).ToString("F1") + " db, "
            + "Z2C length " + EvidenceScoring.ToDecibans(toClientLengthLogLR).ToString("F1") + " db | "
            + "total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + " db, "
            + "prior 1/" + examinedCount + ", posterior " + posterior.ToString("F3");

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferClientUpdate.ScoreCandidate: wireValue=" + candidate.WireValue + " " + ledger,
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
    // RateLogLR
    //
    // Log likelihood ratio from how many of a candidate's packets pass a test.  Under the
    // hypothesis each packet passes with ValidRateIfClientUpdate; otherwise with q, the
    // smoothed pass rate across the other wire values' packets:
    //
    //   log LR = k * (log(p) - log(q)) + (n - k) * (log(1 - p) - log(1 - q))
    //
    // Packets arrive in correlated bursts, so the total is clamped to +/- MaxAbsLogLR.  Zero
    // when the candidate has no packets.
    //
    // passing:       The candidate's packets that pass.
    // packets:       The candidate's packets.
    // otherPassing:  Other wire values' packets that pass.
    // otherPackets:  Other wire values' packets.
    //
    // Returns:  Natural-log likelihood ratio from the pass rate, clamped.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double RateLogLR(int passing, int packets, int otherPassing, int otherPackets)
    {
        if (packets == 0)
        {
            return 0.0;
        }

        double q = (otherPassing + 1.0) / (otherPackets + 2.0);
        double p = ValidRateIfClientUpdate;
        double logLR = passing * (Math.Log(p) - Math.Log(q))
            + (packets - passing) * (Math.Log(1.0 - p) - Math.Log(1.0 - q));

        if (logLR > MaxAbsLogLR)
        {
            logLR = MaxAbsLogLR;
        }
        if (logLR < -MaxAbsLogLR)
        {
            logLR = -MaxAbsLogLR;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferClientUpdate.RateLogLR: passing=" + passing + "/" + packets
            + " q=" + q.ToString("F4") + " logLR=" + logLR.ToString("F3"), LogLevel.Trace);
        return logLR;
    }
}