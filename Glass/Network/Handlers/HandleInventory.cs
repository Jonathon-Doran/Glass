using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using PacketDotNet;
using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.Intrinsics.X86;
using System.Security.Policy;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Xml.Linq;
using static Glass.Network.Protocol.PacketBus;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleInventory
//
// Handles OP_Inventory packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleInventory : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_Inventory";
    private readonly CollectionHandle _topLevelCollectionHandle;
    private readonly PatchOpcode _opcodeHandled;
    private readonly PatchRegistry _registry;
    private readonly PatchLevel _patchLevel;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _Gate_Top_Level_Item_List;
    private readonly SlotId _ItemCount_Slot;

    private readonly SlotId _Gate_Inventory_Item_Slot;
    private readonly SlotId _Item_String_Slot;
    private readonly SlotId _Field_2_Slot;
    private readonly SlotId _Field_3_Slot;
    private readonly SlotId _Field_4_Slot;
    private readonly SlotId _Field_5_Slot;
    private readonly SlotId _Field_6_Slot;
    private readonly SlotId _Field_7_Slot;
    private readonly SlotId _Field_8_Slot;
    private readonly SlotId _Field_9_Slot;
    private readonly SlotId _Field_10_Slot;
    private readonly SlotId _Field_11_Slot;
    private readonly SlotId _Field_12_Slot;
    private readonly SlotId _Field_13_Slot;
    private readonly SlotId _Field_14_Slot;
    private readonly SlotId _Field_15_Slot;
    private readonly SlotId _Field_16_Slot;
    private readonly SlotId _Field_17_Slot;
    private readonly SlotId _Presence_Slot;
    private readonly SlotId _Gate_InventoryOptional24_Slot;
    private readonly SlotId _Field_18_Slot;
    private readonly SlotId _Field_19_Slot;
    private readonly SlotId _Field_20_Slot;
    private readonly SlotId _Field_21_Slot;
    private readonly SlotId _Field_22_Slot;
    private readonly SlotId _Field_23_Slot;
    private readonly SlotId _Field_24_Slot;
    private readonly SlotId _Field_25_Slot;
    private readonly SlotId _Field_26_Slot;
    private readonly SlotId _Field_27_Slot;
    private readonly SlotId _Item_Type_Slot;
    private readonly SlotId _Item_Name_Slot;
    private readonly SlotId _Item_Lore_Slot;
    private readonly SlotId _DF_3_Slot;
    private readonly SlotId _DF_4_Slot;
    private readonly SlotId _Item_ID_Slot;
    private readonly SlotId _Weight_x10_Slot;
    private readonly SlotId _DF_7_Slot;
    private readonly SlotId _DF_8_Slot;
    private readonly SlotId _DF_9_Slot;
    private readonly SlotId _Field_E8_Slot;
    private readonly SlotId _DF_10_Slot;
    private readonly SlotId _DF_11_Slot;
    private readonly SlotId _Icon_ID_Slot;
    private readonly SlotId _DF_13_Slot;
    private readonly SlotId _Save_Cold_Slot;
    private readonly SlotId _Save_Disease_Slot;
    private readonly SlotId _Save_Poison_Slot;
    private readonly SlotId _Save_Magic_Slot;
    private readonly SlotId _Save_Fire_Slot;
    private readonly SlotId _Field_Fb_Slot;
    private readonly SlotId _Plus_Strength_Slot;
    private readonly SlotId _Plus_Stamina_Slot;
    private readonly SlotId _Plus_Agility_Slot;
    private readonly SlotId _Plus_Dexterity_Slot;
    private readonly SlotId _Plus_Charisma_Slot;
    private readonly SlotId _Plus_Intelligence_Slot;
    private readonly SlotId _Plus_Wisdom_Slot;
    private readonly SlotId _Plus_HP_Slot;
    private readonly SlotId _Plus_Mana_Slot;
    private readonly SlotId _Plus_Endurance_Slot;
    private readonly SlotId _Plus_AC_Slot;
    private readonly SlotId _HP_Regen_Slot;
    private readonly SlotId _Mana_Regen_Slot;
    private readonly SlotId _Field_57C_Slot;
    private readonly SlotId _Field_140_Slot;
    private readonly SlotId _Field_144_Slot;
    private readonly SlotId _Field_148_Slot;
    private readonly SlotId _Field_11C_Slot;
    private readonly SlotId _Field_120_Slot;
    private readonly SlotId _Field_118_Slot;
    private readonly SlotId _Field_124_Slot;
    private readonly SlotId _Field_128_Slot;
    private readonly SlotId _Field_12C_Slot;
    private readonly SlotId _Field_134_Slot;
    private readonly SlotId _Field_150_Slot;
    private readonly SlotId _Food_Drink_Value_Slot;
    private readonly SlotId _Required_Level_Slot;
    private readonly SlotId _Recommended_Level_Slot;
    private readonly SlotId _Field_138_Slot;
    private readonly SlotId _Field_151_Slot;
    private readonly SlotId _Weapon_Delay_Slot;
    private readonly SlotId _Field_153_Slot;
    private readonly SlotId _Field_154_Slot;
    private readonly SlotId _Field_155_Slot;
    private readonly SlotId _Field_158_Slot;
    private readonly SlotId _Field_14C_Slot;
    private readonly SlotId _Field_18C_Slot;
    private readonly SlotId _Field_190_Slot;
    private readonly SlotId _Field_194_Slot;
    private readonly SlotId _Field_19C_Slot;
    private readonly SlotId _Field_198_Slot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleInventory (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_Inventory from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleInventory()
    {
        _registry = GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel,  _opcodeName);
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        // handles of collections that we expect
        _topLevelCollectionHandle = _registry.GetCollectionHandle(_patchLevel, "Inventory Top_Level");
        _ItemCount_Slot = _registry.IndexOfField(_topLevelCollectionHandle, "ChildCount");

        CollectionHandle itemCollection = _registry.GetCollectionHandle(_patchLevel, "Inventory Item");
        _Item_Name_Slot = _registry.IndexOfField(itemCollection, "ItemName");
        _Item_Lore_Slot = _registry.IndexOfField(itemCollection, "ItemLore");

    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Dispose
    //
    // Log any errors in the cold-path, dispose of any local storage. 
    ///////////////////////////////////////////////////////////////////////////////////////////////

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeName
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public string OpcodeName
    {
        get { return _opcodeName; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Dispatches to direction-specific handlers.
    //
    // data:       The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        switch (metadata.Channel)
        {
            case SoeConstants.StreamId.StreamClientToZone:
                HandleClientToZone(data, metadata);
                break;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleClientToZone
    //
    // Processes client-to-zone
    //
    // data:    The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandleClientToZone(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        if (metadata.Channel != SoeConstants.StreamId.StreamZoneToClient)
        {
            return;
        }

        FieldExtractor extractor = GlassContext.FieldExtractor;
        uint bagCount = 0;

        DebugLog.Write(LogChannel.Opcodes, "Starting Inventory Extract", LogLevel.Info);
        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);
            bagCount = extractor.BagCount(rootGate);

            DebugLog.Write(LogChannel.Opcodes, "Inventory top level sees " + bagCount + " bags", LogLevel.Info);

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                extractor.EnterGate(rootGate, bagIndex);
            }

        }
        finally
        {
            extractor.Release();
            DebugLog.Write(LogChannel.Opcodes, "Finished with inventory packet", LogLevel.Info);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Builds the inventory display tree for an OP_Inventory packet.  Extracts the packet,
    // walks the top-level Item List gate, and calls DescribeItem for each item so every item
    // and its nested children are built identically.  Each returned item node is added to the
    // root.  Releases the extraction before returning.
    //
    // data:      The application payload for the inventory packet.
    // metadata:  Packet metadata, used to resolve the owning character name.
    //
    // Returns:  The root display node titled with the character name.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;
        FieldDisplayNode root = new FieldDisplayNode();
        string characterName = GlassContext.SessionRegistry.CharacterNameFromMetadata(metadata);
        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);
            SlotId itemListSlot = GlassContext.PatchRegistry.IndexOfField(extractor.CollectionOf(), "Item List");
            GateHandle itemListGate = extractor.GetGateAt(itemListSlot);
            if (itemListGate.Exists == true)
            {
                uint itemCount = extractor.BagCount(itemListGate);
                DebugLog.Write(LogChannel.Opcodes, "Describe: Item List gate has " + itemCount + " items", LogLevel.Info);
                for (uint i = 0; i < itemCount; i++)
                {
                    FieldDisplayNode itemNode = DescribeItem(extractor, itemListGate, i, "Item " + (i+1) + ": ");
                    root.AddChild(itemNode);
                }
            }
            else
            {
                DebugLog.Write(LogChannel.Opcodes, "Describe: no Item List gate", LogLevel.Warn);
            }
        }
        finally
        {
            extractor.Release();
        }
        root.Text = "Inventory (" + characterName + ")";
        return root;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // DescribeItem
    //
    // Builds a display node for the item in the given gate's instance at itemIndex, then
    // descends into that item's ChildItems, recursing to build a child node for each.  Holds
    // its own gate handle and index in locals so the active bag is re-entered after each
    // descent, restoring position on unwind.  The caller supplies the title so top-level and
    // child items can be labelled differently.
    //
    // extractor:  The field extractor for the current extraction.
    // itemGate:   The gate whose instance holds this item.
    // itemIndex:  The instance index of this item within itemGate.
    // title:      The node title the caller has composed for this item.
    //
    // Returns:  The item's display node, with any child items nested underneath.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private FieldDisplayNode DescribeItem(FieldExtractor extractor, GateHandle itemGate,
        uint itemIndex, string title)
    {
        extractor.EnterGate(itemGate, itemIndex);
        FieldDisplayNode itemNode = BuildItemNode(extractor, title);

        SlotId childItemsSlot = GlassContext.PatchRegistry.IndexOfField(extractor.CollectionOf(), "ChildItems");
        if (extractor.IsPresent(childItemsSlot) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "DescribeItem: '" + title + "' has no ChildItems", LogLevel.Trace);
            return itemNode;
        }

        GateHandle childItemsGate = extractor.GetGateAt(childItemsSlot);
        if (childItemsGate.Exists == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "DescribeItem: '" + title + "' ChildItems slot present but no gate", LogLevel.Warn);
            return itemNode;
        }

        uint childCount = extractor.BagCount(childItemsGate);
        DebugLog.Write(LogChannel.Opcodes, "DescribeItem: '" + title + "' is a container with " + childCount + " child slot(s)", LogLevel.Info);

        for (uint c = 0; c < childCount; c++)
        {
            extractor.EnterGate(childItemsGate, c);

            SlotId childIndexSlot = GlassContext.PatchRegistry.IndexOfField(extractor.CollectionOf(), "Child Index");
            uint childIndex = extractor.GetUIntAt(childIndexSlot);

            SlotId childItemSlot = GlassContext.PatchRegistry.IndexOfField(extractor.CollectionOf(), "Child Item");
            if (extractor.IsPresent(childItemSlot) == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "DescribeItem: child slot " + childIndex + " has no item", LogLevel.Trace);
                extractor.EnterGate(childItemsGate, c);
                continue;
            }

            GateHandle singleItemGate = extractor.GetGateAt(childItemSlot);
            if (singleItemGate.Exists == true)
            {
                FieldDisplayNode childNode = DescribeItem(extractor, singleItemGate, 0u, "Slot " + (childIndex+1) + ": ");
                itemNode.AddChild(childNode);
            }

            extractor.EnterGate(childItemsGate, c);
        }

        return itemNode;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BuildItemNode
    //
    // Builds a display node for the item in the extractor's active bag: a titled node with
    // the item's Lore field nested underneath.  The caller supplies a title prefix; this
    // method appends the item's Name to it.  Reads the Name and Lore slots from the active
    // bag and attaches their byte ranges.
    //
    // extractor:    The field extractor, positioned with the item's bag active.
    // titlePrefix:  The title prefix the caller has composed (e.g. "Item 3: " or "Slot 5: ").
    //
    // Returns:  The item's display node, with its Lore child attached.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private FieldDisplayNode BuildItemNode(FieldExtractor extractor, string titlePrefix)
    {
        string itemName = extractor.GetStringAt(_Item_Name_Slot);
        DebugLog.Write(LogChannel.Opcodes, "BuildItemNode: building node '" + titlePrefix + itemName + "'", LogLevel.Trace);

        FieldDisplayNode itemNode = new FieldDisplayNode(titlePrefix + itemName);
        itemNode.AddByteRange(extractor.GetByteRangeFor(_Item_Name_Slot));

        string itemLore = extractor.GetStringAt(_Item_Lore_Slot);
        FieldDisplayNode itemLoreNode = new FieldDisplayNode("Lore: " + itemLore);
        itemLoreNode.AddByteRange(extractor.GetByteRangeFor(_Item_Lore_Slot));
        itemNode.AddChild(itemLoreNode);

        return itemNode;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHandled
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode OpcodeHandled
    {
        get { return _opcodeHandled; }
    }
}
