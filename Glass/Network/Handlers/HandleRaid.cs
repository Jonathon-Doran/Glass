using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;
using System.Text;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleRaid
//
// Handles OP_Raid packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleRaid : OpcodeHandler
{
    private readonly CollectionHandle _top_level_collection;
    private readonly GateDefinitionHandle _top_level_gate;
    private GateHandle _root_gate;

    private readonly SlotId _subType_Slot;
    private readonly SlotId _name_Slot;
    private readonly SlotId _raidId_Slot;


    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleNewZone(constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleRaid(PatchLevel patchLevel)
        : base(patchLevel, "OP_Raid_General")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        if (_opcodeHandled == PatchOpcode.None)
        {
            // happens when replaying captures before the opcode was added
            return;
        }

        _top_level_collection = _registry.GetCollectionHandle(_patchLevel, "OP_Raid_General");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _subType_Slot = _registry.IndexOfField(_top_level_collection, "Subtype");
        _name_Slot = _registry.IndexOfField(_top_level_collection, "Name");
        _raidId_Slot = _registry.IndexOfField(_top_level_collection, "RaidId");
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
            _root_gate = _extractor.Extract(_top_level_gate, data);

            if (!_root_gate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleRaid:  No RootGate", LogLevel.Error);
                return;
            }

            uint subtype = _extractor.GetUIntAt(_subType_Slot);
            string characterName = _extractor.GetStringAt(_name_Slot);
            uint raidId = _extractor.GetUIntAt(_raidId_Slot);

            HandleAllSubtypes();
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleAllSubtypes - Check for each of the optional subtype collections under the OP_Raid_General header
    //
    // Exactly one subtype should be present in the data
    //
    // Returns:   Nothing
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleAllSubtypes()
    {
        CollectionHandle Z2C_01_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_01");
        CollectionHandle Z2C_03_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_03");

        SlotId Z2C_00_Slot = _registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_00");
        SlotId Z2C_01_Slot = _registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_01");
        SlotId Z2C_03_Slot = _registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_03");

        if (_extractor.IsPresent(Z2C_00_Slot))
        {
            GateHandle z2c_00_Gate = _extractor.GetGateAt(Z2C_00_Slot);
            if (z2c_00_Gate.Exists)
            {
                if (Handle_Z2C_00(z2c_00_Gate) == true)
                {
                    return;
                }
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Handle_Z2C_00 -- handle the optional Z2C 00 subtype fields for OP_Raid_General
    //
    // Extracts the data from the subtype fields
    //
    // gate:  A gate to the subtype data
    //
    // Returns:   True if a bag is extracted, false if no data exists
    ///////////////////////////////////////////////////////////////////////////////////////////////

    private bool Handle_Z2C_00(GateHandle gate)
    {
        uint bagCount = _extractor.BagCount(gate);

        if (bagCount == 0)
        {
            return false;
        }

        _extractor.EnterGate(gate, 0);

        CollectionHandle Z2C_00_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_00");
        SlotId name_Slot = _registry.IndexOfField(Z2C_00_Collection, "Name");
        SlotId characterId_Slot = _registry.IndexOfField(Z2C_00_Collection, "CharacterID");
        SlotId position_Slot = _registry.IndexOfField(Z2C_00_Collection, "Position");
        SlotId class_Slot = _registry.IndexOfField(Z2C_00_Collection, "Class");
        SlotId level_Slot = _registry.IndexOfField(Z2C_00_Collection, "Level");

        _extractor.EnterGate(_root_gate, 0);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Handle_Z2C_01 -- handle the optional Z2C 01 subtype fields for OP_Raid_General
    //
    // Extracts the data from the subtype fields
    //
    // gate:  A gate to the subtype data
    //
    // Returns:   True if a bag is extracted, false if no data exists
    ///////////////////////////////////////////////////////////////////////////////////////////////

    private bool Handle_Z2C_01(GateHandle gate)
    {
        uint bagCount = _extractor.BagCount(gate);

        if (bagCount == 0)
        {
            return false;
        }

        _extractor.EnterGate(gate, 0);

        CollectionHandle Z2C_00_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_01");
        SlotId name_Slot = _registry.IndexOfField(Z2C_00_Collection, "Name");
  
        _extractor.EnterGate(_root_gate, 0);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_Raid against the active patch and builds a display tree: a root node for
    // the collection with one leaf child per field (short_name, long_name, zone_id), each
    // carrying its payload byte range.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    //
    // Returns:   The root FieldDisplayNode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldDisplayNode root = new FieldDisplayNode();

        try
        {
            _root_gate = _extractor.Extract(_top_level_gate, data);

            if (!_root_gate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleRaid:  No RootGate", LogLevel.Error);
                return root;
            }

            uint subtype = FieldNodes.AddUIntNode(_extractor, _subType_Slot, "Subtype", root);
            FieldNodes.AddStringNode(_extractor, _name_Slot, "Name", root);
            FieldNodes.AddUIntNode(_extractor, _raidId_Slot, "Raid ID or Ticket", root);

            string operation;

            switch (subtype)
            {
                case 0x0:  operation = "Add member to raid"; break;
                case 0x1:  operation = "Remove member from raid"; break;
                case 0x3:  operation = "Member change group"; break;
                case 0x5:  operation = "Disband raid"; break;
                case 0x6:  operation = "Raid roster"; break;
                case 0x8:  operation = "Form raid"; break;
                case 0xa:  operation = "Initialize raid"; break;
                case 0xd:  operation = "New Group Leader"; break;
                case 0xf:  operation = "Member leaves group"; break;
                case 0x10:  operation = "Member joins group"; break;
                case 0x11:  operation = "Request raid lock"; break;
                case 0x12:  operation = "Request raid unlock"; break;
                case 0x14:  operation = "Invite character to raid"; break;
                case 0x16:  operation = "Invite group to raid"; break;
                case 0x1E:  operation = "Raid leadership change"; break;
                default:    operation = "Unknown 0x" + subtype.ToString("X"); break;
            }
            root.Text = OpcodeName + " (" + operation + ")";

            DescribeAllSubtypes(root);
        }
        finally
        {
            _extractor.Release();
        }

        return root;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // DescribeAllSubtypes - Check for each of the optional subtype collections under the OP_Raid_General header
    //
    // Exactly one subtype should be present in the data
    //
    // parent:  The parent FDN where new data should be stored
    //
    // Returns:   Nothing
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void DescribeAllSubtypes(FieldDisplayNode parent)
    {
        SlotId Z2C_00_Slot = _registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_00");
        SlotId Z2C_01_Slot = _registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_01");
        SlotId Z2C_03_Slot = _registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_03");
        SlotId Z2C_05_Slot = _registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_05");
        SlotId Z2C_06_Slot = _registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_06");
        SlotId Z2C_08_Slot = _registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_08");
        SlotId Z2C_0A_Slot = _registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_0A");
        SlotId Z2C_0D_Slot = _registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_0D");
        SlotId Z2C_0F_Slot = _registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_0F");
        SlotId Z2C_10_Slot = _registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_10");
        SlotId Z2C_11_Slot = _registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_11");
        SlotId Z2C_12_Slot = _registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_12");
        SlotId Z2C_14_Slot = _registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_14");
        SlotId Z2C_16_Slot = _registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_16");
        SlotId Z2C_1E_Slot = _registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_1E");

        if (_extractor.IsPresent(Z2C_00_Slot))
        {
            GateHandle z2c_00_Gate = _extractor.GetGateAt(Z2C_00_Slot);
            if (z2c_00_Gate.Exists)
            {
                if (Describe_Z2C_00(z2c_00_Gate, parent) == true)
                {
                    return;
                }
            }
        }

        if (_extractor.IsPresent(Z2C_01_Slot))
        {
            GateHandle z2c_01_Gate = _extractor.GetGateAt(Z2C_01_Slot);
            if (z2c_01_Gate.Exists)
            {
                if (Describe_Z2C_01(z2c_01_Gate, parent) == true)
                {
                    return;
                }
            }
        }

        if (_extractor.IsPresent(Z2C_03_Slot))
        {
            GateHandle z2c_03_Gate = _extractor.GetGateAt(Z2C_03_Slot);
            if (z2c_03_Gate.Exists)
            {
                if (Describe_Z2C_03(z2c_03_Gate, parent) == true)
                {
                    return;
                }
            }
        }

        if (_extractor.IsPresent(Z2C_05_Slot))
        {
            GateHandle z2c_05_Gate = _extractor.GetGateAt(Z2C_05_Slot);
            if (z2c_05_Gate.Exists)
            {
                // uses the same format as Z2C_01
                if (Describe_Z2C_01(z2c_05_Gate, parent) == true)
                {
                    return;
                }
            }
        }

        if (_extractor.IsPresent(Z2C_06_Slot))
        {
            GateHandle z2c_06_Gate = _extractor.GetGateAt(Z2C_06_Slot);
            if (z2c_06_Gate.Exists)
            {
                if (Describe_Z2C_06(z2c_06_Gate, parent) == true)
                {
                    return;
                }
            }
        }

        if (_extractor.IsPresent(Z2C_08_Slot))
        {
            GateHandle z2c_08_Gate = _extractor.GetGateAt(Z2C_08_Slot);
            if (z2c_08_Gate.Exists)
            {
                if (Describe_Z2C_08(z2c_08_Gate, parent) == true)
                {
                    return;
                }
            }
        }

        if (_extractor.IsPresent(Z2C_0A_Slot))
        {
            GateHandle z2c_0A_Gate = _extractor.GetGateAt(Z2C_0A_Slot);
            if (z2c_0A_Gate.Exists)
            {
                if (Describe_Z2C_0A(z2c_0A_Gate, parent) == true)
                {
                    return;
                }
            }
        }

        if (_extractor.IsPresent(Z2C_0D_Slot))
        {
            GateHandle z2c_0D_Gate = _extractor.GetGateAt(Z2C_0D_Slot);
            if (z2c_0D_Gate.Exists)
            {
                if (Describe_Z2C_0D(z2c_0D_Gate, parent) == true)
                {
                    return;
                }
            }
        }

        if (_extractor.IsPresent(Z2C_0F_Slot))
        {
            GateHandle z2c_0F_Gate = _extractor.GetGateAt(Z2C_0F_Slot);
            if (z2c_0F_Gate.Exists)
            {
                if (Describe_Z2C_0F(z2c_0F_Gate, parent) == true)
                {
                    return;
                }
            }
        }

        if (_extractor.IsPresent(Z2C_10_Slot))
        {
            GateHandle z2c_10_Gate = _extractor.GetGateAt(Z2C_10_Slot);
            if (z2c_10_Gate.Exists)
            {
                if (Describe_Z2C_10(z2c_10_Gate, parent) == true)
                {
                    return;
                }
            }
        }

        if (_extractor.IsPresent(Z2C_11_Slot))
        {
            GateHandle z2c_11_Gate = _extractor.GetGateAt(Z2C_11_Slot);
            if (z2c_11_Gate.Exists)
            {
                if (Describe_Z2C_11(z2c_11_Gate, parent) == true)
                {
                    return;
                }
            }
        }

        // left off

        if (_extractor.IsPresent(Z2C_12_Slot))
        {
            GateHandle z2c_12_Gate = _extractor.GetGateAt(Z2C_12_Slot);
            if (z2c_12_Gate.Exists)
            {
                if (Describe_Z2C_12(z2c_12_Gate, parent) == true)
                {
                    return;
                }
            }
        }

        if (_extractor.IsPresent(Z2C_14_Slot))
        {
            GateHandle z2c_14_Gate = _extractor.GetGateAt(Z2C_14_Slot);
            if (z2c_14_Gate.Exists)
            {
                if (Describe_Z2C_14(z2c_14_Gate, parent) == true)
                {
                    return;
                }
            }
        }

        if (_extractor.IsPresent(Z2C_16_Slot))
        {
            GateHandle z2c_16_Gate = _extractor.GetGateAt(Z2C_16_Slot);
            if (z2c_16_Gate.Exists)
            {
                if (Describe_Z2C_16(z2c_16_Gate, parent) == true)
                {
                    return;
                }
            }
        }

        if (_extractor.IsPresent(Z2C_1E_Slot))
        {
            GateHandle z2c_1E_Gate = _extractor.GetGateAt(Z2C_1E_Slot);
            if (z2c_1E_Gate.Exists)
            {
                if (Describe_Z2C_1E(z2c_1E_Gate, parent) == true)
                {
                    return;
                }
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_00 -- describe the optional Z2C 00 subtype fields for OP_Raid_General (Add Member)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // gate:      A gate to the subtype data
    // parent:    The parent FDN where data should be displayed
    //
    // Returns:   True if a bag is extracted, false if no data exists
    ///////////////////////////////////////////////////////////////////////////////////////////////

    private bool Describe_Z2C_00(GateHandle gate, FieldDisplayNode parent)
    {
        uint bagCount = _extractor.BagCount(gate);

        if (bagCount == 0)
        {
            return false;
        }

        _extractor.EnterGate(gate, 0);

        CollectionHandle Z2C_00_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_00");
        SlotId name_Slot = _registry.IndexOfField(Z2C_00_Collection, "Name");
        SlotId characterId_Slot = _registry.IndexOfField(Z2C_00_Collection, "CharacterID");
        SlotId position_Slot = _registry.IndexOfField(Z2C_00_Collection, "Position");
        SlotId class_Slot = _registry.IndexOfField(Z2C_00_Collection, "Class");
        SlotId level_Slot = _registry.IndexOfField(Z2C_00_Collection, "Level");
        SlotId isGroupLeader_Slot = _registry.IndexOfField(Z2C_00_Collection, "Is_Group_Leader");

        FieldNodes.AddStringNode(_extractor, name_Slot, "New Member", parent);
        FieldNodes.AddUIntNode(_extractor, characterId_Slot, "Character ID", parent);
        FieldNodes.AddUIntNode(_extractor, position_Slot, "Position", parent);

        EQClass classValue = (EQClass)_extractor.GetUIntAt(class_Slot);

        FieldNodes.AddLabeledNode(_extractor, class_Slot, "Class: " + classValue.ToDisplayString() + " (" + classValue.ToString("X") +
            ")", parent);
        FieldNodes.AddUIntNode(_extractor, level_Slot, "Level", parent);
        FieldNodes.AddUIntNode(_extractor, isGroupLeader_Slot, "Is Group Leader", parent);

        _extractor.EnterGate(_root_gate, 0);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_01 -- describe the optional Z2C 01 subtype fields for OP_Raid_General
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // gate:      A gate to the subtype data
    // parent:    The parent FDN where data should be displayed
    //
    // Returns:   True if a bag is extracted, false if no data exists
    ///////////////////////////////////////////////////////////////////////////////////////////////

    private bool Describe_Z2C_01(GateHandle gate, FieldDisplayNode parent)
    {
        uint bagCount = _extractor.BagCount(gate);

        if (bagCount == 0)
        {
            return false;
        }

        _extractor.EnterGate(gate, 0);

        CollectionHandle Z2C_01_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_01");
        SlotId name_Slot = _registry.IndexOfField(Z2C_01_Collection, "Name");

        FieldNodes.AddStringNode(_extractor, name_Slot, "Name", parent);
 

        _extractor.EnterGate(_root_gate, 0);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_03 -- describe the optional Z2C 03 subtype fields for OP_Raid_General
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // gate:      A gate to the subtype data
    // parent:    The parent FDN where data should be displayed
    //
    // Returns:   True if a bag is extracted, false if no data exists
    ///////////////////////////////////////////////////////////////////////////////////////////////

    private bool Describe_Z2C_03(GateHandle gate, FieldDisplayNode parent)
    {
        uint bagCount = _extractor.BagCount(gate);

        if (bagCount == 0)
        {
            return false;
        }

        _extractor.EnterGate(gate, 0);

        CollectionHandle Z2C_03_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_03");
        SlotId name_Slot = _registry.IndexOfField(Z2C_03_Collection, "Name");
        SlotId groupId_Slot = _registry.IndexOfField(Z2C_03_Collection, "GroupId");

        FieldNodes.AddStringNode(_extractor, name_Slot, "Name", parent);
        FieldNodes.AddUIntNode(_extractor, groupId_Slot, "Group", parent, "X");


        _extractor.EnterGate(_root_gate, 0);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_06 -- describe the optional Z2C 06 subtype fields for OP_Raid_General
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // gate:      A gate to the subtype data
    // parent:    The parent FDN where data should be displayed
    //
    // Returns:   True if a bag is extracted, false if no data exists
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private bool Describe_Z2C_06(GateHandle gate, FieldDisplayNode parent)
    {
        uint bagCount = _extractor.BagCount(gate);

        if (bagCount == 0)
        {
            return false;
        }

        _extractor.EnterGate(gate, 0);

        CollectionHandle Z2C_06_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_Roster");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_06_Collection, "Unknown_1");
        SlotId unknown2_Slot = _registry.IndexOfField(Z2C_06_Collection, "Unknown_2");
        SlotId leaderName_Slot = _registry.IndexOfField(Z2C_06_Collection, "Leader_Name");
        SlotId memberCount_Slot = _registry.IndexOfField(Z2C_06_Collection, "Member_Count");
        SlotId roster_Slot = _registry.IndexOfField(Z2C_06_Collection, "Roster");


        FieldNodes.AddStringNode(_extractor, leaderName_Slot, "Leader", parent);
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown2_Slot, "Unknown 2", parent, "X");
        FieldNodes.AddUIntNode(_extractor, memberCount_Slot, "Member Count", parent, "D");

        CollectionHandle RosterEntries_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_Roster_Entry");
        SlotId entryMemberName_Slot = _registry.IndexOfField(RosterEntries_Collection, "Member_Name");
        SlotId entryUnknown1_Slot = _registry.IndexOfField(RosterEntries_Collection, "Unknown_1");
        SlotId entryClass_Slot = _registry.IndexOfField(RosterEntries_Collection, "Class");
        SlotId entryGroup_Slot = _registry.IndexOfField(RosterEntries_Collection, "Group_Number");
        SlotId entryLevel_Slot = _registry.IndexOfField(RosterEntries_Collection, "Level");
        SlotId entryIsRLeader_Slot = _registry.IndexOfField(RosterEntries_Collection, "Is_Raid_Leader");
        SlotId entryIsGLeader_Slot = _registry.IndexOfField(RosterEntries_Collection, "Is_Group_Leader");

        GateHandle Roster_Entries_Gate = _extractor.GetGateAt(roster_Slot);
        if (Roster_Entries_Gate.Exists)
        {
            uint membersSeen = 0;

            uint rosterBagCount = _extractor.BagCount(Roster_Entries_Gate);
            FieldDisplayNode rosterEntries = new FieldDisplayNode("Members");
            for (uint bagIndex = 0; bagIndex < rosterBagCount; bagIndex++)
            {
                _extractor.EnterGate(Roster_Entries_Gate, bagIndex);
                membersSeen++;

                string memberName = _extractor.GetStringAt(entryMemberName_Slot);
                EQClass classValue = (EQClass)_extractor.GetUIntAt(entryClass_Slot);

                FieldDisplayNode memberNode = new FieldDisplayNode(memberName);
                rosterEntries.AddChild(memberNode);

                FieldNodes.AddStringNode(_extractor, entryMemberName_Slot, "Member", memberNode);
                FieldNodes.AddLabeledNode(_extractor, entryClass_Slot, "Class: " + classValue.ToDisplayString() + " (0x" +
                       classValue.ToString("X") + ")", memberNode);
                FieldNodes.AddUIntNode(_extractor, entryLevel_Slot, "Level", memberNode, "D");
                FieldNodes.AddUIntNode(_extractor, entryGroup_Slot, "Group", memberNode, "D");
                FieldNodes.AddUIntNode(_extractor, entryIsRLeader_Slot, "Is Raid Leader", memberNode, "D");
                FieldNodes.AddUIntNode(_extractor, entryIsGLeader_Slot, "Is Group Leader", memberNode, "D");
                FieldNodes.AddUIntNode(_extractor, entryUnknown1_Slot, "Unknown 1", memberNode);
            }
            if (membersSeen > 0)
            {
                parent.AddChild(rosterEntries);
            }
        }

        _extractor.EnterGate(_root_gate, 0);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_08 -- describe the optional Z2C 08 subtype fields for OP_Raid_General
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // gate:      A gate to the subtype data
    // parent:    The parent FDN where data should be displayed
    //
    // Returns:   True if a bag is extracted, false if no data exists
    ///////////////////////////////////////////////////////////////////////////////////////////////

    private bool Describe_Z2C_08(GateHandle gate, FieldDisplayNode parent)
    {
        uint bagCount = _extractor.BagCount(gate);

        if (bagCount == 0)
        {
            return false;
        }

        _extractor.EnterGate(gate, 0);

        CollectionHandle Z2C_08_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_08");
        SlotId name_Slot = _registry.IndexOfField(Z2C_08_Collection, "Name");
        SlotId characterID_Slot = _registry.IndexOfField(Z2C_08_Collection, "CharacterId");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_08_Collection, "Unknown1");

        FieldNodes.AddStringNode(_extractor, name_Slot, "Leader ", parent);
        FieldNodes.AddUIntNode(_extractor, characterID_Slot, "Character ID", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");

        _extractor.EnterGate(_root_gate, 0);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_0A -- describe the optional Z2C 0A subtype fields for OP_Raid_General (Initialize Raid)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // gate:      A gate to the subtype data
    // parent:    The parent FDN where data should be displayed
    //
    // Returns:   True if a bag is extracted, false if no data exists
    ///////////////////////////////////////////////////////////////////////////////////////////////

    private bool Describe_Z2C_0A(GateHandle gate, FieldDisplayNode parent)
    {
        uint bagCount = _extractor.BagCount(gate);

        if (bagCount == 0)
        {
            return false;
        }

        _extractor.EnterGate(gate, 0);

        CollectionHandle Z2C_0A_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_0A");
        SlotId name_Slot = _registry.IndexOfField(Z2C_0A_Collection, "Name");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_0A_Collection, "Unknown1");
        SlotId unknown2_Slot = _registry.IndexOfField(Z2C_0A_Collection, "Unknown2");

        FieldNodes.AddStringNode(_extractor, name_Slot, "Name", parent);
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown2_Slot, "Unknown 2", parent, "X");

        _extractor.EnterGate(_root_gate, 0);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_0D -- describe the optional Z2C 0D subtype fields for OP_Raid_General  (New group leader)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // gate:      A gate to the subtype data
    // parent:    The parent FDN where data should be displayed
    //
    // Returns:   True if a bag is extracted, false if no data exists
    ///////////////////////////////////////////////////////////////////////////////////////////////

    private bool Describe_Z2C_0D(GateHandle gate, FieldDisplayNode parent)
    {
        uint bagCount = _extractor.BagCount(gate);

        if (bagCount == 0)
        {
            return false;
        }

        _extractor.EnterGate(gate, 0);

        CollectionHandle Z2C_0D_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_0D");
        SlotId name_Slot = _registry.IndexOfField(Z2C_0D_Collection, "Name");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_0D_Collection, "Unknown1");
        SlotId group_Slot = _registry.IndexOfField(Z2C_0D_Collection, "Group_Number");

        FieldNodes.AddStringNode(_extractor, name_Slot, "New Group Leader", parent);
        FieldNodes.AddUIntNode(_extractor, group_Slot, "Group", parent, "D");
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");

        _extractor.EnterGate(_root_gate, 0);
        return true;
    }


    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_0F -- describe the optional Z2C 0F subtype fields for OP_Raid_General (Member Leaves Group)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // gate:      A gate to the subtype data
    // parent:    The parent FDN where data should be displayed
    //
    // Returns:   True if a bag is extracted, false if no data exists
    ///////////////////////////////////////////////////////////////////////////////////////////////

    private bool Describe_Z2C_0F(GateHandle gate, FieldDisplayNode parent)
    {
        uint bagCount = _extractor.BagCount(gate);

        if (bagCount == 0)
        {
            return false;
        }

        _extractor.EnterGate(gate, 0);

        CollectionHandle Z2C_0F_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_0F");
        SlotId name_Slot = _registry.IndexOfField(Z2C_0F_Collection, "Name");
        SlotId leaderId_Slot = _registry.IndexOfField(Z2C_0F_Collection, "Leader_ID");
        SlotId leavingCharName_Slot = _registry.IndexOfField(Z2C_0F_Collection, "Leaving_Character");
        SlotId characterId_Slot = _registry.IndexOfField(Z2C_0F_Collection, "Character_ID");

        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_0F_Collection, "Unknown1");
        SlotId unknown2_Slot = _registry.IndexOfField(Z2C_0F_Collection, "Unknown2");
        SlotId unknown3_Slot = _registry.IndexOfField(Z2C_0F_Collection, "Unknown3");

        FieldNodes.AddStringNode(_extractor, name_Slot, "Leader", parent);
        FieldNodes.AddUIntNode(_extractor, leaderId_Slot, "Leader ID", parent, "X");
        FieldNodes.AddStringNode(_extractor, leavingCharName_Slot, "Leaving Character", parent);
        FieldNodes.AddUIntNode(_extractor, characterId_Slot, "Chararacter ID", parent, "X");

        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown2_Slot, "Unknown 2", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown3_Slot, "Unknown 3", parent, "X");
        _extractor.EnterGate(_root_gate, 0);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_10 -- describe the optional Z2C 10 subtype fields for OP_Raid_General (Member Joins Group)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // gate:      A gate to the subtype data
    // parent:    The parent FDN where data should be displayed
    //
    // Returns:   True if a bag is extracted, false if no data exists
    ///////////////////////////////////////////////////////////////////////////////////////////////

    private bool Describe_Z2C_10(GateHandle gate, FieldDisplayNode parent)
    {
        uint bagCount = _extractor.BagCount(gate);

        if (bagCount == 0)
        {
            return false;
        }

        _extractor.EnterGate(gate, 0);

        CollectionHandle Z2C_10_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_10");
        SlotId name_Slot = _registry.IndexOfField(Z2C_10_Collection, "Name");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_10_Collection, "Unknown1");
        SlotId unknown2_Slot = _registry.IndexOfField(Z2C_10_Collection, "Unknown2");

        FieldNodes.AddStringNode(_extractor, name_Slot, "Name", parent);
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown2_Slot, "Unknown 2", parent, "X");

        _extractor.EnterGate(_root_gate, 0);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_11 -- describe the optional Z2C 11 subtype fields for OP_Raid_General (Request lock)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // gate:      A gate to the subtype data
    // parent:    The parent FDN where data should be displayed
    //
    // Returns:   True if a bag is extracted, false if no data exists
    ///////////////////////////////////////////////////////////////////////////////////////////////

    private bool Describe_Z2C_11(GateHandle gate, FieldDisplayNode parent)
    {
        uint bagCount = _extractor.BagCount(gate);

        if (bagCount == 0)
        {
            return false;
        }

        _extractor.EnterGate(gate, 0);

        CollectionHandle Z2C_11_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_11");
        SlotId name_Slot = _registry.IndexOfField(Z2C_11_Collection, "Name");
        SlotId characterId_Slot = _registry.IndexOfField(Z2C_11_Collection, "CharacterId");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_11_Collection, "Unknown1");

        FieldNodes.AddStringNode(_extractor, name_Slot, "Locker", parent);
        FieldNodes.AddUIntNode(_extractor, characterId_Slot, "ID", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");

        _extractor.EnterGate(_root_gate, 0);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_12 -- describe the optional Z2C 12 subtype fields for OP_Raid_General (Request Unlock)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // gate:      A gate to the subtype data
    // parent:    The parent FDN where data should be displayed
    //
    // Returns:   True if a bag is extracted, false if no data exists
    ///////////////////////////////////////////////////////////////////////////////////////////////

    private bool Describe_Z2C_12(GateHandle gate, FieldDisplayNode parent)
    {
        uint bagCount = _extractor.BagCount(gate);

        if (bagCount == 0)
        {
            return false;
        }

        _extractor.EnterGate(gate, 0);

        CollectionHandle Z2C_12_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_12");
        SlotId name_Slot = _registry.IndexOfField(Z2C_12_Collection, "Name");
        SlotId characterId_Slot = _registry.IndexOfField(Z2C_12_Collection, "CharacterId");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_12_Collection, "Unknown1");

        FieldNodes.AddStringNode(_extractor, name_Slot, "Unlocker", parent);
        FieldNodes.AddUIntNode(_extractor, characterId_Slot, "ID", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");

        _extractor.EnterGate(_root_gate, 0);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_14 -- describe the optional Z2C 14 subtype fields for OP_Raid_General (Invite Character to Raid)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // gate:      A gate to the subtype data
    // parent:    The parent FDN where data should be displayed
    //
    // Returns:   True if a bag is extracted, false if no data exists
    ///////////////////////////////////////////////////////////////////////////////////////////////

    private bool Describe_Z2C_14(GateHandle gate, FieldDisplayNode parent)
    {
        uint bagCount = _extractor.BagCount(gate);

        if (bagCount == 0)
        {
            return false;
        }

        _extractor.EnterGate(gate, 0);

        CollectionHandle Z2C_14_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_14");
        SlotId name_Slot = _registry.IndexOfField(Z2C_14_Collection, "Character_Name");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_14_Collection, "Unknown1");
        SlotId unknown2_Slot = _registry.IndexOfField(Z2C_14_Collection, "Unknown2");

        FieldNodes.AddStringNode(_extractor, name_Slot, "Raid Leader", parent);
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown2_Slot, "Unknown 2", parent, "X");

        _extractor.EnterGate(_root_gate, 0);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_16 -- describe the optional Z2C 16 subtype fields for OP_Raid_General (Invite Group to Raid)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // gate:      A gate to the subtype data
    // parent:    The parent FDN where data should be displayed
    //
    // Returns:   True if a bag is extracted, false if no data exists
    ///////////////////////////////////////////////////////////////////////////////////////////////

    private bool Describe_Z2C_16(GateHandle gate, FieldDisplayNode parent)
    {
        uint bagCount = _extractor.BagCount(gate);

        if (bagCount == 0)
        {
            return false;
        }

        _extractor.EnterGate(gate, 0);

        CollectionHandle Z2C_16_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_16");
        SlotId name_Slot = _registry.IndexOfField(Z2C_16_Collection, "Name");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_16_Collection, "Unknown1");
        SlotId unknown2_Slot = _registry.IndexOfField(Z2C_16_Collection, "Unknown2");

        FieldNodes.AddStringNode(_extractor, name_Slot, "Raid Leader", parent);
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown2_Slot, "Unknown 2", parent, "X");

        _extractor.EnterGate(_root_gate, 0);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_1E -- describe the optional Z2C 1E subtype fields for OP_Raid_General (Raid Leadership Change)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // gate:      A gate to the subtype data
    // parent:    The parent FDN where data should be displayed
    //
    // Returns:   True if a bag is extracted, false if no data exists
    ///////////////////////////////////////////////////////////////////////////////////////////////

    private bool Describe_Z2C_1E(GateHandle gate, FieldDisplayNode parent)
    {
        uint bagCount = _extractor.BagCount(gate);

        if (bagCount == 0)
        {
            return false;
        }

        _extractor.EnterGate(gate, 0);

        CollectionHandle Z2C_1E_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_1E");
        SlotId name_Slot = _registry.IndexOfField(Z2C_1E_Collection, "New_Leader");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_1E_Collection, "Unknown1");

        FieldNodes.AddStringNode(_extractor, name_Slot, "New Leader", parent);
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");

        _extractor.EnterGate(_root_gate, 0);
        return true;
    }
}
