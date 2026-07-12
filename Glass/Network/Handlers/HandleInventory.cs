using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System.Printing;
using System.Windows;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleInventory
//
// Handles OP_Inventory packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleInventory : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_Inventory";
    private readonly PatchOpcode _opcodeHandled;
    private readonly PatchRegistry _registry;
    private readonly PatchLevel _patchLevel;
    private readonly GateDefinitionHandle _top_level_gate;
    private readonly FieldExtractor _extractor;

    private readonly SlotId _Item_String_Slot;
    private readonly SlotId _Current_Stack_Size_Slot;
    private readonly SlotId _Field_3_Slot;
    private readonly SlotId _Current_Location_Slot;
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
    private readonly SlotId _Field_19_Slot;
    private readonly SlotId _Field_20_Slot;
    private readonly SlotId _Field_21_Slot;
    private readonly SlotId _Field_22_Slot;
    private readonly SlotId _Field_23_Slot;
    private readonly SlotId _Field_24_Slot;
    private readonly SlotId _Field_25_Slot;
    private readonly SlotId _Field_26_Slot;
    private readonly SlotId _Field_27_Slot;
    private readonly SlotId _Item_Type_Slot2;
    private readonly SlotId _Item_Name_Slot;
    private readonly SlotId _Item_Lore_Slot;
    private readonly SlotId _ITFile_Slot;
    private readonly SlotId _DF_4_Slot;
    private readonly SlotId _Item_ID_Slot;
    private readonly SlotId _Weight_Slot;
    private readonly SlotId _DF_7_Slot;
    private readonly SlotId _DF_8_Slot;
    private readonly SlotId _DF_9_Slot;
    private readonly SlotId _Size_Slot;
    private readonly SlotId _Usable_Slot_Mask;
    private readonly SlotId _Cost_Slot;
    private readonly SlotId _Icon_ID_Slot;
    private readonly SlotId _DF_13_Slot;
    private readonly SlotId _Tradeskill_Slot;
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
    private readonly SlotId _Class_Mask_Slot;
    private readonly SlotId _Race_Mask_Slot;
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
    private readonly SlotId _Bard_Value_Slot;
    private readonly SlotId _Field_13C_Slot;
    private readonly SlotId _Field_151_Slot;
    private readonly SlotId _Weapon_Delay_Slot;
    private readonly SlotId _Field_153_Slot;
    private readonly SlotId _Field_154_Slot;
    private readonly SlotId _Field_155_Slot;
    private readonly SlotId _Base_Damage_Slot;
    private readonly SlotId _Color_Slot;
    private readonly SlotId _Field_18C_Slot;
    private readonly SlotId _Item_Type_Slot;
    private readonly SlotId _Material_Slot;
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

    private readonly SlotId _Augment_Field_1;
    private readonly SlotId _Augment_Field_2;
    private readonly SlotId _Augment_Field_3;

    private readonly SlotId _Field_1F0_Slot;
    private readonly SlotId _Field_1E8_Slot;
    private readonly SlotId _Field_1EC_Slot;
    private readonly SlotId _Field_1F4_Slot;
    private readonly SlotId _Field_1F8_Slot;
    private readonly SlotId _Field_53C_Slot;
    private readonly SlotId _Bag_Space_Slot;
    private readonly SlotId _Bag_Size_Slot;
    private readonly SlotId _Weight_Reduction_Slot;
    private readonly SlotId _Field_540_Slot;
    private readonly SlotId _Field_541_Slot;
    private readonly SlotId _String_542_Slot;
    private readonly SlotId _Lore_Group_Slot;
    private readonly SlotId _Field_F4_Slot;
    private readonly SlotId _Field_560_Slot;
    private readonly SlotId _Field_568_Slot;
    private readonly SlotId _Plus_Attack_Slot;
    private readonly SlotId _Field_580_Slot;
    private readonly SlotId _Field_564_Slot;
    private readonly SlotId _Aug_Distiller_Needed;
    private readonly SlotId _Field_584_Slot;
    private readonly SlotId _Field_588_Slot;
    private readonly SlotId _Field_58C_Slot;
    private readonly SlotId _Field_58D_Slot;
    private readonly SlotId _Max_Stack_Size_Slot;
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

    private readonly SlotId _Field_5A0_Slot;
    private readonly SlotId _Field_5A8_Slot;
    private readonly SlotId _Field_598_Slot;
    private readonly SlotId _Field_59C_Slot;
    private readonly SlotId _Backstab_Damage_Slot;  // 140
    private readonly SlotId _Heroic_Strength;
    private readonly SlotId _Field_164_Slot;
    private readonly SlotId _Field_168_Slot;
    private readonly SlotId _Heroic_Agility_Slot;
    private readonly SlotId _Field_170_Slot;
    private readonly SlotId _Field_174_Slot;
    private readonly SlotId _Field_178_Slot;
    private readonly SlotId _Field_17C_Slot;
    private readonly SlotId _Field_180_Slot;
    private readonly SlotId _Field_5AC_Slot; // 150
    private readonly SlotId _Field_5b0_Slot;
    private readonly SlotId _Field_5b4_Slot;
    private readonly SlotId _Field_5A4_Slot;
    private readonly SlotId _Field_D3_Slot;
    private readonly SlotId _Field_5b8_Slot;
    private readonly SlotId _Field_5bC_Slot;
    private readonly SlotId _Field_5C0_Slot;
    private readonly SlotId _Field_5C4_Slot;
    private readonly SlotId _Field_5C8_Slot;
    private readonly SlotId _Field_5CC_Slot;  // 160
    private readonly SlotId _Field_5D0_Slot;
    private readonly SlotId _Field_5D4_Slot;
    private readonly SlotId _String_5D8_Slot;
    private readonly SlotId _Field_614_Slot;
    private readonly SlotId _Field_5F8_Slot;
    private readonly SlotId _Field_5FC_Slot;
    private readonly SlotId _Field_5FD_Slot;
    private readonly SlotId _Field_600_Slot;
    private readonly SlotId _Field_604_Slot;
    private readonly SlotId _Field_608_Slot; // 170
    private readonly SlotId _Field_60C_Slot;
    private readonly SlotId _Field_610_Slot;
    private readonly SlotId _Field_65C_Slot;
    // 4byte fields
    private readonly SlotId _Field_D4_Slot;
    private readonly SlotId _Field_D5_Slot;
    private readonly SlotId _Field_D6_Slot;
    private readonly SlotId _Field_D7_Slot;
    private readonly SlotId _Field_D8_Slot;
    private readonly SlotId _Field_DC_Slot;  // 180
    private readonly SlotId _Field_DD_Slot;
    private readonly SlotId _Field_DE_Slot;
    private readonly SlotId _Field_DF_Slot;
    private readonly SlotId _Field_E0_Slot;
    private readonly SlotId _Field_E4_Slot;
    private readonly SlotId _Field_184_Slot;
    private readonly SlotId _Field_188_Slot;
    private readonly SlotId _Field_F0_Slot;
    private readonly SlotId _Field_F5_Slot;
    private readonly SlotId _Field_618_Slot;  // 190
    private readonly SlotId _String_61C_Slot;
    private readonly SlotId _Child_Count_Slot;
    private readonly SlotId _Field_2C_Slot;
    private readonly SlotId _Field_30_Slot;
    private readonly SlotId _Field_48_Slot;  // 199

    private readonly SlotId _Field_Optional_4_Byte_Slot;        // array of optional values seen


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
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);
        _extractor = GlassContext.FieldExtractor;

        // handles of collections that we expect
        CollectionHandle itemCollection = _registry.GetCollectionHandle(_patchLevel, "Inventory Item");
        _Gate_InventoryOptional24_Slot = _registry.IndexOfField(itemCollection, "Optional24");
        CollectionHandle augmentCollection = _registry.GetCollectionHandle(_patchLevel, "Inventory_AugmentFields");
        CollectionHandle strideCollection = _registry.GetCollectionHandle(_patchLevel, "Inventory_Stride");
        CollectionHandle optional4sCollection = _registry.GetCollectionHandle(_patchLevel, "Inventory_Optional_4s");
        _Item_Name_Slot = _registry.IndexOfField(itemCollection, "ItemName");
        _Item_Lore_Slot = _registry.IndexOfField(itemCollection, "ItemLore");

        _Item_String_Slot = _registry.IndexOfField(itemCollection, "ItemString");

        _Current_Stack_Size_Slot = _registry.IndexOfField(itemCollection, "Field2");
        _Field_3_Slot = _registry.IndexOfField(itemCollection, "Field3");
        _Current_Location_Slot = _registry.IndexOfField(itemCollection, "Field4");
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
        _Item_Type_Slot2 = _registry.IndexOfField(itemCollection, "Item_Type");
        _Item_Name_Slot = _registry.IndexOfField(itemCollection, "ItemName");
        _Item_Lore_Slot = _registry.IndexOfField(itemCollection, "ItemLore");
        _ITFile_Slot = _registry.IndexOfField(itemCollection, "DF_3");
        _DF_4_Slot = _registry.IndexOfField(itemCollection, "DF_4");
        _Weight_Slot = _registry.IndexOfField(itemCollection, "Weight");
        _Item_ID_Slot = _registry.IndexOfField(itemCollection, "Item_ID");
        _DF_7_Slot = _registry.IndexOfField(itemCollection, "DF_7");
        _DF_8_Slot = _registry.IndexOfField(itemCollection, "DF_8");
        _DF_9_Slot = _registry.IndexOfField(itemCollection, "DF_9");
        _Size_Slot = _registry.IndexOfField(itemCollection, "Field_E8");
        _Usable_Slot_Mask = _registry.IndexOfField(itemCollection, "DF_10");
        _Cost_Slot = _registry.IndexOfField(itemCollection, "DF_11");
        _Icon_ID_Slot = _registry.IndexOfField(itemCollection, "Icon_ID");
        _DF_13_Slot = _registry.IndexOfField(itemCollection, "DF_13");
        _Tradeskill_Slot = _registry.IndexOfField(itemCollection, "Field_EA");
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
        _Class_Mask_Slot = _registry.IndexOfField(itemCollection, "Class Mask");
        _Race_Mask_Slot = _registry.IndexOfField(itemCollection, "Race Mask");
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
        _Bard_Value_Slot = _registry.IndexOfField(itemCollection, "Field_138");
        _Field_13C_Slot = _registry.IndexOfField(itemCollection, "Field_13C");
        _Field_151_Slot = _registry.IndexOfField(itemCollection, "Field_151");
        _Weapon_Delay_Slot = _registry.IndexOfField(itemCollection, "Weapon_Delay");
        _Field_153_Slot = _registry.IndexOfField(itemCollection, "Field_153");
        _Field_154_Slot = _registry.IndexOfField(itemCollection, "Field_154");
        _Field_155_Slot = _registry.IndexOfField(itemCollection, "Field_155");
        _Base_Damage_Slot = _registry.IndexOfField(itemCollection, "Field_158");
        _Color_Slot = _registry.IndexOfField(itemCollection, "Field_14C");
        _Field_18C_Slot = _registry.IndexOfField(itemCollection, "Field_18C");
        _Item_Type_Slot = _registry.IndexOfField(itemCollection, "Field_190");
        _Material_Slot = _registry.IndexOfField(itemCollection, "Field_194");
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

        _Augment_Field_1 = _registry.IndexOfField(augmentCollection, "Augment_Field_1");
        _Augment_Field_2 = _registry.IndexOfField(augmentCollection, "Augment_Field_2");
        _Augment_Field_3 = _registry.IndexOfField(augmentCollection, "Augment_Field_3");

        _Field_1F0_Slot = _registry.IndexOfField(itemCollection, "Field_1F0");
        _Field_1E8_Slot = _registry.IndexOfField(itemCollection, "Field_1E8");
        _Field_1EC_Slot = _registry.IndexOfField(itemCollection, "Field_1EC");
        _Field_1F4_Slot = _registry.IndexOfField(itemCollection, "Field_1F4");
        _Field_1F8_Slot = _registry.IndexOfField(itemCollection, "Field_1F8");

        _Field_220_Slot = _registry.IndexOfField(strideCollection, "Unknown_220");
        _Field_224_Slot = _registry.IndexOfField(strideCollection, "Unknown_224");
        _Field_225_Slot = _registry.IndexOfField(strideCollection, "Unknown_225");
        _Field_228_Slot = _registry.IndexOfField(strideCollection, "Unknown_228");
        _Field_22C_Slot = _registry.IndexOfField(strideCollection, "Unknown_22C");
        _Field_230_Slot = _registry.IndexOfField(strideCollection, "Unknown_230");
        _Field_234_Slot = _registry.IndexOfField(strideCollection, "Unknown_234");
        _Field_238_Slot = _registry.IndexOfField(strideCollection, "Unknown_238");
        _Field_23C_Slot = _registry.IndexOfField(strideCollection, "Unknown_23C");
        _String_240_Slot = _registry.IndexOfField(strideCollection, "String_240");
        _Field_280_Slot = _registry.IndexOfField(strideCollection, "Unknown_280");

        _Field_53C_Slot = _registry.IndexOfField(itemCollection, "Field_53C");
        _Bag_Space_Slot = _registry.IndexOfField(itemCollection, "Field_53D");
        _Bag_Size_Slot = _registry.IndexOfField(itemCollection, "Field_53E");
        _Weight_Reduction_Slot = _registry.IndexOfField(itemCollection, "Field_53F");
        _Field_540_Slot = _registry.IndexOfField(itemCollection, "Field_540");
        _Field_541_Slot = _registry.IndexOfField(itemCollection, "Field_541");
        _String_542_Slot = _registry.IndexOfField(itemCollection, "String_542");
        _Lore_Group_Slot = _registry.IndexOfField(itemCollection, "Field_EC");
        _Field_F4_Slot = _registry.IndexOfField(itemCollection, "Field_F4");
        _Field_560_Slot = _registry.IndexOfField(itemCollection, "Field_560");
        _Field_568_Slot = _registry.IndexOfField(itemCollection, "Field_568");
        _Plus_Attack_Slot = _registry.IndexOfField(itemCollection, "Field_570");
        _Field_580_Slot = _registry.IndexOfField(itemCollection, "Field_580");
        _Field_564_Slot = _registry.IndexOfField(itemCollection, "Field_564");
        _Aug_Distiller_Needed = _registry.IndexOfField(itemCollection, "Field_1E4");    //126
        _Field_584_Slot = _registry.IndexOfField(itemCollection, "Field_584");
        _Field_588_Slot = _registry.IndexOfField(itemCollection, "Field_588");
        _Field_58C_Slot = _registry.IndexOfField(itemCollection, "Field_58C");
        _Field_58D_Slot = _registry.IndexOfField(itemCollection, "Field_58D");
        _Max_Stack_Size_Slot = _registry.IndexOfField(itemCollection, "Field_590");
        _Field_594_Slot = _registry.IndexOfField(itemCollection, "Field_594");
        _Field_5A9_Slot = _registry.IndexOfField(itemCollection, "Field_5A9");
        // blob 4DC
        _Field_5A0_Slot = _registry.IndexOfField(itemCollection, "Field_5A0");
        _Field_5A8_Slot = _registry.IndexOfField(itemCollection, "Field_5A8");
        _Field_598_Slot = _registry.IndexOfField(itemCollection, "Field_598");
        _Field_59C_Slot = _registry.IndexOfField(itemCollection, "Field_59C");
        _Backstab_Damage_Slot = _registry.IndexOfField(itemCollection, "Field_15C");  // 140
        _Heroic_Strength = _registry.IndexOfField(itemCollection, "Field_160");
        _Field_164_Slot = _registry.IndexOfField(itemCollection, "Field_164");
        _Field_168_Slot = _registry.IndexOfField(itemCollection, "Field_168");
        _Heroic_Agility_Slot = _registry.IndexOfField(itemCollection, "Field_16C");
        _Field_170_Slot = _registry.IndexOfField(itemCollection, "Field_170");
        _Field_174_Slot = _registry.IndexOfField(itemCollection, "Field_174");
        _Field_178_Slot = _registry.IndexOfField(itemCollection, "Field_178");
        _Field_17C_Slot = _registry.IndexOfField(itemCollection, "Field_17C");
        _Field_180_Slot = _registry.IndexOfField(itemCollection, "Field_180");
        _Field_5AC_Slot = _registry.IndexOfField(itemCollection, "Field_5AC");  // 150
        _Field_5b0_Slot = _registry.IndexOfField(itemCollection, "Field_5b0");
        _Field_5b4_Slot = _registry.IndexOfField(itemCollection, "Field_5b4");
        _Field_5A4_Slot = _registry.IndexOfField(itemCollection, "Field_5A4");
        _Field_D3_Slot = _registry.IndexOfField(itemCollection, "Field_D3");
        _Field_5b8_Slot = _registry.IndexOfField(itemCollection, "Field_5b8");
        _Field_5bC_Slot = _registry.IndexOfField(itemCollection, "Field_5bC");
        _Field_5C0_Slot = _registry.IndexOfField(itemCollection, "Field_5C0");
        _Field_5C4_Slot = _registry.IndexOfField(itemCollection, "Field_5C4");
        _Field_5C8_Slot = _registry.IndexOfField(itemCollection, "Field_5C8");
        _Field_5CC_Slot = _registry.IndexOfField(itemCollection, "Field_5CC");  // 160
        _Field_5D0_Slot = _registry.IndexOfField(itemCollection, "Field_5D0");
        _Field_5D4_Slot = _registry.IndexOfField(itemCollection, "Field_5D4");
        _String_5D8_Slot = _registry.IndexOfField(itemCollection, "String_5D8");
        _Field_614_Slot = _registry.IndexOfField(itemCollection, "Field_614");
        _Field_5F8_Slot = _registry.IndexOfField(itemCollection, "Field_5F8");
        _Field_5FC_Slot = _registry.IndexOfField(itemCollection, "Field_5FC");
        _Field_5FD_Slot = _registry.IndexOfField(itemCollection, "Field_5FD");
        _Field_600_Slot = _registry.IndexOfField(itemCollection, "Field_600");
        _Field_604_Slot = _registry.IndexOfField(itemCollection, "Field_604");
        _Field_608_Slot = _registry.IndexOfField(itemCollection, "Field_608"); // 170
        _Field_60C_Slot = _registry.IndexOfField(itemCollection, "Field_60C");
        _Field_610_Slot = _registry.IndexOfField(itemCollection, "Field_610");
        _Field_65C_Slot = _registry.IndexOfField(itemCollection, "Field_65C");
        _Field_D4_Slot = _registry.IndexOfField(itemCollection, "Field_D4");
        _Field_D5_Slot = _registry.IndexOfField(itemCollection, "Field_D5");
        _Field_D6_Slot = _registry.IndexOfField(itemCollection, "Field_D6");
        _Field_D7_Slot = _registry.IndexOfField(itemCollection, "Field_D7");
        _Field_D8_Slot = _registry.IndexOfField(itemCollection, "Field_D8");
        _Field_DC_Slot = _registry.IndexOfField(itemCollection, "Field_DC");  // 180
        _Field_DD_Slot = _registry.IndexOfField(itemCollection, "Field_DD");
        _Field_DE_Slot = _registry.IndexOfField(itemCollection, "Field_DE");
        _Field_DF_Slot = _registry.IndexOfField(itemCollection, "Field_DF");
        _Field_E0_Slot = _registry.IndexOfField(itemCollection, "Field_E0");
        _Field_E4_Slot = _registry.IndexOfField(itemCollection, "Field_E4");
        _Field_184_Slot = _registry.IndexOfField(itemCollection, "Field_184");
        _Field_188_Slot = _registry.IndexOfField(itemCollection, "Field_188");
        _Field_F0_Slot = _registry.IndexOfField(itemCollection, "Field_F0");
        _Field_F5_Slot = _registry.IndexOfField(itemCollection, "Field_F5");
        _Field_618_Slot = _registry.IndexOfField(itemCollection, "Field_618");  // 190
        _String_61C_Slot = _registry.IndexOfField(itemCollection, "String_61C");
        _Child_Count_Slot = _registry.IndexOfField(itemCollection, "ChildCount");
        _Field_2C_Slot = _registry.IndexOfField(itemCollection, "Field_2C");
        _Field_30_Slot = _registry.IndexOfField(itemCollection, "Field_30");
        _Field_48_Slot = _registry.IndexOfField(itemCollection, "Field_48");

        _Field_Optional_4_Byte_Slot = _registry.IndexOfField(optional4sCollection, "Unknown_Optional_Int");
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
        if (data.Length == 0)
        {
            return root;
        }

        string characterName = GlassContext.SessionRegistry.CharacterNameFromMetadata(metadata);
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            SlotId itemListSlot = _registry.IndexOfField(_extractor.CollectionOf(), "Item List");
            SlotId optional24 = _registry.IndexOfField(_extractor.CollectionOf(), "Optional24");


            GateHandle itemListGate = _extractor.GetGateAt(itemListSlot);
            if (itemListGate.Exists == true)
            {
                uint itemCount = _extractor.BagCount(itemListGate);
                DebugLog.Write(LogChannel.Opcodes, "Describe: Item List gate has " + itemCount + " items", LogLevel.Info);
                for (uint i = 0; i < itemCount; i++)
                {
                    FieldDisplayNode itemNode = DescribeItem(itemListGate, i, "Item " + (i + 1) + ": ");
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
        FieldDisplayNode itemNode = BuildItemNode(title, itemGate, itemIndex);

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

        // Process child item gates
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
                FieldDisplayNode childNode = DescribeItem(singleItemGate, 0u, "Slot " + (childIndex + 1) + ": ");
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
    private FieldDisplayNode BuildItemNode(string titlePrefix, GateHandle itemGate, uint itemIndex)
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
        AddUIntNode(_Item_Type_Slot2, "Item Type2", itemNode);
        AddUIntNode(_Item_Type_Slot, "Item_Type", itemNode, "D");
        AddWornSlotNode(_Usable_Slot_Mask, "Slots", itemNode);
        AddUIntNode(_Current_Stack_Size_Slot, "Current Stack Size", itemNode, "D");
        AddUIntNode(_Max_Stack_Size_Slot, "Max Stack Size", itemNode, "D");
        AddUIntNode(_ITFile_Slot, "IT File#", itemNode, "D");
        AddUIntNode(_Icon_ID_Slot, "Icon ID", itemNode);
        AddUIntNode(_Required_Level_Slot, "Required Level", itemNode, "D");
        AddUIntNode(_Recommended_Level_Slot, "Recommended Level", itemNode, "D");
        AddFloatNode(_Weight_Slot, "Weight", itemNode);
        AddUIntNode(_Remaining_Charges_Slot, "Remaining Charges", itemNode, "D");
        AddUIntNode(_Food_Drink_Value_Slot, "Food/Drink Value", itemNode, "D");
        AddSizeNode(_Size_Slot, "Size", itemNode);
        AddUIntNode(_Cost_Slot, "Cost", itemNode, "D");
        AddUIntNode(_Color_Slot, "Color", itemNode);

        FieldDisplayNode bagSubtree = new FieldDisplayNode("Bag Fields");
        itemNode.AddChild(bagSubtree);
        AddUIntNode(_Bag_Space_Slot, "Bag Slots", bagSubtree, "D");
        AddSizeNode(_Bag_Size_Slot, "Content Size", bagSubtree);
        AddUIntNode(_Weight_Reduction_Slot, "Weight Reduction (%)", bagSubtree, "D");

        FieldDisplayNode weaponSubtree = new FieldDisplayNode("Weapon Fields");
        itemNode.AddChild(weaponSubtree);
        AddUIntNode(_Weapon_Delay_Slot, "Weapon Delay", weaponSubtree, "D");
        AddUIntNode(_Base_Damage_Slot, "Base Damage", weaponSubtree, "D");
        AddRatioNode(_Base_Damage_Slot, _Weapon_Delay_Slot, "Ratio", weaponSubtree);

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
        AddUIntNode(_Heroic_Strength, "Heroic Strength", statModSubtree, "D");
        AddUIntNode(_Heroic_Agility_Slot, "Heroic Agility", statModSubtree, "D");
        AddUIntNode(_Plus_Attack_Slot, "Plus Attack", statModSubtree, "D");

        AddAugmentFields(itemGate, itemIndex, itemNode);
        AddStrides(itemGate, itemIndex, itemNode);

        AddIntNode(_HP_Regen_Slot, "HP Regen", itemNode, "D");
        AddIntNode(_Mana_Regen_Slot, "Mana Regen", itemNode, "D");

        AddClassListNode(_Class_Mask_Slot, "Class Mask", itemNode);
        AddRaceListNode(_Race_Mask_Slot, "Race Mask", itemNode);
        AddUIntNode(_Aug_Distiller_Needed, "Augmentation Distiller Needed", itemNode, "D");
        AddUIntNode(_Bard_Value_Slot, "Bard Value", itemNode, "D");
        AddUIntNode(_Material_Slot, "Material", itemNode, "D");
        AddUIntNode(_Tradeskill_Slot, "Used in Tradeskills", itemNode, "D");
        AddUIntNode(_Current_Location_Slot, "Inventory Slot", itemNode, "D");
        AddUIntNode(_Lore_Group_Slot, "Lore Group", itemNode, "X");
        AddUIntNode(_Backstab_Damage_Slot, "Backstab Dmg", itemNode, "D");

        AddUIntNode(_Field_3_Slot, "Field 3", itemNode, "?");
        AddUIntNode(_Field_5_Slot, "Field 5", itemNode, "?");
        AddUIntNode(_Field_6_Slot, "Field 6", itemNode, "?");
        AddUIntNode(_Field_7_Slot, "Field 7", itemNode, "?");
        AddUIntNode(_Field_8_Slot, "Field 8", itemNode, "?");
        AddUIntNode(_Field_9_Slot, "Field 9", itemNode, "?");
        AddUIntNode(_Field_10_Slot, "Field 10", itemNode, "?");
        AddUIntNode(_Field_11_Slot, "Timestamp", itemNode);
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

        AddUIntNode(_DF_13_Slot, "DF 13", itemNode, "?");

        AddUIntNode(_Field_2C_Slot, "Field 2C", itemNode, "?");
        AddUIntNode(_Field_30_Slot, "Field 30", itemNode, "?");
        AddUIntNode(_Field_48_Slot, "Field 48", itemNode, "?");
        AddUIntNode(_Field_D3_Slot, "Field D3", itemNode, "?");
        AddUIntNode(_Field_D4_Slot, "Field D4", itemNode, "?");
        AddUIntNode(_Field_D5_Slot, "Field D5", itemNode, "?");
        AddUIntNode(_Field_D6_Slot, "Field D6", itemNode, "?");
        AddUIntNode(_Field_D7_Slot, "Field D7", itemNode, "?");
        AddUIntNode(_Field_D8_Slot, "Field D8", itemNode, "?");
        AddUIntNode(_Field_DC_Slot, "Field DC", itemNode, "?");
        AddUIntNode(_Field_DD_Slot, "Field DD", itemNode, "?");
        AddUIntNode(_Field_DE_Slot, "Field DE", itemNode, "?");
        AddUIntNode(_Field_DF_Slot, "Field DF", itemNode, "?");
        AddUIntNode(_Field_E0_Slot, "Field E0", itemNode, "?");
        AddUIntNode(_Field_E4_Slot, "Field E4", itemNode, "?");

        AddUIntNode(_Field_F0_Slot, "Field F0", itemNode, "?");
        AddUIntNode(_Field_F4_Slot, "Field F4", itemNode, "?");
        AddUIntNode(_Field_F5_Slot, "Field F5", itemNode, "?");
        AddUIntNode(_Field_Fb_Slot, "Field Fb", itemNode, "?");

        AddUIntNode(_Field_118_Slot, "Field 118", itemNode, "?");
        AddUIntNode(_Field_11C_Slot, "Field 11C", itemNode, "?");
        AddUIntNode(_Field_120_Slot, "Field 120", itemNode, "?");
        AddUIntNode(_Field_124_Slot, "Field 124", itemNode, "?");
        AddUIntNode(_Field_128_Slot, "Field 128", itemNode, "?");
        AddUIntNode(_Field_12C_Slot, "Field 12C", itemNode, "?");
        AddUIntNode(_Field_130_Slot, "Field 130", itemNode, "?");
        AddUIntNode(_Field_134_Slot, "Field 134", itemNode, "?");

        AddUIntNode(_Field_13C_Slot, "Field 13C", itemNode, "?");

        AddUIntNode(_Field_148_Slot, "Field 148", itemNode, "?");

        AddUIntNode(_Field_150_Slot, "Field 150", itemNode, "?");
        AddUIntNode(_Field_151_Slot, "Field 151", itemNode, "?");
        AddUIntNode(_Field_153_Slot, "Field 153", itemNode, "?");
        AddUIntNode(_Field_154_Slot, "Field 154", itemNode, "?");
        AddUIntNode(_Field_155_Slot, "Field 155", itemNode, "?");

        AddUIntNode(_Field_164_Slot, "Field 164", itemNode, "?");
        AddUIntNode(_Field_168_Slot, "Field 168", itemNode, "?");

        AddUIntNode(_Field_170_Slot, "Field 170", itemNode, "?");
        AddUIntNode(_Field_174_Slot, "Field 174", itemNode, "?");
        AddUIntNode(_Field_178_Slot, "Field 178", itemNode, "?");
        AddUIntNode(_Field_17C_Slot, "Field 17C", itemNode, "?");
        AddUIntNode(_Field_180_Slot, "Field 180", itemNode, "?");
        AddUIntNode(_Field_184_Slot, "Field 184", itemNode, "?");
        AddUIntNode(_Field_188_Slot, "Field 188", itemNode, "?");
        AddUIntNode(_Field_18C_Slot, "Field 18C", itemNode, "?");

        AddUIntNode(_Field_198_Slot, "Field 198", itemNode, "?");
        AddUIntNode(_Field_19C_Slot, "Field 19C", itemNode, "?");
        AddUIntNode(_Field_1A0_Slot, "Field 1A0", itemNode, "?");
        AddUIntNode(_Field_1A4_Slot, "Field 1A4", itemNode, "?");

        AddUIntNode(_Field_1D8_Slot, "Field 1D8", itemNode, "?");
        AddUIntNode(_Field_1DC_Slot, "Field 1DC", itemNode, "?");
        AddUIntNode(_Field_1E0_Slot, "Field 1E0", itemNode, "?");

        AddUIntNode(_Field_1E8_Slot, "Field 1E8", itemNode, "?");
        AddUIntNode(_Field_1EC_Slot, "Field 1EC", itemNode, "?");
        AddUIntNode(_Field_1F0_Slot, "Field 1F0", itemNode, "?");
        AddUIntNode(_Field_1F4_Slot, "Field 1F4", itemNode, "?");
        AddUIntNode(_Field_1F8_Slot, "Field 1F8", itemNode, "?");
        AddStringNode(_String_1FC_Slot, "String 1FC", itemNode);

        // blob 4DC

        AddUIntNode(_Field_21C_Slot, "Field 21C", itemNode, "?");

        AddUIntNode(_Field_52C_Slot, "Field 52C", itemNode, "?");
        AddUIntNode(_Field_530_Slot, "Field 530", itemNode, "?");
        AddUIntNode(_Field_534_Slot, "Field 534", itemNode, "?");
        AddUIntNode(_Field_53C_Slot, "Field 53C", itemNode, "?");  // 112


        AddUIntNode(_Field_540_Slot, "Field 540", itemNode, "?");
        AddUIntNode(_Field_541_Slot, "Field 541", itemNode, "?");
        AddStringNode(_String_542_Slot, "String 542", itemNode);

        AddUIntNode(_Field_560_Slot, "Field 560", itemNode, "?");
        AddUIntNode(_Field_564_Slot, "Field 564", itemNode, "?");
        AddUIntNode(_Field_568_Slot, "Field 568", itemNode, "?");

        AddUIntNode(_Field_57C_Slot, "Field 57C", itemNode, "?");
        AddUIntNode(_Field_580_Slot, "Field 580", itemNode, "?");
        AddUIntNode(_Field_584_Slot, "Field 584", itemNode, "?");
        AddUIntNode(_Field_588_Slot, "Field 588", itemNode, "?");
        AddUIntNode(_Field_58C_Slot, "Field 58C", itemNode, "?");
        AddUIntNode(_Field_58D_Slot, "Field 58D", itemNode, "?");  // 130

        AddUIntNode(_Field_594_Slot, "Field 594", itemNode, "?");

        AddUIntNode(_Field_598_Slot, "Field 598", itemNode, "?");
        AddUIntNode(_Field_5A0_Slot, "Field 5A0", itemNode, "?");
        AddUIntNode(_Field_5A4_Slot, "Field 5A4", itemNode, "?");
        AddUIntNode(_Field_5A8_Slot, "Field 5A8", itemNode, "?");
        AddUIntNode(_Field_5A9_Slot, "Field 5A9", itemNode, "?");
        AddUIntNode(_Field_5AC_Slot, "Field 5AC", itemNode, "?");
        AddUIntNode(_Field_5b0_Slot, "Field 5B0", itemNode, "?");
        AddUIntNode(_Field_5b4_Slot, "Field 5B4", itemNode, "?");
        AddUIntNode(_Field_5b8_Slot, "Field 5B8", itemNode, "?");
        AddUIntNode(_Field_5bC_Slot, "Field 5BC", itemNode, "?");
        AddUIntNode(_Field_5C0_Slot, "Field 5C0", itemNode, "?");
        AddUIntNode(_Field_5C4_Slot, "Field 5C4", itemNode, "?");
        AddFloatNode(_Field_5C8_Slot, "Field 5C8", itemNode);
        AddFloatNode(_Field_5CC_Slot, "Field 5CC", itemNode);
        AddUIntNode(_Field_5D0_Slot, "Field 5D0", itemNode, "?");
        AddUIntNode(_Field_5D4_Slot, "Field 5D4", itemNode, "?");
        AddStringNode(_String_5D8_Slot, "String 5D8", itemNode);
        AddUIntNode(_Field_5F8_Slot, "Field 5F8", itemNode, "?");
        AddUIntNode(_Field_5FC_Slot, "Field 5FC", itemNode, "?");
        AddUIntNode(_Field_5FD_Slot, "Field 5FD", itemNode, "?");

        AddUIntNode(_Field_600_Slot, "Field 600", itemNode, "?");
        AddFloatNode(_Field_604_Slot, "Field 604", itemNode);
        AddUIntNode(_Field_608_Slot, "Field 608", itemNode, "?");
        AddUIntNode(_Field_60C_Slot, "Field 60C", itemNode, "?");
        AddUIntNode(_Field_610_Slot, "Field 610", itemNode, "?");
        AddUIntNode(_Field_614_Slot, "Field 614", itemNode, "?");
 
        AddUIntNode(_Field_618_Slot, "Field 618", itemNode, "?");
        AddStringNode(_String_61C_Slot, "String 61C", itemNode);
        AddUIntNode(_Field_65C_Slot, "Field 65C", itemNode, "?");
        AddUIntNode(_Child_Count_Slot, "Child Count", itemNode, "?");

        AddOptional24(itemGate, itemIndex, itemNode);
        AddOptional4s(itemGate, itemIndex, itemNode);

        return itemNode;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddOptional24
    //
    // Adds the Optional24 fields for the item in the extractor's active bag.  Resolves the
    // Optional24 gate slot on the active collection, and if the field is present and the gate
    // exists, enters the gate's single (Once) bag and adds each Optional24 field beneath the
    // supplied parent node.  Restores the item's bag before returning by re-entering itemGate
    // at itemIndex, so the active bag on exit matches the active bag on entry.
    //
    // itemGate:   The gate whose instance holds the current item.
    // itemIndex:  The instance index of the current item within itemGate.
    // parent:     The display node the Optional24 fields are added beneath.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AddOptional24(GateHandle itemGate, uint itemIndex, FieldDisplayNode parent)
    {
        SlotId optional24Slot = GlassContext.PatchRegistry.IndexOfField(_extractor.CollectionOf(), "Optional24");
        if (_extractor.IsPresent(optional24Slot) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "AddOptional24: no Optional24 present", LogLevel.Trace);
            return;
        }

        GateHandle optional24Gate = _extractor.GetGateAt(optional24Slot);
        if (optional24Gate.Exists == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "AddOptional24: Optional24 slot present but no gate", LogLevel.Warn);
            return;
        }

        uint bagCount = _extractor.BagCount(optional24Gate);
        if (bagCount == 0)
        {
            DebugLog.Write(LogChannel.Opcodes, "AddOptional24: Optional24 gate has no bag", LogLevel.Warn);
            return;
        }

        DebugLog.Write(LogChannel.Opcodes, "AddOptional24: entering Optional24 bag", LogLevel.Info);
        _extractor.EnterGate(optional24Gate, 0u);

        FieldDisplayNode optional24Node = new FieldDisplayNode("Optional24");
        parent.AddChild(optional24Node);

        // Optional24 fields go here, e.g.:
        // AddUIntNode(_Field_XXX_Slot, "Field XXX", optional24Node, "?");

        DebugLog.Write(LogChannel.Opcodes, "AddOptional24: restoring item bag", LogLevel.Trace);
        _extractor.EnterGate(itemGate, itemIndex);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddAugmentFields
    //
    // Add the Augment fields for the item in the extractor's active bag.  Resolves the
    // AugmentFields gate slot on the active collection, and if the field is present and the
    // gate exists, iterates every bag under the gate.  Each bag is entered in turn and its
    // fields are added beneath a per-bag node under the supplied parent.  Restores the item's
    // bag before returning by re-entering itemGate at itemIndex, so the active bag on exit
    // matches the active bag on entry.
    //
    // itemGate:   The gate whose instance holds the current item.
    // itemIndex:  The instance index of the current item within itemGate.
    // parent:     The display node the AugmentFields nodes are added beneath.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AddAugmentFields(GateHandle itemGate, uint itemIndex, FieldDisplayNode parent)
    {
        SlotId augmentFieldsSlot = GlassContext.PatchRegistry.IndexOfField(_extractor.CollectionOf(), "AugmentFields");
        if (_extractor.IsPresent(augmentFieldsSlot) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "AddAugmentFields: no AugmentFields present", LogLevel.Trace);
            return;
        }

        GateHandle augmentFieldsGate = _extractor.GetGateAt(augmentFieldsSlot);
        if (augmentFieldsGate.Exists == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "AddAugmentFields: AugmentFields slot present but no gate", LogLevel.Warn);
            return;
        }

        uint bagCount = _extractor.BagCount(augmentFieldsGate);
        DebugLog.Write(LogChannel.Opcodes, "AddAugmentFields: AugmentFields gate has " + bagCount + " bag(s)", LogLevel.Info);

        FieldDisplayNode augmentFieldsNode = new FieldDisplayNode("Augment Fields");
        parent.AddChild(augmentFieldsNode);

        for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
        {
            _extractor.EnterGate(augmentFieldsGate, bagIndex);

            FieldDisplayNode bagNode = new FieldDisplayNode("Augment " + (bagIndex + 1));
            augmentFieldsNode.AddChild(bagNode);

            AddUIntNode(_Augment_Field_1, "Field 1", bagNode, "?");
            AddUIntNode(_Augment_Field_2, "Field 2", bagNode, "?");
            AddUIntNode(_Augment_Field_3, "Field 3", bagNode, "?");
        }

        DebugLog.Write(LogChannel.Opcodes, "AddAugmentFields: restoring item bag", LogLevel.Trace);
        _extractor.EnterGate(itemGate, itemIndex);
    }
    
    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddStrides
    //
    // Adds the Strides for the item in the extractor's active bag.  Resolves the Strides gate
    // slot on the active collection, and if the field is present and the gate exists, iterates
    // every bag under the gate.  Each bag is entered in turn and its fields are added beneath a
    // per-bag node under the supplied parent.  Restores the item's bag before returning by
    // re-entering itemGate at itemIndex, so the active bag on exit matches the active bag on
    // entry.
    //
    // itemGate:   The gate whose instance holds the current item.
    // itemIndex:  The instance index of the current item within itemGate.
    // parent:     The display node the Stride nodes are added beneath.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AddStrides(GateHandle itemGate, uint itemIndex, FieldDisplayNode parent)
    {
        SlotId stridesSlot = GlassContext.PatchRegistry.IndexOfField(_extractor.CollectionOf(), "Strides");
        if (_extractor.IsPresent(stridesSlot) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "AddStrides: no Strides present", LogLevel.Trace);
            return;
        }

        GateHandle stridesGate = _extractor.GetGateAt(stridesSlot);
        if (stridesGate.Exists == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "AddStrides: Strides slot present but no gate", LogLevel.Warn);
            return;
        }

        uint bagCount = _extractor.BagCount(stridesGate);

        FieldDisplayNode stridesNode = new FieldDisplayNode("Strides");
        parent.AddChild(stridesNode);

        for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
        {
            _extractor.EnterGate(stridesGate, bagIndex);

            FieldDisplayNode bagNode = new FieldDisplayNode("Stride " + (bagIndex + 1));
            stridesNode.AddChild(bagNode);

            AddUIntNode(_Field_220_Slot, "Field 220", bagNode, "?");
            AddUIntNode(_Field_224_Slot, "Field 224", bagNode, "?");
            AddUIntNode(_Field_225_Slot, "Field 225", bagNode, "?");
            AddUIntNode(_Field_228_Slot, "Field 228", bagNode, "?");
            AddUIntNode(_Field_22C_Slot, "Field 22C", bagNode, "?");
            AddUIntNode(_Field_230_Slot, "Field 230", bagNode, "?");
            AddUIntNode(_Field_234_Slot, "Field 234", bagNode, "?");
            AddUIntNode(_Field_238_Slot, "Field 238", bagNode, "?");
            AddUIntNode(_Field_23C_Slot, "Field 23C", bagNode, "?");
            AddStringNode(_String_240_Slot, "String_240", bagNode);
            AddUIntNode(_Field_280_Slot, "Field 280", bagNode, "?");
        }
        _extractor.EnterGate(itemGate, itemIndex);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddOptional4s
    //
    // Adds the optional trailing 4-byte fields for the item in the extractor's active bag.
    // Resolves the optional-fields gate slot on the active collection, and if the field is
    // present and the gate exists, iterates every bag under the gate.  Each bag is entered in
    // turn and its single 4-byte value is added beneath a group node under the supplied
    // parent.  Restores the item's bag before returning by re-entering itemGate at itemIndex,
    // so the active bag on exit matches the active bag on entry.
    //
    // itemGate:   The gate whose instance holds the current item.
    // itemIndex:  The instance index of the current item within itemGate.
    // parent:     The display node the optional-field nodes are added beneath.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AddOptional4s(GateHandle itemGate, uint itemIndex, FieldDisplayNode parent)
    {
        SlotId optionalSlot = GlassContext.PatchRegistry.IndexOfField(_extractor.CollectionOf(), "Optional_4ByteFields");
        if (_extractor.IsPresent(optionalSlot) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "AddOptional4s: no optional fields present", LogLevel.Trace);
            return;
        }
        GateHandle optionalGate = _extractor.GetGateAt(optionalSlot);
        if (optionalGate.Exists == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "AddOptional4s: optional slot present but no gate", LogLevel.Warn);
            return;
        }
        uint bagCount = _extractor.BagCount(optionalGate);
        DebugLog.Write(LogChannel.Opcodes, "AddOptional4s: extracting " + bagCount + " optional fields", LogLevel.Trace);
        FieldDisplayNode optionalNode = new FieldDisplayNode("Optional 4-Byte Fields");
        parent.AddChild(optionalNode);
        for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
        {
            _extractor.EnterGate(optionalGate, bagIndex);
            AddUIntNode(_Field_Optional_4_Byte_Slot, "Unknown", optionalNode, "?");
        }
        _extractor.EnterGate(itemGate, itemIndex);
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
        FieldDisplayNode newNode = new FieldDisplayNode(label + ": \"" + value + "\"");
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
    // AddRatioNode
    //
    // Adds a synthetic node whose value is the quotient of two uint fields.  Reads the numerator
    // and denominator from the extractor, formats the quotient to two decimal places, and appends
    // the byte ranges of both source fields to the node.  A zero denominator displays "n/a" and
    // still records both ranges.
    //
    // numeratorSlot:    Slot of the numerator field.
    // denominatorSlot:  Slot of the denominator field.
    // label:            Display label for the node.
    // parent:           Node to append the new node under.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AddRatioNode(SlotId numeratorSlot, SlotId denominatorSlot, string label, FieldDisplayNode parent)
    {
        uint numerator = _extractor.GetUIntAt(numeratorSlot);
        uint denominator = _extractor.GetUIntAt(denominatorSlot);

        string valueString;
        if (denominator == 0u)
        {
            valueString = "n/a";
        }
        else
        {
            double ratio = (double)numerator / (double)denominator;
            valueString = ratio.ToString("F2");
            DebugLog.Write(LogChannel.Fields,
                "AddRatioNode: '" + label + "' = " + numerator + "/" + denominator
                + " = " + valueString, LogLevel.Trace);
        }

        FieldDisplayNode newNode = new FieldDisplayNode(label + ": " + valueString);
        newNode.AddByteRange(_extractor.GetByteRangeFor(numeratorSlot));
        newNode.AddByteRange(_extractor.GetByteRangeFor(denominatorSlot));
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
        FieldDisplayNode newNode = new FieldDisplayNode(label + ": (" + value.ToString("X4") + ") " + slots);
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

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddRaceListNode
    //
    // Builds a display node for a mask of race names.
    //
    // slotId:  The slot to extract
    // label:   The label to use in the new display node
    // parent   The display node's parent
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AddRaceListNode(SlotId slotId, string label, FieldDisplayNode parent)
    {
        uint value = _extractor.GetUIntAt(slotId);
        string slots = DecodeRaceMask(value);
        FieldDisplayNode newNode = new FieldDisplayNode(label + ": (" + value.ToString("X4") + ") " + slots);
        newNode.AddByteRange(_extractor.GetByteRangeFor(slotId));
        parent.AddChild(newNode);
    }

    const uint RACE_Drakkin = 1 << 15;
    const uint RACE_Froglok = 1 << 14;
    const uint RACE_VahShir = 1 << 13;
    const uint RACE_Iksar = 1 << 12;
    const uint RACE_Gnome = 1 << 11;
    const uint RACE_Halfling = 1 << 10;
    const uint RACE_Ogre = 1 << 9;
    const uint RACE_Troll = 1 << 8;
    const uint RACE_Dwarf = 1 << 7;
    const uint RACE_HalfElf = 1 << 6;
    const uint RACE_DarkElf = 1 << 5;
    const uint RACE_HighElf = 1 << 4;
    const uint RACE_WoodElf = 1 << 3;
    const uint RACE_Erudite = 1 << 2;
    const uint RACE_Barbarian = 1 << 1;
    const uint RACE_Human = 1;
    private string DecodeRaceMask(uint mask)
    {
        List<string> races = new List<string>();

        if ((mask & RACE_Drakkin) != 0)
        {
            races.Add("Drakkin");
        }
        if ((mask & RACE_Froglok) != 0)
        {
            races.Add("Froglok");
        }
        if ((mask & RACE_VahShir) != 0)
        {
            races.Add("Vah Shir");
        }
        if ((mask & RACE_Iksar) != 0)
        {
            races.Add("Iksar");
        }
        if ((mask & RACE_Gnome) != 0)
        {
            races.Add("Gnome");
        }
        if ((mask & RACE_Halfling) != 0)
        {
            races.Add("Halfling");
        }
        if ((mask & RACE_Ogre) != 0)
        {
            races.Add("Ogre");
        }
        if ((mask & RACE_Troll) != 0)
        {
            races.Add("Troll");
        }
        if ((mask & RACE_Dwarf) != 0)
        {
            races.Add("Dwarf");
        }
        if ((mask & RACE_HalfElf) != 0)
        {
            races.Add("Half Elf");
        }
        if ((mask & RACE_DarkElf) != 0)
        {
            races.Add("Dark Elf");
        }
        if ((mask & RACE_HighElf) != 0)
        {
            races.Add("High Elf");
        }
        if ((mask & RACE_WoodElf) != 0)
        {
            races.Add("Wood Elf");
        }
        if ((mask & RACE_Erudite) != 0)
        {
            races.Add("Erudite");
        }
        if ((mask & RACE_Barbarian) != 0)
        {
            races.Add("Barbarian");
        }
        if ((mask & RACE_Human) != 0)
        {
            races.Add("Human");
        }
        return string.Join(",", races);
    }

    const uint CLASS_Berserker = 1 << 15;
    const uint CLASS_Beastlord = 1 << 14;
    const uint CLASS_Enchanter = 1 << 13;
    const uint CLASS_Magician = 1 << 12;
    const uint CLASS_Wizard = 1 << 11;
    const uint CLASS_Necromancer = 1 << 10;
    const uint CLASS_Shaman = 1 << 9;
    const uint CLASS_Rogue = 1 << 8;
    const uint CLASS_Bard = 1 << 7;
    const uint CLASS_Monk = 1 << 6;
    const uint CLASS_Druid = 1 << 5;
    const uint CLASS_ShadowKnight = 1 << 4;
    const uint CLASS_Ranger = 1 << 3;
    const uint CLASS_Paladin = 1 << 2;
    const uint CLASS_Cleric = 1 << 1;
    const uint CLASS_Warrior = 1;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddClassListNode
    //
    // Builds a display node for a mask of class names.
    //
    // slotId:  The slot to extract
    // label:   The label to use in the new display node
    // parent   The display node's parent
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AddClassListNode(SlotId slotId, string label, FieldDisplayNode parent)
    {
        uint value = _extractor.GetUIntAt(slotId);
        string slots = DecodeClassMask(value);
        FieldDisplayNode newNode = new FieldDisplayNode(label + ": (" + value.ToString("X4") + ") " + slots);
        newNode.AddByteRange(_extractor.GetByteRangeFor(slotId));
        parent.AddChild(newNode);
    }
    private string DecodeClassMask(uint mask)
    {
        List<string> classes = new List<string>();

        if ((mask & CLASS_Berserker) != 0)
        {
            classes.Add("Berserker");
        }
        if ((mask & CLASS_Beastlord) != 0)
        {
            classes.Add("Beastlord");
        }
        if ((mask & CLASS_Enchanter) != 0)
        {
            classes.Add("Enchanter");
        }
        if ((mask & CLASS_Magician) != 0)
        {
            classes.Add("Magician");
        }
        if ((mask & CLASS_Wizard) != 0)
        {
            classes.Add("Wizard");
        }
        if ((mask & CLASS_Necromancer) != 0)
        {
            classes.Add("Necromancer");
        }
        if ((mask & CLASS_Shaman) != 0)
        {
            classes.Add("Shaman");
        }
        if ((mask & CLASS_Rogue) != 0)
        {
            classes.Add("Rogue");
        }
        if ((mask & CLASS_Bard) != 0)
        {
            classes.Add("Bard");
        }
        if ((mask & CLASS_Druid) != 0)
        {
            classes.Add("Druid");
        }
        if ((mask & CLASS_ShadowKnight) != 0)
        {
            classes.Add("Shadow Knight");
        }
        if ((mask & CLASS_Ranger) != 0)
        {
            classes.Add("Ranger");
        }
        if ((mask & CLASS_Paladin) != 0)
        {
            classes.Add("Paladin");
        }
        if ((mask & CLASS_Cleric) != 0)
        {
            classes.Add("Cleric");
        }
        if ((mask & CLASS_Warrior) != 0)
        {
            classes.Add("Warrior");
        }
        return string.Join(",", classes.ToArray());
    }

    private static readonly string[] ItemSizeNames = { "Tiny", "Small", "Medium", "Large", "Giant" };

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddSizeNode
    //
    // Builds a display node for an item size.
    //
    // slotId:  The slot to extract
    // label:   The label to use in the new display node
    // parent   The display node's parent
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AddSizeNode(SlotId slotId, string label, FieldDisplayNode parent)
    {
        uint value = _extractor.GetUIntAt(slotId);
        string size = DecodeItemSize(value);
        FieldDisplayNode newNode = new FieldDisplayNode(label + ": " + size);
        newNode.AddByteRange(_extractor.GetByteRangeFor(slotId));
        parent.AddChild(newNode);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // DecodeItemSize
    //
    // Returns the display string for an item size value: the size name with the numeric value
    // in parentheses, or "Unknown" with the value when it is outside the known range.
    //
    // size:     The item size value.
    // returns:  The display string.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private string DecodeItemSize(uint size)
    {
        if (size < (uint)ItemSizeNames.Length)
        {
            return ItemSizeNames[size] + " (" + size + ")";
        }

        DebugLog.Write(LogChannel.Fields,
            "DecodeItemSize: unknown size " + size, LogLevel.Warn);
        return "Unknown (" + size + ")";
    }
}
