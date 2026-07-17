using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Inference.Core;
using Inference.Models;
using System.Collections.Generic;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// InferPlayerProfile
//
// Identifies OP_PlayerProfile.  PlayerProfile is the first large zone-to-client message on
// a freshly established zone session: a session response arrives on a port pair, small
// setup traffic follows, and the first payload at or above the size floor on that pair is
// the profile.  Candidates come from the catalog's per-session walk; the proposal is the
// wire value those candidates carry, with confidence set by cross-session agreement.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferPlayerProfile : IInferOpcodes
{
    private const string OpcodeNameConstant = "OP_PlayerProfile";
    private const int MinimumProfileSize = 10000;      // smallest payload accepted as a profile

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
    // Collects the per-session PlayerProfile candidates from the catalog, tallies their
    // wire values, and emits one proposal per distinct wire value, ordered by session
    // count descending.  Confidence is the fraction of candidate sessions carrying that
    // wire value; the evidence string reports the agreement count and the candidate
    // payload lengths.  Wire values already accepted this session are skipped.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  Proposals ordered best-first, empty when no session produced a candidate.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<InferenceProposal> Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferPlayerProfile.Infer: starting",
            LogLevel.Trace);
        List<InferenceProposal> proposals = new List<InferenceProposal>();

        List<CatalogedPacket> candidates = FindCandidates(catalog);
        if (candidates.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferPlayerProfile.Infer: no candidates, no proposals", LogLevel.Warn);
            return proposals;
        }

        Dictionary<OpcodeValue, int> tally = new Dictionary<OpcodeValue, int>();
        Dictionary<OpcodeValue, string> lengths = new Dictionary<OpcodeValue, string>();
        for (int i = 0; i < candidates.Count; i++)
        {
            OpcodeValue wireValue = candidates[i].Metadata.WireValue;
            int seen;
            if (tally.TryGetValue(wireValue, out seen))
            {
                tally[wireValue] = seen + 1;
                lengths[wireValue] = lengths[wireValue] + ", " + candidates[i].Payload.Length;
            }
            else
            {
                tally[wireValue] = 1;
                lengths[wireValue] = candidates[i].Payload.Length.ToString();
            }
        }

        List<OpcodeValue> byCountDescending = new List<OpcodeValue>(tally.Keys);
        byCountDescending.Sort((a, b) => tally[b].CompareTo(tally[a]));

        for (int i = 0; i < byCountDescending.Count; i++)
        {
            OpcodeValue wireValue = byCountDescending[i];
            if (AcceptedOpcodes.Instance.IsAccepted(wireValue))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferPlayerProfile.Infer: wireValue=" + wireValue + " already accepted as "
                    + AcceptedOpcodes.Instance.GetName(wireValue) + ", skipping", LogLevel.Info);
                continue;
            }

            double confidence = (double)tally[wireValue] / candidates.Count;
            InferenceProposal proposal = new InferenceProposal();
            proposal.Label = Label;
            proposal.Opcode = new PatchOpcode(GlassContext.CurrentPatchLevel, wireValue);
            proposal.Confidence = confidence;
            proposal.Evidence =
                tally[wireValue] + "/" + candidates.Count
                + " zone sessions' first Z2C message over " + MinimumProfileSize
                + " bytes | lengths " + lengths[wireValue]
                + " | agreement " + confidence.ToString("F3");
            proposals.Add(proposal);

            DebugLog.Write(LogChannel.InferenceDebug,
                "InferPlayerProfile.Infer: wireValue=" + wireValue + " sessions="
                + tally[wireValue] + "/" + candidates.Count + " confidence="
                + confidence.ToString("F3"), LogLevel.Trace);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferPlayerProfile.Infer: " + proposals.Count + " proposals", LogLevel.Info);
        return proposals;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindCandidates
    //
    // Walks the catalog in arrival order looking for session responses (net opcode 0x0200
    // on the zone-to-client channel).  For each one, records its source/dest port pair and
    // walks forward from it, considering only packets on that pair in either direction.
    // The first packet on the pair whose payload length meets MinimumProfileSize is that
    // session's candidate.  A session disconnect (net opcode 0x0500) on the pair ends that
    // session's walk with no candidate.  Ordering is arrival order alone; timestamps are
    // not consulted.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  One candidate packet per qualifying session response, in the order the
    //           session responses were seen.  Empty when none qualify.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private List<CatalogedPacket> FindCandidates(PacketCatalog catalog)
    {
        List<CatalogedPacket> candidates = new List<CatalogedPacket>();
        uint totalCount = (uint)catalog.TotalPacketCount();

        for (uint i = 0; i < totalCount; i++)
        {
            CatalogedPacket? startOrNull = catalog.PacketAt(i);
            if (startOrNull == null)
            {
                DebugLog.Write(LogChannel.Inference,
                    "InferPlayerProfile.FindCandidates: null packet at index " + i, LogLevel.Error);
                continue;
            }
            CatalogedPacket start = startOrNull.Value;

            if (start.Metadata.WireValue.Value != SoeConstants.OP_SessionResponse)
            {
                continue;
            }
            if (start.Metadata.Channel != SoeConstants.StreamId.StreamZoneToClient)
            {
                continue;
            }

            int portA = start.Metadata.SourcePort;
            int portB = start.Metadata.DestPort;
            DebugLog.Write(LogChannel.Inference,
                "InferPlayerProfile.FindCandidates: session response at index "
                + start.PacketIndex + ", ports " + portA + "/" + portB, LogLevel.Trace);

            for (uint j = i + 1; j < totalCount; j++)
            {
                CatalogedPacket? packetOrNull = catalog.PacketAt(j);
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
                        "InferPlayerProfile.FindCandidates: disconnect at index "
                        + packet.PacketIndex + ", no candidate for this session", LogLevel.Trace);
                    break;
                }

                if (packet.Payload.Length >= MinimumProfileSize)
                {
                    candidates.Add(packet);
                    DebugLog.Write(LogChannel.Inference,
                        "InferPlayerProfile.FindCandidates: candidate at index "
                        + packet.PacketIndex + ", length " + packet.Payload.Length
                        + ", ports " + portA + "/" + portB, LogLevel.Trace);
                    break;
                }
            }
        }

        DebugLog.Write(LogChannel.Inference,
            "InferPlayerProfile.FindCandidates: " + candidates.Count + " candidates",
            LogLevel.Trace);
        return candidates;
    }
}
