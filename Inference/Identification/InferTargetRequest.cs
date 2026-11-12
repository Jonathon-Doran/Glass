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
    private const LogLevel defaultLoglevel = LogLevel.Info;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // WireFacts
    //
    // One wire value's packets on zone sessions.  Packets counts them and ToServer how many
    // are client-to-zone.  SpawnTested counts the packets the spawn test judged (did not
    // abstain on), and SpawnPass how many of those hold a known spawn id with zero upper
    // bytes.  EchoTested counts the packets the echo test judged, and EchoPass how many of
    // those are followed by one zone-to-client packet with the same payload before the next
    // packet of this wire value on that session.  Lengths holds the packet count per length.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class WireFacts
    {
        public OpcodeValue WireValue;
        public int Packets = 0;
        public int ToServer = 0;
        public int SpawnTested = 0;
        public int SpawnPass = 0;
        public int EchoTested = 0;
        public int EchoPass = 0;
        public int EchoNextRequest = 0;
        public int EchoPayloadMismatch = 0;
        public int EchoEndOfSearch = 0;
        public Dictionary<int, int> Lengths = new Dictionary<int, int>();

        ///////////////////////////////////////////////////////////////////////////////////////
        // AllToServer
        //
        // True when the wire value has packets and every one of them is client-to-zone.
        ///////////////////////////////////////////////////////////////////////////////////////
        public bool AllToServer
        {
            get { return Packets > 0 && ToServer == Packets; }
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // AllSpawnPass
        //
        // True when the spawn test judged at least one packet and passed every packet it
        // judged.  A wire value the test abstained on entirely is not counted as passing.
        ///////////////////////////////////////////////////////////////////////////////////////
        public bool AllSpawnPass
        {
            get { return SpawnTested > 0 && SpawnPass == SpawnTested; }
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // AllEchoPass
        //
        // True when the echo test judged at least one packet and passed every packet it
        // judged.  A wire value the test abstained on entirely is not counted as passing.
        ///////////////////////////////////////////////////////////////////////////////////////
        public bool AllEchoPass
        {
            get { return EchoTested > 0 && EchoPass == EchoTested; }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CandidateEvidence
    //
    // One unaccepted wire value's score before normalization: its facts, the sum of its
    // per-feature log likelihood ratios, and the itemized deciban ledger for its proposal.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class CandidateEvidence
    {
        public readonly WireFacts Facts;
        public readonly double TotalLogLR;
        public readonly string Ledger;

        ///////////////////////////////////////////////////////////////////////////////////////
        // CandidateEvidence (constructor)
        //
        // Stores the wire value's facts, summed log likelihood ratio and ledger.
        //
        // facts:       The wire value's facts.
        // totalLogLR:  Sum of the wire value's per-feature natural-log likelihood ratios.
        // ledger:      Itemized per-feature decibans and the total, without a posterior.
        ///////////////////////////////////////////////////////////////////////////////////////
        public CandidateEvidence(WireFacts facts, double totalLogLR, string ledger)
        {
            Facts = facts;
            TotalLogLR = totalLogLR;
            Ledger = ledger;
        }
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
    // TestResult
    //
    // The outcome of one per-packet test.  Pass and Fail count toward the test's tally.
    // Abstain means the packet carries nothing the test can judge, such as a zero spawn id
    // or an all-zero payload, and the packet is left out of the test's tally entirely.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private enum TestResult
    {
        Pass,
        Fail,
        Abstain
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
    // them, then scores each unaccepted wire value.  The scores are normalized against one
    // another and against a none-of-these hypothesis carrying one candidate's prior weight:
    //
    //   posterior(i) = exp(L(i)) / (1 + sum over unaccepted j of exp(L(j)))
    //
    // so candidates compete for the opcode and a capture without it leaves every
    // candidate low.  Candidates below ReportThreshold are not proposed.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  Proposals ordered by posterior descending, empty when no wire value has
    //           packets on a zone session or nothing scores at threshold.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<InferenceProposal> Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferTargetRequest.Infer: starting", defaultLoglevel);
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
            if (facts[i].AllToServer)
            {
                allToServer++;
            }
            if (facts[i].AllSpawnPass)
            {
                allSpawn++;
            }
            if (facts[i].AllEchoPass)
            {
                allEcho++;
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferTargetRequest.Infer: backgrounds over " + facts.Count + " wire values, all C2Z "
            + allToServer + ", all spawn " + allSpawn + ", all echo " + allEcho, defaultLoglevel);

        Dictionary<int, int> pooledSizes = catalog.PooledSizeHistogram();
        List<CandidateEvidence> scored = new List<CandidateEvidence>();
        for (int i = 0; i < facts.Count; i++)
        {
            if (AcceptedOpcodes.Instance.IsAccepted(facts[i].WireValue))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferTargetRequest.Infer: wireValue=" + facts[i].WireValue + " already accepted as "
                    + AcceptedOpcodes.Instance.GetName(facts[i].WireValue) + ", skipping", defaultLoglevel);
                continue;
            }
            scored.Add(ScoreCandidate(facts[i], allToServer, allSpawn, allEcho,
                facts.Count, pooledSizes));
        }

        if (scored.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferTargetRequest.Infer: every examined wire value is accepted, no proposals",
                defaultLoglevel);
            return proposals;
        }

        double weightSum = 1.0;
        for (int i = 0; i < scored.Count; i++)
        {
            weightSum += Math.Exp(scored[i].TotalLogLR);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferTargetRequest.Infer: " + scored.Count + " candidates, none-of-these posterior "
            + (1.0 / weightSum).ToString("F3"), defaultLoglevel);

        scored.Sort((a, b) => b.TotalLogLR.CompareTo(a.TotalLogLR));
        int shown = Math.Min(3, scored.Count);
        for (int i = 0; i < shown; i++)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferTargetRequest.Infer: rank " + (i + 1) + " wireValue=" + scored[i].Facts.WireValue
                + " " + scored[i].Ledger + ", posterior "
                + (Math.Exp(scored[i].TotalLogLR) / weightSum).ToString("F3"), defaultLoglevel);
        }

        for (int i = 0; i < scored.Count; i++)
        {
            double posterior = Math.Exp(scored[i].TotalLogLR) / weightSum;
            if (posterior < ReportThreshold)
            {
                continue;
            }

            InferenceProposal proposal = new InferenceProposal();
            proposal.Label = Label;
            proposal.Opcode = new PatchOpcode(GlassContext.CurrentPatchLevel, scored[i].Facts.WireValue);
            proposal.Confidence = posterior;
            proposal.Evidence = scored[i].Ledger + ", posterior " + posterior.ToString("F3")
                + " over " + scored.Count + " candidates and none";
            proposals.Add(proposal);
        }

        proposals.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferTargetRequest.Infer: " + facts.Count + " wire values examined, "
            + proposals.Count + " proposals", defaultLoglevel);
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
    // TestSpawn
    //
    // Tests whether the payload is a 16-bit spawn id known in the zone, followed by two zero
    // bytes.  A spawn id of zero is not evidence of anything, so the test abstains on it.
    //
    // payload:  The bytes to test.
    // zoneId:   The zone of the packet's session.
    //
    // Returns:  Abstain when the payload is at least 4 bytes, the upper 16 bits are zero and
    //           the spawn id is zero.  Pass when the upper 16 bits are zero and the lower 16
    //           bits are a known spawn id in the zone.  Fail otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static TestResult TestSpawn(ReadOnlySpan<byte> payload, ZoneId zoneId)
    {
        if (payload.Length < 4)
        {
            return TestResult.Fail;
        }

        if (payload[2] != 0 || payload[3] != 0)
        {
            return TestResult.Fail;
        }

        ushort rawSpawnId = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        if (rawSpawnId == 0)
        {
            return TestResult.Abstain;
        }

        SpawnId spawnId = (SpawnId)(uint)rawSpawnId;
        if (ZoneRepository.Instance.IsKnownSpawn(zoneId, spawnId))
        {
            return TestResult.Pass;
        }

        return TestResult.Fail;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindResponse
    //
    // Searches for a zone-to-client packet on the request's session carrying a payload
    // identical to the request's.  Packets are examined in arrival order from the one after
    // the request up to, but not including, endIndex.  Packets that do not match are ignored.
    // A request whose payload is all zeros, or empty, is not evidence of anything, since
    // zeros match far too much, so the test abstains on it without searching.
    //
    // catalog:   The session's cataloged packets.
    // request:   The request packet.
    // endIndex:  Message id at which the search stops: the next packet of the request's
    //            wire value on the same session, or one past the last message id.
    //
    // Returns:  Abstain when the request payload holds no nonzero byte.  Pass when a matching
    //           packet is found before endIndex.  Fail otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static TestResult FindResponse(PacketCatalog catalog, CatalogedPacket request, uint endIndex)
    {
        Session session = request.Metadata.Session;
        ReadOnlySpan<byte> requestPayload = request.Payload.AsReadOnlySpan();

        if (!requestPayload.ContainsAnyExcept((byte)0))
        {
            return TestResult.Abstain;
        }

        for (uint index = request.PacketIndex + 1; index < endIndex; index++)
        {
            CatalogedPacket? found = catalog.PacketAt(index);
            if (found == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferTargetRequest.FindResponse: no packet at index " + index
                    + ", stopping search", LogLevel.Error);
                return TestResult.Fail;
            }
            CatalogedPacket following = found.Value;

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
                return TestResult.Pass;
            }
        }
        return TestResult.Fail;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ExamineWireValue
    //
    // Walks one wire value's packets once, from last to first, keeping only packets on zone
    // sessions, and tallies direction, length, and the spawn and echo tests.  A test that
    // abstains on a packet leaves that packet out of the test's tally.  The echo test for
    // each packet stops at the next packet of this wire value on the same session, or one
    // past the last message id when there is none.
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

        uint catalogEnd = (uint)catalog.TotalPacketCount() + 1;
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

            TestResult spawnResult = TestSpawn(payload, session.ZoneId);
            if (spawnResult != TestResult.Abstain)
            {
                wire.SpawnTested++;
                if (spawnResult == TestResult.Pass)
                {
                    wire.SpawnPass++;
                }
            }

            TestResult echoResult = FindResponse(catalog, bucket[i], endIndex);
            if (echoResult != TestResult.Abstain)
            {
                wire.EchoTested++;
                if (echoResult == TestResult.Pass)
                {
                    wire.EchoPass++;
                }
            }
        }

        if (wire.SpawnTested > 0 && wire.SpawnPass == wire.SpawnTested
                 && wire.EchoPass > 0 && wire.EchoPass < wire.EchoTested)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferTargetRequest.ExamineWireValue: wireValue=" + wireValue + " passes spawn on all "
                + wire.SpawnTested + " tested, echo matched " + wire.EchoPass + " of "
                + wire.EchoTested + " tested", LogLevel.Warn);
        }
        return wire;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScoreCandidate
    //
    // Scores the direction, spawn and echo tests once for the wire value: passing on every
    // judged packet is compared with how many other examined wire values do the same, and
    // each failing packet is charged the leak.  The spawn and echo tests are scored over the
    // packets they judged; a test that abstained on every packet contributes zero.  Length
    // is scored against ExpectedLength.  Sums the log likelihood ratios and builds an
    // itemized deciban ledger ending with the total.
    //
    // candidate:      The candidate's facts.
    // allToServer:    Examined wire values whose every packet is client-to-zone.
    // allSpawn:       Examined wire values whose every judged packet passes the spawn test.
    // allEcho:        Examined wire values whose every judged packet passes the echo test.
    // examinedCount:  Number of examined wire values.
    // pooledSizes:    Message count per length across the capture.
    //
    // Returns:  The candidate's summed log likelihood ratio and ledger.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private CandidateEvidence ScoreCandidate(WireFacts candidate, int allToServer, int allSpawn,
        int allEcho, int examinedCount, Dictionary<int, int> pooledSizes)
    {
        int others = examinedCount - 1;

        int othersToServer = allToServer - (candidate.AllToServer ? 1 : 0);
        double directionLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.ToServer,
            candidate.Packets, othersToServer, others, LeakProbability, MaxAbsLogLR);

        double lengthLogLR = EvidenceScoring.SizeRangeLogLR(candidate.Lengths, ExpectedLength,
            ExpectedLength, pooledSizes, LengthLeakProbability, LengthDistancePenalty, MaxAbsLogLR);

        double spawnLogLR = 0.0;
        if (candidate.SpawnTested > 0)
        {
            int othersSpawn = allSpawn - (candidate.AllSpawnPass ? 1 : 0);
            spawnLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.SpawnPass,
                candidate.SpawnTested, othersSpawn, others, LeakProbability, MaxAbsLogLR);
        }

        double echoLogLR = 0.0;
        if (candidate.EchoTested > 0)
        {
            int othersEcho = allEcho - (candidate.AllEchoPass ? 1 : 0);
            echoLogLR = EvidenceScoring.PayloadPredicateLogLR(candidate.EchoPass,
                candidate.EchoTested, othersEcho, others, LeakProbability, MaxAbsLogLR);
        }

        double totalLogLR = directionLogLR + lengthLogLR + spawnLogLR + echoLogLR;

        string ledger =
            candidate.Packets + " packets, " + candidate.ToServer + " C2Z, spawn "
            + candidate.SpawnPass + "/" + candidate.SpawnTested + ", echo "
            + candidate.EchoPass + "/" + candidate.EchoTested + " | "
            + "direction " + EvidenceScoring.ToDecibans(directionLogLR).ToString("F1") + " db, "
            + "length " + EvidenceScoring.ToDecibans(lengthLogLR).ToString("F1") + " db, "
            + "spawn " + EvidenceScoring.ToDecibans(spawnLogLR).ToString("F1") + " db, "
            + "echo " + EvidenceScoring.ToDecibans(echoLogLR).ToString("F1") + " db | "
            + "total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + " db";

        return new CandidateEvidence(candidate, totalLogLR, ledger);
    }
}