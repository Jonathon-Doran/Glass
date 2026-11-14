using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Network.Client;
using Glass.Network.Protocol;
using Inference.Core;
using Inference.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// InferRaidGeneral
//
// Identifies OP_Raid_General.  The zone server sends it to the client in many subtypes, and
// every payload begins with a small 32-bit subtype followed by the null-terminated name of a
// character.  Every wire value is tallied in one pass.  A candidate is scored on two
// features, each a log likelihood ratio against the other wire values in the capture: every
// payload has that header naming a known character, and every zone session with a character
// received one naming that session's own character.  The second feature only supports and
// never refutes.  A wire value with too few distinct subtypes, or with a single payload size,
// is not a candidate.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferRaidGeneral : IInferOpcodes
{
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    private const string OpcodeNameConstant = "OP_Raid_General";

    private const int SubtypeLength = 4;                 // bytes in the subtype at the start of a payload
    private const uint SubtypeLimit = 0x80;              // a subtype must be below this value
    private const int MinSubtypes = 3;                   // distinct subtypes a candidate must carry
    private const int ReportedCandidates = 3;            // best proposals returned, highest confidence first
    private const int ExampleCount = 5;                  // message numbers reported per failed test
    private const double ReportThreshold = 0.40;         // minimum posterior to emit a proposal
    private const double LeakProbability = 0.001;        // per-payload allowance for a failed header
    private const double MaxAbsLogLR = 12.0;             // clamp per feature, roughly +/- 52 decibans

    ///////////////////////////////////////////////////////////////////////////////////////////
    // WireTally
    //
    // What one wire value's zone-to-client packets showed.  Payloads is the packet count and
    // HeaderHits the number with the raid header.  Subtypes holds the distinct subtypes of
    // the hits.  ZoneInSessions holds the zone sessions that received a hit naming the
    // session's own character.  HeaderMissing holds up to ExampleCount message numbers of
    // packets without the header.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class WireTally
    {
        public int Payloads;
        public int HeaderHits;
        public HashSet<uint> Subtypes = new HashSet<uint>();
        public HashSet<Session> ZoneInSessions = new HashSet<Session>();
        public List<uint> HeaderMissing = new List<uint>();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Infer
    //
    // Collects the characters on the capture's zone sessions and the unaccepted
    // zone-to-client packets, tallies every wire value, and counts the background: the wire
    // values whose packets all have the raid header, and those received with a header naming
    // the session's own character on every zone session with a character.  Every unaccepted
    // wire value with more than one payload size and at least MinSubtypes distinct subtypes
    // is scored with prior odds of one over the number of observed opcodes, its own result
    // removed from the background.  Proposals at or above ReportThreshold are kept, and the
    // best ReportedCandidates are returned, highest posterior first.  The counts at each
    // stage are added to the result's messages.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  The result for OP_Raid_General.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IdentificationResult Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferRaidGeneral.Infer: starting", defaultLoglevel);

        IdentificationResult result = IdentificationResult.ForOpcode(OpcodeNameConstant);

        List<Character> characters = InferSupport.CollectCharacters(catalog);
        if (characters.Count == 0)
        {
            result.Messages.Add("no zone session has a character");
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferRaidGeneral.Infer: no zone session has a character", LogLevel.Warn);
            return result;
        }
        result.Messages.Add(characters.Count + " characters");

        List<CatalogedPacket> packets = InferSupport.BuildZoneToClientList(catalog);
        if (packets.Count == 0)
        {
            result.Messages.Add("no unaccepted zone-to-client traffic");
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferRaidGeneral.Infer: no unaccepted zone-to-client traffic", LogLevel.Warn);
            return result;
        }

        List<byte[]> nameBytes = new List<byte[]>();
        for (int characterIndex = 0; characterIndex < characters.Count; characterIndex++)
        {
            nameBytes.Add(Encoding.ASCII.GetBytes(characters[characterIndex].Name));
        }

        HashSet<Session> characterSessions = new HashSet<Session>();
        Dictionary<OpcodeValue, WireTally> tallies = TallyPackets(packets, characters, nameBytes, characterSessions);

        int allHeader = 0;
        int allZoneIn = 0;
        foreach (WireTally tally in tallies.Values)
        {
            if (tally.Payloads > 0 && tally.HeaderHits == tally.Payloads)
            {
                allHeader++;
            }
            if (characterSessions.Count > 0 && tally.ZoneInSessions.Count == characterSessions.Count)
            {
                allZoneIn++;
            }
        }

        List<OpcodeValue> ranked = catalog.OpcodesByCountDescending();
        int opcodeCount = ranked.Count;
        double priorOdds = 1.0 / opcodeCount;

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferRaidGeneral.Infer: " + opcodeCount + " opcodes, " + allHeader + " all with the header, "
            + allZoneIn + " zone-in on every session", defaultLoglevel);

        List<InferenceProposal> proposals = new List<InferenceProposal>();
        int scored = 0;

        for (int rank = 0; rank < ranked.Count; rank++)
        {
            OpcodeValue wireValue = ranked[rank];
            if (AcceptedOpcodes.Instance.IsAccepted(wireValue))
            {
                continue;
            }

            OpcodeStats? statsOrNull = catalog.StatsFor(wireValue);
            if (statsOrNull == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferRaidGeneral.Infer: no stats for wireValue " + wireValue, LogLevel.Warn);
                continue;
            }
            if (statsOrNull.Value.DistinctSizeCount < 2)
            {
                continue;
            }

            WireTally? candidate;
            if (!tallies.TryGetValue(wireValue, out candidate))
            {
                continue;
            }
            if (candidate.Subtypes.Count < MinSubtypes)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferRaidGeneral.Infer: wireValue " + wireValue + " has " + candidate.Subtypes.Count
                    + " subtypes, not a candidate", defaultLoglevel);
                continue;
            }

            bool candidateAllHeader = candidate.Payloads > 0 && candidate.HeaderHits == candidate.Payloads;
            bool candidateAllZoneIn = characterSessions.Count > 0
                && candidate.ZoneInSessions.Count == characterSessions.Count;
            int othersAllHeader = allHeader - (candidateAllHeader ? 1 : 0);
            int othersAllZoneIn = allZoneIn - (candidateAllZoneIn ? 1 : 0);

            InferenceProposal proposal = ScoreCandidate(wireValue, candidate, characterSessions, othersAllHeader,
                othersAllZoneIn, opcodeCount, priorOdds);
            scored++;

            if (proposal.Confidence < ReportThreshold)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferRaidGeneral.Infer: wireValue " + wireValue + " posterior "
                    + proposal.Confidence.ToString("F3") + " below threshold, skipping", defaultLoglevel);
                continue;
            }
            proposals.Add(proposal);
        }

        result.Messages.Add(scored + " candidates scored, " + proposals.Count + " at threshold");
        if (proposals.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferRaidGeneral.Infer: " + scored + " candidates scored, none at threshold", LogLevel.Warn);
            return result;
        }

        proposals.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
        int reported = Math.Min(ReportedCandidates, proposals.Count);
        for (int proposalIndex = 0; proposalIndex < reported; proposalIndex++)
        {
            result.Proposals.Add(proposals[proposalIndex]);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferRaidGeneral.Infer: " + scored + " candidates scored, " + reported + " reported, best "
            + result.Proposals[0].Confidence.ToString("F3"), defaultLoglevel);
        return result;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ReadHeader
    //
    // Tests whether a payload begins with the raid header: a 32-bit little-endian subtype
    // below SubtypeLimit, immediately followed by the name of a known character and a null
    // byte.
    //
    // payload:     The bytes to test.
    // characters:  The known characters.
    // nameBytes:   Each known character's name as ASCII bytes, in the same order.
    // subtype:     Receives the subtype when the header is present, 0 otherwise.
    // named:       Receives the character named in the header, or null.
    //
    // Returns:  True when the header is present.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool ReadHeader(ReadOnlySpan<byte> payload, List<Character> characters, List<byte[]> nameBytes,
        out uint subtype, out Character? named)
    {
        subtype = 0;
        named = null;

        if (payload.Length < SubtypeLength)
        {
            return false;
        }

        uint value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(payload);
        if (value >= SubtypeLimit)
        {
            return false;
        }

        for (int nameIndex = 0; nameIndex < nameBytes.Count; nameIndex++)
        {
            byte[] name = nameBytes[nameIndex];
            int terminator = SubtypeLength + name.Length;
            if (name.Length == 0 || terminator >= payload.Length)
            {
                continue;
            }
            if (payload[terminator] != 0x00)
            {
                continue;
            }
            if (payload.Slice(SubtypeLength, name.Length).SequenceEqual(name))
            {
                subtype = value;
                named = characters[nameIndex];
                return true;
            }
        }

        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TallyPackets
    //
    // Walks the zone-to-client packets once and builds a tally for every wire value: its
    // packet count, how many have the raid header, the distinct subtypes of those, the zone
    // sessions that received one naming the session's own character, and up to
    // ExampleCount message numbers of packets without the header.  Every zone session with a
    // character is added to characterSessions.
    //
    // packets:            The unaccepted zone-to-client packets.
    // characters:         The known characters.
    // nameBytes:          Each known character's name as ASCII bytes, in the same order.
    // characterSessions:  Receives every zone session with a character.
    //
    // Returns:  The tally of each wire value, keyed by wire value.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static Dictionary<OpcodeValue, WireTally> TallyPackets(List<CatalogedPacket> packets,
        List<Character> characters, List<byte[]> nameBytes, HashSet<Session> characterSessions)
    {
        Dictionary<OpcodeValue, WireTally> tallies = new Dictionary<OpcodeValue, WireTally>();
        int headerHits = 0;

        for (int packetIndex = 0; packetIndex < packets.Count; packetIndex++)
        {
            CatalogedPacket packet = packets[packetIndex];
            Session session = packet.Metadata.Session;
            Character? owner = session.Character;
            if (owner != null)
            {
                characterSessions.Add(session);
            }

            OpcodeValue wireValue = packet.Metadata.WireValue;
            WireTally? tally;
            if (!tallies.TryGetValue(wireValue, out tally))
            {
                tally = new WireTally();
                tallies[wireValue] = tally;
            }
            tally.Payloads++;

            uint subtype;
            Character? named;
            if (!ReadHeader(packet.Payload.AsReadOnlySpan(), characters, nameBytes, out subtype, out named)
                || named == null)
            {
                if (tally.HeaderMissing.Count < ExampleCount)
                {
                    tally.HeaderMissing.Add(packet.PacketIndex);
                }
                continue;
            }
            tally.HeaderHits++;
            tally.Subtypes.Add(subtype);
            headerHits++;

            if (owner != null && owner.Name == named.Name)
            {
                tally.ZoneInSessions.Add(session);
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferRaidGeneral.TallyPackets: " + packets.Count + " packets, " + tallies.Count + " wire values, "
            + headerHits + " with the raid header, " + characterSessions.Count + " zone sessions with a character",
            defaultLoglevel);
        return tallies;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScoreCandidate
    //
    // Scores one wire value as OP_Raid_General from its tally.  The header feature is one
    // observation over the candidate's payloads: every payload has the raid header, scored
    // against the other wire values whose payloads all have it, with each failing payload
    // charged LeakProbability.  The zone-in feature is one observation over the zone
    // sessions with a character: every one received a header naming its own character,
    // scored against the other wire values that did the same; when any session did not, it
    // scores 0.  The two log likelihood ratios are summed and converted to a posterior with
    // the prior odds.
    //
    // wireValue:          The wire value to score.
    // tally:              The wire value's tally.
    // characterSessions:  Every zone session with a character.
    // othersAllHeader:    Other wire values whose payloads all have the raid header.
    // othersAllZoneIn:    Other wire values received with an own-name header on every zone
    //                     session with a character.
    // opcodeCount:        Number of distinct opcodes observed in the capture.
    // priorOdds:          Prior odds for any one wire value.
    //
    // Returns:  The proposal, with the posterior as its confidence and an itemized ledger.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static InferenceProposal ScoreCandidate(OpcodeValue wireValue, WireTally tally,
        HashSet<Session> characterSessions, int othersAllHeader, int othersAllZoneIn, int opcodeCount,
        double priorOdds)
    {
        int otherOpcodeCount = opcodeCount - 1;

        double headerLogLR = EvidenceScoring.PayloadPredicateLogLR(tally.HeaderHits, tally.Payloads,
            othersAllHeader, otherOpcodeCount, LeakProbability, MaxAbsLogLR);

        double zoneInLogLR = 0.0;
        List<SessionId> zoneInMissing = new List<SessionId>();
        foreach (Session session in characterSessions)
        {
            if (!tally.ZoneInSessions.Contains(session) && zoneInMissing.Count < ExampleCount)
            {
                zoneInMissing.Add(session.SessionId);
            }
        }

        if (characterSessions.Count > 0 && tally.ZoneInSessions.Count == characterSessions.Count)
        {
            zoneInLogLR = EvidenceScoring.PayloadPredicateLogLR(tally.ZoneInSessions.Count,
                characterSessions.Count, othersAllZoneIn, otherOpcodeCount, LeakProbability, MaxAbsLogLR);
        }
        else
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferRaidGeneral.ScoreCandidate: wireValue " + wireValue + " zone-in "
                + tally.ZoneInSessions.Count + "/" + characterSessions.Count + " sessions, feature scores 0",
                defaultLoglevel);
        }

        double totalLogLR = headerLogLR + zoneInLogLR;
        double posterior = EvidenceScoring.CombinePosterior(totalLogLR, priorOdds);

        string ledger =
            "header " + tally.HeaderHits + "/" + tally.Payloads + " packets, others all "
            + othersAllHeader + "/" + otherOpcodeCount + ", "
            + EvidenceScoring.ToDecibans(headerLogLR).ToString("F1") + " db"
            + (tally.HeaderMissing.Count > 0 ? ", missing at " + string.Join(", ", tally.HeaderMissing) : string.Empty)
            + " | "
            + "zone-in " + tally.ZoneInSessions.Count + "/" + characterSessions.Count + " sessions, others all "
            + othersAllZoneIn + "/" + otherOpcodeCount + ", "
            + EvidenceScoring.ToDecibans(zoneInLogLR).ToString("F1") + " db"
            + (zoneInMissing.Count > 0 ? ", missing on sessions " + string.Join(", ", zoneInMissing) : string.Empty)
            + " | "
            + "total " + EvidenceScoring.ToDecibans(totalLogLR).ToString("F1") + " db, "
            + "prior 1/" + opcodeCount + ", "
            + "posterior " + posterior.ToString("F3");

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferRaidGeneral.ScoreCandidate: wireValue " + wireValue + " " + tally.Subtypes.Count + " subtypes | "
            + ledger, defaultLoglevel);

        return InferenceProposal.ForOpcode(OpcodeNameConstant,
            new PatchOpcode(GlassContext.CurrentPatchLevel, wireValue), posterior, ledger);
    }
}