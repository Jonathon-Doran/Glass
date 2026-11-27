using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Reference;

namespace Glass.Agents.Loadout;

///////////////////////////////////////////////////////////////////////////////////////////////
// LoadoutAdvisor
//
// Recommends spells for a character's loadout.  Recommendations are reported only; nothing
// is memorized or changed.
///////////////////////////////////////////////////////////////////////////////////////////////
public static class LoadoutAdvisor
{
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RankHeals
    //
    // Ranks the heal spells in a character's spellbook and writes three lists to the console,
    // ordered by HP per mana, by cast time, and by heal per second.  A spell is considered
    // when it is in the spell catalog, castable by the character's class at its level, not
    // Self only, and has a healing effect.  Spells dominated by another spell of the same
    // category are left out.
    //
    // character:  The character whose spellbook is ranked.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static void RankHeals(Character character)
    {
        List<HealSpellStats>? kept = CollectHeals(character, out uint measuredCount);
        if (kept == null)
        {
            DebugLog.Write(LogChannel.Healing, "LoadoutAdvisor.RankHeals: '" + character.Name +
                "' cannot be evaluated; nothing ranked", LogLevel.Warn);
            return;
        }

        if (character.Level.HasValue == false)
        {
            DebugLog.Write(LogChannel.Healing, "LoadoutAdvisor.RankHeals: '" + character.Name +
                "' has no level; nothing ranked", LogLevel.Warn);
            return;
        }

        uint level = character.Level.Value;

        List<HealSpellStats> byMana = new List<HealSpellStats>(kept);
        byMana.Sort((first, second) => second.HpPerMana.CompareTo(first.HpPerMana));

        List<HealSpellStats> byCastTime = new List<HealSpellStats>(kept);
        byCastTime.Sort((first, second) =>
        {
            int castOrder = first.Spell.CastTimeMs.CompareTo(second.Spell.CastTimeMs);
            if (castOrder != 0)
            {
                return castOrder;
            }

            return second.HealPerCast.CompareTo(first.HealPerCast);
        });

        List<HealSpellStats> byHealPerSecond = new List<HealSpellStats>(kept);
        byHealPerSecond.Sort((first, second) => second.HealPerSecond.CompareTo(first.HealPerSecond));

        DebugLog.Write(LogChannel.Console, "Heal spells for '" + character.Name + "', level " + level + " " +
            character.Class + ": " + kept.Count + " ranked of " + measuredCount + " heal spells", LogLevel.Info);

        List<KeyValuePair<string, List<HealSpellStats>>> orderings = new List<KeyValuePair<string, List<HealSpellStats>>>
        {
            new KeyValuePair<string, List<HealSpellStats>>("By HP per mana", byMana),
            new KeyValuePair<string, List<HealSpellStats>>("By speed (cast time)", byCastTime),
            new KeyValuePair<string, List<HealSpellStats>>("By heal per second", byHealPerSecond)
        };

        foreach (KeyValuePair<string, List<HealSpellStats>> ordering in orderings)
        {
            DebugLog.Write(LogChannel.Console, ordering.Key + ":", LogLevel.Info);

            foreach (HealSpellStats stats in ordering.Value)
            {
                DebugLog.Write(LogChannel.Console, "  " + stats.Spell.Name + ", " + stats.Category + ", " +
                    stats.HealPerCast + " HP per cast, " + stats.Spell.Mana + " mana, " +
                    stats.HpPerMana.ToString("F2") + " HP per mana, " + stats.HealPerSecond.ToString("F1") +
                    " HP per second, range " + stats.Spell.Range + " game units, cast " +
                    (stats.Spell.CastTimeMs / 1000.0).ToString("F1") + " s", LogLevel.Info);
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RemoveDominated
    //
    // Builds a list of the heal spells that no other spell of the same category dominates.  A
    // spell dominates another when its heal per cast, HP per mana, heal per second and range are each at least
    // as large and its cast time is at least as short, and at least one of these is strictly better.  Spells
    // that are equal on every number are both kept.
    //
    // measured:  The measured heal spells of one character.
    //
    // Returns the spells that are not dominated, in their original order.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static List<HealSpellStats> RemoveDominated(List<HealSpellStats> measured)
    {
        List<HealSpellStats> kept = new List<HealSpellStats>();

        foreach (HealSpellStats candidate in measured)
        {
            HealSpellStats? dominator = null;

            foreach (HealSpellStats other in measured)
            {
                if (ReferenceEquals(other, candidate) == true || other.Category != candidate.Category)
                {
                    continue;
                }

                bool atLeastAsGood = other.HealPerCast >= candidate.HealPerCast &&
                    other.HpPerMana >= candidate.HpPerMana &&
                    other.HealPerSecond >= candidate.HealPerSecond &&
                    other.Spell.CastTimeMs <= candidate.Spell.CastTimeMs &&
                    other.Spell.Range >= candidate.Spell.Range;

                bool better = other.HealPerCast > candidate.HealPerCast ||
                    other.HpPerMana > candidate.HpPerMana ||
                    other.HealPerSecond > candidate.HealPerSecond ||
                    other.Spell.CastTimeMs < candidate.Spell.CastTimeMs ||
                    other.Spell.Range > candidate.Spell.Range;

                if (atLeastAsGood == true && better == true)
                {
                    dominator = other;
                    break;
                }
            }

            if (dominator != null)
            {
                DebugLog.Write(LogChannel.Healing, "LoadoutAdvisor.RemoveDominated: '" + candidate.Spell.Name +
                    "' (" + candidate.Spell.Id + ") " + candidate.Category + " dominated by '" +
                    dominator.Spell.Name + "' (" + dominator.Spell.Id + ")", defaultLoglevel);
                continue;
            }

            kept.Add(candidate);
            DebugLog.Write(LogChannel.Healing, "LoadoutAdvisor.RemoveDominated: '" + candidate.Spell.Name +
                "' (" + candidate.Spell.Id + ") " + candidate.Category + " kept", defaultLoglevel);
        }

        DebugLog.Write(LogChannel.Healing, "LoadoutAdvisor.RemoveDominated: kept " + kept.Count + " of " +
            measured.Count + " heal spells", defaultLoglevel);

        return kept;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CollectHeals
    //
    // Builds the list of heal spells worth considering from a character's spellbook.  A spell
    // is considered when it is in the spell catalog, castable by the character's class at its
    // level, not Self only, and has a healing effect.  Spells dominated by another spell of the
    // same category are left out.  A character with no level, a class with no spell levels, or
    // an empty spellbook cannot be evaluated, and the reason is written to the console.
    //
    // character:      The character whose spellbook is examined.
    // measuredCount:  Receives the number of heal spells measured before pruning.
    //
    // Returns the heal spells kept, or null when the character cannot be evaluated.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static List<HealSpellStats>? CollectHeals(Character character, out uint measuredCount)
    {
        measuredCount = 0;

        if (character.Level.HasValue == false)
        {
            DebugLog.Write(LogChannel.Console, "'" + character.Name + "' has no level; heals cannot be evaluated",
                LogLevel.Info);
            DebugLog.Write(LogChannel.Healing, "LoadoutAdvisor.CollectHeals: '" + character.Name +
                "' has no level", LogLevel.Warn);
            return null;
        }

        uint level = character.Level.Value;
        uint classValue = (uint)character.Class;

        if (classValue == 0 || classValue > SpellRecord.ClassCount)
        {
            DebugLog.Write(LogChannel.Console, "'" + character.Name + "' has class " + character.Class +
                ", which has no spell levels; heals cannot be evaluated", LogLevel.Info);
            DebugLog.Write(LogChannel.Healing, "LoadoutAdvisor.CollectHeals: '" + character.Name +
                "' class value " + classValue + " is outside the spell class levels", LogLevel.Warn);
            return null;
        }

        uint classIndex = classValue - 1;

        if (character.SpellBook.Length == 0)
        {
            DebugLog.Write(LogChannel.Console, "'" + character.Name + "' has an empty spellbook; it is filled " +
                "from the player profile", LogLevel.Info);
            DebugLog.Write(LogChannel.Healing, "LoadoutAdvisor.CollectHeals: '" + character.Name +
                "' spellbook is empty", LogLevel.Warn);
            return null;
        }

        List<HealSpellStats> measured = new List<HealSpellStats>();
        uint missing = 0;
        uint notCastable = 0;
        uint selfOnly = 0;
        uint notHealing = 0;

        foreach (SpellId spellId in character.SpellBook)
        {
            if (SpellCatalog.Instance.TryGet(spellId, out SpellRecord? record) == false)
            {
                missing++;
                DebugLog.Write(LogChannel.Healing, "LoadoutAdvisor.CollectHeals: '" + character.Name +
                    "' spellbook spell " + spellId + " is not in the spell catalog", LogLevel.Warn);
                continue;
            }

            byte classLevel = record.ClassLevels[classIndex];
            if (classLevel == SpellRecord.LevelUnusable || classLevel > level)
            {
                notCastable++;
                DebugLog.Write(LogChannel.Healing, "LoadoutAdvisor.CollectHeals: '" + record.Name + "' (" +
                    record.Id + ") not castable by " + character.Class + " at level " + level, defaultLoglevel);
                continue;
            }

            if (record.TargetType == SpellTargetType.Self)
            {
                selfOnly++;
                DebugLog.Write(LogChannel.Healing, "LoadoutAdvisor.CollectHeals: '" + record.Name + "' (" +
                    record.Id + ") is Self only", defaultLoglevel);
                continue;
            }

            HealSpellStats? stats = HealSpellStats.Measure(record, level);
            if (stats == null)
            {
                notHealing++;
                continue;
            }

            measured.Add(stats);
        }

        DebugLog.Write(LogChannel.Healing, "LoadoutAdvisor.CollectHeals: '" + character.Name + "' spellbook " +
            character.SpellBook.Length + " spells: " + missing + " not in catalog, " + notCastable +
            " not castable, " + selfOnly + " Self only, " + notHealing + " not healing, " +
            measured.Count + " heal spells", defaultLoglevel);

        measuredCount = (uint)measured.Count;
        return RemoveDominated(measured);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RecommendHeals
    //
    // Recommends heal spells for a character's loadout and writes them to the console.  The
    // main heal is the single-target direct heal with the highest HP per mana.  The emergency
    // heal is the Fast heal with the largest heal per cast.  The general heal is the remaining
    // single-target direct heal with the highest heal per second.  The HoT is the HoT with the
    // highest HP per mana.  Group heals and regeneration are not recommended.  Each pick is
    // written with the rule that chose it.  Nothing is memorized or changed.
    //
    // character:  The character whose spellbook is examined.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static void RecommendHeals(Character character)
    {
        List<HealSpellStats>? kept = CollectHeals(character, out uint measuredCount);
        if (kept == null)
        {
            DebugLog.Write(LogChannel.Healing, "LoadoutAdvisor.RecommendHeals: '" + character.Name +
                "' cannot be evaluated; nothing recommended", LogLevel.Warn);
            return;
        }


        if (character.Level.HasValue == false)
        {
            DebugLog.Write(LogChannel.Healing, "LoadoutAdvisor.RecommendHeals: '" + character.Name +
                "' has no level; nothing recommended", LogLevel.Warn);
            return;
        }

        uint level = character.Level.Value;

        HealSpellStats? mainHeal = null;
        HealSpellStats? emergencyHeal = null;
        HealSpellStats? healOverTime = null;

        foreach (HealSpellStats stats in kept)
        {
            switch (stats.Category)
            {
                case HealCategory.Fast:
                case HealCategory.Efficient:
                    if (mainHeal == null || stats.HpPerMana > mainHeal.HpPerMana)
                    {
                        mainHeal = stats;
                    }

                    if (stats.Category == HealCategory.Fast &&
                        (emergencyHeal == null || stats.HealPerCast > emergencyHeal.HealPerCast))
                    {
                        emergencyHeal = stats;
                    }
                    break;

                case HealCategory.HoT:
                    if (healOverTime == null || stats.HpPerMana > healOverTime.HpPerMana)
                    {
                        healOverTime = stats;
                    }
                    break;

                default:
                    DebugLog.Write(LogChannel.Healing, "LoadoutAdvisor.RecommendHeals: '" + stats.Spell.Name +
                        "' is " + stats.Category + ", not considered by the recommendation", defaultLoglevel);
                    break;
            }
        }

        HealSpellStats? generalHeal = null;
        foreach (HealSpellStats stats in kept)
        {
            if (stats.Category != HealCategory.Fast && stats.Category != HealCategory.Efficient)
            {
                continue;
            }

            if (ReferenceEquals(stats, mainHeal) == true || ReferenceEquals(stats, emergencyHeal) == true)
            {
                continue;
            }

            if (generalHeal == null || stats.HealPerSecond > generalHeal.HealPerSecond)
            {
                generalHeal = stats;
            }
        }

        List<(string Role, string Rule, HealSpellStats? Pick)> picks = new List<(string Role, string Rule, HealSpellStats? Pick)>
        {
            ("Main heal", "highest HP per mana among single-target direct heals", mainHeal),
            ("Emergency heal", "largest heal per cast among Fast heals", emergencyHeal),
            ("General heal", "highest HP per second among the other single-target direct heals", generalHeal),
            ("HoT", "highest HP per mana among HoTs", healOverTime)
        };

        DebugLog.Write(LogChannel.Console, "Recommended heals for '" + character.Name + "', level " +
            level + " " + character.Class + ", from " + kept.Count + " of " + measuredCount +
            " heal spells:", LogLevel.Info);

        foreach ((string Role, string Rule, HealSpellStats? Pick) pick in picks)
        {
            if (pick.Pick == null)
            {
                DebugLog.Write(LogChannel.Console, "  " + pick.Role + ": none (" + pick.Rule + ")", LogLevel.Info);
                DebugLog.Write(LogChannel.Healing, "LoadoutAdvisor.RecommendHeals: '" + character.Name +
                    "' has no " + pick.Role, defaultLoglevel);
                continue;
            }

            HealSpellStats stats = pick.Pick;
            DebugLog.Write(LogChannel.Console, "  " + pick.Role + ": " + stats.Spell.Name + " (" + pick.Rule + "), " +
                stats.HealPerCast + " HP per cast, " + stats.Spell.Mana + " mana, " +
                stats.HpPerMana.ToString("F2") + " HP per mana, " + stats.HealPerSecond.ToString("F1") +
                " HP per second, cast " + (stats.Spell.CastTimeMs / 1000.0).ToString("F1") + " s", LogLevel.Info);
            DebugLog.Write(LogChannel.Healing, "LoadoutAdvisor.RecommendHeals: '" + character.Name + "' " +
                pick.Role + " is '" + stats.Spell.Name + "' (" + stats.Spell.Id + ")", defaultLoglevel);
        }
    }
}