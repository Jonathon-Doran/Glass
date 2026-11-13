using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Client;
using Glass.Network.Protocol;
using Inference.Core;
using Inference.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// InferTargetSequence
//
// Identifies the four opcodes of the targeting sequence together, as one group: the
// client's target request, the target's HP report, the target's buff list, and the message
// that closes the sequence.  The four are proposed and accepted as a unit.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferTargetSequence : IInferOpcodes
{
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    private const string RequestOpcodeName = "OP_Target_Request";
    private const string HpReportOpcodeName = "OP_TargetHpUpdate";
    private const string SpellsOpcodeName = "OP_TargetSpells";
    private const string CloseOpcodeName = "OP_TargetResponse";
    private const string GroupLabelConstant = "Target sequence";

    private const int RequestLength = 4;                 // expected target request payload length
    private const int HpReportLength = 18;               // expected HP report payload length
    private const int CloseLength = 4;                   // expected closing message payload length
    private const int MaxWindowPackets = 10;             // unaccepted packets scanned either side of an anchor
    private const int ExampleCount = 5;                  // packet numbers reported per missing member
    private const int ReportedGroups = 3;                // best groups returned, highest confidence first
    private const int BuffListMinLength = 11;            // smallest buff list payload seen
    private const int CountedLengthLimit = 100;          // packets longer than this are left out of the candidate tallies

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TargetWindow
    //
    // What the packets after one request-length client-to-zone packet held, within
    // MaxWindowPackets unaccepted packets of its session's list.  RequestIndex is the request
    // packet's message number and Request its wire value.  BuffLists holds each distinct wire
    // value of zone-to-client packets with a buff list shape in the window.  Closes holds each
    // distinct wire value of zone-to-client packets within range of the close length that
    // follow the first buff list shaped packet in the window.  The window counts only when
    // both sets are non-empty.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class TargetWindow
    {
        public uint RequestIndex;
        public OpcodeValue Request = OpcodeValue.None;
        public HashSet<OpcodeValue> BuffLists = new HashSet<OpcodeValue>();
        public HashSet<OpcodeValue> Closes = new HashSet<OpcodeValue>();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Infer
    //
    // Builds each zone session's list of unaccepted packets, finds the target windows, groups
    // them by request wire value, and scores one group proposal per request wire value.  The
    // best ReportedGroups groups are returned, highest confidence first.  The ForcedRequest
    // group, when it has windows and is not among them, is added after them.  When the best
    // group lacks a wire value for any member, the outcome is Incomplete.  The counts at each
    // stage are added to the result's messages.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  The result for the targeting sequence group.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IdentificationResult Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferTargetSequence.Infer: starting", defaultLoglevel);

        IdentificationResult result = new IdentificationResult();
        result.OpcodeNames.Add(RequestOpcodeName);
        result.OpcodeNames.Add(SpellsOpcodeName);
        result.OpcodeNames.Add(CloseOpcodeName);
        result.GroupLabel = GroupLabelConstant;

        Dictionary<Session, List<CatalogedPacket>> lists = BuildSessionLists(catalog);
        if (lists.Count == 0)
        {
            result.Messages.Add("no zone session has unaccepted traffic");
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferTargetSequence.Infer: no zone session has unaccepted traffic", LogLevel.Warn);
            return result;
        }

        List<TargetWindow> windows = FindWindows(lists);
        result.Messages.Add(windows.Count + " target windows");
        if (windows.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferTargetSequence.Infer: no target windows", LogLevel.Warn);
            return result;
        }

        Dictionary<OpcodeValue, List<TargetWindow>> byRequest = new Dictionary<OpcodeValue, List<TargetWindow>>();
        for (int windowIndex = 0; windowIndex < windows.Count; windowIndex++)
        {
            TargetWindow window = windows[windowIndex];
            List<TargetWindow>? requestWindows;
            if (!byRequest.TryGetValue(window.Request, out requestWindows))
            {
                requestWindows = new List<TargetWindow>();
                byRequest[window.Request] = requestWindows;
            }
            requestWindows.Add(window);
        }
        result.Messages.Add(byRequest.Count + " request candidates");

        List<InferenceProposal> groups = new List<InferenceProposal>();
        foreach (KeyValuePair<OpcodeValue, List<TargetWindow>> entry in byRequest)
        {
            InferenceProposal group = ScoreGroup(entry.Key, entry.Value, lists);
            groups.Add(group);
        }

        groups.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
        int reported = Math.Min(ReportedGroups, groups.Count);
        for (int groupIndex = 0; groupIndex < reported; groupIndex++)
        {
            result.Proposals.Add(groups[groupIndex]);
        }

        InferenceProposal best = result.Proposals[0];
        for (int assignmentIndex = 0; assignmentIndex < best.Assignments.Count; assignmentIndex++)
        {
            if (!best.Assignments[assignmentIndex].Opcode.Exists)
            {
                result.Outcome = IdentificationOutcome.Incomplete;
                result.Messages.Add("best group lacks " + best.Assignments[assignmentIndex].OpcodeName);
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferTargetSequence.Infer: " + windows.Count + " windows, " + byRequest.Count
            + " request candidates, " + reported + " groups reported, best " + best.Confidence.ToString("F3"),
            defaultLoglevel);
        return result;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BuildSessionLists
    //
    // Walks the catalog once and builds, for each zone session, the list of its application
    // packets whose wire values are not accepted, in arrival order.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  The unaccepted packets of each zone session, keyed by session.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static Dictionary<Session, List<CatalogedPacket>> BuildSessionLists(PacketCatalog catalog)
    {
        Dictionary<Session, List<CatalogedPacket>> lists = new Dictionary<Session, List<CatalogedPacket>>();
        uint totalCount = (uint)catalog.TotalPacketCount();
        uint kept = 0;

        for (uint index = 1; index <= totalCount; index++)
        {
            CatalogedPacket? packetOrNull = catalog.PacketAt(index);
            if (packetOrNull == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferTargetSequence.BuildSessionLists: no packet at index " + index, LogLevel.Error);
                continue;
            }
            CatalogedPacket packet = packetOrNull.Value;

            Session session = packet.Metadata.Session;
            if (session == null || session.ServerType != ServerType.Zone)
            {
                continue;
            }
            OpcodeValue wireValue = packet.Metadata.WireValue;
            if (AcceptedOpcodes.Instance.IsAccepted(wireValue))
            {
                continue;
            }

            List<CatalogedPacket>? list;
            if (!lists.TryGetValue(session, out list))
            {
                list = new List<CatalogedPacket>();
                lists[session] = list;
            }
            list.Add(packet);
            kept++;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferTargetSequence.BuildSessionLists: " + lists.Count + " zone sessions, " + kept
            + " unaccepted packets of " + totalCount, defaultLoglevel);
        return lists;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindFirstTargets
    //
    // Finds, in one session list, the position of the first client-to-zone packet of exactly
    // RequestLength bytes with a non-zero payload, for each wire value.  A wire value whose
    // request-length packets all have zero payloads has no entry.
    //
    // list:  One session's unaccepted packets, in arrival order.
    //
    // Returns:  The position of the first non-zero request packet, keyed by wire value.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static Dictionary<OpcodeValue, int> FindFirstTargets(List<CatalogedPacket> list)
    {
        Dictionary<OpcodeValue, int> firstTargets = new Dictionary<OpcodeValue, int>();

        for (int position = 0; position < list.Count; position++)
        {
            CatalogedPacket packet = list[position];
            if (!packet.Metadata.ToServer)
            {
                continue;
            }
            if (packet.Payload.Length != RequestLength)
            {
                continue;
            }

            OpcodeValue wireValue = packet.Metadata.WireValue;
            if (firstTargets.ContainsKey(wireValue))
            {
                continue;
            }

            ReadOnlySpan<byte> payload = packet.Payload.AsReadOnlySpan();
            bool zero = true;
            for (int byteIndex = 0; byteIndex < payload.Length; byteIndex++)
            {
                if (payload[byteIndex] != 0)
                {
                    zero = false;
                    break;
                }
            }

            if (!zero)
            {
                firstTargets[wireValue] = position;
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferTargetSequence.FindFirstTargets: " + firstTargets.Count + " wire values with a non-zero request in "
            + list.Count + " packets", LogLevel.Trace);
        return firstTargets;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindWindows
    //
    // Walks each session list and examines the window after every client-to-zone packet of
    // exactly RequestLength bytes, starting on each session at the first such packet of its
    // wire value with a non-zero payload.  Earlier packets of that wire value, and every
    // packet of a wire value that never has a non-zero payload on the session, are skipped.
    // A window is kept only when it holds a close vote.
    //
    // lists:  The unaccepted packets of each zone session.
    //
    // Returns:  The kept windows, in session list order.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static List<TargetWindow> FindWindows(Dictionary<Session, List<CatalogedPacket>> lists)
    {
        List<TargetWindow> windows = new List<TargetWindow>();
        int requestPackets = 0;
        int skipped = 0;
        int noClose = 0;
        int clears = 0;

        foreach (List<CatalogedPacket> list in lists.Values)
        {
            Dictionary<OpcodeValue, int> firstTargets = FindFirstTargets(list);
            for (int position = 0; position < list.Count; position++)
            {
                CatalogedPacket packet = list[position];
                if (!packet.Metadata.ToServer)
                {
                    continue;
                }
                if (packet.Payload.Length != RequestLength)
                {
                    continue;
                }

                int firstTarget;
                if (!firstTargets.TryGetValue(packet.Metadata.WireValue, out firstTarget) || position < firstTarget)
                {
                    skipped++;
                    continue;
                }
                requestPackets++;

                TargetWindow window = ExamineWindow(list, position);
                if (window.Closes.Count == 0)
                {
                    noClose++;
                    continue;
                }
                if (window.BuffLists.Count == 0)
                {
                    clears++;
                }
                windows.Add(window);
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferTargetSequence.FindWindows: " + windows.Count + " windows kept (" + clears + " clears) of "
            + requestPackets + " request-length packets; " + skipped + " before a non-zero request skipped, "
            + noClose + " without a close", defaultLoglevel);
        return windows;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ExamineWindow
    //
    // Examines the packets that follow one request packet in its session's list, at most
    // MaxWindowPackets positions forward.  The window ends early at the next packet of the
    // request's wire value; other client-to-zone packets are passed over.  When the request
    // payload is all zero the window is a clear: every zone-to-client packet of exactly
    // CloseLength bytes is added to the close votes.  Otherwise every zone-to-client packet
    // from BuffListMinLength to CountedLengthLimit bytes is added to the buff list votes, and
    // every zone-to-client packet of exactly CloseLength bytes after a buff list vote is added
    // to the close votes.
    //
    // list:             The request's session's unaccepted packets, in arrival order.
    // requestPosition:  The request packet's position in the list.
    //
    // Returns:  The window's votes.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static TargetWindow ExamineWindow(List<CatalogedPacket> list, int requestPosition)
    {
        CatalogedPacket request = list[requestPosition];

        TargetWindow window = new TargetWindow();
        window.RequestIndex = request.PacketIndex;
        window.Request = request.Metadata.WireValue;

        bool clear = true;
        ReadOnlySpan<byte> requestPayload = request.Payload.AsReadOnlySpan();
        for (int byteIndex = 0; byteIndex < requestPayload.Length; byteIndex++)
        {
            if (requestPayload[byteIndex] != 0)
            {
                clear = false;
                break;
            }
        }

        int forwardLimit = Math.Min(list.Count - 1, requestPosition + MaxWindowPackets);
        for (int position = requestPosition + 1; position <= forwardLimit; position++)
        {
            CatalogedPacket packet = list[position];
            OpcodeValue wireValue = packet.Metadata.WireValue;

            if (packet.Metadata.ToServer)
            {
                if (wireValue == window.Request)
                {
                    break;
                }
                continue;
            }

            int length = packet.Payload.Length;

            if (length == CloseLength && (clear || window.BuffLists.Count > 0))
            {
                window.Closes.Add(wireValue);
                continue;
            }
            if (!clear && length >= BuffListMinLength && length <= CountedLengthLimit)
            {
                window.BuffLists.Add(wireValue);
            }
        }

        return window;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CountPairs
    //
    // Counts the request packets that are followed by a close packet in their session's list,
    // within MaxWindowPackets positions and before the next packet of the request's wire
    // value.  Other client-to-zone packets are passed over.  On each session, counting starts
    // at the first request packet with a non-zero payload; a session with none contributes
    // nothing.
    //
    // lists:            The unaccepted packets of each zone session.
    // requestWireValue: The wire value taken as the request.
    // closeWireValue:   The wire value taken as the close.
    // requests:         Receives the number of request packets counted.
    // paired:           Receives how many of them were followed by a close.
    // unpaired:         Receives the message numbers of up to ExampleCount unpaired requests.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void CountPairs(Dictionary<Session, List<CatalogedPacket>> lists,
        OpcodeValue requestWireValue, OpcodeValue closeWireValue, out int requests, out int paired,
        List<uint> unpaired)
    {
        requests = 0;
        paired = 0;

        foreach (List<CatalogedPacket> list in lists.Values)
        {
            Dictionary<OpcodeValue, int> firstTargets = FindFirstTargets(list);
            int firstTarget;
            if (!firstTargets.TryGetValue(requestWireValue, out firstTarget))
            {
                continue;
            }

            for (int position = firstTarget; position < list.Count; position++)
            {
                if (list[position].Metadata.WireValue != requestWireValue)
                {
                    continue;
                }
                requests++;

                bool found = false;
                int forwardLimit = Math.Min(list.Count - 1, position + MaxWindowPackets);
                for (int next = position + 1; next <= forwardLimit; next++)
                {
                    CatalogedPacket packet = list[next];
                    if (packet.Metadata.WireValue == requestWireValue)
                    {
                        break;
                    }
                    if (packet.Metadata.WireValue == closeWireValue)
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                {
                    paired++;
                }
                else if (unpaired.Count < ExampleCount)
                {
                    unpaired.Add(list[position].PacketIndex);
                }
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferTargetSequence.CountPairs: request " + requestWireValue + " close " + closeWireValue
            + ", " + paired + "/" + requests + " paired", defaultLoglevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // MostVoted
    //
    // Finds the wire value with the most votes in a tally.  On a tie, the first value found
    // is kept.
    //
    // tally:  Votes per wire value.
    // votes:  Receives the winning value's vote count, 0 when the tally is empty.
    //
    // Returns:  The winning wire value, or None when the tally is empty.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static OpcodeValue MostVoted(Dictionary<OpcodeValue, int> tally, out int votes)
    {
        OpcodeValue winner = OpcodeValue.None;
        votes = 0;

        foreach (KeyValuePair<OpcodeValue, int> entry in tally)
        {
            if (entry.Value > votes)
            {
                winner = entry.Key;
                votes = entry.Value;
            }
        }

        return winner;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScoreGroup
    //
    // Builds the group proposal for one request wire value from the windows that follow its
    // packets.  Every share below is scored as (hits + 1) / (tests + 2), so a share built on
    // few tests scores lower than the same share built on many; a share with no tests is 0.
    // The buff list is the most-voted buff list wire value across the windows that are not
    // clears, and its confidence is the share of those windows holding it.  The close is the
    // most-voted close wire value across all the windows, and its confidence is the share of
    // the windows holding it.  The request's confidence is the share of its tested packets
    // followed by the chosen close, and is 0 when there is no close.  The group confidence is
    // the product of the request, buff list and close confidences.  Each member's evidence
    // lists up to ExampleCount message numbers where its value was missing.
    //
    // requestWireValue:  The wire value taken as the request.
    // windows:           The kept windows whose request has that wire value.
    // lists:             The unaccepted packets of each zone session.
    //
    // Returns:  The group proposal.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static InferenceProposal ScoreGroup(OpcodeValue requestWireValue, List<TargetWindow> windows,
        Dictionary<Session, List<CatalogedPacket>> lists)
    {
        Dictionary<OpcodeValue, int> buffListTally = new Dictionary<OpcodeValue, int>();
        Dictionary<OpcodeValue, int> closeTally = new Dictionary<OpcodeValue, int>();
        int targetWindows = 0;

        for (int windowIndex = 0; windowIndex < windows.Count; windowIndex++)
        {
            TargetWindow window = windows[windowIndex];
            if (window.BuffLists.Count > 0)
            {
                targetWindows++;
            }
            foreach (OpcodeValue buffListValue in window.BuffLists)
            {
                buffListTally[buffListValue] = buffListTally.GetValueOrDefault(buffListValue) + 1;
            }
            foreach (OpcodeValue closeValue in window.Closes)
            {
                closeTally[closeValue] = closeTally.GetValueOrDefault(closeValue) + 1;
            }
        }

        int buffListVotes;
        int closeVotes;
        OpcodeValue buffList = MostVoted(buffListTally, out buffListVotes);
        OpcodeValue close = MostVoted(closeTally, out closeVotes);

        List<uint> buffListMissing = new List<uint>();
        List<uint> closeMissing = new List<uint>();
        for (int windowIndex = 0; windowIndex < windows.Count; windowIndex++)
        {
            TargetWindow window = windows[windowIndex];
            if (window.BuffLists.Count > 0 && !window.BuffLists.Contains(buffList)
                && buffListMissing.Count < ExampleCount)
            {
                buffListMissing.Add(window.RequestIndex);
            }
            if (!window.Closes.Contains(close) && closeMissing.Count < ExampleCount)
            {
                closeMissing.Add(window.RequestIndex);
            }
        }

        double buffListConfidence = targetWindows > 0 ? (buffListVotes + 1.0) / (targetWindows + 2.0) : 0.0;
        double closeConfidence = windows.Count > 0 ? (closeVotes + 1.0) / (windows.Count + 2.0) : 0.0;

        int requests = 0;
        int paired = 0;
        List<uint> unpaired = new List<uint>();
        double requestConfidence = 0.0;
        string pairText = "no request tested for pairing";
        if (close.Exists)
        {
            CountPairs(lists, requestWireValue, close, out requests, out paired, unpaired);
            if (requests > 0)
            {
                requestConfidence = (paired + 1.0) / (requests + 2.0);
                pairText = paired + "/" + requests + " requests followed by close";
                if (unpaired.Count > 0)
                {
                    pairText += ", unpaired at " + string.Join(", ", unpaired);
                }
            }
            else
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferTargetSequence.ScoreGroup: request " + requestWireValue
                    + " has no request tested for pairing, confidence 0", defaultLoglevel);
            }
        }
        else
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferTargetSequence.ScoreGroup: request " + requestWireValue + " has no close vote", LogLevel.Warn);
        }

        PatchLevel level = GlassContext.CurrentPatchLevel;
        InferenceProposal proposal = new InferenceProposal();

        proposal.Assignments.Add(new OpcodeAssignment
        {
            OpcodeName = RequestOpcodeName,
            Label = RequestOpcodeName,
            Opcode = new PatchOpcode(level, requestWireValue),
            Confidence = requestConfidence,
            Evidence = pairText
        });
        proposal.Assignments.Add(new OpcodeAssignment
        {
            OpcodeName = SpellsOpcodeName,
            Label = SpellsOpcodeName,
            Opcode = buffList.Exists ? new PatchOpcode(level, buffList) : PatchOpcode.None,
            Confidence = buffListConfidence,
            Evidence = buffListVotes + "/" + targetWindows + " target windows"
                + (buffListMissing.Count > 0 ? ", missing at " + string.Join(", ", buffListMissing) : string.Empty)
        });
        proposal.Assignments.Add(new OpcodeAssignment
        {
            OpcodeName = CloseOpcodeName,
            Label = CloseOpcodeName,
            Opcode = close.Exists ? new PatchOpcode(level, close) : PatchOpcode.None,
            Confidence = closeConfidence,
            Evidence = closeVotes + "/" + windows.Count + " windows"
                + (closeMissing.Count > 0 ? ", missing at " + string.Join(", ", closeMissing) : string.Empty)
        });

        proposal.Confidence = requestConfidence * buffListConfidence * closeConfidence;
        proposal.Evidence = windows.Count + " windows, " + targetWindows + " targets | request "
            + requestConfidence.ToString("F2") + ", buff list " + buffListConfidence.ToString("F2")
            + ", close " + closeConfidence.ToString("F2") + " | " + pairText + " | group "
            + proposal.Confidence.ToString("F3");

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferTargetSequence.ScoreGroup: request " + requestWireValue + " " + proposal.Evidence,
            defaultLoglevel);
        return proposal;
    }
}
