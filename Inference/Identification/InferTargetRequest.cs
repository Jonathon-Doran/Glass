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
// InferTargetRequest
//
// Identifies OP_Target_Request.  Payloads seen so far are 4 bytes, client-to-zone, holding
// a 16-bit spawn id followed by two zero bytes.  Each request is answered by one
// zone-to-client packet carrying the same payload.
//
// Only packets on zone sessions are examined.  Every application wire value with such
// packets is examined so that each background covers all zone traffic; wire values already
// accepted are kept in the backgrounds but not proposed.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferTargetRequest : IInferOpcodes
{
    private const string OpcodeNameConstant = "OP_Target_Request";
    private const double LeakProbability = 0.001;          // per-packet allowance for a failed test
    private const int ExpectedLength = 4;                  // current OP_Target_Request payload length
    private const double LengthLeakProbability = 0.01;     // per distinct length other than ExpectedLength
    private const double LengthDistancePenalty = 0.5;      // log-LR per byte away from ExpectedLength
    private const double MaxAbsLogLR = 12.0;               // clamp per feature, roughly +/- 52 decibans
    private const double ReportThreshold = 0.40;           // minimum posterior to emit a proposal

    ///////////////////////////////////////////////////////////////////////////////////////////
    // WireFacts
    //
    // One wire value's packets on zone sessions.  Packets counts them and ToServer how many
    // are client-to-zone; SpawnPass counts how many hold a known spawn id with zero upper
    // bytes, and EchoPass how many are followed by one zone-to-client packet with the same
    // payload before the next packet of this wire value on that session.  Lengths holds the
    // packet count per length.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class WireFacts
    {
        public OpcodeValue WireValue;
        public int Packets = 0;
        public int ToServer = 0;
        public int SpawnPass = 0;
        public int EchoPass = 0;
        public int EchoNextRequest = 0;
        public int EchoPayloadMismatch = 0;
        public int EchoEndOfSearch = 0;
        public Dictionary<int, int> Lengths = new Dictionary<int, int>();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // EchoOutcome
    //
    // How the search for a request's echo ended.  Matched: a zone-to-client packet on the
    // same session carried an identical payload.  NextRequest: the next packet of the
    // request's wire value on the same session was reached first.  PayloadMismatch: no
    // match before the search ended, but a zone-to-client packet on the same session had the
    // request's length with different bytes.  EndOfSearch: the search reached its end index
    // with neither a match nor a same-length packet.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private enum EchoOutcome
    {
        Matched,
        NextRequest,
        PayloadMismatch,
        EndOfSearch
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
    // Examines every application wire value, totals each test's results across all of
    // them, then scores each unaccepted wire value.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  Proposals ordered by posterior descending, empty when no wire value has
    //           packets on a zone session or nothing scores at threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<InferenceProposal> Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferTargetRequest.Infer: starting", LogLevel.Info);
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
            if (wire.Packets > 0)
            {
                facts.Add(wire);
            }
        }

        if (facts.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferTargetRequest.Infer: no packets on zone sessions, no proposals", LogLevel.Warn);
            return proposals;
        }

        int allToServer = 0;
        int allSpawn = 0;
        int allEcho = 0;
        for (int i = 0; i < facts.Count; i++)
        {
            if (facts[i].ToServer == facts[i].Packets)
            {
                allToServer++;
            }
            if (facts[i].SpawnPass == facts[i].Packets)
            {
                allSpawn++;
            }
            if (facts[i].EchoPass == facts[i].Packets)
            {
                allEcho++;
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferTargetRequest.Infer: backgrounds over " + facts.Count + " wire values, all C2Z "
            + allToServer + ", all spawn " + allSpawn + ", all echo " + allEcho, LogLevel.Info);

        Dictionary<int, int> pooledSizes = catalog.PooledSizeHistogram();
        for (int i = 0; i < facts.Count; i++)
        {
            if (AcceptedOpcodes.Instance.IsAccepted(facts[i].WireValue))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferTargetRequest.Infer: wireValue=" + facts[i].WireValue + " already accepted as "
                    + AcceptedOpcodes.Instance.GetName(facts[i].WireValue) + ", skipping", LogLevel.Info);
                continue;
            }
            InferenceProposal? proposal = ScoreCandidate(facts[i], allToServer, allSpawn, allEcho,
                facts.Count, pooledSizes);
            if (proposal != null)
            {
                proposals.Add(proposal);
            }
        }

        proposals.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferTargetRequest.Infer: " + facts.Count + " wire values examined, "
            + proposals.Count + " proposals", LogLevel.Info);
        return proposals;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // IsZonePacket
    //
    // Tests whether a packet belongs to a zone session.
    //
    // packet:  The packet.
    //
    // Returns:  True when the packet's Session is a zone session.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool IsZonePacket(CatalogedPacket packet)
    {
        Session session = packet.Metadata.Session;
        return session != null && session.ServerType == ServerType.Zone;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PassesSpawn
    //
    // Tests whether the payload is a 16-bit spawn id known in the zone, followed by two zero
    // bytes.
    //
    // payload:  The bytes to test.
    // zoneId:   The zone of the packet's session.
    //
    // Returns:  True when the payload is ExpectedLength bytes, the upper 16 bits are zero,
    //           and the lower 16 bits are a known spawn id in the zone.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool PassesSpawn(ReadOnlySpan<byte> payload, ZoneId zoneId)
    {
        if (payload.Length < 4)
        {
            return false;
        }

        if (payload[2] != 0 || payload[3] != 0)
        {
            return false;
        }
        ushort rawSpawnId = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        if (rawSpawnId == 0)
        {
            return true;
        }
        SpawnId spawnId = (SpawnId)(uint)rawSpawnId;
        return ZoneRepository.Instance.IsKnownSpawn(zoneId, spawnId);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindResponse
    //
    // Searches for a zone-to-client packet on the request's session carrying a payload
    // identical to the request's.  Packets are examined in arrival order from the one after
    // the request up to, but not including, endIndex.  Packets that do not match are ignored.
    //
    // catalog:   The session's cataloged packets.
    // request:   The request packet.
    // endIndex:  Arrival index at which the search stops: the next packet of the request's
    //            wire value on the same session, or the catalog's total packet count.
    //
    // Returns:  True when a matching packet is found before endIndex.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool FindResponse(PacketCatalog catalog, CatalogedPacket request, uint endIndex)
    {
        Session session = request.Metadata.Session;
        ReadOnlySpan<byte> requestPayload = request.Payload.AsReadOnlySpan();
        bool correctRequest = false;
        bool correctResponse = false;

        if (request.Opcode == 0x720c)
        {
            correctRequest = true;
        }

        for (uint index = request.PacketIndex + 1; index < endIndex; index++)
        {
            CatalogedPacket? found = catalog.PacketAt(index);
            if (found == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferTargetRequest.FindResponse: no packet at index " + index
                    + ", stopping search", LogLevel.Error);
                return false;
            }
            CatalogedPacket following = found.Value;
            if (correctRequest && following.Opcode == 0xf71c)
            {
                correctResponse = true;
            }

            if (!ReferenceEquals(following.Metadata.Session, session))
            {
                continue;
            }
            if (following.Metadata.ToServer)
            {
                continue;
            }
            if (following.Payload.AsReadOnlySpan().SequenceEqual(requestPayload))
            {
                return true;
            }
        }
        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ExamineWireValue
    //
    // Walks one wire value's packets once, from last to first, keeping only packets on zone
    // sessions, and tallies direction, length, and the spawn and echo tests.  The echo test
    // for each packet stops at the next packet of this wire value on the same session, or
    // at the end of the catalog when there is none.
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

        uint catalogEnd = (uint)catalog.TotalPacketCount();
        Dictionary<Session, uint> nextIndexBySession = new Dictionary<Session, uint>();

        List<CatalogedPacket> bucket = catalog.PacketsFor(wireValue, null, null, int.MaxValue);
        for (int i = bucket.Count - 1; i >= 0; i--)
        {
            if (!IsZonePacket(bucket[i]))
            {
                continue;
            }

            Session session = bucket[i].Metadata.Session;
            ReadOnlySpan<byte> payload = bucket[i].Payload.AsReadOnlySpan();

            uint endIndex;
            if (!nextIndexBySession.TryGetValue(session, out endIndex))
            {
                endIndex = catalogEnd;
            }
            nextIndexBySession[session] = bucket[i].PacketIndex;

            wire.Packets++;
            if (bucket[i].Metadata.ToServer)
            {
                wire.ToServer++;
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

            if (PassesSpawn(payload, session.ZoneId))
            {
                wire.SpawnPass++;
            }
            if (FindResponse(catalog, bucket[i], endIndex))
            {
                wire.EchoPass++;
            }
        }

        if (wire.Packets > 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferTargetRequest.ExamineWireValue: wireValue=" + wireValue + " " + wire.Packets
                + " packets, " + wire.ToServer + " C2Z, spawn " + wire.SpawnPass
                + ", echo " + wire.EchoPass, LogLevel.Info);
        }

        if (wire.Packets > 0 && wire.SpawnPass == wire.Packets
                 && wire.EchoPass > 0 && wire.EchoPass < wire.Packets)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferTargetRequest.ExamineWireValue: wireValue=" + wireValue + " passes spawn on all "
                + wire.Packets + ", echo matched " + wire.EchoPass + ", next request first "
                + wire.EchoNextRequest + ", payload mismatch " + wire.EchoPayloadMismatch
                + ", end of search " + wire.EchoEndOfSearch, LogLevel.Warn);
        }
        return wire;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScoreCandidate
    //
    // Scores the direction, spawn and echo tests once for the wire value: passing on every
    // packet is compared with how many other examined wire values do the same, and each
    // failing packet is charged the leak.  Length is scored against ExpectedLength.  Sums the
    // log likelihood ratios and converts the total to a posterior with prior odds of one over
    // the number of examined wire values.  Builds a proposal with an itemized deciban ledger
    // when the posterior reaches ReportThreshold.
    //
    // candidate:      The candidate's facts.
    // allToServer:    Examined wire values whose every packet is client-to-zone.
    // allSpawn:       Examined wire values whose every packet passes the spawn test.
    // allEcho:        Examined wire values whose every packet passes the echo test.
    // examinedCount:  Number of examined wire values.
    // pooledSizes:    Message count per length across the capture.
    //
    // Returns:  The proposal, or null when the posterior is below threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private InferenceProposal? ScoreCandidate(WireFacts candidate, int allToServer, int allSpawn,
        int allEcho, int examinedCount, Dictionary<int, int> pooledSizes)
    {
        int others = examinedCount - 1;

        int othersToServer = allToServer - (candidate.ToServer == candidate.Packets ? 1 : 0);
        double directionLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.ToServer,
            candidate.Packets, othersToServer, others, LeakProbability, MaxAbsLogLR);

        double lengthLogLR = EvidenceScoring.SizeRangeLogLR(candidate.Lengths, ExpectedLength,
            ExpectedLength, pooledSizes, LengthLeakProbability, LengthDistancePenalty, MaxAbsLogLR);

        int othersSpawn = allSpawn - (candidate.SpawnPass == candidate.Packets ? 1 : 0);
        double spawnLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.SpawnPass,
            candidate.Packets, othersSpawn, others, LeakProbability, MaxAbsLogLR);

        int othersEcho = allEcho - (candidate.EchoPass == candidate.Packets ? 1 : 0);
        double echoLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.EchoPass,
            candidate.Packets, othersEcho, others, LeakProbability, MaxAbsLogLR);

        double totalLogLR = directionLogLR + lengthLogLR + spawnLogLR + echoLogLR;
        double posterior = EvidenceScoring.CombinePosterior(totalLogLR, 1.0 / examinedCount);

        string ledger =
            candidate.Packets + " packets, " + candidate.ToServer + " C2Z, spawn "
            + candidate.SpawnPass + ", echo " + candidate.EchoPass + " | "
            + "direction " + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1") + " db, "
            + "length " + EvidenceScoring.ToDecibans(lengthLogLR).ToString("F1") + " db, "
            + "spawn " + EvidenceScoring.ToDecibans(spawnLogLR).ToString("F1") + " db, "
            + "echo " + EvidenceScoring.ToDecibans(echoLogLR).ToString("F1") + " db | "
            + "total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + " db, "
            + "prior 1/" + examinedCount + ", posterior " + posterior.ToString("F3");

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferTargetRequest.ScoreCandidate: wireValue=" + candidate.WireValue + " " + ledger,
            LogLevel.Info);

        if (posterior < ReportThreshold)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferTargetRequest.ScoreCandidate: wireValue=" + candidate.WireValue
                + " below threshold, no proposal", LogLevel.Trace);
            return null;
        }

        InferenceProposal proposal = new InferenceProposal();
        proposal.Label = Label;
        proposal.Opcode = new PatchOpcode(GlassContext.CurrentPatchLevel, candidate.WireValue);
        proposal.Confidence = posterior;
        proposal.Evidence = ledger;
        return proposal;
    }
}