using Glass.Core.Logging;
using Glass.World;

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

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SpellEffectValue
    //
    // Computes the value of one spell effect for a caster of the given level.  The magnitude of
    // Base1 is scaled by the Calc formula: 0 and 100 leave it unchanged, 1 through 99 add the
    // caster level times the Calc value, and 101 through 105 add half the level, the level,
    // twice, three times, or four times the level.  A nonzero Max caps the scaled magnitude.
    // The result takes the sign of Base1.  For any other Calc value, a nonzero Max is used with
    // the sign of Base1; with no Max, Base1 is used and the unsupported Calc value is logged.
    //
    // effect:       The spell effect to evaluate.
    // casterLevel:  Level of the caster of the spell.
    //
    // Returns the effect's value.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private static int SpellEffectValue(SpellEffect effect, uint casterLevel)
    {
        int level = (int)casterLevel;
        int magnitude = Math.Abs(effect.Base1);
        int scaled;

        if (effect.Calc == 0 || effect.Calc == 100)
        {
            scaled = magnitude;
        }
        else if (effect.Calc >= 1 && effect.Calc <= 99)
        {
            scaled = magnitude + level * (int)effect.Calc;
        }
        else if (effect.Calc == 101)
        {
            scaled = magnitude + level / 2;
        }
        else if (effect.Calc == 102)
        {
            scaled = magnitude + level;
        }
        else if (effect.Calc == 103)
        {
            scaled = magnitude + 2 * level;
        }
        else if (effect.Calc == 104)
        {
            scaled = magnitude + 3 * level;
        }
        else if (effect.Calc == 105)
        {
            scaled = magnitude + 4 * level;
        }
        else if (effect.Max != 0)
        {
            int fallback = effect.Base1 < 0 ? -Math.Abs(effect.Max) : Math.Abs(effect.Max);
            DebugLog.Write(LogChannel.Fields, "StatBonuses.SpellEffectValue: SPA " + effect.Spa +
                " calc " + effect.Calc + " not supported at caster level " + casterLevel +
                "; using max, value " + fallback, LogLevel.Warn);
            return fallback;
        }
        else
        {
            DebugLog.Write(LogChannel.Fields, "StatBonuses.SpellEffectValue: SPA " + effect.Spa +
                " calc " + effect.Calc + " not supported at caster level " + casterLevel +
                "; using base " + effect.Base1, LogLevel.Warn);
            return effect.Base1;
        }

        if (effect.Max != 0 && scaled > Math.Abs(effect.Max))
        {
            scaled = Math.Abs(effect.Max);
        }

        int value = effect.Base1 < 0 ? -scaled : scaled;

        DebugLog.Write(LogChannel.Fields, "StatBonuses.SpellEffectValue: SPA " + effect.Spa + " calc " +
            effect.Calc + " base " + effect.Base1 + " max " + effect.Max + " at caster level " +
            casterLevel + ", value " + value, LogLevel.Trace);

        return value;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AddSpellBonuses
    //
    // Adds the stat effects of one spell to these totals.  Each effect's value is computed for
    // the given caster level and added to the matching total: maximum HP, maximum mana,
    // maximum endurance, AC, attack, the seven base stats, and the six resists.  Effects with
    // any other SPA are skipped.
    //
    // record:       The spell whose effects are added.
    // casterLevel:  Level of the caster of the spell.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void AddSpellBonuses(SpellRecord record, uint casterLevel)
    {
        uint applied = 0;
        uint skipped = 0;

        foreach (SpellEffect effect in record.Effects)
        {
            switch (effect.Spa)
            {
                case SPAId.MaxHitpoints:
                    HP += SpellEffectValue(effect, casterLevel);
                    break;
                case SPAId.MaxMana:
                    Mana += SpellEffectValue(effect, casterLevel);
                    break;
                case SPAId.MaxEndurance:
                    Endurance += SpellEffectValue(effect, casterLevel);
                    break;
                case SPAId.ArmorClass:
                    AC += SpellEffectValue(effect, casterLevel);
                    break;
                case SPAId.AttackPower:
                    Attack += SpellEffectValue(effect, casterLevel);
                    break;
                case SPAId.Strength:
                    Strength += SpellEffectValue(effect, casterLevel);
                    break;
                case SPAId.Stamina:
                    Stamina += SpellEffectValue(effect, casterLevel);
                    break;
                case SPAId.Agility:
                    Agility += SpellEffectValue(effect, casterLevel);
                    break;
                case SPAId.Dexterity:
                    Dexterity += SpellEffectValue(effect, casterLevel);
                    break;
                case SPAId.Charisma:
                    Charisma += SpellEffectValue(effect, casterLevel);
                    break;
                case SPAId.Intelligence:
                    Intelligence += SpellEffectValue(effect, casterLevel);
                    break;
                case SPAId.Wisdom:
                    Wisdom += SpellEffectValue(effect, casterLevel);
                    break;
                case SPAId.ResistCold:
                    SaveCold += SpellEffectValue(effect, casterLevel);
                    break;
                case SPAId.ResistDisease:
                    SaveDisease += SpellEffectValue(effect, casterLevel);
                    break;
                case SPAId.ResistPoison:
                    SavePoison += SpellEffectValue(effect, casterLevel);
                    break;
                case SPAId.ResistMagic:
                    SaveMagic += SpellEffectValue(effect, casterLevel);
                    break;
                case SPAId.ResistFire:
                    SaveFire += SpellEffectValue(effect, casterLevel);
                    break;
                case SPAId.ResistCorruption:
                    SaveCorruption += SpellEffectValue(effect, casterLevel);
                    break;
                default:
                    skipped++;
                    continue;
            }

            applied++;
        }

        if (skipped > 0)
        {
            DebugLog.Write(LogChannel.Fields, "StatBonuses:  skipped " + skipped + " effects", LogLevel.Warn);
        }

        DebugLog.Write(LogChannel.Fields, "StatBonuses.AddSpellBonuses: '" + record.Name + "' (" +
            record.Id + ") at caster level " + casterLevel + ": " + applied + " effects applied, " +
            skipped + " skipped; totals now HP " + HP + ", AC " + AC, LogLevel.Trace);
    }
}