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
    public ItemId Id { get; set; } = ItemId.None;                   // 34
    public string IdString { get; set; } = string.Empty;            // 1
    public ContainerType ContainerType { get; set; }                // 3

    // unknown:  7, 8
    public string Name { get; set; } = string.Empty;                // 30
    public string Lore { get; set; } = string.Empty;                // 31
    public uint LoreGroup { get; set; }


    public byte ItemType1 { get; set; }
    public byte ItemType2 { get; set; }                             // 29
    public uint ClassMask { get; set; }
    public uint RaceMask { get; set; }
    public uint UsableSlotMask { get; set; }
    public uint RequiredLevel { get; set; }
    public uint RecommendedLevel { get; set; }

    public Boolean IsAttuned { get; set; }                          // 13
    public uint FoodDrinkValue { get; set; }
    public Boolean IsTradeskill { get; set; }                       // 45

    // Stat adjustments
    public sbyte PlusStrength { get; set; }                         // 52
    public sbyte PlusStamina { get; set; }                          // 53
    public sbyte PlusAgility { get; set; }          // 54
    public sbyte PlusDexterity { get; set; }        // 55
    public sbyte PlusCharisma { get; set; }         // 56
    public sbyte PlusIntelligence { get; set; }     // 57
    public sbyte PlusWisdom { get; set; }           // 58
    public int PlusHP { get; set; }                 // 59   (note:  no 60)
    public int PlusMana { get; set; }               // 61
    public int PlusEndurance { get; set; }          // 62
    public int PlusAC { get; set; }                 // 63
    public int PlusAttack { get; set; }             // 123
    public int HpRegen { get; set; }                // 64
    public int ManaRegen { get; set; }              // 65

    // Heroic stats.  Only strength and agility have identified wire fields;
    // the rest fill in as identification completes.
    public uint HeroicStrength { get; set; }
    public uint HeroicStamina { get; set; }
    public uint HeroicAgility { get; set; }
    public uint HeroicDexterity { get; set; }
    public uint HeroicCharisma { get; set; }
    public uint HeroicIntelligence { get; set; }
    public uint HeroicWisdom { get; set; }

    // Saves
    public byte SaveCold { get; set; }                   // 46
    public byte SaveDisease { get; set; }                // 47
    public byte SavePoison { get; set; }                 // 48
    public byte SaveMagic { get; set; }                  // 49
    public byte SaveFire { get; set; }                   // 50
    public byte SaveCorruption { get; set; }             // 51


    public uint AugDistillerNeeded { get; set; }         // 126

    // Skill modifier
    public uint SkillModSkill { get; set; }
    public int SkillModPercent { get; set; }
    public int SkillModMax { get; set; }

    // Weapon
    public uint WeaponDelay { get; set; }
    public uint BaseDamage { get; set; }
    public uint BackstabDamage { get; set; }
    public uint WeaponRange { get; set; }

    // Bag
    public byte BagSlots { get; set; }                   // 113
    public byte BagContentSize { get; set; }             // 114
    public byte BagWeightReduction { get; set; }         // 115

    // Physical
    public float Weight { get; set; }                    // 35
    public byte Size { get; set; }                       // 40
    public uint Cost { get; set; }


    // Effects granted by this item
    public List<ItemEffect> Effects { get; set; } = new List<ItemEffect>();
}