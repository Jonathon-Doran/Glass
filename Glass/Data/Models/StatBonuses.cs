using Glass.Core.Logging;

namespace Glass.Data.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// StatBonuses
//
// The bonuses one character receives on top of its base values.  Each item's bonuses are
// adjusted for that character as they are added, so every total is the effective amount.
// Every total starts at zero; no caps or stacking rules are applied.
///////////////////////////////////////////////////////////////////////////////////////////////
public class StatBonuses
{
    public int Strength { get; set; }
    public int Stamina { get; set; }
    public int Agility { get; set; }
    public int Dexterity { get; set; }
    public int Charisma { get; set; }
    public int Intelligence { get; set; }
    public int Wisdom { get; set; }

    public int HeroicStrength { get; set; }
    public int HeroicStamina { get; set; }
    public int HeroicAgility { get; set; }
    public int HeroicDexterity { get; set; }
    public int HeroicCharisma { get; set; }
    public int HeroicIntelligence { get; set; }
    public int HeroicWisdom { get; set; }

    public int HP { get; set; }
    public int Mana { get; set; }
    public int Endurance { get; set; }
    public int AC { get; set; }
    public int HpRegen { get; set; }
    public int ManaRegen { get; set; }

    public int SaveCold { get; set; }
    public int SaveDisease { get; set; }
    public int SavePoison { get; set; }
    public int SaveMagic { get; set; }
    public int SaveFire { get; set; }
    public int SaveCorruption { get; set; }

    public int Attack { get; set; }
    public int Haste { get; set; }
    public int HealAmount { get; set; }
    public int SpellDamage { get; set; }
    public int Clairvoyance { get; set; }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ScaleForRecommendedLevel
    //
    // Scales one bonus for a character below an item's recommended level.  The bonus is
    // multiplied by character level and divided by recommended level, rounding up to the next
    // whole value when the division is not exact.  A recommended level of 0, or a character at
    // or above it, leaves the bonus unchanged.
    //
    // value:             The bonus to scale.
    // characterLevel:    The level of the character wearing the item.
    // recommendedLevel:  The item's recommended level, 0 if it has none.
    //
    // Returns the bonus that applies to the character.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private static int ScaleForRecommendedLevel(int value, uint characterLevel, uint recommendedLevel)
    {
        if (recommendedLevel == 0 || characterLevel >= recommendedLevel)
        {
            return value;
        }

        long product = (long)value * characterLevel;

        if (product < 0)
        {
            return (int)(product / recommendedLevel);
        }

        return (int)((product + recommendedLevel - 1) / recommendedLevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AddItemBonuses
    //
    // Adds one item definition's bonuses to these totals, unadjusted.  Each total is increased
    // by the matching value on the item.
    //
    // record:  The item definition whose bonuses are added.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void AddItemBonuses(ItemRecord record)
    {
        Strength += record.PlusStrength;
        Stamina += record.PlusStamina;
        Agility += record.PlusAgility;
        Dexterity += record.PlusDexterity;
        Charisma += record.PlusCharisma;
        Intelligence += record.PlusIntelligence;
        Wisdom += record.PlusWisdom;

        HeroicStrength += (int)record.Heroic_Strength;
        HeroicStamina += (int)record.Heroic_Stamina;
        HeroicAgility += (int)record.Heroic_Agility;
        HeroicDexterity += (int)record.Heroic_Dexterity;
        HeroicCharisma += (int)record.Heroic_Charisma;
        HeroicIntelligence += (int)record.Heroic_Intelligence;
        HeroicWisdom += (int)record.Heroic_Wisdom;

        HP += record.PlusHP;
        Mana += record.PlusMana;
        Endurance += record.PlusEndurance;
        AC += record.PlusAC;
        HpRegen += record.HpRegen;
        ManaRegen += record.ManaRegen;

        SaveCold += record.SaveCold;
        SaveDisease += record.SaveDisease;
        SavePoison += record.SavePoison;
        SaveMagic += record.SaveMagic;
        SaveFire += record.SaveFire;
        SaveCorruption += record.SaveCorruption;

        Attack += record.PlusAttack;
        Haste += (int)record.Haste;
        HealAmount += (int)record.Heal_Amount;
        SpellDamage += (int)record.Spell_Damage;
        Clairvoyance += (int)record.Clairvoyance;

        DebugLog.Write(LogChannel.Inventory, "StatBonuses.AddItemBonuses: added '" + record.Name + "' (" +
            record.Id + "); totals now HP " + HP + ", mana " + Mana + ", AC " + AC, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AddScaledBonuses
    //
    // Adds another set of totals into these totals, adjusted for a character below the
    // recommended level that applies to them.  Base stats, HP, mana, endurance, AC, attack, and
    // resists are scaled down by the ratio of character level to recommended level, rounding up.
    // Heroic stats, regen, haste, heal amount, spell damage, and clairvoyance are added
    // unchanged.
    //
    // other:             The totals to add.
    // characterLevel:    The level of the character receiving the bonuses.
    // recommendedLevel:  The recommended level that applies to the totals, 0 if none.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void AddScaledBonuses(StatBonuses other, uint characterLevel, uint recommendedLevel)
    {
        if (recommendedLevel != 0 && characterLevel < recommendedLevel)
        {
            DebugLog.Write(LogChannel.Inventory, "StatBonuses.AddScaledBonuses: scaling for level " +
                characterLevel + " of recommended " + recommendedLevel + ", HP " + other.HP + " becomes " +
                ScaleForRecommendedLevel(other.HP, characterLevel, recommendedLevel), LogLevel.Trace);
        }
        else
        {
            DebugLog.Write(LogChannel.Inventory, "StatBonuses.AddScaledBonuses: no scaling at level " +
                characterLevel + " (recommended " + recommendedLevel + "), HP " + other.HP, LogLevel.Trace);
        }

        Strength += ScaleForRecommendedLevel(other.Strength, characterLevel, recommendedLevel);
        Stamina += ScaleForRecommendedLevel(other.Stamina, characterLevel, recommendedLevel);
        Agility += ScaleForRecommendedLevel(other.Agility, characterLevel, recommendedLevel);
        Dexterity += ScaleForRecommendedLevel(other.Dexterity, characterLevel, recommendedLevel);
        Charisma += ScaleForRecommendedLevel(other.Charisma, characterLevel, recommendedLevel);
        Intelligence += ScaleForRecommendedLevel(other.Intelligence, characterLevel, recommendedLevel);
        Wisdom += ScaleForRecommendedLevel(other.Wisdom, characterLevel, recommendedLevel);

        HeroicStrength += other.HeroicStrength;
        HeroicStamina += other.HeroicStamina;
        HeroicAgility += other.HeroicAgility;
        HeroicDexterity += other.HeroicDexterity;
        HeroicCharisma += other.HeroicCharisma;
        HeroicIntelligence += other.HeroicIntelligence;
        HeroicWisdom += other.HeroicWisdom;

        HP += ScaleForRecommendedLevel(other.HP, characterLevel, recommendedLevel);
        Mana += ScaleForRecommendedLevel(other.Mana, characterLevel, recommendedLevel);
        Endurance += ScaleForRecommendedLevel(other.Endurance, characterLevel, recommendedLevel);
        AC += ScaleForRecommendedLevel(other.AC, characterLevel, recommendedLevel);
        HpRegen += other.HpRegen;
        ManaRegen += other.ManaRegen;

        SaveCold += ScaleForRecommendedLevel(other.SaveCold, characterLevel, recommendedLevel);
        SaveDisease += ScaleForRecommendedLevel(other.SaveDisease, characterLevel, recommendedLevel);
        SavePoison += ScaleForRecommendedLevel(other.SavePoison, characterLevel, recommendedLevel);
        SaveMagic += ScaleForRecommendedLevel(other.SaveMagic, characterLevel, recommendedLevel);
        SaveFire += ScaleForRecommendedLevel(other.SaveFire, characterLevel, recommendedLevel);
        SaveCorruption += ScaleForRecommendedLevel(other.SaveCorruption, characterLevel, recommendedLevel);

        Attack += ScaleForRecommendedLevel(other.Attack, characterLevel, recommendedLevel);
        Haste += other.Haste;
        HealAmount += other.HealAmount;
        SpellDamage += other.SpellDamage;
        Clairvoyance += other.Clairvoyance;

        DebugLog.Write(LogChannel.Inventory, "StatBonuses.AddScaledBonuses: totals now HP " + HP +
            ", mana " + Mana + ", AC " + AC, LogLevel.Trace);
    }
}