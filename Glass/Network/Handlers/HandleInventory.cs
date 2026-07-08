using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;

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
    private readonly FieldExtractor _extractor;
               

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
    private readonly SlotId _Remaining_Charges_Slot;
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
    private readonly SlotId _ITFile_Slot;
    private readonly SlotId _DF_4_Slot;
    private readonly SlotId _Item_ID_Slot;
    private readonly SlotId _Weight_Slot;
    private readonly SlotId _DF_7_Slot;
    private readonly SlotId _DF_8_Slot;
    private readonly SlotId _DF_9_Slot;
    private readonly SlotId _Field_E8_Slot;
    private readonly SlotId _DF_10_Slot;
    private readonly SlotId _DF_11_Slot;
    private readonly SlotId _Icon_ID_Slot;
    private readonly SlotId _DF_13_Slot;
    private readonly SlotId _Field_EA_Slot;
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
    private readonly SlotId _Field_130_Slot;
    private readonly SlotId _Field_150_Slot;
    private readonly SlotId _Food_Drink_Value_Slot;
    private readonly SlotId _Required_Level_Slot;
    private readonly SlotId _Recommended_Level_Slot;
    private readonly SlotId _Field_138_Slot;
    private readonly SlotId _Field_13C_Slot;
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
    private readonly SlotId _Field_1A0_Slot;
    private readonly SlotId _Field_1A4_Slot;
    private readonly SlotId _Field_21C_Slot;
    private readonly SlotId _Field_52C_Slot;
    private readonly SlotId _Field_530_Slot;
    private readonly SlotId _Field_534_Slot;
    private readonly SlotId _String_1FC_Slot;
    private readonly SlotId _Field_1D8_Slot;
    private readonly SlotId _Field_1DC_Slot;
    private readonly SlotId _Field_1E0_Slot;
    private readonly SlotId _Field_1A8_Slot;
    private readonly SlotId _Field_1AC_Slot;
    private readonly SlotId _Field_1AD_Slot;
    private readonly SlotId _Field_1AE_Slot;
    private readonly SlotId _Field_1b2_Slot;
    private readonly SlotId _Field_1b3_Slot;
    private readonly SlotId _Field_1b4_Slot;
    private readonly SlotId _Field_1b8_Slot;
    private readonly SlotId _Field_1b9_Slot;
    private readonly SlotId _Field_1bA_Slot;
    private readonly SlotId _Field_1bE_Slot;
    private readonly SlotId _Field_1bF_Slot;
    private readonly SlotId _Field_1C0_Slot;
    private readonly SlotId _Field_1C4_Slot;
    private readonly SlotId _Field_1C5_Slot;
    private readonly SlotId _Field_1C6_Slot;
    private readonly SlotId _Field_1CA_Slot;
    private readonly SlotId _Field_1Cb_Slot;
    private readonly SlotId _Field_1F0_Slot;
    private readonly SlotId _Field_1E8_Slot;
    private readonly SlotId _Field_1EC_Slot;
    private readonly SlotId _Field_1F4_Slot;
    private readonly SlotId _Field_1F8_Slot;
    private readonly SlotId _Field_53C_Slot;
    private readonly SlotId _Field_53D_Slot;
    private readonly SlotId _Field_53E_Slot;
    private readonly SlotId _Field_53F_Slot;
    private readonly SlotId _Field_540_Slot;
    private readonly SlotId _Field_541_Slot;
    private readonly SlotId _String_542_Slot;
    private readonly SlotId _Field_EC_Slot;
    private readonly SlotId _Field_F4_Slot;
    private readonly SlotId _Field_560_Slot;
    private readonly SlotId _Field_568_Slot;
    private readonly SlotId _Field_570_Slot;
    private readonly SlotId _Field_580_Slot;
    private readonly SlotId _Field_564_Slot;
    private readonly SlotId _Field_1E4_Slot;
    private readonly SlotId _Field_584_Slot;
    private readonly SlotId _Field_588_Slot;
    private readonly SlotId _Field_58C_Slot;
    private readonly SlotId _Field_58D_Slot;
    private readonly SlotId _Field_590_Slot;
    private readonly SlotId _Field_594_Slot;
    private readonly SlotId _Field_5A9_Slot;
    private readonly SlotId _Blob_4DC_Slot;
    private readonly SlotId _Field_220_Slot;
    private readonly SlotId _Field_224_Slot;
    private readonly SlotId _Field_225_Slot;
    private readonly SlotId _Field_228_Slot;
    private readonly SlotId _Field_22C_Slot;
    private readonly SlotId _Field_230_Slot;
    private readonly SlotId _Field_234_Slot;
    private readonly SlotId _Field_238_Slot;
    private readonly SlotId _Field_23C_Slot;
    private readonly SlotId _String_240_Slot;
    private readonly SlotId _Field_280_Slot;
    private readonly SlotId _Field_284_Slot;
    private readonly SlotId _Field_288_Slot;
    private readonly SlotId _Field_289_Slot;
    private readonly SlotId _Field_28C_Slot;
    private readonly SlotId _Field_290_Slot;
    private readonly SlotId _Field_294_Slot;
    private readonly SlotId _Field_298_Slot;
    private readonly SlotId _Field_29C_Slot;
    private readonly SlotId _Field_2A0_Slot;
    private readonly SlotId _String_2A4_Slot;
    private readonly SlotId _Field_2E4_Slot;
    private readonly SlotId _Field_2E8_Slot;
    private readonly SlotId _Field_2EC_Slot;
    private readonly SlotId _Field_2ED_Slot;
    private readonly SlotId _Field_2F0_Slot;

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
        _extractor = GlassContext.FieldExtractor;

        // handles of collections that we expect
        _topLevelCollectionHandle = _registry.GetCollectionHandle(_patchLevel, "Inventory Top_Level");
        _ItemCount_Slot = _registry.IndexOfField(_topLevelCollectionHandle, "ChildCount");

        CollectionHandle itemCollection = _registry.GetCollectionHandle(_patchLevel, "Inventory Item");
        _Item_Name_Slot = _registry.IndexOfField(itemCollection, "ItemName");
        _Item_Lore_Slot = _registry.IndexOfField(itemCollection, "ItemLore");

        _Item_String_Slot = _registry.IndexOfField(itemCollection, "ItemString");

        _Field_2_Slot = _registry.IndexOfField(itemCollection, "Field2");
        _Field_3_Slot = _registry.IndexOfField(itemCollection, "Field3");
        _Field_4_Slot = _registry.IndexOfField(itemCollection, "Field4");
        _Field_5_Slot = _registry.IndexOfField(itemCollection, "Field5");
        _Field_6_Slot = _registry.IndexOfField(itemCollection, "Field6");
        _Field_7_Slot = _registry.IndexOfField(itemCollection, "Field7");
        _Field_8_Slot = _registry.IndexOfField(itemCollection, "Field8");
        _Field_9_Slot = _registry.IndexOfField(itemCollection, "Field9");
        _Field_10_Slot = _registry.IndexOfField(itemCollection, "Field10");
        _Field_11_Slot = _registry.IndexOfField(itemCollection, "Field11");
        _Remaining_Charges_Slot = _registry.IndexOfField(itemCollection, "Remaining_Charges");
        _Field_13_Slot = _registry.IndexOfField(itemCollection, "Field13");
        _Field_14_Slot = _registry.IndexOfField(itemCollection, "Field14");
        _Field_15_Slot = _registry.IndexOfField(itemCollection, "Field15");
        _Field_16_Slot = _registry.IndexOfField(itemCollection, "Field16");
        _Field_17_Slot = _registry.IndexOfField(itemCollection, "Field17");
        _Field_18_Slot = _registry.IndexOfField(itemCollection, "Field18");
        _Presence_Slot = _registry.IndexOfField(itemCollection, "Presence");
        _Field_19_Slot = _registry.IndexOfField(itemCollection, "Field19");
        _Field_20_Slot = _registry.IndexOfField(itemCollection, "Field20");
        _Field_21_Slot = _registry.IndexOfField(itemCollection, "Field21");
        _Field_22_Slot = _registry.IndexOfField(itemCollection, "Field22");
        _Field_23_Slot = _registry.IndexOfField(itemCollection, "Field23");
        _Field_24_Slot = _registry.IndexOfField(itemCollection, "Field24");
        _Field_25_Slot = _registry.IndexOfField(itemCollection, "Field25");
        _Field_26_Slot = _registry.IndexOfField(itemCollection, "Field26");
        _Field_27_Slot = _registry.IndexOfField(itemCollection, "Field27");
        _Item_Type_Slot = _registry.IndexOfField(itemCollection, "Item_Type");
        _Item_Name_Slot = _registry.IndexOfField(itemCollection, "ItemName");
        _Item_Lore_Slot = _registry.IndexOfField(itemCollection, "ItemLore");
        _ITFile_Slot = _registry.IndexOfField(itemCollection, "DF_3");
        _DF_4_Slot = _registry.IndexOfField(itemCollection, "DF_4");
        _Weight_Slot = _registry.IndexOfField(itemCollection, "Weight");
        _Item_ID_Slot = _registry.IndexOfField(itemCollection, "Item_ID");
        _DF_8_Slot = _registry.IndexOfField(itemCollection, "DF_8");
        _DF_9_Slot = _registry.IndexOfField(itemCollection, "DF_9");
        _Field_E8_Slot = _registry.IndexOfField(itemCollection, "Field_E8");
        _DF_10_Slot = _registry.IndexOfField(itemCollection, "DF_10");
        _DF_11_Slot = _registry.IndexOfField(itemCollection, "DF_11");
        _Icon_ID_Slot = _registry.IndexOfField(itemCollection, "Icon_ID");
        _DF_13_Slot = _registry.IndexOfField(itemCollection, "DF_13");
        _Field_EA_Slot = _registry.IndexOfField(itemCollection, "Field_EA");
        _Save_Cold_Slot = _registry.IndexOfField(itemCollection, "Save_Cold");
        _Save_Disease_Slot = _registry.IndexOfField(itemCollection, "Save_Disease");
        _Save_Poison_Slot = _registry.IndexOfField(itemCollection, "Save_Poison");
        _Save_Magic_Slot = _registry.IndexOfField(itemCollection, "Save_Magic");
        _Save_Fire_Slot = _registry.IndexOfField(itemCollection, "Save_Fire");
        _Field_Fb_Slot = _registry.IndexOfField(itemCollection, "Field_Fb");
        _Plus_Strength_Slot = _registry.IndexOfField(itemCollection, "Plus_Strength");
        _Plus_Stamina_Slot = _registry.IndexOfField(itemCollection, "Plus_Stamina");
        _Plus_Agility_Slot = _registry.IndexOfField(itemCollection, "Plus_Agility");
        _Plus_Dexterity_Slot = _registry.IndexOfField(itemCollection, "Plus_Dexterity");
        _Plus_Charisma_Slot = _registry.IndexOfField(itemCollection, "Plus_Charisma");
        _Plus_Intelligence_Slot = _registry.IndexOfField(itemCollection, "Plus_Intelligence");
        _Plus_Wisdom_Slot = _registry.IndexOfField(itemCollection, "Plus_Wisdom");
        _Plus_HP_Slot = _registry.IndexOfField(itemCollection, "Plus_HP");
        _Plus_Mana_Slot = _registry.IndexOfField(itemCollection, "Plus_Mana");
        _Plus_Endurance_Slot = _registry.IndexOfField(itemCollection, "Plus_End");
        _Plus_AC_Slot = _registry.IndexOfField(itemCollection, "Plus_AC");
        _HP_Regen_Slot = _registry.IndexOfField(itemCollection, "HP_Regen");
        _Mana_Regen_Slot = _registry.IndexOfField(itemCollection, "Mana_Regen");
        _Field_57C_Slot = _registry.IndexOfField(itemCollection, "Field_57C");
        _Field_140_Slot = _registry.IndexOfField(itemCollection, "Field_140");
        _Field_144_Slot = _registry.IndexOfField(itemCollection, "Field_144");
        _Field_148_Slot = _registry.IndexOfField(itemCollection, "Field_148");
        _Field_11C_Slot = _registry.IndexOfField(itemCollection, "Field_11C");
        _Field_120_Slot = _registry.IndexOfField(itemCollection, "Field_120");
        _Field_118_Slot = _registry.IndexOfField(itemCollection, "Field_118");
        _Field_124_Slot = _registry.IndexOfField(itemCollection, "Field_124");
        _Field_128_Slot = _registry.IndexOfField(itemCollection, "Field_128");
        _Field_12C_Slot = _registry.IndexOfField(itemCollection, "Field_12C");
        _Field_134_Slot = _registry.IndexOfField(itemCollection, "Field_134");
        _Field_130_Slot = _registry.IndexOfField(itemCollection, "Field_130");
        _Field_150_Slot = _registry.IndexOfField(itemCollection, "Field_150");
        _Food_Drink_Value_Slot = _registry.IndexOfField(itemCollection, "FoodDrink_Value");
        _Required_Level_Slot = _registry.IndexOfField(itemCollection, "Required_Level");
        _Recommended_Level_Slot = _registry.IndexOfField(itemCollection, "Recommended_Level");
        _Field_138_Slot = _registry.IndexOfField(itemCollection, "Field_138");
        _Field_13C_Slot = _registry.IndexOfField(itemCollection, "Field_13C");
        _Field_151_Slot = _registry.IndexOfField(itemCollection, "Field_151");
        _Weapon_Delay_Slot = _registry.IndexOfField(itemCollection, "Weapon_Delay");
        _Field_153_Slot = _registry.IndexOfField(itemCollection, "Field_153");
        _Field_154_Slot = _registry.IndexOfField(itemCollection, "Field_154");
        _Field_155_Slot = _registry.IndexOfField(itemCollection, "Field_155");
        _Field_158_Slot = _registry.IndexOfField(itemCollection, "Field_158");
        _Field_14C_Slot = _registry.IndexOfField(itemCollection, "Field_14C");
        _Field_18C_Slot = _registry.IndexOfField(itemCollection, "Field_18C");
        _Field_190_Slot = _registry.IndexOfField(itemCollection, "Field_190");
        _Field_194_Slot = _registry.IndexOfField(itemCollection, "Field_194");
        _Field_19C_Slot = _registry.IndexOfField(itemCollection, "Field_19C");
        _Field_198_Slot = _registry.IndexOfField(itemCollection, "Field_198");
        _Field_1A0_Slot = _registry.IndexOfField(itemCollection, "Field_1A0");
        _Field_1A4_Slot = _registry.IndexOfField(itemCollection, "Field_1A4");
        _Field_21C_Slot = _registry.IndexOfField(itemCollection, "Field_21C");
        _Field_52C_Slot = _registry.IndexOfField(itemCollection, "Field_52C");
        _Field_530_Slot = _registry.IndexOfField(itemCollection, "Field_530");
        _Field_534_Slot = _registry.IndexOfField(itemCollection, "Field_534");
        _String_1FC_Slot = _registry.IndexOfField(itemCollection, "String_1FC");
        _Field_1D8_Slot = _registry.IndexOfField(itemCollection, "Field_1D8");
        _Field_1DC_Slot = _registry.IndexOfField(itemCollection, "Field_1DC");
        _Field_1E0_Slot = _registry.IndexOfField(itemCollection, "Field_1E0");

        _Field_1A8_Slot = _registry.IndexOfField(itemCollection, "Field_1A8");  // managed 1/6
        _Field_1AC_Slot = _registry.IndexOfField(itemCollection, "Field_1AC");
        _Field_1AD_Slot = _registry.IndexOfField(itemCollection, "Field_1AD");
        _Field_1AE_Slot = _registry.IndexOfField(itemCollection, "Field_1AE");  // 2/6
        _Field_1b2_Slot = _registry.IndexOfField(itemCollection, "Field_1b2");
        _Field_1b3_Slot = _registry.IndexOfField(itemCollection, "Field_1b3");
        _Field_1b4_Slot = _registry.IndexOfField(itemCollection, "Field_1b4");  // 3/6
        _Field_1b8_Slot = _registry.IndexOfField(itemCollection, "Field_1b8");
        _Field_1b9_Slot = _registry.IndexOfField(itemCollection, "Field_1b9");
        _Field_1bA_Slot = _registry.IndexOfField(itemCollection, "Field_1bA");  // 4/6
        _Field_1bE_Slot = _registry.IndexOfField(itemCollection, "Field_1bE");
        _Field_1bF_Slot = _registry.IndexOfField(itemCollection, "Field_1bF");
        _Field_1C0_Slot = _registry.IndexOfField(itemCollection, "Field_1C0");  // 5/6
        _Field_1C4_Slot = _registry.IndexOfField(itemCollection, "Field_1C4");
        _Field_1C5_Slot = _registry.IndexOfField(itemCollection, "Field_1C5");
        _Field_1C6_Slot = _registry.IndexOfField(itemCollection, "Field_1C6");  // 6/6
        _Field_1CA_Slot = _registry.IndexOfField(itemCollection, "Field_1CA");
        _Field_1Cb_Slot = _registry.IndexOfField(itemCollection, "Field_1Cb");  // end of managed

        _Field_1F0_Slot = _registry.IndexOfField(itemCollection, "Field_1F0");
        _Field_1E8_Slot = _registry.IndexOfField(itemCollection, "Field_1E8");
        _Field_1EC_Slot = _registry.IndexOfField(itemCollection, "Field_1EC");
        _Field_1F4_Slot = _registry.IndexOfField(itemCollection, "Field_1F4");
        _Field_1F8_Slot = _registry.IndexOfField(itemCollection, "Field_1F8");
        _Field_53C_Slot = _registry.IndexOfField(itemCollection, "Field_53C");
        _Field_53D_Slot = _registry.IndexOfField(itemCollection, "Field_53D");
        _Field_53E_Slot = _registry.IndexOfField(itemCollection, "Field_53E");
        _Field_53F_Slot = _registry.IndexOfField(itemCollection, "Field_53F");
        _Field_540_Slot = _registry.IndexOfField(itemCollection, "Field_540");
        _Field_541_Slot = _registry.IndexOfField(itemCollection, "Field_541");
        _String_542_Slot = _registry.IndexOfField(itemCollection, "String_542");
        _Field_EC_Slot = _registry.IndexOfField(itemCollection, "Field_EC");
        _Field_F4_Slot = _registry.IndexOfField(itemCollection, "Field_F4");
        _Field_560_Slot = _registry.IndexOfField(itemCollection, "Field_560");
        _Field_568_Slot = _registry.IndexOfField(itemCollection, "Field_568");
        _Field_570_Slot = _registry.IndexOfField(itemCollection, "Field_570");
        _Field_580_Slot = _registry.IndexOfField(itemCollection, "Field_580");
        _Field_564_Slot = _registry.IndexOfField(itemCollection, "Field_564");
        _Field_1E4_Slot = _registry.IndexOfField(itemCollection, "Field_1E4");

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
    // OpcodeHandled
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode OpcodeHandled
    {
        get { return _opcodeHandled; }
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

        uint bagCount = 0;

        DebugLog.Write(LogChannel.Opcodes, "Starting Inventory Extract", LogLevel.Info);
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            bagCount = _extractor.BagCount(rootGate);

            DebugLog.Write(LogChannel.Opcodes, "Inventory top level sees " + bagCount + " bags", LogLevel.Info);

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                _extractor.EnterGate(rootGate, bagIndex);
            }

        }
        finally
        {
            _extractor.Release();
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
        FieldDisplayNode root = new FieldDisplayNode();
        string characterName = GlassContext.SessionRegistry.CharacterNameFromMetadata(metadata);
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            SlotId itemListSlot = GlassContext.PatchRegistry.IndexOfField(_extractor.CollectionOf(), "Item List");
            GateHandle itemListGate = _extractor.GetGateAt(itemListSlot);
            if (itemListGate.Exists == true)
            {
                uint itemCount = _extractor.BagCount(itemListGate);
                DebugLog.Write(LogChannel.Opcodes, "Describe: Item List gate has " + itemCount + " items", LogLevel.Info);
                for (uint i = 0; i < itemCount; i++)
                {
                    FieldDisplayNode itemNode = DescribeItem(itemListGate, i, "Item " + (i+1) + ": ");
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
            _extractor.Release();
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
    // child items can be labelled differently.  Item fields are extracted by a helper.
    //
    // itemGate:   The gate whose instance holds this item.
    // itemIndex:  The instance index of this item within itemGate.
    // title:      The node title the caller has composed for this item.
    //
    // Returns:  The item's display node, with any child items nested underneath.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private FieldDisplayNode DescribeItem(GateHandle itemGate, uint itemIndex, string title)
    {
        _extractor.EnterGate(itemGate, itemIndex);
        FieldDisplayNode itemNode = BuildItemNode(title);

        SlotId childItemsSlot = GlassContext.PatchRegistry.IndexOfField(_extractor.CollectionOf(), "ChildItems");
        if (_extractor.IsPresent(childItemsSlot) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "DescribeItem: '" + title + 
                     "' has no ChildItems", LogLevel.Trace);
            return itemNode;
        }

        GateHandle childItemsGate = _extractor.GetGateAt(childItemsSlot);
        if (childItemsGate.Exists == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "DescribeItem: '" + title + 
                     "' ChildItems slot present but no gate", LogLevel.Warn);
            return itemNode;
        }

        uint childCount = _extractor.BagCount(childItemsGate);
        DebugLog.Write(LogChannel.Opcodes, "DescribeItem: '" + title + 
                     "' is a container with " + childCount + " child slot(s)", LogLevel.Info);

        for (uint c = 0; c < childCount; c++)
        {
            _extractor.EnterGate(childItemsGate, c);

            SlotId childIndexSlot = GlassContext.PatchRegistry.IndexOfField(_extractor.CollectionOf(), "Child Index");
            uint childIndex = _extractor.GetUIntAt(childIndexSlot);

            SlotId childItemSlot = GlassContext.PatchRegistry.IndexOfField(_extractor.CollectionOf(), "Child Item");
            if (_extractor.IsPresent(childItemSlot) == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "DescribeItem: child slot " + childIndex + " has no item", LogLevel.Trace);
                    _extractor.EnterGate(childItemsGate, c);
                continue;
            }

            GateHandle singleItemGate = _extractor.GetGateAt(childItemSlot);
            if (singleItemGate.Exists == true)
            {
                FieldDisplayNode childNode = DescribeItem(singleItemGate, 0u, "Slot " + (childIndex+1) + ": ");
                itemNode.AddChild(childNode);
            }
        }

        return itemNode;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BuildItemNode
    //
    // Builds a display node for the item in the extractor's active bag: a titled node with
    // the item's Lore field nested underneath.  The caller supplies a title prefix; this
    // method appends the item's Name to it.
    //
    // titlePrefix:  The title prefix the caller has composed (e.g. "Item 3: " or "Slot 5: ").
    //
    // Returns:  The item's display node, with its Lore child attached.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private FieldDisplayNode BuildItemNode(string titlePrefix)
    {
        string itemName = _extractor.GetStringAt(_Item_Name_Slot);
        DebugLog.Write(LogChannel.Opcodes, "BuildItemNode: building node '" + titlePrefix + 
                    itemName + "'", LogLevel.Trace);

        FieldDisplayNode itemNode = new FieldDisplayNode(titlePrefix + itemName);
        itemNode.AddByteRange(_extractor.GetByteRangeFor(_Item_Name_Slot));

        AddStringNode(_Item_Name_Slot, "Name", itemNode);
        AddStringNode(_Item_Lore_Slot, "Lore", itemNode);
        AddStringNode(_Item_String_Slot, "ID String", itemNode);
        AddUIntNode(_Item_ID_Slot, "ID", itemNode, "D");
        AddUIntNode(_Item_Type_Slot, "Item Type", itemNode);
        AddUIntNode(_ITFile_Slot, "IT File#", itemNode, "D");
        AddUIntNode(_Icon_ID_Slot, "Icon ID", itemNode);
        AddUIntNode(_Required_Level_Slot, "Required Level", itemNode, "D");
        AddUIntNode(_Recommended_Level_Slot, "Recommended Level", itemNode, "D");
        AddFloatNode(_Weight_Slot, "Weight", itemNode);
        AddUIntNode(_Remaining_Charges_Slot, "Remaining Charges", itemNode, "D");
        AddUIntNode(_Food_Drink_Value_Slot, "Food/Drink Value", itemNode, "D");

        FieldDisplayNode saveSubtree = new FieldDisplayNode("Saves");
        itemNode.AddChild(saveSubtree);
        AddIntNode(_Save_Cold_Slot, "Save vs Cold", saveSubtree, "D");
        AddIntNode(_Save_Disease_Slot, "Save vs Disease", saveSubtree, "D");
        AddIntNode(_Save_Poison_Slot, "Save vs Poison", saveSubtree, "D");
        AddIntNode(_Save_Magic_Slot, "Save vs Magic", saveSubtree, "D");
        AddIntNode(_Save_Fire_Slot, "Save vs Fire", saveSubtree, "D");

        FieldDisplayNode statModSubtree = new FieldDisplayNode("Stat Modifiers");
        itemNode.AddChild(statModSubtree);
        AddIntNode(_Plus_Strength_Slot, "Plus Strength", statModSubtree, "D");
        AddIntNode(_Plus_Stamina_Slot, "Plus Stamina", statModSubtree, "D");
        AddIntNode(_Plus_Agility_Slot, "Plus Agility", statModSubtree, "D");
        AddIntNode(_Plus_Dexterity_Slot, "Plus Dexterity", statModSubtree, "D");
        AddIntNode(_Plus_Charisma_Slot, "Plus Charisma", statModSubtree, "D");
        AddIntNode(_Plus_Intelligence_Slot, "Plus Intelligence", statModSubtree, "D");
        AddIntNode(_Plus_Wisdom_Slot, "Plus Wisdom", statModSubtree, "D");
        AddIntNode(_Plus_HP_Slot, "Plus HP", statModSubtree, "D");
        AddIntNode(_Plus_Mana_Slot, "Plus Mana", statModSubtree, "D");
        AddIntNode(_Plus_AC_Slot, "Plus AC", statModSubtree, "D");
        AddIntNode(_Plus_Endurance_Slot, "Plus Endurance", statModSubtree, "D");

        AddIntNode(_HP_Regen_Slot, "HP Regen", itemNode, "D");
        AddIntNode(_Mana_Regen_Slot, "Mana Regen", itemNode, "D");
        AddUIntNode(_Weapon_Delay_Slot, "Weapon Delay", itemNode, "D");

        AddUIntNode(_Field_2_Slot, "Field 2", itemNode, "?");
        AddUIntNode(_Field_3_Slot, "Field 3", itemNode, "?");
        AddUIntNode(_Field_4_Slot, "Inventory Slot", itemNode, "D");
        AddUIntNode(_Field_5_Slot, "Field 5", itemNode, "?");
        AddUIntNode(_Field_6_Slot, "Field 6", itemNode, "?");
        AddUIntNode(_Field_7_Slot, "Field 7", itemNode, "?");
        AddUIntNode(_Field_8_Slot, "Field 8", itemNode, "?");
        AddUIntNode(_Field_9_Slot, "Field 9", itemNode, "?");
        AddUIntNode(_Field_10_Slot, "Field 10", itemNode, "?");
        AddUIntNode(_Field_11_Slot, "Field 11", itemNode, "?");
        AddUIntNode(_Field_13_Slot, "Field 13", itemNode, "?");
        AddUIntNode(_Field_14_Slot, "Field 14", itemNode, "?");
        AddUIntNode(_Field_15_Slot, "Field 15", itemNode, "?");
        AddUIntNode(_Field_16_Slot, "Field 16", itemNode, "?");
        AddUIntNode(_Field_17_Slot, "Field 17", itemNode, "?");
        AddUIntNode(_Field_19_Slot, "Field 19", itemNode, "?");
        AddUIntNode(_Field_20_Slot, "Field 20", itemNode, "?");
        AddUIntNode(_Field_21_Slot, "Field 21", itemNode, "?");
        AddUIntNode(_Field_22_Slot, "Field 22", itemNode, "?");
        AddUIntNode(_Field_23_Slot, "Field 23", itemNode, "?");
        AddUIntNode(_Field_24_Slot, "Field 24", itemNode, "?");
        AddUIntNode(_Field_25_Slot, "Field 25", itemNode, "?");
        AddUIntNode(_Field_26_Slot, "Field 26", itemNode, "?");
        AddUIntNode(_Field_27_Slot, "Field 27", itemNode, "?");

        AddUIntNode(_DF_4_Slot, "DF 4", itemNode, "?");
        AddUIntNode(_DF_7_Slot, "DF 7", itemNode, "?");
        AddUIntNode(_DF_8_Slot, "DF 8", itemNode, "?");
        AddUIntNode(_DF_9_Slot, "DF 9", itemNode, "?");
        AddUIntNode(_Field_E8_Slot, "Field E8", itemNode, "?");
        AddWornSlotNode(_DF_10_Slot, "Slots", itemNode);
        AddUIntNode(_DF_11_Slot, "DF 11", itemNode, "?");
        AddUIntNode(_DF_13_Slot, "DF 13", itemNode, "?");
        AddUIntNode(_Field_EA_Slot, "Field EA", itemNode, "?");
        AddUIntNode(_Field_Fb_Slot, "Field Fb", itemNode, "?");
        AddUIntNode(_Field_57C_Slot, "Field 57C", itemNode, "?");
        AddUIntNode(_Field_140_Slot, "Field 140", itemNode, "?");
        AddUIntNode(_Field_144_Slot, "Field 144", itemNode, "?");
        AddUIntNode(_Field_148_Slot, "Field 148", itemNode, "?");
        AddUIntNode(_Field_11C_Slot, "Field 11C", itemNode, "?");
        AddUIntNode(_Field_120_Slot, "Field 120", itemNode, "?");
        AddUIntNode(_Field_118_Slot, "Field 118", itemNode, "?");
        AddUIntNode(_Field_124_Slot, "Field 124", itemNode, "?");
        AddUIntNode(_Field_128_Slot, "Field 128", itemNode, "?");
        AddUIntNode(_Field_12C_Slot, "Field 12C", itemNode, "?");
        AddUIntNode(_Field_134_Slot, "Field 134", itemNode, "?");
        AddUIntNode(_Field_130_Slot, "Field 130", itemNode, "?");
        AddUIntNode(_Field_150_Slot, "Field 150", itemNode, "?");
        AddUIntNode(_Field_138_Slot, "Field 138", itemNode, "?");
        AddUIntNode(_Field_13C_Slot, "Field 13C", itemNode, "?");
        AddUIntNode(_Field_151_Slot, "Field 151", itemNode, "?");
        AddUIntNode(_Field_153_Slot, "Field 153", itemNode, "?");
        AddUIntNode(_Field_154_Slot, "Field 154", itemNode, "?");
        AddUIntNode(_Field_155_Slot, "Field 155", itemNode, "?");
        AddUIntNode(_Field_158_Slot, "Field 158", itemNode, "?");
        AddUIntNode(_Field_14C_Slot, "Field 14C", itemNode, "?");
        AddUIntNode(_Field_18C_Slot, "Field 18C", itemNode, "?");
        AddUIntNode(_Field_190_Slot, "Field 190", itemNode, "?");
        AddUIntNode(_Field_194_Slot, "Field 194", itemNode, "?");
        AddUIntNode(_Field_198_Slot, "Field 198", itemNode, "?");
        AddUIntNode(_Field_1A0_Slot, "Field 1A0", itemNode, "?");
        AddUIntNode(_Field_1A4_Slot, "Field 1A4", itemNode, "?");
        AddUIntNode(_Field_21C_Slot, "Field 21C", itemNode, "?");
        AddUIntNode(_Field_52C_Slot, "Field 52C", itemNode, "?");
        AddUIntNode(_Field_530_Slot, "Field 530", itemNode, "?");
        AddUIntNode(_Field_534_Slot, "Field 534", itemNode, "?");
        AddStringNode(_String_1FC_Slot, "String 1FC", itemNode);
        AddUIntNode(_Field_1D8_Slot, "Field 1D8", itemNode, "?");
        AddUIntNode(_Field_1DC_Slot, "Field 1DC", itemNode, "?");
        AddUIntNode(_Field_1E0_Slot, "Field 1E0", itemNode, "?");
        // managed fields skipped
        AddUIntNode(_Field_1F0_Slot, "Field 1F0", itemNode, "?");
        AddUIntNode(_Field_1E8_Slot, "Field 1E8", itemNode, "?");
        AddUIntNode(_Field_1EC_Slot, "Field 1EC", itemNode, "?");
        AddUIntNode(_Field_1F4_Slot, "Field 1F4", itemNode, "?");
        AddUIntNode(_Field_1F8_Slot, "Field 1F8", itemNode, "?");
        AddUIntNode(_Field_53C_Slot, "Field 53C", itemNode, "?");


        return itemNode;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddStringNode
    //
    // Builds a display node for a string field.
    //
    // slotId:  The slot to extract
    // label:   The label to use in the new display node
    // parent   The display node's parent
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AddStringNode(SlotId slotId, string label, FieldDisplayNode parent)
    {
        string value = _extractor.GetStringAt(slotId);
        FieldDisplayNode newNode = new FieldDisplayNode(label + ": " + value);
        newNode.AddByteRange(_extractor.GetByteRangeFor(slotId));
        parent.AddChild(newNode);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddUintNode
    //
    // Builds a display node for a uint field.
    //
    // slotId:  The slot to extract
    // label:   The label to use in the new display node
    // parent   The display node's parent
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AddUIntNode(SlotId slotId, string label, FieldDisplayNode parent, string Format = "X")
    {
        uint value = _extractor.GetUIntAt(slotId);
        string valueString;
        if (Format == "?")
        {
            valueString = "0x" + value.ToString("X") + " (" + value.ToString("D") + ")";
        }
        else if (Format[0] == 'X')
        {
            valueString = "0x" + value.ToString(Format);
        }
        else
        {
            valueString = value.ToString(Format);
        }

        FieldDisplayNode newNode = new FieldDisplayNode(label + ": " + valueString);
        newNode.AddByteRange(_extractor.GetByteRangeFor(slotId));
        parent.AddChild(newNode);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddIntNode
    //
    // Builds a display node for an int field.
    //
    // slotId:  The slot to extract
    // label:   The label to use in the new display node
    // parent   The display node's parent
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AddIntNode(SlotId slotId, string label, FieldDisplayNode parent, string Format = "X")
    {
        int value = _extractor.GetIntAt(slotId);
        string valueString;
        if (Format == "?")
        {
            valueString = "0x" + value.ToString("X") + " (" + value.ToString("D") + ")";
        }
        else if (Format[0] == 'X')
        {
            valueString = "0x" + value.ToString(Format);
        }
        else
        {
            valueString = value.ToString(Format);
        }

        FieldDisplayNode newNode = new FieldDisplayNode(label + ": " + valueString);
        newNode.AddByteRange(_extractor.GetByteRangeFor(slotId));
        parent.AddChild(newNode);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddFloatNode
    //
    // Builds a display node for a float field.
    //
    // slotId:  The slot to extract
    // label:   The label to use in the new display node
    // parent   The display node's parent
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AddFloatNode(SlotId slotId, string label, FieldDisplayNode parent, string Format = "F")
    {
        float value = _extractor.GetFloatAt(slotId);
        FieldDisplayNode newNode = new FieldDisplayNode(label + ": " + value.ToString(Format));
        newNode.AddByteRange(_extractor.GetByteRangeFor(slotId));
        parent.AddChild(newNode);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddWornSlot
    //
    // Builds a display node for a mask of worn slots.
    //
    // slotId:  The slot to extract
    // label:   The label to use in the new display node
    // parent   The display node's parent
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AddWornSlotNode(SlotId slotId, string label, FieldDisplayNode parent)
    {
        uint value = _extractor.GetUIntAt(slotId);
        string slots = DecodeSlotMask(value);
        FieldDisplayNode newNode = new FieldDisplayNode(label + ": " + slots);
        newNode.AddByteRange(_extractor.GetByteRangeFor(slotId));
        parent.AddChild(newNode);
    }
    const uint SLOT_L_Ear = 1 << 1;
    const uint SLOT_Head = 1 << 2;
    const uint SLOT_Face = 1 << 3;
    const uint SLOT_R_Ear = 1 << 4;
    const uint SLOT_Neck = 1 << 5;
    const uint SLOT_Shoulder = 1 << 6;
    const uint SLOT_Arms = 1 << 7;
    const uint SLOT_Back = 1 << 8;
    const uint SLOT_L_Wrist = 1 << 9;
    const uint SLOT_R_Wrist = 1 << 10;
    const uint SLOT_Range = 1 << 11;
    const uint SLOT_Hands = 1 << 12;
    const uint SLOT_Primary = 1 << 13;
    const uint SLOT_Secondary = 1 << 14;
    const uint SLOT_L_Finger = 1 << 15;
    const uint SLOT_R_Finger = 1 << 16;
    const uint SLOT_Chest = 1 << 17;
    const uint SLOT_Legs = 1 << 18;
    const uint SLOT_Feet = 1 << 19;
    const uint SLOT_Waist = 1 << 20;
    const uint SLOT_Ammo = 1 << 21;
    const uint SLOT_Power_Source = 1 << 22;

    private string DecodeSlotMask(uint mask)
    {
        List<string> slots = new List<string>();

        if ((mask & SLOT_L_Ear) != 0)
        {
            slots.Add("L-Ear");
        }
        if ((mask & SLOT_R_Ear) != 0)
        {
            slots.Add("R-Ear");
        }
        if ((mask & SLOT_Head) != 0)
        {
            slots.Add("Head");
        }
        if ((mask & SLOT_Face) != 0)
        {
            slots.Add("Face");
        }
        if ((mask & SLOT_Neck) != 0)
        {
            slots.Add("Neck");
        }
        if ((mask & SLOT_Shoulder) != 0)
        {
            slots.Add("Shoulder");
        }
        if ((mask & SLOT_Arms) != 0)
        {
            slots.Add("Arms");
        }
        if ((mask & SLOT_Back) != 0)
        {
            slots.Add("Back");
        }
        if ((mask & SLOT_L_Wrist) != 0)
        {
            slots.Add("L-Wrist");
        }
        if ((mask & SLOT_R_Wrist) != 0)
        {
            slots.Add("R-Wrist");
        }
        if ((mask & SLOT_Range) != 0)
        {
            slots.Add("Range");
        }
        if ((mask & SLOT_Hands) != 0)
        {
            slots.Add("Hands");
        }
        if ((mask & SLOT_Primary) != 0)
        {
            slots.Add("Primary");
        }
        if ((mask & SLOT_Secondary) != 0)
        {
            slots.Add("Secondary");
        }
        if ((mask & SLOT_L_Finger) != 0)
        {
            slots.Add("L-Finger");
        }
        if ((mask & SLOT_R_Finger) != 0)
        {
            slots.Add("R-Finger");
        }
        if ((mask & SLOT_Chest) != 0)
        {
            slots.Add("Chest");
        }
        if ((mask & SLOT_Legs) != 0)
        {
            slots.Add("Legs");
        }
        if ((mask & SLOT_Feet) != 0)
        {
            slots.Add("Feet");
        }
        if ((mask & SLOT_Waist) != 0)
        {
            slots.Add("Waist");
        }
        if ((mask & SLOT_Ammo) != 0)
        {
            slots.Add("Ammo");
        }
        if ((mask & SLOT_Power_Source) != 0)
        {
            slots.Add("Power Source");
        }
        return string.Join(",", slots);
    }
}
