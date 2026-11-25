using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Client;
using Glass.Network.Protocol;
using Inference.Core;
using Inference.Models;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// InferCastBegin
//
// Identifies OP_CastBegin, the zone's reply that a cast has started.  It is taken to be the
// first zone-to-client message of BeginLength bytes on a session after an OP_CastRequest on
// that session, and its payload is taken to begin with the requested spell id.  Requires an
// accepted OP_CastRequest.  Each request votes for the wire value of that first message, and
// the vote is a hit when the message begins with the request's spell id.  A wire value's
// share is its hits divided by the number of requests.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferCastBegin : IInferOpcodes
{
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    private const string OpcodeNameConstant = "OP_CastBegin";
    private const string RequestOpcodeName = "OP_CastRequest";

    private const int RequestSpellIdOffset = 4;          // offset of the 32-bit spell id in a cast request
    private const int BeginLength = 15;                  // payload length of a cast begin
    private const int BeginSpellIdOffset = 0;            // offset of the 32-bit spell id in a cast begin
    private const int MinHits = 2;                       // hits a wire value needs before it can be chosen
    private const int ExampleCount = 5;                  // message numbers reported per failed test
    private const double ReportThreshold = 0.40;         // minimum share to emit a proposal

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PendingRequest
    //
    // A cast request still waiting for its first BeginLength message.  PacketIndex is the
    // request's message number and SpellId the spell id it carries.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class PendingRequest
    {
        public uint PacketIndex;
        public uint SpellId;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Infer
    //
    // Requires an accepted OP_CastRequest wire value; without one, no proposal is made.
    // Tallies each request's vote, then chooses the wire value with the most hits among those
    // with at least MinHits.  When two or more share the most hits, none is chosen.  The
    // chosen wire value's share is its hits divided by the number of requests, and it is
    // proposed when the share reaches ReportThreshold.  The counts at each stage are added to
    // the result's messages.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  The result for OP_CastBegin.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IdentificationResult Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferCastBegin.Infer: starting", defaultLoglevel);

        IdentificationResult result = IdentificationResult.ForOpcode(OpcodeNameConstant);

        OpcodeValue requestWireValue = AcceptedOpcodes.Instance.WireValueFor(RequestOpcodeName);
        if (!requestWireValue.Exists)
        {
            result.Messages.Add(RequestOpcodeName + " not accepted");
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferCastBegin.Infer: " + RequestOpcodeName + " not accepted, no proposal", LogLevel.Warn);
            return result;
        }

        Dictionary<OpcodeValue, int> votes = new Dictionary<OpcodeValue, int>();
        Dictionary<OpcodeValue, int> hits = new Dictionary<OpcodeValue, int>();
        List<uint> unanswered = new List<uint>();
        int requests = TallyVotes(catalog, requestWireValue, votes, hits, unanswered);

        result.Messages.Add(requests + " cast requests");
        if (requests == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferCastBegin.Infer: no cast requests, no proposal", LogLevel.Warn);
            return result;
        }

        OpcodeValue winner = OpcodeValue.None;
        int winnerHits = 0;
        List<OpcodeValue> best = new List<OpcodeValue>();
        foreach (KeyValuePair<OpcodeValue, int> entry in hits)
        {
            if (entry.Value < MinHits)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferCastBegin.Infer: wireValue " + entry.Key + " has " + entry.Value + " hits, below "
                    + MinHits + ", not eligible", defaultLoglevel);
                continue;
            }
            if (entry.Value > winnerHits)
            {
                winner = entry.Key;
                winnerHits = entry.Value;
                best.Clear();
                best.Add(entry.Key);
            }
            else if (entry.Value == winnerHits)
            {
                best.Add(entry.Key);
            }
        }

        if (best.Count == 0)
        {
            result.Messages.Add("no wire value with " + MinHits + " hits");
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferCastBegin.Infer: no wire value with " + MinHits + " hits, no proposal", LogLevel.Warn);
            return result;
        }
        if (best.Count > 1)
        {
            string tied = string.Join(", ", best);
            result.Messages.Add("tie at " + winnerHits + " hits between " + tied);
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferCastBegin.Infer: tie at " + winnerHits + " hits between " + tied + ", no proposal",
                LogLevel.Warn);
            return result;
        }

        double share = (double)winnerHits / requests;
        string evidence = winnerHits + "/" + requests + " requests answered by a " + BeginLength
            + "-byte message starting with the spell id, " + votes.GetValueOrDefault(winner) + " votes"
            + (unanswered.Count > 0 ? ", unanswered at " + string.Join(", ", unanswered) : string.Empty);

        if (share < ReportThreshold)
        {
            result.Messages.Add(winner + " share " + share.ToString("F2") + " below threshold");
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferCastBegin.Infer: wireValue " + winner + " " + evidence + ", share " + share.ToString("F2")
                + " below threshold, no proposal", defaultLoglevel);
            return result;
        }

        result.Proposals.Add(InferenceProposal.ForOpcode(OpcodeNameConstant,
            new PatchOpcode(GlassContext.CurrentPatchLevel, winner), share, evidence));

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferCastBegin.Infer: proposed " + winner + ", " + evidence + ", share " + share.ToString("F2"),
            defaultLoglevel);
        return result;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TallyVotes
    //
    // Walks the capture once in arrival order, on zone sessions only.  Each client-to-zone
    // packet of the request wire value becomes the session's pending request, replacing any
    // request still pending there.  The first zone-to-client packet of BeginLength bytes on
    // the session after it, of a wire value not yet accepted, is the request's vote: its wire
    // value gains a vote, and a hit when the packet's 32-bit value at BeginSpellIdOffset
    // equals the request's spell id.  A request replaced before any vote, or still pending at
    // the end, is unanswered.
    //
    // catalog:           The session's cataloged packets.
    // requestWireValue:  The accepted OP_CastRequest wire value.
    // votes:             Receives the votes per wire value.
    // hits:              Receives the hits per wire value.
    // unanswered:        Receives up to ExampleCount message numbers of unanswered requests.
    //
    // Returns:  The number of requests seen.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static int TallyVotes(PacketCatalog catalog, OpcodeValue requestWireValue,
        Dictionary<OpcodeValue, int> votes, Dictionary<OpcodeValue, int> hits, List<uint> unanswered)
    {
        Dictionary<Session, PendingRequest> pending = new Dictionary<Session, PendingRequest>();
        int requests = 0;
        int unansweredCount = 0;
        uint totalCount = (uint)catalog.TotalPacketCount();

        for (uint index = 1; index <= totalCount; index++)
        {
            CatalogedPacket? packetOrNull = catalog.PacketAt(index);
            if (packetOrNull == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferCastBegin.TallyVotes: no packet at index " + index, LogLevel.Error);
                continue;
            }
            CatalogedPacket packet = packetOrNull.Value;

            Session session = packet.Metadata.Session;
            if (session == null || session.ServerType != ServerType.Zone)
            {
                continue;
            }

            OpcodeValue wireValue = packet.Metadata.WireValue;
            ReadOnlySpan<byte> payload = packet.Payload.AsReadOnlySpan();

            if (packet.Metadata.ToServer)
            {
                if (wireValue != requestWireValue)
                {
                    continue;
                }
                if (payload.Length < RequestSpellIdOffset + 4)
                {
                    DebugLog.Write(LogChannel.InferenceDebug,
                        "InferCastBegin.TallyVotes: request at " + packet.PacketIndex + " has " + payload.Length
                        + " bytes, too short for a spell id, not counted", LogLevel.Warn);
                    continue;
                }

                requests++;
                PendingRequest? replaced;
                if (pending.TryGetValue(session, out replaced))
                {
                    unansweredCount++;
                    if (unanswered.Count < ExampleCount)
                    {
                        unanswered.Add(replaced.PacketIndex);
                    }
                    DebugLog.Write(LogChannel.InferenceDebug,
                        "InferCastBegin.TallyVotes: request at " + replaced.PacketIndex + " replaced by request at "
                        + packet.PacketIndex + " before any vote", defaultLoglevel);
                }

                PendingRequest request = new PendingRequest();
                request.PacketIndex = packet.PacketIndex;
                request.SpellId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(RequestSpellIdOffset));
                pending[session] = request;
                continue;
            }

            if (payload.Length != BeginLength)
            {
                continue;
            }
            if (AcceptedOpcodes.Instance.IsAccepted(wireValue))
            {
                continue;
            }

            PendingRequest? waiting;
            if (!pending.TryGetValue(session, out waiting))
            {
                continue;
            }
            pending.Remove(session);

            votes[wireValue] = votes.GetValueOrDefault(wireValue) + 1;
            uint spellId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(BeginSpellIdOffset));
            if (spellId == waiting.SpellId)
            {
                hits[wireValue] = hits.GetValueOrDefault(wireValue) + 1;
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferCastBegin.TallyVotes: request at " + waiting.PacketIndex + " spell " + waiting.SpellId
                    + " answered by " + wireValue + " at " + packet.PacketIndex + ", spell matches", defaultLoglevel);
            }
            else
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferCastBegin.TallyVotes: request at " + waiting.PacketIndex + " spell " + waiting.SpellId
                    + " answered by " + wireValue + " at " + packet.PacketIndex + ", spell " + spellId
                    + " does not match", defaultLoglevel);
            }
        }

        foreach (PendingRequest left in pending.Values)
        {
            unansweredCount++;
            if (unanswered.Count < ExampleCount)
            {
                unanswered.Add(left.PacketIndex);
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferCastBegin.TallyVotes: request " + requestWireValue + ", " + requests + " requests, "
            + votes.Count + " wire values voted for, " + unansweredCount + " unanswered", defaultLoglevel);
        return requests;
    }
}