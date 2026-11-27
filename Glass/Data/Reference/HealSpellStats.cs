using Glass.Core.Logging;
using Glass.Data.Models;

namespace Glass.Data.Reference;

///////////////////////////////////////////////////////////////////////////////////////////////
// HealCategory
//
// The kinds of HP restoration spells.  Fast heals cast quickly at a poor mana cost.
// Efficient heals cast slowly at a good mana cost.  HoTs heal each tick for a short
// duration.  Regeneration heals each tick for a long duration at a low rate.  Group heals
// land on several characters.
///////////////////////////////////////////////////////////////////////////////////////////////
public enum HealCategory
{
    Fast,
    Efficient,
    HoT,
    Regeneration,
    Group
}

///////////////////////////////////////////////////////////////////////////////////////////////
// HealSpellStats
//
// The healing numbers and category of one spell for a caster of a given level.  Amounts are
// the sum of the spell's positive healing effect values scaled to the caster level.
///////////////////////////////////////////////////////////////////////////////////////////////
public class HealSpellStats
{
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    private const double TickSeconds = 6.0;

    // A direct heal with a cast time below this is Fast.
    private const uint FastCastLimitMs = 3000;

    // A duration heal longer than this many ticks (2 minutes) is Regeneration.
    private const uint HotMaximumTicks = 20;

    public SpellRecord Spell { get; }
    public uint CasterLevel { get; }
    public HealCategory Category { get; }

    // Total healing of one cast.  For a duration heal, the per tick amount times the tick count.
    public uint HealPerCast { get; }

    // Healing of one tick.  Zero for a heal with no duration.
    public uint HealPerTick { get; }

    // Healing per point of mana.  Infinity when the spell costs no mana.
    public double HpPerMana { get; }

    // No duration: healing per second of the longer of cast time and recast time, infinity
    // when both are zero.  With a duration: the per tick amount over the tick length.
    public double HealPerSecond { get; }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HealSpellStats (constructor)
    //
    // Stores the computed numbers.
    //
    // spell:          The spell measured.
    // casterLevel:    The caster level the numbers were computed for.
    // category:       The kind of heal.
    // healPerCast:    Total healing of one cast.
    // healPerTick:    Healing of one tick, zero with no duration.
    // hpPerMana:      Healing per point of mana.
    // healPerSecond:  Healing rate.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private HealSpellStats(SpellRecord spell, uint casterLevel, HealCategory category, uint healPerCast,
        uint healPerTick, double hpPerMana, double healPerSecond)
    {
        Spell = spell;
        CasterLevel = casterLevel;
        Category = category;
        HealPerCast = healPerCast;
        HealPerTick = healPerTick;
        HpPerMana = hpPerMana;
        HealPerSecond = healPerSecond;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Measure
    //
    // Computes the healing numbers and category of a spell for a caster of the given level.
    // The positive values of the Hitpoints, HealOverTime, CompleteHeal and PercentHeal effects
    // at the caster level are summed.  A spell with a duration heals that sum once per tick for
    // DurationCapTicks ticks; a spell without a duration heals the sum once.  The category is
    // Group for a group target type, Regeneration or HoT for a duration longer or not longer
    // than the HoT limit, and Fast or Efficient for a cast time below or not below the fast
    // limit.
    //
    // record:       The spell to measure.
    // casterLevel:  The level of the caster.
    //
    // Returns the numbers, or null when the spell has no healing effect.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static HealSpellStats? Measure(SpellRecord record, uint casterLevel)
    {
        uint healSum = 0;

        foreach (SpellEffect effect in record.Effects)
        {
            if (effect.Spa != SPAId.Hitpoints && effect.Spa != SPAId.HealOverTime &&
                effect.Spa != SPAId.CompleteHeal && effect.Spa != SPAId.PercentHeal)
            {
                continue;
            }

            int value = StatBonuses.SpellEffectValue(effect, casterLevel);
            if (value <= 0)
            {
                DebugLog.Write(LogChannel.Healing, "HealSpellStats.Measure: '" + record.Name + "' (" +
                    record.Id + ") slot " + effect.Slot + " SPA " + effect.Spa + " value " + value +
                    " is not healing; skipped", defaultLoglevel);
                continue;
            }

            if (effect.Spa == SPAId.PercentHeal)
            {
                DebugLog.Write(LogChannel.Healing, "HealSpellStats.Measure: '" + record.Name + "' (" +
                    record.Id + ") slot " + effect.Slot + " PercentHeal value " + value +
                    " may be a percentage rather than HP", LogLevel.Warn);
            }

            healSum += (uint)value;
            DebugLog.Write(LogChannel.Healing, "HealSpellStats.Measure: '" + record.Name + "' (" +
                record.Id + ") slot " + effect.Slot + " SPA " + effect.Spa + " heals " + value +
                ", sum " + healSum, defaultLoglevel);
        }

        if (healSum == 0)
        {
            DebugLog.Write(LogChannel.Healing, "HealSpellStats.Measure: '" + record.Name + "' (" +
                record.Id + ") has no healing effect", defaultLoglevel);
            return null;
        }

        uint healPerCast;
        uint healPerTick;
        double healPerSecond;

        if (record.DurationCapTicks > 0)
        {
            healPerTick = healSum;
            healPerCast = healSum * record.DurationCapTicks;
            healPerSecond = healSum / TickSeconds;
            DebugLog.Write(LogChannel.Healing, "HealSpellStats.Measure: '" + record.Name + "' (" +
                record.Id + ") heals " + healPerTick + " per tick for " + record.DurationCapTicks +
                " ticks", defaultLoglevel);
        }
        else
        {
            healPerTick = 0;
            healPerCast = healSum;
            uint cycleMs = Math.Max(record.CastTimeMs, record.RecastTimeMs);

            if (cycleMs == 0)
            {
                healPerSecond = double.PositiveInfinity;
                DebugLog.Write(LogChannel.Healing, "HealSpellStats.Measure: '" + record.Name + "' (" +
                    record.Id + ") has no cast or recast time; rate is infinite", LogLevel.Warn);
            }
            else
            {
                healPerSecond = healPerCast * 1000.0 / cycleMs;
                DebugLog.Write(LogChannel.Healing, "HealSpellStats.Measure: '" + record.Name + "' (" +
                    record.Id + ") heals " + healPerCast + " once, cycle " + cycleMs + " ms", defaultLoglevel);
            }
        }

        double hpPerMana;
        if (record.Mana == 0)
        {
            hpPerMana = double.PositiveInfinity;
            DebugLog.Write(LogChannel.Healing, "HealSpellStats.Measure: '" + record.Name + "' (" +
                record.Id + ") costs no mana; HP per mana is infinite", defaultLoglevel);
        }
        else
        {
            hpPerMana = (double)healPerCast / record.Mana;
        }

        HealCategory category;
        if (record.TargetType == SpellTargetType.CasterGroup || record.TargetType == SpellTargetType.TargetGroup ||
            record.TargetType == SpellTargetType.NearbyPlayers)
        {
            category = HealCategory.Group;
        }
        else if (record.DurationCapTicks > HotMaximumTicks)
        {
            category = HealCategory.Regeneration;
        }
        else if (record.DurationCapTicks > 0)
        {
            category = HealCategory.HoT;
        }
        else if (record.CastTimeMs < FastCastLimitMs)
        {
            category = HealCategory.Fast;
        }
        else
        {
            category = HealCategory.Efficient;
        }

        DebugLog.Write(LogChannel.Healing, "HealSpellStats.Measure: '" + record.Name + "' (" +
            record.Id + ") at level " + casterLevel + ": " + category + ", target " + record.TargetType +
            ", " + healPerCast + " per cast, " + hpPerMana.ToString("F2") + " HP per mana, " +
            healPerSecond.ToString("F1") + " per second", defaultLoglevel);

        return new HealSpellStats(record, casterLevel, category, healPerCast, healPerTick, hpPerMana,
            healPerSecond);
    }
}