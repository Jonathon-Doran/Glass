using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
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
public class HandleInventory : OpcodeHandler
{
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _Item_String_Slot;
    private readonly SlotId _Current_Stack_Size_Slot;
    private readonly SlotId _ContainerType_Slot;
    private readonly SlotId _Current_Location_Slot;
    private readonly SlotId _SubPosition_Slot;
    private readonly SlotId _AugPosition_Slot;
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
    private readonly SlotId _Skill_Percent_Change;
    private readonly SlotId _Skill_Max_Change;
    private readonly SlotId _Skill_Slot;
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
    private readonly SlotId _Weapon_Range_Slot;
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

    private readonly SlotId _Effect_SpellId_Slot;
    private readonly SlotId _EffectLevel2_Slot;
    private readonly SlotId _Effect_Type_Slot;
    private readonly SlotId _Effect_Level_Slot;
    private readonly SlotId _Effect_Max_Charges_Slot;
    private readonly SlotId _Effect_Casttime_Slot;
    private readonly SlotId _Effect_Recasttime_Slot;
    private readonly SlotId _Effect_Recasttype_Slot;
    private readonly SlotId _Effect_Recastdelay_Slot;
    private readonly SlotId _Effect_Name_Slot;
    private readonly SlotId _Effect_Unknown7_Slot;

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
    private readonly SlotId _Optional_24_Field1_Slot;
    private readonly SlotId _Evolving_Current_Rank_Slot;
    private readonly SlotId _Optional_24_Field3_Slot;
    private readonly SlotId _Evolving_Max_Rank_Slot;
    private readonly SlotId _Optional_24_Field5_Slot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleInventory (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleInventory(PatchLevel patchLevel)
        :base(patchLevel, "OP_Inventory")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);


        // handles of collections that we expect
        CollectionHandle itemCollection = _registry.GetCollectionHandle(_patchLevel, "Inventory Item");
        CollectionHandle optional24Collection = _registry.GetCollectionHandle(_patchLevel, "InventoryOptional24");
        CollectionHandle augmentCollection = _registry.GetCollectionHandle(_patchLevel, "Inventory_AugmentFields");
        CollectionHandle strideCollection = _registry.GetCollectionHandle(_patchLevel, "Inventory_Stride");
        CollectionHandle optional4sCollection = _registry.GetCollectionHandle(_patchLevel, "Inventory_Optional_4s");

        // child gates of interest
        _Gate_InventoryOptional24_Slot = _registry.IndexOfField(itemCollection, "Optional24");

        _Item_Name_Slot = _registry.IndexOfField(itemCollection, "ItemName");
        _Item_Lore_Slot = _registry.IndexOfField(itemCollection, "ItemLore");

        _Item_String_Slot = _registry.IndexOfField(itemCollection, "ItemString");

        _Current_Stack_Size_Slot = _registry.IndexOfField(itemCollection, "StackSize");
        _ContainerType_Slot = _registry.IndexOfField(itemCollection, "ContainerType");
        _Current_Location_Slot = _registry.IndexOfField(itemCollection, "Location");
        _SubPosition_Slot = _registry.IndexOfField(itemCollection, "SubPosition");
        _AugPosition_Slot = _registry.IndexOfField(itemCollection, "AugPosition");
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
        _Skill_Percent_Change = _registry.IndexOfField(itemCollection, "Field_11C");
        _Skill_Max_Change = _registry.IndexOfField(itemCollection, "Field_120");
        _Skill_Slot = _registry.IndexOfField(itemCollection, "Field_118");
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
        _Weapon_Range_Slot = _registry.IndexOfField(itemCollection, "Weapon_Range");
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

        _Effect_SpellId_Slot = _registry.IndexOfField(strideCollection, "Unknown_220");
        _EffectLevel2_Slot = _registry.IndexOfField(strideCollection, "Unknown_224");
        _Effect_Type_Slot = _registry.IndexOfField(strideCollection, "Unknown_225");
        _Effect_Level_Slot = _registry.IndexOfField(strideCollection, "Unknown_228");
        _Effect_Max_Charges_Slot = _registry.IndexOfField(strideCollection, "Unknown_22C");
        _Effect_Casttime_Slot = _registry.IndexOfField(strideCollection, "Unknown_230");
        _Effect_Recasttime_Slot = _registry.IndexOfField(strideCollection, "Unknown_234");
        _Effect_Recasttype_Slot = _registry.IndexOfField(strideCollection, "Unknown_238");
        _Effect_Recastdelay_Slot = _registry.IndexOfField(strideCollection, "Unknown_23C");
        _Effect_Name_Slot = _registry.IndexOfField(strideCollection, "String_240");
        _Effect_Unknown7_Slot = _registry.IndexOfField(strideCollection, "Unknown_280");

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
        _Optional_24_Field1_Slot = _registry.IndexOfField(optional24Collection, "Field1");
        _Evolving_Current_Rank_Slot = _registry.IndexOfField(optional24Collection, "Field2");
        _Optional_24_Field3_Slot = _registry.IndexOfField(optional24Collection, "Field3");
        _Evolving_Max_Rank_Slot = _registry.IndexOfField(optional24Collection, "Field4");
        _Optional_24_Field5_Slot = _registry.IndexOfField(optional24Collection, "Field5");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Dispatches to direction-specific handlers.
    //
    // data:       The application payload
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
    // Extracts an inventory packet and stores the worn items it carries into
    // the owning character.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (rootGate.Exists == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "Inventory: no root gate, nothing stored",
                    LogLevel.Warn);
                return;
            }

            CaptureWornItems(metadata);
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CaptureWornItems
    //
    // Stores the worn items from an extracted inventory packet into the owning
    // character's WornItems dictionary.  Walks the top-level Item List gate,
    // classifies each item's location, and records every item in a worn
    // position.  The dictionary is cleared first because the packet is a
    // complete snapshot.  Must be called with the extraction active and the
    // root gate's bag current.
    //
    // metadata:  Packet metadata, used to resolve the owning character.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void CaptureWornItems(PacketMetadata metadata)
    {
        string characterName = GlassContext.SessionRegistry.CharacterNameFromMetadata(metadata);

        Character? character = CharacterRepository.Instance.GetByName(characterName);
        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "CaptureWornItems: no Character named '" + characterName +
                "' in repository; worn items not stored", LogLevel.Warn);
            return;
        }

        SlotId itemListSlot = _registry.IndexOfField(_extractor.CollectionOf(), "Item List");
        GateHandle itemListGate = _extractor.GetGateAt(itemListSlot);
        if (itemListGate.Exists == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "CaptureWornItems: no Item List gate; worn items not stored",
                LogLevel.Warn);
            return;
        }

        character.WornItems.Clear();
        DebugLog.Write(LogChannel.Opcodes, "CaptureWornItems: cleared worn items for '" + characterName + "'",
            LogLevel.Info);

        uint itemCount = _extractor.BagCount(itemListGate);

        for (uint itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            _extractor.EnterGate(itemListGate, itemIndex);

            StorageSystem storageType = (StorageSystem)_extractor.GetUIntAt(_ContainerType_Slot);
            uint mainPosition = _extractor.GetUIntAt(_Current_Location_Slot);

            if (Character.TryGetWornPosition(storageType, mainPosition, out WornPosition wornPosition) == false)
            {
                continue;
            }

            WornItem wornItem = new WornItem();
            wornItem.ItemId = (ItemId)_extractor.GetUIntAt(_Item_ID_Slot);
            wornItem.Name = _extractor.GetStringAt(_Item_Name_Slot);
            wornItem.WornPosition = wornPosition;
            wornItem.DeltaHP = _extractor.GetIntAt(_Plus_HP_Slot);

            character.WornItems[wornPosition] = wornItem;
            DebugLog.Write(LogChannel.Opcodes, "CaptureWornItems: " + wornPosition.DisplayName() + " = '" +
                wornItem.Name + "' (" + wornItem.ItemId + "), deltaHP " + wornItem.DeltaHP, LogLevel.Info);
        }

        DebugLog.Write(LogChannel.Opcodes, "CaptureWornItems: stored " + character.WornItems.Count +
            " worn items for '" + characterName + "'", LogLevel.Info);
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
    public override FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
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
            if (rootGate == GateHandle.None)
            {
                DebugLog.Write(LogChannel.Fields,
                    "InventoryHandler.Describe: extraction aborted, nothing to describe", LogLevel.Warn);
                return root;
            }

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
                FieldDisplayNode childNode = DescribeItem(singleItemGate, 0u, "Slot " + childIndex + ": ");
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

        FieldNodes.AddStringNode(_extractor, _Item_Name_Slot, "Name", itemNode);
        FieldNodes.AddStringNode(_extractor, _Item_Lore_Slot, "Lore", itemNode);
        FieldNodes.AddStringNode(_extractor, _Item_String_Slot, "ID String", itemNode);
        FieldNodes.AddUIntNode(_extractor, _Item_ID_Slot, "ID", itemNode, "D");
        FieldNodes.AddUIntNode(_extractor, _Item_Type_Slot2, "Item Type2", itemNode);
        FieldNodes.AddUIntNode(_extractor, _Item_Type_Slot, "Item_Type", itemNode, "D");

        FieldDisplayNode locationSubtree = new FieldDisplayNode("Item Location");
        itemNode.AddChild(locationSubtree);

        AddWornSlotNode(_Usable_Slot_Mask, "Usable Slots", locationSubtree);

        uint mainPosition = _extractor.GetUIntAt(_Current_Location_Slot);
        StorageSystem storageType = (StorageSystem) _extractor.GetUIntAt(_ContainerType_Slot);
        uint subPosition = _extractor.GetUIntAt(_SubPosition_Slot);
        uint augPosition = _extractor.GetUIntAt(_AugPosition_Slot);
        string storageText = Character.DescribeStorageLocation(storageType);
        string subPosText = Character.DescribePosition(subPosition);
        string augPosText = Character.DescribePosition(augPosition);

        String location = Character.DescribeLocation(storageType, mainPosition, subPosition, augPosition);

        FieldDisplayNode locationNode = new FieldDisplayNode("Item Location: " + location);
        locationNode.AddByteRange(_extractor.GetByteRangeFor(_Current_Location_Slot));
        locationNode.AddByteRange(_extractor.GetByteRangeFor(_ContainerType_Slot));
        locationNode.AddByteRange(_extractor.GetByteRangeFor(_SubPosition_Slot));
        locationNode.AddByteRange(_extractor.GetByteRangeFor(_AugPosition_Slot));
        locationSubtree.AddChild(locationNode);


        FieldNodes.AddLabeledNode(_extractor, _ContainerType_Slot, "Storage: " + storageText +
            " (" + storageType + ")", locationSubtree);
        FieldNodes.AddUIntNode(_extractor, _Current_Location_Slot, "Storage Slot", locationSubtree, "D");
        FieldNodes.AddLabeledNode(_extractor, _SubPosition_Slot, "SubPosition: " + subPosText +
            " (" + subPosition + ")", locationSubtree);
        FieldNodes.AddLabeledNode(_extractor, _AugPosition_Slot, "AugPosition: " + augPosText +
            " (" + augPosition + ")", locationSubtree);

        FieldNodes.AddUIntNode(_extractor, _Current_Stack_Size_Slot, "Current Stack Size", itemNode, "D");
        FieldNodes.AddUIntNode(_extractor, _Max_Stack_Size_Slot, "Max Stack Size", itemNode, "D");
        FieldNodes.AddUIntNode(_extractor, _ITFile_Slot, "IT File#", itemNode, "D");
        FieldNodes.AddUIntNode(_extractor, _Icon_ID_Slot, "Icon ID", itemNode);
        FieldNodes.AddUIntNode(_extractor, _Required_Level_Slot, "Required Level", itemNode, "D");
        FieldNodes.AddUIntNode(_extractor, _Recommended_Level_Slot, "Recommended Level", itemNode, "D");
        FieldNodes.AddFloatNode(_extractor, _Weight_Slot, "Weight", itemNode);
        FieldNodes.AddUIntNode(_extractor, _Remaining_Charges_Slot, "Remaining Charges", itemNode, "D");
        FieldNodes.AddUIntNode(_extractor, _Food_Drink_Value_Slot, "Food/Drink Value", itemNode, "D");
        AddSizeNode(_Size_Slot, "Size", itemNode);
        FieldNodes.AddUIntNode(_extractor, _Cost_Slot, "Cost", itemNode, "D");
        FieldNodes.AddUIntNode(_extractor, _Color_Slot, "Color", itemNode);

        FieldDisplayNode bagSubtree = new FieldDisplayNode("Bag Fields");
        itemNode.AddChild(bagSubtree);
        FieldNodes.AddUIntNode(_extractor, _Bag_Space_Slot, "Bag Slots", bagSubtree, "D");
        AddSizeNode(_Bag_Size_Slot, "Content Size", bagSubtree);
        FieldNodes.AddUIntNode(_extractor, _Weight_Reduction_Slot, "Weight Reduction (%)", bagSubtree, "D");

        FieldDisplayNode weaponSubtree = new FieldDisplayNode("Weapon Fields");
        itemNode.AddChild(weaponSubtree);
        FieldNodes.AddUIntNode(_extractor, _Weapon_Delay_Slot, "Delay", weaponSubtree, "D");
        FieldNodes.AddUIntNode(_extractor, _Base_Damage_Slot, "Base Damage", weaponSubtree, "D");
        FieldNodes.AddUIntNode(_extractor, _Weapon_Range_Slot, "Range", weaponSubtree, "D");
        FieldNodes.AddUIntNode(_extractor, _Backstab_Damage_Slot, "Backstab Dmg", weaponSubtree, "D");
        AddRatioNode(_Base_Damage_Slot, _Weapon_Delay_Slot, "Ratio", weaponSubtree);

        FieldDisplayNode saveSubtree = new FieldDisplayNode("Saves");
        itemNode.AddChild(saveSubtree);
        FieldNodes.AddIntNode(_extractor, _Save_Cold_Slot, "Save vs Cold", saveSubtree, "D");
        FieldNodes.AddIntNode(_extractor, _Save_Disease_Slot, "Save vs Disease", saveSubtree, "D");
        FieldNodes.AddIntNode(_extractor, _Save_Poison_Slot, "Save vs Poison", saveSubtree, "D");
        FieldNodes.AddIntNode(_extractor, _Save_Magic_Slot, "Save vs Magic", saveSubtree, "D");
        FieldNodes.AddIntNode(_extractor, _Save_Fire_Slot, "Save vs Fire", saveSubtree, "D");

        FieldDisplayNode statModSubtree = new FieldDisplayNode("Stat Modifiers");
        itemNode.AddChild(statModSubtree);
        FieldNodes.AddIntNode(_extractor, _Plus_Strength_Slot, "Plus Strength", statModSubtree, "D");
        FieldNodes.AddIntNode(_extractor, _Plus_Stamina_Slot, "Plus Stamina", statModSubtree, "D");
        FieldNodes.AddIntNode(_extractor, _Plus_Agility_Slot, "Plus Agility", statModSubtree, "D");
        FieldNodes.AddIntNode(_extractor, _Plus_Dexterity_Slot, "Plus Dexterity", statModSubtree, "D");
        FieldNodes.AddIntNode(_extractor, _Plus_Charisma_Slot, "Plus Charisma", statModSubtree, "D");
        FieldNodes.AddIntNode(_extractor, _Plus_Intelligence_Slot, "Plus Intelligence", statModSubtree, "D");
        FieldNodes.AddIntNode(_extractor, _Plus_Wisdom_Slot, "Plus Wisdom", statModSubtree, "D");
        FieldNodes.AddIntNode(_extractor, _Plus_HP_Slot, "Plus HP", statModSubtree, "D");
        FieldNodes.AddIntNode(_extractor, _Plus_Mana_Slot, "Plus Mana", statModSubtree, "D");
        FieldNodes.AddIntNode(_extractor, _Plus_AC_Slot, "Plus AC", statModSubtree, "D");
        FieldNodes.AddIntNode(_extractor, _Plus_Endurance_Slot, "Plus Endurance", statModSubtree, "D");
        FieldNodes.AddUIntNode(_extractor, _Heroic_Strength, "Heroic Strength", statModSubtree, "D");
        FieldNodes.AddUIntNode(_extractor, _Heroic_Agility_Slot, "Heroic Agility", statModSubtree, "D");
        FieldNodes.AddUIntNode(_extractor, _Plus_Attack_Slot, "Plus Attack", statModSubtree, "D");

        AddAugmentFields(itemGate, itemIndex, itemNode);
        AddItemEffects(itemGate, itemIndex, itemNode);
        AddEvolvingItem(itemGate, itemIndex, itemNode);

        FieldNodes.AddIntNode(_extractor, _HP_Regen_Slot, "HP Regen", itemNode, "D");
        FieldNodes.AddIntNode(_extractor, _Mana_Regen_Slot, "Mana Regen", itemNode, "D");

        AddClassListNode(_Class_Mask_Slot, "Class Mask", itemNode);
        AddRaceListNode(_Race_Mask_Slot, "Race Mask", itemNode);
        FieldNodes.AddUIntNode(_extractor, _Aug_Distiller_Needed, "Augmentation Distiller Needed", itemNode, "D");
        FieldNodes.AddUIntNode(_extractor, _Bard_Value_Slot, "Bard Value", itemNode, "D");
        FieldNodes.AddUIntNode(_extractor, _Material_Slot, "Material", itemNode, "D");
        FieldNodes.AddUIntNode(_extractor, _Tradeskill_Slot, "Used in Tradeskills", itemNode, "D");

        FieldNodes.AddUIntNode(_extractor, _Lore_Group_Slot, "Lore Group", itemNode, "X");


        uint skillID = _extractor.GetUIntAt(_Skill_Slot);

        FieldNodes.AddLabeledNode(_extractor, _Skill_Slot, "Skill: " + Skills.GetSkillName(skillID), itemNode);
        FieldNodes.AddUIntNode(_extractor, _Skill_Percent_Change, "Skill Percent Change", itemNode, "D");
        FieldNodes.AddUIntNode(_extractor, _Skill_Max_Change, "Skill Max Change", itemNode, "D");


        FieldNodes.AddUIntNode(_extractor, _Field_7_Slot, "Field 7", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_8_Slot, "Field 8", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_9_Slot, "Field 9", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_10_Slot, "Field 10", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_11_Slot, "Timestamp", itemNode);
        FieldNodes.AddUIntNode(_extractor, _Field_13_Slot, "Field 13", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_14_Slot, "Field 14", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_15_Slot, "Field 15", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_16_Slot, "Field 16", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_17_Slot, "Field 17", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_19_Slot, "Field 19", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_20_Slot, "Field 20", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_21_Slot, "Field 21", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_22_Slot, "Field 22", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_23_Slot, "Field 23", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_24_Slot, "Field 24", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_25_Slot, "Field 25", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_26_Slot, "Field 26", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_27_Slot, "Field 27", itemNode, "?");

        FieldNodes.AddUIntNode(_extractor, _DF_4_Slot, "DF 4", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _DF_7_Slot, "DF 7", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _DF_8_Slot, "DF 8", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _DF_9_Slot, "DF 9", itemNode, "?");

        FieldNodes.AddUIntNode(_extractor, _DF_13_Slot, "DF 13", itemNode, "?");

        FieldNodes.AddUIntNode(_extractor, _Field_2C_Slot, "Field 2C", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_30_Slot, "Field 30", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_48_Slot, "Field 48", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_D3_Slot, "Field D3", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_D4_Slot, "Field D4", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_D5_Slot, "Field D5", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_D6_Slot, "Field D6", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_D7_Slot, "Field D7", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_D8_Slot, "Field D8", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_DC_Slot, "Field DC", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_DD_Slot, "Field DD", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_DE_Slot, "Field DE", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_DF_Slot, "Field DF", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_E0_Slot, "Field E0", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_E4_Slot, "Field E4", itemNode, "?");

        FieldNodes.AddUIntNode(_extractor, _Field_F0_Slot, "Field F0", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_F4_Slot, "Field F4", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_F5_Slot, "Field F5", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_Fb_Slot, "Field Fb", itemNode, "?");


        FieldNodes.AddUIntNode(_extractor, _Field_124_Slot, "Field 124", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_128_Slot, "Field 128", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_12C_Slot, "Field 12C", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_130_Slot, "Field 130", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_134_Slot, "Field 134", itemNode, "?");

        FieldNodes.AddUIntNode(_extractor, _Field_13C_Slot, "Field 13C", itemNode, "?");

        FieldNodes.AddUIntNode(_extractor, _Field_148_Slot, "Field 148", itemNode, "?");

        FieldNodes.AddUIntNode(_extractor, _Field_150_Slot, "Field 150", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_151_Slot, "Field 151", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_153_Slot, "Field 153", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_154_Slot, "Field 154", itemNode, "?");


        FieldNodes.AddUIntNode(_extractor, _Field_164_Slot, "Field 164", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_168_Slot, "Field 168", itemNode, "?");

        FieldNodes.AddUIntNode(_extractor, _Field_170_Slot, "Field 170", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_174_Slot, "Field 174", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_178_Slot, "Field 178", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_17C_Slot, "Field 17C", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_180_Slot, "Field 180", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_184_Slot, "Field 184", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_188_Slot, "Field 188", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_18C_Slot, "Field 18C", itemNode, "?");

        FieldNodes.AddUIntNode(_extractor, _Field_198_Slot, "Field 198", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_19C_Slot, "Field 19C", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_1A0_Slot, "Field 1A0", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_1A4_Slot, "Field 1A4", itemNode, "?");

        FieldNodes.AddUIntNode(_extractor, _Field_1D8_Slot, "Field 1D8", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_1DC_Slot, "Field 1DC", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_1E0_Slot, "Field 1E0", itemNode, "?");

        FieldNodes.AddUIntNode(_extractor, _Field_1E8_Slot, "Field 1E8", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_1EC_Slot, "Field 1EC", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_1F0_Slot, "Field 1F0", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_1F4_Slot, "Field 1F4", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_1F8_Slot, "Field 1F8", itemNode, "?");
        FieldNodes.AddStringNode(_extractor, _String_1FC_Slot, "String 1FC", itemNode);

        // blob 4DC

        FieldNodes.AddUIntNode(_extractor, _Field_21C_Slot, "Field 21C", itemNode, "?");

        FieldNodes.AddUIntNode(_extractor, _Field_52C_Slot, "Field 52C", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_530_Slot, "Field 530", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_534_Slot, "Field 534", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_53C_Slot, "Field 53C", itemNode, "?");  // 112


        FieldNodes.AddUIntNode(_extractor, _Field_540_Slot, "Field 540", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_541_Slot, "Field 541", itemNode, "?");
        FieldNodes.AddStringNode(_extractor, _String_542_Slot, "String 542", itemNode);

        FieldNodes.AddUIntNode(_extractor, _Field_560_Slot, "Field 560", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_564_Slot, "Field 564", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_568_Slot, "Field 568", itemNode, "?");

        FieldNodes.AddUIntNode(_extractor, _Field_57C_Slot, "Field 57C", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_580_Slot, "Field 580", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_584_Slot, "Field 584", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_588_Slot, "Field 588", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_58C_Slot, "Field 58C", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_58D_Slot, "Field 58D", itemNode, "?");  // 130

        FieldNodes.AddUIntNode(_extractor, _Field_594_Slot, "Field 594", itemNode, "?");

        FieldNodes.AddUIntNode(_extractor, _Field_598_Slot, "Field 598", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_5A0_Slot, "Field 5A0", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_5A4_Slot, "Field 5A4", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_5A8_Slot, "Field 5A8", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_5A9_Slot, "Field 5A9", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_5AC_Slot, "Field 5AC", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_5b0_Slot, "Field 5B0", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_5b4_Slot, "Field 5B4", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_5b8_Slot, "Field 5B8", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_5bC_Slot, "Field 5BC", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_5C0_Slot, "Field 5C0", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_5C4_Slot, "Field 5C4", itemNode, "?");
        FieldNodes.AddFloatNode(_extractor, _Field_5C8_Slot, "Field 5C8", itemNode);
        FieldNodes.AddFloatNode(_extractor, _Field_5CC_Slot, "Field 5CC", itemNode);
        FieldNodes.AddUIntNode(_extractor, _Field_5D0_Slot, "Field 5D0", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_5D4_Slot, "Field 5D4", itemNode, "?");
        FieldNodes.AddStringNode(_extractor, _String_5D8_Slot, "String 5D8", itemNode);
        FieldNodes.AddUIntNode(_extractor, _Field_5F8_Slot, "Field 5F8", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_5FC_Slot, "Field 5FC", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_5FD_Slot, "Field 5FD", itemNode, "?");

        FieldNodes.AddUIntNode(_extractor, _Field_600_Slot, "Field 600", itemNode, "?");
        FieldNodes.AddFloatNode(_extractor, _Field_604_Slot, "Field 604", itemNode);
        FieldNodes.AddUIntNode(_extractor, _Field_608_Slot, "Field 608", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_60C_Slot, "Field 60C", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_610_Slot, "Field 610", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Field_614_Slot, "Field 614", itemNode, "?");

        FieldNodes.AddUIntNode(_extractor, _Field_618_Slot, "Field 618", itemNode, "?");
        FieldNodes.AddStringNode(_extractor, _String_61C_Slot, "String 61C", itemNode);
        FieldNodes.AddUIntNode(_extractor, _Field_65C_Slot, "Field 65C", itemNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Child_Count_Slot, "Child Count", itemNode, "?");


        AddOptional4s(itemGate, itemIndex, itemNode);

        return itemNode;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddEvolvingItem
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
    private void AddEvolvingItem(GateHandle itemGate, uint itemIndex, FieldDisplayNode parent)
    {
        SlotId evolvingSlot = GlassContext.PatchRegistry.IndexOfField(_extractor.CollectionOf(), "Optional24");
        if (_extractor.IsPresent(evolvingSlot) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "AddEvolvingItem: no Optional24 present", LogLevel.Trace);
            return;
        }

        GateHandle evolvingGate = _extractor.GetGateAt(evolvingSlot);
        if (evolvingGate.Exists == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "AddEvolvingItem: Optional24 slot present but no gate", LogLevel.Warn);
            return;
        }

        uint bagCount = _extractor.BagCount(evolvingGate);
        if (bagCount == 0)
        {
            DebugLog.Write(LogChannel.Opcodes, "AddEvolvingItem: Optional24 gate has no bag", LogLevel.Warn);
            return;
        }

        _extractor.EnterGate(evolvingGate, 0u);

        FieldDisplayNode evolvingNode = new FieldDisplayNode("Evolving Item");
        parent.AddChild(evolvingNode);

        FieldNodes.AddUIntNode(_extractor, _Optional_24_Field1_Slot, "Field 1", evolvingNode, "?");
        FieldNodes.AddUIntNode(_extractor, _Evolving_Current_Rank_Slot, "Current Rank", evolvingNode, "D");
        FieldNodes.AddUIntNode(_extractor, _Evolving_Max_Rank_Slot, "Max Rank", evolvingNode, "D");
        FieldNodes.AddUIntNode(_extractor, _Optional_24_Field3_Slot, "Field 3", evolvingNode, "?");

        FieldNodes.AddUIntNode(_extractor, _Optional_24_Field5_Slot, "Field 5", evolvingNode, "?");

        // restore item bag
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
        FieldDisplayNode augmentFieldsNode = new FieldDisplayNode("Augment Fields");
        parent.AddChild(augmentFieldsNode);

        for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
        {
            _extractor.EnterGate(augmentFieldsGate, bagIndex);

            FieldDisplayNode bagNode = new FieldDisplayNode("Augment " + (bagIndex + 1));
            augmentFieldsNode.AddChild(bagNode);

            FieldNodes.AddUIntNode(_extractor, _Augment_Field_1, "Field 1", bagNode, "?");
            FieldNodes.AddUIntNode(_extractor, _Augment_Field_2, "Field 2", bagNode, "?");
            FieldNodes.AddUIntNode(_extractor, _Augment_Field_3, "Field 3", bagNode, "?");
        }

        DebugLog.Write(LogChannel.Opcodes, "AddAugmentFields: restoring item bag", LogLevel.Trace);
        _extractor.EnterGate(itemGate, itemIndex);
    }
    
    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddItemEffects
    //
    // Adds the Effects for the item in the extractor's active bag.  Resolves the Effects gate
    // slot on the active collection, and if the field is present and the gate exists, iterates
    // every bag under the gate.  Each bag is entered in turn and its fields are added beneath a
    // per-bag node under the supplied parent.  Restores the item's bag before returning by
    // re-entering itemGate at itemIndex, so the active bag on exit matches the active bag on
    // entry.
    //
    // itemGate:   The gate whose instance holds the current item.
    // itemIndex:  The instance index of the current item within itemGate.
    // parent:     The display node the Effect nodes are added beneath.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AddItemEffects(GateHandle itemGate, uint itemIndex, FieldDisplayNode parent)
    {
        SlotId effectsSlot = GlassContext.PatchRegistry.IndexOfField(_extractor.CollectionOf(), "Strides");
        if (_extractor.IsPresent(effectsSlot) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "AddItemEffects: no Effects present", LogLevel.Trace);
            return;
        }

        GateHandle effectsGate = _extractor.GetGateAt(effectsSlot);
        if (effectsGate.Exists == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "AddItemEffects: Effects slot present but no gate", LogLevel.Warn);
            return;
        }

        uint bagCount = _extractor.BagCount(effectsGate);

        FieldDisplayNode stridesNode = new FieldDisplayNode("Effects");
        parent.AddChild(stridesNode);

        for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
        {
            _extractor.EnterGate(effectsGate, bagIndex);

            FieldDisplayNode bagNode = new FieldDisplayNode("Effect " + (bagIndex + 1));
            stridesNode.AddChild(bagNode);

            FieldNodes.AddUIntNode(_extractor, _Effect_SpellId_Slot, "Spell-ID", bagNode, "?");
            FieldNodes.AddUIntNode(_extractor, _Effect_Level_Slot, "Level", bagNode, "?");
            FieldNodes.AddUIntNode(_extractor, _EffectLevel2_Slot, "Cast as Level", bagNode, "D");
            FieldNodes.AddUIntNode(_extractor, _Effect_Type_Slot, "Effect Type", bagNode, "D");

            FieldNodes.AddUIntNode(_extractor, _Effect_Max_Charges_Slot, "Effect Max Charges", bagNode, "?");
            FieldNodes.AddUIntNode(_extractor, _Effect_Casttime_Slot, "Cast time (ms)", bagNode, "D");
            FieldNodes.AddUIntNode(_extractor, _Effect_Recasttime_Slot, "Recast time (s)", bagNode, "D");
            FieldNodes.AddUIntNode(_extractor, _Effect_Recasttype_Slot, "Timer", bagNode, "D");
            FieldNodes.AddUIntNode(_extractor, _Effect_Recastdelay_Slot, "Recast delay (s)", bagNode, "D");
            FieldNodes.AddStringNode(_extractor, _Effect_Name_Slot, "Name", bagNode);
            FieldNodes.AddUIntNode(_extractor, _Effect_Unknown7_Slot, "Unknown7", bagNode, "?");
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
            FieldNodes.AddUIntNode(_extractor, _Field_Optional_4_Byte_Slot, "Unknown", optionalNode, "?");
        }
        _extractor.EnterGate(itemGate, itemIndex);
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
        if ((mask & CLASS_Monk) != 0)
        {
            classes.Add("Monk");
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
