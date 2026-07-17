using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Inference.Core;
using Inference.Models;
using System;
using System.Collections.Generic;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// InferZoneEntry_Z2C
//
// Identifies OP_ZoneEntry_Z2C.  ZoneEntry announcements follow the PlayerProfile on a zone
// session: after the profile arrives on a port pair, the first zone-to-client packet on
// that pair whose payload begins with a null-terminated printable string is a ZoneEntry.
// Requires OP_PlayerProfile to have been accepted this session; the accepted wire value
// anchors the walk.  Each profile packet contributes one vote; confidence is cross-session
// agreement.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferZoneEntry_Z2C : IInferOpcodes
{
    private const string OpcodeNameConstant = "OP_ZoneEntry_Z2C";
    private const string AnchorOpcodeName = "OP_PlayerProfile";   // must match InferPlayerProfile

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
    // Looks up the wire value accepted as OP_PlayerProfile, walks the catalog for packets
    // carrying it, and for each one takes the first following zone-to-client packet on the
    // same port pair whose payload begins with a null-terminated printable string as that
    // session's ZoneEntry vote.  Votes are tallied by wire value and emitted as proposals
    // ordered by vote count descending, confidence being the fraction of votes carried.
    // Returns no proposals when OP_PlayerProfile has not been accepted this session.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  Proposals ordered best-first, empty when the anchor is missing or no
    //           session produced a vote.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<InferenceProposal> Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.Inference, "InferZoneEntry_Z2C.Infer: starting", LogLevel.Trace);
        List<InferenceProposal> proposals = new List<InferenceProposal>();

        OpcodeValue anchorWireValue = AcceptedOpcodes.Instance.WireValueFor(AnchorOpcodeName);
        if (!anchorWireValue.Exists)
        {
            DebugLog.Write(LogChannel.Inference,
                "InferZoneEntry_Z2C.Infer: " + AnchorOpcodeName
                + " not accepted this session, cannot anchor, no proposals", LogLevel.Warn);
            return proposals;
        }

        Dictionary<OpcodeValue, int> votes = new Dictionary<OpcodeValue, int>();
        int anchorsFound = 0;
        uint totalCount = (uint)catalog.TotalPacketCount();

        for (uint i = 0; i < totalCount; i++)
        {
            CatalogedPacket? anchorOrNull = catalog.PacketAt(i);
            if (anchorOrNull == null)
            {
                continue;
            }
            CatalogedPacket anchor = anchorOrNull.Value;

            if (!anchor.Metadata.WireValue.Equals(anchorWireValue))
            {
                continue;
            }
            anchorsFound++;

            OpcodeValue vote = FindFirstStringPacketOnPair(catalog, i + 1,
                anchor.Metadata.SourcePort, anchor.Metadata.DestPort);
            if (!vote.Exists)
            {
                DebugLog.Write(LogChannel.Inference,
                    "InferZoneEntry_Z2C.Infer: no vote after profile at index "
                    + anchor.PacketIndex, LogLevel.Warn);
                continue;
            }

            int seen;
            if (votes.TryGetValue(vote, out seen))
            {
                votes[vote] = seen + 1;
            }
            else
            {
                votes[vote] = 1;
            }
        }

        if (votes.Count == 0)
        {
            DebugLog.Write(LogChannel.Inference,
                "InferZoneEntry_Z2C.Infer: " + anchorsFound + " anchors, no votes, no proposals",
                LogLevel.Warn);
            return proposals;
        }

        int totalVotes = 0;
        foreach (KeyValuePair<OpcodeValue, int> entry in votes)
        {
            totalVotes += entry.Value;
        }

        List<OpcodeValue> byVotesDescending = new List<OpcodeValue>(votes.Keys);
        byVotesDescending.Sort((a, b) => votes[b].CompareTo(votes[a]));

        for (int i = 0; i < byVotesDescending.Count; i++)
        {
            OpcodeValue wireValue = byVotesDescending[i];
            if (AcceptedOpcodes.Instance.IsAccepted(wireValue))
            {
                DebugLog.Write(LogChannel.Inference,
                    "InferZoneEntry_Z2C.Infer: wireValue=" + wireValue + " already accepted as "
                    + AcceptedOpcodes.Instance.GetName(wireValue) + ", skipping", LogLevel.Info);
                continue;
            }

            double confidence = (double)votes[wireValue] / totalVotes;
            InferenceProposal proposal = new InferenceProposal();
            proposal.Label = Label;
            proposal.Opcode = new PatchOpcode(GlassContext.CurrentPatchLevel, wireValue);
            proposal.Confidence = confidence;
            proposal.Evidence =
                votes[wireValue] + "/" + totalVotes + " zone sessions' first string-leading Z2C"
                + " message after " + AnchorOpcodeName
                + " | " + catalog.CountFor(wireValue) + " packets in capture"
                + " | agreement " + confidence.ToString("F3");
            proposals.Add(proposal);

            DebugLog.Write(LogChannel.Inference,
                "InferZoneEntry_Z2C.Infer: wireValue=" + wireValue + " votes="
                + votes[wireValue] + "/" + totalVotes + " confidence="
                + confidence.ToString("F3"), LogLevel.Trace);
        }

        DebugLog.Write(LogChannel.Inference,
            "InferZoneEntry_Z2C.Infer: " + anchorsFound + " anchors, "
            + proposals.Count + " proposals", LogLevel.Info);
        return proposals;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindFirstStringPacketOnPair
    //
    // Walks the catalog in arrival order from the given start index, considering only
    // zone-to-client packets whose source/dest ports match the given pair.  Returns the
    // wire value of the first such packet whose payload begins at byte 0 with a
    // null-terminated printable-ASCII string: one or more bytes in the range 0x20-0x7E
    // followed by a 0x00 terminator inside the payload.  No minimum string length is
    // required.  A session disconnect (net opcode 0x0500) on the pair ends the walk.
    //
    // catalog:     The session's cataloged packets.
    // startIndex:  Arrival index at which to begin (typically the packet after the
    //              session's PlayerProfile).
    // portA:       One port of the pair.
    // portB:       The other port of the pair.
    //
    // Returns:  The qualifying packet's wire value, or OpcodeValue.None when the pair
    //           disconnected or the catalog was exhausted first.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private OpcodeValue FindFirstStringPacketOnPair(PacketCatalog catalog, uint startIndex,
        int portA, int portB)
    {
        uint totalCount = (uint)catalog.TotalPacketCount();

        for (uint i = startIndex; i < totalCount; i++)
        {
            CatalogedPacket? packetOrNull = catalog.PacketAt(i);
            if (packetOrNull == null)
            {
                continue;
            }
            CatalogedPacket packet = packetOrNull.Value;

            bool onPair =
                (packet.Metadata.SourcePort == portA && packet.Metadata.DestPort == portB)
                || (packet.Metadata.SourcePort == portB && packet.Metadata.DestPort == portA);
            if (!onPair)
            {
                continue;
            }

            if (packet.Metadata.WireValue.Value == SoeConstants.OP_SessionDisconnect)
            {
                DebugLog.Write(LogChannel.Inference,
                    "InferZoneEntry_Z2C.FindFirstStringPacketOnPair: disconnect at index "
                    + packet.PacketIndex + ", walk ended", LogLevel.Trace);
                return OpcodeValue.None;
            }

            if (packet.Metadata.Channel != SoeConstants.StreamId.StreamZoneToClient)
            {
                continue;
            }

            ReadOnlySpan<byte> payload = packet.Payload.AsReadOnlySpan();
            int stringLength = 0;
            while (stringLength < payload.Length
                && payload[stringLength] >= 0x20 && payload[stringLength] <= 0x7E)
            {
                stringLength++;
            }

            if (stringLength > 0
                && stringLength < payload.Length
                && payload[stringLength] == 0x00)
            {
                DebugLog.Write(LogChannel.Inference,
                    "InferZoneEntry_Z2C.FindFirstStringPacketOnPair: match at index "
                    + packet.PacketIndex + ", wireValue=" + packet.Metadata.WireValue
                    + ", string length " + stringLength, LogLevel.Trace);
                return packet.Metadata.WireValue;
            }
        }

        DebugLog.Write(LogChannel.Inference,
            "InferZoneEntry_Z2C.FindFirstStringPacketOnPair: exhausted without match",
            LogLevel.Trace);
        return OpcodeValue.None;
    }
}
