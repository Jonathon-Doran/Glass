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
// InferGroupMembership
//
// Identifies the four group membership opcodes together, as one group: the group roster,
// the join confirmation, the leader notice, and the leave notice.  Every feature rests on
// identifiers: the names and character ids of the characters on the capture's zone
// sessions, and the server group id, which is taken from the first four bytes of roster
// packets.  The four are proposed and accepted as a unit.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferGroupMembership : IInferOpcodes
{
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    private const string RosterOpcodeName = "OP_Group_Roster";
    private const string JoinOpcodeName = "OP_Group_Join";
    private const string LeaderOpcodeName = "OP_Group_Leader";
    private const string LeaveOpcodeName = "OP_Group_Leave";
    private const string GroupLabelConstant = "Group membership";

    private const int GroupIdLength = 4;                 // bytes in the server group id at the start of a roster
    private const int RosterNameRepeats = 2;             // times one known name appears in a roster packet (the leader)
    private const int JoinIdRepeats = 2;                 // times the named character's id appears in a join packet
    private const int ReportedGroups = 3;                // best groups returned, highest confidence first
    private const int ExampleCount = 5;                  // packet numbers reported per failed test
    private const double ReportThreshold = 0.40;         // minimum group confidence to emit a proposal

    ///////////////////////////////////////////////////////////////////////////////////////////
    // MemberPacket
    //
    // One unaccepted zone-to-client packet holding a roster candidate's group id and exactly
    // one known character name.  PacketIndex is its message number, WireValue its wire value
    // and Length its payload length.  Named is the character whose name it holds.
    // OnNamedSession is true when it arrived on that character's own session.  IdCount is
    // the number of times that character's id appears in the payload.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class MemberPacket
    {
        public uint PacketIndex;
        public OpcodeValue WireValue = OpcodeValue.None;
        public int Length;
        public Character? Named;
        public bool OnNamedSession;
        public int IdCount;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // MemberEvent
    //
    // The copies of one message: member packets with the same wire value and identical
    // payload bytes.  FirstIndex is the message number of the first copy, WireValue the wire
    // value, Length the payload length, and Named the character the copies name.
    // ReachedNamed is true when any copy arrived on the named character's own session.
    // NextRosterLeader is true when the next roster packet on the first copy's session holds
    // the named character's name at least RosterNameRepeats times.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class MemberEvent
    {
        public uint FirstIndex;
        public OpcodeValue WireValue = OpcodeValue.None;
        public int Length;
        public Character? Named;
        public bool ReachedNamed;
        public bool NextRosterLeader;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Infer
    //
    // Collects the characters on the capture's zone sessions and the unaccepted
    // zone-to-client packets, then builds one group proposal per roster candidate: every
    // unaccepted wire value with more than one payload size whose packets score as a roster
    // and yield at least one group id.  The best ReportedGroups proposals are returned,
    // highest confidence first.  When the best proposal lacks a wire value for any member,
    // the outcome is Incomplete.  The counts at each stage are added to the result's
    // messages.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  The result for the group membership group.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IdentificationResult Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferGroupMembership.Infer: starting", defaultLoglevel);

        IdentificationResult result = new IdentificationResult();
        result.OpcodeNames.Add(RosterOpcodeName);
        result.OpcodeNames.Add(JoinOpcodeName);
        result.OpcodeNames.Add(LeaderOpcodeName);
        result.OpcodeNames.Add(LeaveOpcodeName);
        result.GroupLabel = GroupLabelConstant;

        List<Character> characters = InferSupport.CollectCharacters(catalog);
        if (characters.Count == 0)
        {
            result.Messages.Add("no zone session has a character");
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferGroupMembership.Infer: no zone session has a character", LogLevel.Warn);
            return result;
        }
        result.Messages.Add(characters.Count + " characters");

        List<CatalogedPacket> packets = InferSupport.BuildZoneToClientList(catalog);
        if (packets.Count == 0)
        {
            result.Messages.Add("no unaccepted zone-to-client traffic");
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferGroupMembership.Infer: no unaccepted zone-to-client traffic", LogLevel.Warn);
            return result;
        }

        List<InferenceProposal> groups = new List<InferenceProposal>();
        List<OpcodeValue> ranked = catalog.OpcodesByCountDescending();

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
                    "InferGroupMembership.Infer: no stats for wireValue " + wireValue, LogLevel.Warn);
                continue;
            }
            if (statsOrNull.Value.DistinctSizeCount < 2)
            {
                continue;
            }

            HashSet<uint> groupIds = new HashSet<uint>();
            string rosterEvidence;
            double rosterConfidence = ScoreRoster(wireValue, packets, characters, groupIds, out rosterEvidence);
            if (groupIds.Count == 0)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferGroupMembership.Infer: wireValue " + wireValue + " yields no group id, not a roster candidate",
                    defaultLoglevel);
                continue;
            }

            List<MemberPacket> members = CollectMembers(packets, wireValue, groupIds, characters);
            List<MemberEvent> events = BuildEvents(catalog, members);
            MarkRosterLeaders(packets, wireValue, events);
            InferenceProposal group = ScoreMembers(wireValue, rosterConfidence, rosterEvidence, members, events);
            if (group.Confidence < ReportThreshold)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferGroupMembership.Infer: roster candidate " + wireValue + " group "
                    + group.Confidence.ToString("F3") + " below threshold, skipping", defaultLoglevel);
                continue;
            }
            groups.Add(group);
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferGroupMembership.Infer: roster candidate " + wireValue + ", " + groupIds.Count + " group ids, "
                + members.Count + " member packets, " + events.Count + " events, group "
                + group.Confidence.ToString("F3"), defaultLoglevel);
        }

        result.Messages.Add(groups.Count + " roster candidates");
        if (groups.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferGroupMembership.Infer: no roster candidates", LogLevel.Warn);
            return result;
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
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferGroupMembership.Infer: best group lacks " + best.Assignments[assignmentIndex].OpcodeName,
                    LogLevel.Warn);
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferGroupMembership.Infer: " + groups.Count + " roster candidates, " + reported
            + " groups reported, best " + best.Confidence.ToString("F3"), defaultLoglevel);
        return result;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScoreRoster
    //
    // Scores one wire value as the group roster over its packets in the zone-to-client list.
    // A packet is a hit when some known character's name occurs in it at least
    // RosterNameRepeats times.  The first GroupIdLength bytes of each hit, read as a
    // little-endian value, are added to the group ids when not 0.  The confidence is
    // (hits + 1) / (tests + 2), or 0 when the wire value has no packets in the list.
    //
    // wireValue:   The wire value taken as the roster.
    // packets:     The unaccepted zone-to-client packets.
    // characters:  The known characters.
    // groupIds:    Receives the group ids read from the hits.
    // evidence:    Receives the hit count, test count, and up to ExampleCount message
    //              numbers of misses.
    //
    // Returns:  The roster confidence.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static double ScoreRoster(OpcodeValue wireValue, List<CatalogedPacket> packets,
        List<Character> characters, HashSet<uint> groupIds, out string evidence)
    {
        List<byte[]> nameBytes = new List<byte[]>();
        for (int characterIndex = 0; characterIndex < characters.Count; characterIndex++)
        {
            nameBytes.Add(Encoding.ASCII.GetBytes(characters[characterIndex].Name));
        }

        int tests = 0;
        int hits = 0;
        List<uint> misses = new List<uint>();

        for (int packetIndex = 0; packetIndex < packets.Count; packetIndex++)
        {
            CatalogedPacket packet = packets[packetIndex];
            if (packet.Metadata.WireValue != wireValue)
            {
                continue;
            }
            tests++;

            ReadOnlySpan<byte> payload = packet.Payload.AsReadOnlySpan();
            bool repeated = false;
            if (payload.Length >= GroupIdLength)
            {
                for (int nameIndex = 0; nameIndex < nameBytes.Count; nameIndex++)
                {
                    if (InferSupport.CountName(payload, nameBytes[nameIndex]) >= RosterNameRepeats)
                    {
                        repeated = true;
                        break;
                    }
                }
            }

            if (!repeated)
            {
                if (misses.Count < ExampleCount)
                {
                    misses.Add(packet.PacketIndex);
                }
                continue;
            }
            hits++;

            uint groupId = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(payload);
            if (groupId == 0)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferGroupMembership.ScoreRoster: wireValue " + wireValue + " packet " + packet.PacketIndex
                    + " has a repeated name but group id 0", LogLevel.Warn);
                continue;
            }
            if (groupIds.Add(groupId))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferGroupMembership.ScoreRoster: wireValue " + wireValue + " packet " + packet.PacketIndex
                    + " group id 0x" + groupId.ToString("X"), defaultLoglevel);
            }
        }

        double confidence = tests > 0 ? (hits + 1.0) / (tests + 2.0) : 0.0;
        evidence = hits + "/" + tests + " packets with a name twice"
            + (misses.Count > 0 ? ", missing at " + string.Join(", ", misses) : string.Empty);

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferGroupMembership.ScoreRoster: wireValue " + wireValue + " " + evidence + ", "
            + groupIds.Count + " group ids, confidence " + confidence.ToString("F2"), defaultLoglevel);
        return confidence;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CollectMembers
    //
    // Gathers the member packets for one roster candidate: zone-to-client packets of other
    // wire values that hold at least one of the candidate's group ids and the name of
    // exactly one known character.  Each records the named character, whether the packet
    // arrived on that character's own session, and how many times that character's id
    // occurs in the payload.
    //
    // packets:          The unaccepted zone-to-client packets.
    // rosterWireValue:  The wire value taken as the roster; its packets are not members.
    // groupIds:         The group ids read from the roster candidate.
    // characters:       The known characters.
    //
    // Returns:  The member packets, in arrival order.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static List<MemberPacket> CollectMembers(List<CatalogedPacket> packets, OpcodeValue rosterWireValue,
        HashSet<uint> groupIds, List<Character> characters)
    {
        List<byte[]> nameBytes = new List<byte[]>();
        for (int characterIndex = 0; characterIndex < characters.Count; characterIndex++)
        {
            nameBytes.Add(Encoding.ASCII.GetBytes(characters[characterIndex].Name));
        }

        List<MemberPacket> members = new List<MemberPacket>();
        int withGroupId = 0;
        int multipleNames = 0;

        for (int packetIndex = 0; packetIndex < packets.Count; packetIndex++)
        {
            CatalogedPacket packet = packets[packetIndex];
            if (packet.Metadata.WireValue == rosterWireValue)
            {
                continue;
            }

            ReadOnlySpan<byte> payload = packet.Payload.AsReadOnlySpan();
            bool holdsGroupId = false;
            foreach (uint groupId in groupIds)
            {
                if (InferSupport.CountUInt(payload, groupId) > 0)
                {
                    holdsGroupId = true;
                    break;
                }
            }
            if (!holdsGroupId)
            {
                continue;
            }
            withGroupId++;

            Character? named = null;
            int namesFound = 0;
            for (int nameIndex = 0; nameIndex < nameBytes.Count; nameIndex++)
            {
                if (InferSupport.CountName(payload, nameBytes[nameIndex]) > 0)
                {
                    namesFound++;
                    named = characters[nameIndex];
                }
            }
            if (namesFound == 0 || named == null)
            {
                continue;
            }
            if (namesFound > 1)
            {
                multipleNames++;
                continue;
            }

            Character? owner = packet.Metadata.Session.Character;

            MemberPacket member = new MemberPacket();
            member.PacketIndex = packet.PacketIndex;
            member.WireValue = packet.Metadata.WireValue;
            member.Length = payload.Length;
            member.Named = named;
            member.OnNamedSession = owner != null && owner.Name == named.Name;
            member.IdCount = InferSupport.CountUInt(payload, named.CharacterId.Value);
            members.Add(member);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferGroupMembership.CollectMembers: roster " + rosterWireValue + ", " + withGroupId
            + " packets with a group id, " + multipleNames + " with several names left out, "
            + members.Count + " member packets", defaultLoglevel);
        return members;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BuildEvents
    //
    // Groups member packets into events: packets with the same wire value and identical
    // payload bytes are copies of one event.  An event reached the named character when any
    // of its copies arrived on that character's own session.  Each member's payload is read
    // from the catalog by message number; a member whose packet cannot be read is left out.
    //
    // catalog:  The session's cataloged packets.
    // members:  The member packets, in arrival order.
    //
    // Returns:  The events, in order of their first copy.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static List<MemberEvent> BuildEvents(PacketCatalog catalog, List<MemberPacket> members)
    {
        List<MemberEvent> events = new List<MemberEvent>();
        Dictionary<string, MemberEvent> byContent = new Dictionary<string, MemberEvent>();

        for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
        {
            MemberPacket member = members[memberIndex];

            CatalogedPacket? packetOrNull = catalog.PacketAt(member.PacketIndex);
            if (packetOrNull == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferGroupMembership.BuildEvents: no packet at index " + member.PacketIndex, LogLevel.Error);
                continue;
            }

            string key = member.WireValue + ":" + Convert.ToHexString(packetOrNull.Value.Payload.AsReadOnlySpan());

            MemberEvent? memberEvent;
            if (!byContent.TryGetValue(key, out memberEvent))
            {
                memberEvent = new MemberEvent();
                memberEvent.FirstIndex = member.PacketIndex;
                memberEvent.WireValue = member.WireValue;
                memberEvent.Length = member.Length;
                memberEvent.Named = member.Named;
                byContent[key] = memberEvent;
                events.Add(memberEvent);
            }

            if (member.OnNamedSession && !memberEvent.ReachedNamed)
            {
                memberEvent.ReachedNamed = true;
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferGroupMembership.BuildEvents: event at " + memberEvent.FirstIndex + " wireValue "
                    + memberEvent.WireValue + " reached '" + member.Named?.Name + "' at " + member.PacketIndex,
                    defaultLoglevel);
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferGroupMembership.BuildEvents: " + members.Count + " member packets, " + events.Count + " events",
            defaultLoglevel);
        return events;
    }
    ///////////////////////////////////////////////////////////////////////////////////////////
    // MarkRosterLeaders
    //
    // Sets NextRosterLeader on each event from the next roster packet on its first copy's
    // session.  The packets are walked once in arrival order: an event's first copy places
    // the event in waiting on that copy's session, and a roster packet resolves every event
    // waiting on its session, marking it when the event's character's name occurs in the
    // roster at least RosterNameRepeats times.  An event with no later roster on its session
    // is left unmarked.
    //
    // packets:          The unaccepted zone-to-client packets, in arrival order.
    // rosterWireValue:  The wire value taken as the roster.
    // events:           The events to mark.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void MarkRosterLeaders(List<CatalogedPacket> packets, OpcodeValue rosterWireValue,
        List<MemberEvent> events)
    {
        Dictionary<uint, MemberEvent> byFirstIndex = new Dictionary<uint, MemberEvent>();
        for (int eventIndex = 0; eventIndex < events.Count; eventIndex++)
        {
            byFirstIndex[events[eventIndex].FirstIndex] = events[eventIndex];
        }

        Dictionary<Session, List<MemberEvent>> waiting = new Dictionary<Session, List<MemberEvent>>();
        int resolved = 0;
        int matched = 0;

        for (int packetIndex = 0; packetIndex < packets.Count; packetIndex++)
        {
            CatalogedPacket packet = packets[packetIndex];
            Session session = packet.Metadata.Session;

            MemberEvent? memberEvent;
            if (byFirstIndex.TryGetValue(packet.PacketIndex, out memberEvent))
            {
                List<MemberEvent>? sessionWaiting;
                if (!waiting.TryGetValue(session, out sessionWaiting))
                {
                    sessionWaiting = new List<MemberEvent>();
                    waiting[session] = sessionWaiting;
                }
                sessionWaiting.Add(memberEvent);
                continue;
            }

            if (packet.Metadata.WireValue != rosterWireValue)
            {
                continue;
            }

            List<MemberEvent>? rosterWaiting;
            if (!waiting.TryGetValue(session, out rosterWaiting) || rosterWaiting.Count == 0)
            {
                continue;
            }

            ReadOnlySpan<byte> payload = packet.Payload.AsReadOnlySpan();
            for (int waitingIndex = 0; waitingIndex < rosterWaiting.Count; waitingIndex++)
            {
                MemberEvent pending = rosterWaiting[waitingIndex];
                resolved++;
                if (pending.Named == null)
                {
                    DebugLog.Write(LogChannel.InferenceDebug,
                        "InferGroupMembership.MarkRosterLeaders: event at " + pending.FirstIndex
                        + " has no named character", LogLevel.Error);
                    continue;
                }

                byte[] nameBytes = Encoding.ASCII.GetBytes(pending.Named.Name);
                pending.NextRosterLeader = InferSupport.CountName(payload, nameBytes) >= RosterNameRepeats;
                if (pending.NextRosterLeader)
                {
                    matched++;
                }

                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferGroupMembership.MarkRosterLeaders: event at " + pending.FirstIndex + " wireValue "
                    + pending.WireValue + " '" + pending.Named.Name + "' roster at " + packet.PacketIndex
                    + (pending.NextRosterLeader ? " names it leader" : " does not name it leader"),
                    defaultLoglevel);
            }
            rosterWaiting.Clear();
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferGroupMembership.MarkRosterLeaders: roster " + rosterWireValue + ", " + events.Count + " events, "
            + resolved + " followed by a roster, " + matched + " named leader, "
            + (events.Count - resolved) + " with no later roster", defaultLoglevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScoreMembers
    //
    // Chooses the join, leader and leave wire values for one roster candidate and builds the
    // group proposal.  Every share is scored as (hits + 1) / (tests + 2), and only a wire
    // value with at least one hit is chosen; a member with no choice has confidence 0.
    //
    // The join is chosen first, over member packets: a hit is a packet on the named
    // character's own session holding that character's id at least JoinIdRepeats times.  The
    // join length is the payload length of the chosen join's first hit.  The leader is chosen
    // next, over events of the other wire values: a hit is an event that reached the named
    // character and whose next roster names that character leader.  The leave is chosen
    // last, over events of the remaining wire values: a hit is an event that did not reach
    // the named character.  When a join was chosen, a leader or leave event is a hit only
    // when its length equals the join length.  The group confidence is the lowest of the
    // four member confidences.  Each member's evidence lists up to ExampleCount message
    // numbers where its test failed.
    //
    // rosterWireValue:   The wire value taken as the roster.
    // rosterConfidence:  The roster's confidence.
    // rosterEvidence:    The roster's evidence.
    // members:           The member packets for this roster candidate.
    // events:            The events built from those member packets.
    //
    // Returns:  The group proposal.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static InferenceProposal ScoreMembers(OpcodeValue rosterWireValue, double rosterConfidence,
        string rosterEvidence, List<MemberPacket> members, List<MemberEvent> events)
    {
        static OpcodeValue BestShare(Dictionary<OpcodeValue, int> tests, Dictionary<OpcodeValue, int> hits,
            out int bestHits, out int bestTests)
        {
            OpcodeValue winner = OpcodeValue.None;
            double bestShare = 0.0;
            bestHits = 0;
            bestTests = 0;

            foreach (KeyValuePair<OpcodeValue, int> entry in hits)
            {
                int testCount = tests[entry.Key];
                double share = (entry.Value + 1.0) / (testCount + 2.0);
                if (share > bestShare)
                {
                    winner = entry.Key;
                    bestShare = share;
                    bestHits = entry.Value;
                    bestTests = testCount;
                }
            }

            return winner;
        }

        // join, over member packets
        Dictionary<OpcodeValue, int> joinTests = new Dictionary<OpcodeValue, int>();
        Dictionary<OpcodeValue, int> joinHits = new Dictionary<OpcodeValue, int>();
        for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
        {
            MemberPacket member = members[memberIndex];
            joinTests[member.WireValue] = joinTests.GetValueOrDefault(member.WireValue) + 1;
            if (member.OnNamedSession && member.IdCount >= JoinIdRepeats)
            {
                joinHits[member.WireValue] = joinHits.GetValueOrDefault(member.WireValue) + 1;
            }
        }

        int joinVotes;
        int joinCount;
        OpcodeValue join = BestShare(joinTests, joinHits, out joinVotes, out joinCount);

        int joinLength = -1;
        List<uint> joinMissing = new List<uint>();
        for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
        {
            MemberPacket member = members[memberIndex];
            if (!join.Exists || member.WireValue != join)
            {
                continue;
            }
            if (member.OnNamedSession && member.IdCount >= JoinIdRepeats)
            {
                if (joinLength < 0)
                {
                    joinLength = member.Length;
                }
            }
            else if (joinMissing.Count < ExampleCount)
            {
                joinMissing.Add(member.PacketIndex);
            }
        }

        if (!join.Exists)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferGroupMembership.ScoreMembers: roster " + rosterWireValue + " has no join hit, length not tested",
                LogLevel.Warn);
        }

        // leader, over events of the other wire values
        Dictionary<OpcodeValue, int> leaderTests = new Dictionary<OpcodeValue, int>();
        Dictionary<OpcodeValue, int> leaderHits = new Dictionary<OpcodeValue, int>();
        for (int eventIndex = 0; eventIndex < events.Count; eventIndex++)
        {
            MemberEvent memberEvent = events[eventIndex];
            if (join.Exists && memberEvent.WireValue == join)
            {
                continue;
            }
            leaderTests[memberEvent.WireValue] = leaderTests.GetValueOrDefault(memberEvent.WireValue) + 1;
            bool lengthMatches = joinLength < 0 || memberEvent.Length == joinLength;
            if (lengthMatches && memberEvent.ReachedNamed && memberEvent.NextRosterLeader)
            {
                leaderHits[memberEvent.WireValue] = leaderHits.GetValueOrDefault(memberEvent.WireValue) + 1;
            }
        }

        int leaderVotes;
        int leaderCount;
        OpcodeValue leader = BestShare(leaderTests, leaderHits, out leaderVotes, out leaderCount);

        // leave, over events of the remaining wire values
        Dictionary<OpcodeValue, int> leaveTests = new Dictionary<OpcodeValue, int>();
        Dictionary<OpcodeValue, int> leaveHits = new Dictionary<OpcodeValue, int>();
        for (int eventIndex = 0; eventIndex < events.Count; eventIndex++)
        {
            MemberEvent memberEvent = events[eventIndex];
            if (join.Exists && memberEvent.WireValue == join)
            {
                continue;
            }
            if (leader.Exists && memberEvent.WireValue == leader)
            {
                continue;
            }
            leaveTests[memberEvent.WireValue] = leaveTests.GetValueOrDefault(memberEvent.WireValue) + 1;
            bool lengthMatches = joinLength < 0 || memberEvent.Length == joinLength;
            if (lengthMatches && !memberEvent.ReachedNamed)
            {
                leaveHits[memberEvent.WireValue] = leaveHits.GetValueOrDefault(memberEvent.WireValue) + 1;
            }
        }

        int leaveVotes;
        int leaveCount;
        OpcodeValue leave = BestShare(leaveTests, leaveHits, out leaveVotes, out leaveCount);

        List<uint> leaderMissing = new List<uint>();
        List<uint> leaveMissing = new List<uint>();
        for (int eventIndex = 0; eventIndex < events.Count; eventIndex++)
        {
            MemberEvent memberEvent = events[eventIndex];
            bool lengthMatches = joinLength < 0 || memberEvent.Length == joinLength;
            bool leaderHit = lengthMatches && memberEvent.ReachedNamed && memberEvent.NextRosterLeader;
            bool leaveHit = lengthMatches && !memberEvent.ReachedNamed;
            if (leader.Exists && memberEvent.WireValue == leader && !leaderHit
                && leaderMissing.Count < ExampleCount)
            {
                leaderMissing.Add(memberEvent.FirstIndex);
            }
            if (leave.Exists && memberEvent.WireValue == leave && !leaveHit
                && leaveMissing.Count < ExampleCount)
            {
                leaveMissing.Add(memberEvent.FirstIndex);
            }
        }

        double joinConfidence = join.Exists ? (joinVotes + 1.0) / (joinCount + 2.0) : 0.0;
        double leaderConfidence = leader.Exists ? (leaderVotes + 1.0) / (leaderCount + 2.0) : 0.0;
        double leaveConfidence = leave.Exists ? (leaveVotes + 1.0) / (leaveCount + 2.0) : 0.0;

        PatchLevel level = GlassContext.CurrentPatchLevel;
        InferenceProposal proposal = new InferenceProposal();

        proposal.Assignments.Add(new OpcodeAssignment
        {
            OpcodeName = RosterOpcodeName,
            Label = RosterOpcodeName,
            Opcode = new PatchOpcode(level, rosterWireValue),
            Confidence = rosterConfidence,
            Evidence = rosterEvidence
        });
        proposal.Assignments.Add(new OpcodeAssignment
        {
            OpcodeName = JoinOpcodeName,
            Label = JoinOpcodeName,
            Opcode = join.Exists ? new PatchOpcode(level, join) : PatchOpcode.None,
            Confidence = joinConfidence,
            Evidence = joinVotes + "/" + joinCount + " packets on the named session with its id twice"
                + (joinMissing.Count > 0 ? ", missing at " + string.Join(", ", joinMissing) : string.Empty)
        });
        proposal.Assignments.Add(new OpcodeAssignment
        {
            OpcodeName = LeaderOpcodeName,
            Label = LeaderOpcodeName,
            Opcode = leader.Exists ? new PatchOpcode(level, leader) : PatchOpcode.None,
            Confidence = leaderConfidence,
            Evidence = leaderVotes + "/" + leaderCount + " events reaching the named character and named leader by the next roster"
                + (leaderMissing.Count > 0 ? ", missing at " + string.Join(", ", leaderMissing) : string.Empty)
        });
        proposal.Assignments.Add(new OpcodeAssignment
        {
            OpcodeName = LeaveOpcodeName,
            Label = LeaveOpcodeName,
            Opcode = leave.Exists ? new PatchOpcode(level, leave) : PatchOpcode.None,
            Confidence = leaveConfidence,
            Evidence = leaveVotes + "/" + leaveCount + " events not reaching the named character"
                + (leaveMissing.Count > 0 ? ", missing at " + string.Join(", ", leaveMissing) : string.Empty)
        });

        proposal.Confidence = Math.Min(Math.Min(rosterConfidence, joinConfidence),
            Math.Min(leaderConfidence, leaveConfidence));
        proposal.Evidence = "roster " + rosterConfidence.ToString("F2") + ", join " + joinConfidence.ToString("F2")
            + ", leader " + leaderConfidence.ToString("F2") + ", leave " + leaveConfidence.ToString("F2")
            + " | join length " + joinLength + " | group " + proposal.Confidence.ToString("F3");

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferGroupMembership.ScoreMembers: roster " + rosterWireValue + " join " + join + " leader " + leader
            + " leave " + leave + " | " + proposal.Evidence, defaultLoglevel);
        return proposal;
    }
}