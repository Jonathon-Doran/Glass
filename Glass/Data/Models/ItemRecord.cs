using System;

namespace Glass.Data.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// ItemRecord
//
// The definition of one item.  Holds only properties true of every copy;
// per-instance state lives in ItemInstance.
///////////////////////////////////////////////////////////////////////////////////////////////
public class ItemRecord
{

    public string IdString { get; set; } = string.Empty;            // 1
    // 2 = current stack size

    public ContainerType ContainerType { get; set; }                // 3
    // 4,5,6 = Item location

    public ulong Unknown_7 { get; set; }                            // 7 (Field_7)
    public uint Unknown_8 { get; set; }                             // 8 (Field_8)
    public uint Unknown_9 { get; set; }                             // 9 (Field_9)
    public ulong Unknown_10 { get; set; }                           // 10 (Field_10)
    public uint Unknown_11 { get; set; }                            // 11 (Field_11)

    // 12 = Remaining Charges
    // 13 = IsAttuned
    public uint Unknown_14 { get; set; }                            // 14 (Field_14)
    public uint Unknown_15 { get; set; }                            // 15 (Field_15)
    public byte Unknown_16 { get; set; }                            // 16 (Field_16)
    public uint Unknown_17 { get; set; }                            // 17 (Field_17)
    public Boolean Is_Evolving_Item { get; set; }                   // 18 
    // 19 = Evolving Gate
    public uint Unknown_20 { get; set; }                            // 20 (Field_19)
    public uint Unknown_21 { get; set; }                            // 21 (Field_20)
    public uint Unknown_22 { get; set; }                            // 22 (Field_21)
    public uint Unknown_23 { get; set; }                            // 23 (Field_22)
    public uint Unknown_24 { get; set; }                            // 24 (Field_23)
    public uint Unknown_25 { get; set; }                            // 25 (Field_24)

    // 26 = Is_Copied

    public uint Unknown_27 { get; set; }                            // 27 (Field_26)
    public uint Unknown_28 { get; set; }                            // 28 (Field_27)
    public byte ItemType2 { get; set; }                             // 29

    public string Name { get; set; } = string.Empty;                // 30
    public string Lore { get; set; } = string.Empty;                // 31
    public uint IT_File { get; set; }                               // 32
    public uint Unknown_33 { get; set; }                            // 33 (DF_4)
    public ItemId Id { get; set; } = ItemId.None;                   // 34 (DF_5)
    public float Weight { get; set; }                               // 35 (DF_6)
    public byte Unknown_36 { get; set; }                            // 36 (DF_7)
    public byte Tradeable { get; set; }                             // 37 (DF_8)*
    public byte Attuneable { get; set; }                            // 38 (DF_9)*
    public byte Size { get; set; }                                  // 40 (DF_10)
    public uint UsableSlotMask { get; set; }                        // 41
    public uint Cost { get; set; }                                  // 42 (DF_11)
    public uint Icon_ID { get; set; }                               // 43 (DF_12)
    public byte Unknown_44 { get; set; }                            // 44 (DF_13)
    public Boolean IsTradeskill { get; set; }                       // 45
    public byte SaveCold { get; set; }                              // 46
    public byte SaveDisease { get; set; }                           // 47
    public byte SavePoison { get; set; }                            // 48
    public byte SaveMagic { get; set; }                             // 49
    public byte SaveFire { get; set; }                              // 50
    public byte SaveCorruption { get; set; }                        // 51
    public sbyte PlusStrength { get; set; }                         // 52
    public sbyte PlusStamina { get; set; }                          // 53
    public sbyte PlusAgility { get; set; }                          // 54
    public sbyte PlusDexterity { get; set; }                        // 55
    public sbyte PlusCharisma { get; set; }                         // 56
    public sbyte PlusIntelligence { get; set; }                     // 57
    public sbyte PlusWisdom { get; set; }                           // 58
    public int PlusHP { get; set; }                                 // 59
                                                                    // note:  no 60
    public int PlusMana { get; set; }                               // 61
    public int PlusEndurance { get; set; }                          // 62
    public int PlusAC { get; set; }                                 // 63
    public int HpRegen { get; set; }                                // 64
    public int ManaRegen { get; set; }                              // 65
    public uint Unknown_66 {get; set; }                             // 66 (Field_57C)
    public uint ClassMask { get; set; }                             // 67 
    public uint RaceMask { get; set; }                              // 68
    public uint Deity { get; set; }                                 // 69 (Field_148)*
    public uint Skill_Percent_Chance { get; set; }                  // 70 (Field_11C)
    public uint Skill_Max_Change { get; set; }                      // 71 (Field_120)
    public uint Skill_ID { get; set; }                               // 72 (Field_118)
    public uint Unknown_73 { get; set; }                            // 73 (Field_124)
    public uint Unknown_74 { get; set; }                            // 74 (Field_128)
    public uint Unknown_75 { get; set; }                            // 75 (Field_12C)
    public uint Unknown_76 { get; set; }                            // 76 (Field_134)
    public uint Unknown_77 { get; set; }                            // 77 (Field_130)
    public byte Is_Magic { get; set; }                              // 78 (Field_150) *
    public uint FoodDrinkValue { get; set; }                        // 79
    public uint RequiredLevel { get; set; }                         // 80
    public uint RecommendedLevel { get; set; }                      // 81
    public uint Bard_Value { get; set; }                            // 82 (Field_138)
    public uint Unknown_83 { get; set; }                            // 83 (Field_13C)
    public byte Light { get; set; }                                 // 84 (Field_151) *
    public byte Weapon_Delay { get; set; }                          // 85
    public byte Elemental_Damage_Type { get; set; }                 // 86 (Field_153)
    public byte Elemental_Damage_Amount { get; set; }               // 87 (Field_154) *
    public byte Weapon_Range { get; set; }                          // 88
    public uint Weapon_Base_Damage { get; set; }                    // 89 (Field_158)
    public uint Color { get; set; }                                 // 90 (Field_14C)
    public uint Prestige { get; set; }                              // 91 (Field_18C) *
    public byte ItemType1 { get; set; }                             // 92
    public uint Material { get; set; }                              // 93 (Field_194)
    public uint Unknown_94 { get; set; }                            // 94 (Field_19C)
    public uint Unknown_95 { get; set; }                            // 95 (Field_198)
    public uint Unknown_96 { get; set; }                            // 96 (Field_1A0)
    public uint Material2 { get; set; }                            // 97 (Field_1A4) *
    public uint Unknown_98 { get; set; }                            // 98 (Field_21C)
    public uint Unknown_99 { get; set; }                            // 99 (Field_52C)
    public uint Unknown_100 { get; set; }                           // 100 (Field_530)
    public uint CharmFileID { get; set; }                           // 101 (Field_534) *
    public string CharmFile { get; set; } = string.Empty;           // 102 (String_1FC)*
    public uint AugValue { get; set; }                              // 103 (Field_1D8) *
    public uint Unknown_104 { get; set; }                           // 104 (Field_1DC)
    public uint AugRestriction { get; set; }                        // 105 (Field_1E0) *
    // 106 = Augment Gate

    public uint LDON_Sold { get; set; }                             // 107 (Field_1F0) *
    public uint LDON_Theme { get; set; }                            // 108 (Field_1E8) *
    public uint LDON_Price { get; set; }                            // 109 (Field_1EC)  *
    public uint Unknown_110 { get; set; }                           // 110 (Field_1F4)
    public uint Unknown_111 { get; set; }                           // 111 (Field_1F8)
    public byte Bag_Type { get; set; }                              // 112
    public byte Bag_Slot_Count { get; set; }                        // 113
    public byte Bag_Size { get; set; }                              // 114
    public byte Bag_Weight_Reduction { get; set; }                  // 115
    public byte Unknown_116 { get; set; }                           // 116 (Field_540)
    public byte Unknown_117 { get; set; }                           // 117 (Field_541)
    public string Unknown_118 { get; set; } = string.Empty;         // 118 (String_542)
    public uint LoreGroup { get; set; }                             // 119
    public byte Unknown_120 { get; set; }                           // 120 (Field_F4)
    public uint Tribute { get; set; }                               // 121
    public uint FV_Nodrop { get; set; }                             // 122 (Field_568)  *
    public int PlusAttack { get; set; }                             // 123
    public uint Haste { get; set; }                                 // 124
    public uint Unknown_125 { get; set; }                           // 125 (Field_564)
    public uint AugDistillerNeeded { get; set; }                    // 126
    public uint Unknown_127 { get; set; }                           // 127 (Field_584)
    public uint Unknown_128 { get; set; }                           // 128 (Field_588)
    public byte Unknown_129 { get; set; }                           // 129 (Field_58C)
    public byte Unknown_130 { get; set; }                           // 130 (Field_58D)
    public uint Max_Stack_Size { get; set; }                        // 131
    public byte Unknown_132 { get; set; }                           // 132 (Field_594)
    public byte Unknown_133 { get; set; }                           // 133 (Field_5A9)
    public byte[] Unknown_134 { get; set; } = new byte[78];         // 134 (Blob_4DC)
    // 135 = Stides Gate (Effects)
    public uint Unknown_136 { get; set; }                           // 136 (Field_5A0)
    public byte Unknown_137 { get; set; }                           // 137 (Field_5A8)
    public uint Unknown_138 { get; set; }                           // 138 (Field_598)
    public uint Purity { get; set; }                                // 139 (Field_59C) *
    public uint Backstab_Damage { get; set; }                       // 140 (Field_15C)
    public uint Heroic_Strength { get; set; }                       // 141 (Field_160)
    public uint Heroic_Intelligence { get; set; }                   // 142 (Field_164)
    public uint Heroic_Wisdom { get; set; }                         // 143 (Field_168)
    public uint Heroic_Agility { get; set; }                        // 144 (Field_16C)
    public uint Heroic_Dexterity { get; set; }                      // 145 (Field_170)
    public uint Heroic_Stamina { get; set; }                        // 146 (Field_174)
    public uint Heroic_Charisma { get; set; }                       // 147 (Field_178)
    public uint Heal_Amount { get; set; }                           // 148 (Field_17C) *
    public uint Spell_Damage { get; set; }                          // 149 (Field_180) *
    public uint Clairvoyance { get; set; }                          // 150 (Field_5AC) *
    public uint Unknown_151 { get; set; }                           // 151 (Field_5b0)
    public byte Unknown_152 { get; set; }                           // 152 (Field_5b4)
    public uint Unknown_153 { get; set; }                           // 153 (Field_5A4)
    public byte Unknown_154 { get; set; }                           // 154 (Field_D3)
    public uint Placeable2 { get; set; }                            // 155 (Field_5b8) *
    public byte Unknown_156 { get; set; }                           // 156 (Field_5bC)
    public uint Unknown_157 { get; set; }                           // 157 (Field_5C0)
    public uint Unknown_158 { get; set; }                           // 158 (Field_5C4)
    public uint Unknown_159 { get; set; }                           // 159 (Field_5C8)
    public uint Unknown_160 { get; set; }                           // 160 (Field_5CC)
    public uint Unknown_161 { get; set; }                           // 161 (Field_5D0)
    public uint Unknown_162 { get; set; }                           // 162 (Field_5D4)
    public string Unknown_163 { get; set; } = string.Empty;         // 163 (String_5D8)
    public byte Unknown_164 { get; set; }                           // 164 (Field_614)
    public uint Unknown_165 { get; set; }                           // 165 (Field_5F8)
    public byte Unknown_166 { get; set; }                           // 166 (Field_5FC)
    public byte Unknown_167 { get; set; }                           // 167 (Field_5FD)
    public uint Unknown_168 { get; set; }                           // 168 (Field_600)
    public uint Unknown_169 { get; set; }                           // 169 (Field_604)
    public uint Unknown_170 { get; set; }                           // 170 (Field_608)
    public uint Unknown_171 { get; set; }                           // 171 (Field_60C)
    public uint Unknown_172 { get; set; }                           // 172 (Field_610)
    public byte Unknown_173 { get; set; }                           // 173 (Field_65C)
    // 174 = Optional 4's Gate
    public byte Unknown_175 { get; set; }                           // 175 (Field_D4)
    public byte Unknown_176 { get; set; }                           // 176 (Field_D5)
    public byte Unknown_177 { get; set; }                           // 177 (Field_D6)
    public byte Unknown_178 { get; set; }                           // 178 (Field_D7)
    public uint Unknown_179 { get; set; }                           // 179 (Field_D8)
    public byte Unknown_180 { get; set; }                           // 180 (Field_DC)
    public byte Unknown_181 { get; set; }                           // 181 (Field_DD)
    public byte Unknown_182 { get; set; }                           // 182 (Field_DE)
    public byte Unknown_183 { get; set; }                           // 183 (Field_DF)
    public byte Unknown_184 { get; set; }                           // 184 (Field_E0)
    public uint Unknown_185 { get; set; }                           // 185 (Field_E4)
    public uint Unknown_186 { get; set; }                           // 186 (Field_184)
    public uint Unknown_187 { get; set; }                           // 187 (Field_188)
    public uint Unknown_188 { get; set; }                           // 188 (Field_F0)
    public byte Unknown_189 { get; set; }                           // 189 (Field_F5)
    public uint Unknown_190 { get; set; }                           // 190 (Field_618)
    public string Unknown_191 { get; set; } = string.Empty;         // 191 (String_61C)

    // No 193
    // 194 = ChildItems Gate
    // No 195
    public byte Unknown_196 { get; set; }                           // 196 (Field_2C)
    public ulong Unknown_198 { get; set; }                          // 198 (Field_30)
    public uint Unknown_199 { get; set; }                           // 199 (Field_48)


    // Effects granted by this item
    public List<ItemEffect> Effects { get; set; } = new List<ItemEffect>();
}