namespace Glass.Data.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// ItemRecord
//
// The definition of one item.  Holds only properties true of every copy;
// per-instance state lives in ItemInstance.
///////////////////////////////////////////////////////////////////////////////////////////////
public class ItemRecord
{
    // Identity
    public ItemId Id { get; set; } = ItemId.None;
    public string Name { get; set; } = string.Empty;
    public string Lore { get; set; } = string.Empty;
    public uint LoreGroup { get; set; }

    // Classification
    public uint ItemType { get; set; }
    public uint ItemType2 { get; set; }
    public uint ClassMask { get; set; }
    public uint RaceMask { get; set; }
    public uint UsableSlotMask { get; set; }
    public uint RequiredLevel { get; set; }
    public uint RecommendedLevel { get; set; }
    public uint Tradeskill { get; set; }
    public uint FoodDrinkValue { get; set; }

    // Stat adjustments
    public int PlusStrength { get; set; }
    public int PlusStamina { get; set; }
    public int PlusAgility { get; set; }
    public int PlusDexterity { get; set; }
    public int PlusCharisma { get; set; }
    public int PlusIntelligence { get; set; }
    public int PlusWisdom { get; set; }
    public int PlusHP { get; set; }
    public int PlusMana { get; set; }
    public int PlusEndurance { get; set; }
    public int PlusAC { get; set; }
    public int PlusAttack { get; set; }
    public int HpRegen { get; set; }
    public int ManaRegen { get; set; }

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
    public int SaveCold { get; set; }
    public int SaveDisease { get; set; }
    public int SavePoison { get; set; }
    public int SaveMagic { get; set; }
    public int SaveFire { get; set; }

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
    public uint BagSlots { get; set; }
    public uint BagContentSize { get; set; }
    public uint BagWeightReduction { get; set; }

    // Physical
    public uint Weight { get; set; }
    public uint Size { get; set; }
    public uint Cost { get; set; }

    // Effects granted by this item
    public List<ItemEffect> Effects { get; set; } = new List<ItemEffect>();
}