using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;
using System.Security.Policy;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleGroupRoster
//
// Handles GroupRoster messages.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleGroupRoster : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _groupId_Slot;
    private readonly SlotId _memberCount_Slot;
    private readonly SlotId _leaderName_Slot;
    private readonly SlotId _rosterGate_Slot;

    private readonly SlotId _entryPosition_Slot;
    private readonly SlotId _entryName_Slot;
    private readonly SlotId _entryLevel_Slot;
    private readonly SlotId _entryTimestamp_Slot;

    private readonly SlotId _entryUnknown1_Slot;
    private readonly SlotId _entryUnknown2_Slot;
    private readonly SlotId _entryUnknown3_Slot;
    private readonly SlotId _entryUnknown4_Slot;
    private readonly SlotId _entryUnknown5_Slot;
    private readonly SlotId _entryUnknown6_Slot;
    private readonly SlotId _entryUnknown7_Slot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleGroupRoster (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_GroupRoster from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_GroupRoster, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleGroupRoster(PatchLevel patchLevel)
        : base(patchLevel, "OP_Group_Roster")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        if (_opcodeHandled == PatchOpcode.None)
        {
            // happens when replaying captures before the opcode was added
            return;
        }

        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_Group_Roster");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _groupId_Slot = _registry.IndexOfField(_collectionHandle, "Group_ID");
        _memberCount_Slot = _registry.IndexOfField(_collectionHandle, "Member_Count");
        _leaderName_Slot = _registry.IndexOfField(_collectionHandle, "Leader_Name");
        _rosterGate_Slot = _registry.IndexOfField(_collectionHandle, "Roster");

        // Roster entry fields
        CollectionHandle RosterCollection = _registry.GetCollectionHandle(_patchLevel, "Group_Roster_Entry");
        _entryPosition_Slot = _registry.IndexOfField(RosterCollection, "Position");
        _entryName_Slot = _registry.IndexOfField(RosterCollection, "Name");
        _entryLevel_Slot = _registry.IndexOfField(RosterCollection, "Level");
        _entryTimestamp_Slot = _registry.IndexOfField(RosterCollection, "Timestamp");

        _entryUnknown1_Slot = _registry.IndexOfField(RosterCollection, "Unknown_1");
        _entryUnknown2_Slot = _registry.IndexOfField(RosterCollection, "Unknown_2");
        _entryUnknown3_Slot = _registry.IndexOfField(RosterCollection, "Unknown_3");
        _entryUnknown4_Slot = _registry.IndexOfField(RosterCollection, "Unknown_4");
        _entryUnknown5_Slot = _registry.IndexOfField(RosterCollection, "Unknown_5");
        _entryUnknown6_Slot = _registry.IndexOfField(RosterCollection, "Unknown_6");
        _entryUnknown7_Slot = _registry.IndexOfField(RosterCollection, "Unknown_7");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Dispatches to direction-specific handlers.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        switch (metadata.Channel)
        {
            case SoeConstants.StreamId.StreamZoneToClient:
                HandleZoneToClient(data, metadata);
                break;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleZoneToClient
    //
    // Processes zone-to-client traffic
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleDeath:  No RootGate", LogLevel.Error);
                return;
            }

            uint groupId = _extractor.GetUIntAt(_groupId_Slot);
            uint memberCount = _extractor.GetUIntAt(_memberCount_Slot);
            string leaderName = _extractor.GetStringAt(_leaderName_Slot);

            GateHandle Roster_Entries_Gate = _extractor.GetGateAt(_rosterGate_Slot);
            if (Roster_Entries_Gate.Exists)
            {
                uint rosterBagCount = _extractor.BagCount(Roster_Entries_Gate);

                for (uint bagIndex = 0; bagIndex < rosterBagCount; bagIndex++)
                {
                    _extractor.EnterGate(Roster_Entries_Gate, bagIndex);

                    uint position = _extractor.GetUIntAt(_entryPosition_Slot);
                    string name = _extractor.GetStringAt(_entryName_Slot);
                    uint level = _extractor.GetUIntAt(_entryLevel_Slot);
                    uint timestamp = _extractor.GetUIntAt(_entryTimestamp_Slot);

                    uint unknown_1 = _extractor.GetUIntAt(_entryUnknown1_Slot);
                    uint unknown_2 = _extractor.GetUIntAt(_entryUnknown2_Slot);
                    uint unknown_3 = _extractor.GetUIntAt(_entryUnknown3_Slot);
                    uint unknown_4 = _extractor.GetUIntAt(_entryUnknown4_Slot);
                    uint unknown_5 = _extractor.GetUIntAt(_entryUnknown5_Slot);
                    uint unknown_6 = _extractor.GetUIntAt(_entryUnknown6_Slot);
                    uint unknown_7 = _extractor.GetUIntAt(_entryUnknown7_Slot);
                }
            }
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_GroupRoster against the active patch and builds a display tree: a root node for
    // the collection with one leaf child per field each carrying its payload byte range.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    //
    // Returns:   The root FieldDisplayNode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        ZoneId zoneId = GlassContext.SessionRegistry.ZoneFromMetadata(metadata);
        FieldDisplayNode root = new FieldDisplayNode();

        uint groupId;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupRoster:  No RootGate", LogLevel.Error);
                return root;
            }

            groupId = FieldNodes.AddUIntNode(_extractor, _groupId_Slot, "Group", root);

            string leader = _extractor.GetStringAt(_leaderName_Slot);
            if (leader.Length == 0)
            {
                leader = "None";
            }
            FieldNodes.AddLabeledNode(_extractor, _leaderName_Slot, "Leader: " + leader, root);

            FieldDisplayNode rosterEntries = new FieldDisplayNode("Members");

            GateHandle Roster_Entries_Gate = _extractor.GetGateAt(_rosterGate_Slot);
            if (Roster_Entries_Gate.Exists)
            {
                uint membersSeen = 0;
                uint rosterBagCount = _extractor.BagCount(Roster_Entries_Gate);

                for (uint bagIndex = 0; bagIndex < rosterBagCount; bagIndex++)
                {
                    _extractor.EnterGate(Roster_Entries_Gate, bagIndex);
                    membersSeen++;

                    string name = _extractor.GetStringAt(_entryName_Slot);
                    FieldDisplayNode memberNode = new FieldDisplayNode(name);
                    rosterEntries.AddChild(memberNode);
                    if (name.Length == 0)
                    {
                        name = "None";
                    }
                    FieldNodes.AddLabeledNode(_extractor, _entryName_Slot, "Name: " + name, memberNode);
                    FieldNodes.AddUIntNode(_extractor, _entryPosition_Slot, "Position", memberNode, "D");
                    FieldNodes.AddUIntNode(_extractor, _entryLevel_Slot, "Level", memberNode, "D");

                    uint timestampValue = _extractor.GetUIntAt(_entryTimestamp_Slot);
                    string timestamp = DateTimeOffset.FromUnixTimeSeconds(timestampValue).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

                    if (timestampValue == 0)
                    {
                        timestamp = "None";
                    }

                    FieldNodes.AddLabeledNode(_extractor, _entryTimestamp_Slot, "Timestamp: " + timestamp, memberNode);

                    FieldNodes.AddUIntNode(_extractor, _entryUnknown1_Slot, "Unknown 1", memberNode);
                    FieldNodes.AddUIntNode(_extractor, _entryUnknown2_Slot, "Unknown 2", memberNode);
                    FieldNodes.AddUIntNode(_extractor, _entryUnknown3_Slot, "Unknown 3", memberNode);
                    FieldNodes.AddUIntNode(_extractor, _entryUnknown4_Slot, "Unknown 4", memberNode);
                    FieldNodes.AddUIntNode(_extractor, _entryUnknown5_Slot, "Unknown 5", memberNode);
                    FieldNodes.AddUIntNode(_extractor, _entryUnknown6_Slot, "Unknown 6", memberNode);
                    FieldNodes.AddUIntNode(_extractor, _entryUnknown7_Slot, "Unknown 7", memberNode);
                }

                if (membersSeen > 0)
                {
                    root.AddChild(rosterEntries);
                    string members = (membersSeen == 1) ? "Member" : "Members";

                    rosterEntries.Text = membersSeen.ToString() + " " + members;
                }
            }
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Group Roster (0x" + groupId.ToString("X4") + ")";
        return root;
    }
}