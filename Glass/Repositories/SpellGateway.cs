using Glass.Core.Logging;
using Glass.World;
using Microsoft.Data.Sqlite;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// SpellGateway
//
// Database interface for spell records and their effects, class levels, and reagents.  Nothing is
// cached here; every read and write goes to the SpellRecords, SpellEffects, SpellClassLevels, and
// SpellReagents tables.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class SpellGateway
{
    private static SpellGateway? _instance = null;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // Lazy singleton accessor.  The instance is created on first access.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static SpellGateway Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new SpellGateway();
            }
            return _instance;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SpellGateway
    //
    // Private constructor.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private SpellGateway()
    {
        DebugLog.Write(LogChannel.Database, "SpellGateway: singleton instance created.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ReadSpell
    //
    // Reads one spell with its effects, class levels, and reagents from the database.  Effects are
    // returned in slot order.  A class with no stored level is given SpellRecord.LevelUnusable.  A
    // reagent position with no stored item is given -1.
    //
    // spellId:  The spell to read.
    //
    // Returns the spell, or null when the id is SpellId.None or no row has that id.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public SpellRecord? ReadSpell(SpellId spellId)
    {
        if (!spellId.Exists)
        {
            DebugLog.Write(LogChannel.Database, "SpellGateway.ReadSpell: called with no id.", LogLevel.Warn);
            return null;
        }

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        SpellRecord record = new SpellRecord();

        using (SqliteCommand spellCmd = conn.CreateCommand())
        {
            spellCmd.CommandText = @"
                SELECT name, cast_range, cast_time_ms, recast_time_ms, duration_formula, duration_cap_ticks,
                       mana, primary_category, secondary_category, secondary_category_2, target_type,
                       cast_restriction
                FROM SpellRecords
                WHERE id = @id";
            spellCmd.Parameters.AddWithValue("@id", spellId.Value);

            using SqliteDataReader spellReader = spellCmd.ExecuteReader();
            if (!spellReader.Read())
            {
                DebugLog.Write(LogChannel.Database, "SpellGateway.ReadSpell: spell " + spellId +
                    " is not stored.", LogLevel.Trace);
                return null;
            }

            record.Id = spellId;
            record.Name = spellReader.GetString(0);
            record.Range = (uint)spellReader.GetInt64(1);
            record.CastTimeMs = (uint)spellReader.GetInt64(2);
            record.RecastTimeMs = (uint)spellReader.GetInt64(3);
            record.DurationFormula = (uint)spellReader.GetInt64(4);
            record.DurationCapTicks = (uint)spellReader.GetInt64(5);
            record.Mana = (uint)spellReader.GetInt64(6);
            record.PrimaryCategory = (SpellCategoryId)(uint)spellReader.GetInt64(7);
            record.SecondaryCategory = (SpellCategoryId)(uint)spellReader.GetInt64(8);
            record.SecondaryCategory2 = (SpellCategoryId)(uint)spellReader.GetInt64(9);
            record.TargetType = (SpellTargetType)(uint)spellReader.GetInt64(10);
            record.CastRestriction = (SpellCastRestriction)(uint)spellReader.GetInt64(11);
        }

        using (SqliteCommand effectCmd = conn.CreateCommand())
        {
            effectCmd.CommandText = @"
                SELECT slot, spa, base1, base2, calc, max_value
                FROM SpellEffects
                WHERE spell_id = @spell_id
                ORDER BY slot";
            effectCmd.Parameters.AddWithValue("@spell_id", spellId.Value);

            List<SpellEffect> effects = new List<SpellEffect>();
            using SqliteDataReader effectReader = effectCmd.ExecuteReader();
            while (effectReader.Read())
            {
                SpellEffect effect = new SpellEffect();
                effect.Slot = (uint)effectReader.GetInt64(0);
                effect.Spa = (SPAId)effectReader.GetInt32(1);
                effect.Base1 = effectReader.GetInt32(2);
                effect.Base2 = effectReader.GetInt32(3);
                effect.Calc = (uint)effectReader.GetInt64(4);
                effect.Max = effectReader.GetInt32(5);
                effects.Add(effect);
            }
            record.Effects = effects.ToArray();
        }

        for (uint classIndex = 0; classIndex < SpellRecord.ClassCount; classIndex++)
        {
            record.ClassLevels[classIndex] = SpellRecord.LevelUnusable;
        }

        using (SqliteCommand classCmd = conn.CreateCommand())
        {
            classCmd.CommandText = "SELECT class, level FROM SpellClassLevels WHERE spell_id = @spell_id";
            classCmd.Parameters.AddWithValue("@spell_id", spellId.Value);

            using SqliteDataReader classReader = classCmd.ExecuteReader();
            while (classReader.Read())
            {
                uint classId = (uint)classReader.GetInt64(0);
                if ((classId < 1) || (classId > SpellRecord.ClassCount))
                {
                    DebugLog.Write(LogChannel.Database, "SpellGateway.ReadSpell: spell " + spellId +
                        " has a stored class " + classId + " outside 1 to " + SpellRecord.ClassCount +
                        ", ignored.", LogLevel.Warn);
                    continue;
                }

                record.ClassLevels[classId - 1] = (byte)classReader.GetInt64(1);
            }
        }

        for (uint position = 0; position < 4; position++)
        {
            record.ReagentIds[position] = -1;
            record.ReagentCounts[position] = 0;
            record.NoExpendReagentIds[position] = -1;
        }

        using (SqliteCommand reagentCmd = conn.CreateCommand())
        {
            reagentCmd.CommandText = @"
                SELECT expended, position, item_id, quantity
                FROM SpellReagents
                WHERE spell_id = @spell_id";
            reagentCmd.Parameters.AddWithValue("@spell_id", spellId.Value);

            using SqliteDataReader reagentReader = reagentCmd.ExecuteReader();
            while (reagentReader.Read())
            {
                bool expended = reagentReader.GetInt64(0) != 0;
                uint position = (uint)reagentReader.GetInt64(1);
                if (position >= 4)
                {
                    DebugLog.Write(LogChannel.Database, "SpellGateway.ReadSpell: spell " + spellId +
                        " has a stored reagent position " + position + " outside 0 to 3, ignored.",
                        LogLevel.Warn);
                    continue;
                }

                if (expended == true)
                {
                    record.ReagentIds[position] = reagentReader.GetInt32(2);
                    record.ReagentCounts[position] = (uint)reagentReader.GetInt64(3);
                }
                else
                {
                    record.NoExpendReagentIds[position] = reagentReader.GetInt32(2);
                }
            }
        }

        DebugLog.Write(LogChannel.Database, "SpellGateway.ReadSpell: read spell " + spellId + " '" +
            record.Name + "' with " + record.Effects.Length + " effects.", LogLevel.Trace);
        return record;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // StoreSpells
    //
    // Replaces every stored spell with the given set in one transaction.  All rows of the four spell
    // tables are deleted, then each spell is inserted with one SpellEffects row per effect, one
    // SpellClassLevels row per class that can cast it, and one SpellReagents row per reagent position
    // that holds an item.  A reagent item id below zero means the position is empty.  A failure rolls
    // the transaction back and leaves the previous rows in place.
    //
    // spells:  The spells to store.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void StoreSpells(IReadOnlyCollection<SpellRecord> spells)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteTransaction tx = conn.BeginTransaction();
        try
        {
            using (SqliteCommand deleteCmd = conn.CreateCommand())
            {
                deleteCmd.Transaction = tx;
                deleteCmd.CommandText = @"
                    DELETE FROM SpellReagents;
                    DELETE FROM SpellClassLevels;
                    DELETE FROM SpellEffects;
                    DELETE FROM SpellRecords;";
                int deleted = deleteCmd.ExecuteNonQuery();
                DebugLog.Write(LogChannel.Database, "SpellGateway.StoreSpells: deleted " + deleted +
                    " existing rows.", LogLevel.Trace);
            }

            using SqliteCommand spellCmd = conn.CreateCommand();
            spellCmd.Transaction = tx;
            spellCmd.CommandText = @"
                INSERT INTO SpellRecords (
                    id, name, cast_range, cast_time_ms, recast_time_ms, duration_formula, duration_cap_ticks,
                    mana, primary_category, secondary_category, secondary_category_2, target_type,
                    cast_restriction
                ) VALUES (
                    @id, @name, @cast_range, @cast_time_ms, @recast_time_ms, @duration_formula,
                    @duration_cap_ticks, @mana, @primary_category, @secondary_category, @secondary_category_2,
                    @target_type, @cast_restriction
                )";
            spellCmd.Parameters.Add("@id", SqliteType.Integer);
            spellCmd.Parameters.Add("@name", SqliteType.Text);
            spellCmd.Parameters.Add("@cast_range", SqliteType.Integer);
            spellCmd.Parameters.Add("@cast_time_ms", SqliteType.Integer);
            spellCmd.Parameters.Add("@recast_time_ms", SqliteType.Integer);
            spellCmd.Parameters.Add("@duration_formula", SqliteType.Integer);
            spellCmd.Parameters.Add("@duration_cap_ticks", SqliteType.Integer);
            spellCmd.Parameters.Add("@mana", SqliteType.Integer);
            spellCmd.Parameters.Add("@primary_category", SqliteType.Integer);
            spellCmd.Parameters.Add("@secondary_category", SqliteType.Integer);
            spellCmd.Parameters.Add("@secondary_category_2", SqliteType.Integer);
            spellCmd.Parameters.Add("@target_type", SqliteType.Integer);
            spellCmd.Parameters.Add("@cast_restriction", SqliteType.Integer);

            using SqliteCommand effectCmd = conn.CreateCommand();
            effectCmd.Transaction = tx;
            effectCmd.CommandText = @"
                INSERT INTO SpellEffects (spell_id, slot, spa, base1, base2, calc, max_value)
                VALUES (@spell_id, @slot, @spa, @base1, @base2, @calc, @max_value)";
            effectCmd.Parameters.Add("@spell_id", SqliteType.Integer);
            effectCmd.Parameters.Add("@slot", SqliteType.Integer);
            effectCmd.Parameters.Add("@spa", SqliteType.Integer);
            effectCmd.Parameters.Add("@base1", SqliteType.Integer);
            effectCmd.Parameters.Add("@base2", SqliteType.Integer);
            effectCmd.Parameters.Add("@calc", SqliteType.Integer);
            effectCmd.Parameters.Add("@max_value", SqliteType.Integer);

            using SqliteCommand classCmd = conn.CreateCommand();
            classCmd.Transaction = tx;
            classCmd.CommandText = @"
                INSERT INTO SpellClassLevels (spell_id, class, level)
                VALUES (@spell_id, @class, @level)";
            classCmd.Parameters.Add("@spell_id", SqliteType.Integer);
            classCmd.Parameters.Add("@class", SqliteType.Integer);
            classCmd.Parameters.Add("@level", SqliteType.Integer);

            using SqliteCommand reagentCmd = conn.CreateCommand();
            reagentCmd.Transaction = tx;
            reagentCmd.CommandText = @"
                INSERT INTO SpellReagents (spell_id, expended, position, item_id, quantity)
                VALUES (@spell_id, @expended, @position, @item_id, @quantity)";
            reagentCmd.Parameters.Add("@spell_id", SqliteType.Integer);
            reagentCmd.Parameters.Add("@expended", SqliteType.Integer);
            reagentCmd.Parameters.Add("@position", SqliteType.Integer);
            reagentCmd.Parameters.Add("@item_id", SqliteType.Integer);
            reagentCmd.Parameters.Add("@quantity", SqliteType.Integer);

            uint effectRows = 0;
            uint classRows = 0;
            uint reagentRows = 0;

            foreach (SpellRecord spell in spells)
            {
                uint spellId = spell.Id.Value;

                spellCmd.Parameters["@id"].Value = spellId;
                spellCmd.Parameters["@name"].Value = spell.Name;
                spellCmd.Parameters["@cast_range"].Value = spell.Range;
                spellCmd.Parameters["@cast_time_ms"].Value = spell.CastTimeMs;
                spellCmd.Parameters["@recast_time_ms"].Value = spell.RecastTimeMs;
                spellCmd.Parameters["@duration_formula"].Value = spell.DurationFormula;
                spellCmd.Parameters["@duration_cap_ticks"].Value = spell.DurationCapTicks;
                spellCmd.Parameters["@mana"].Value = spell.Mana;
                spellCmd.Parameters["@primary_category"].Value = spell.PrimaryCategory.Value;
                spellCmd.Parameters["@secondary_category"].Value = spell.SecondaryCategory.Value;
                spellCmd.Parameters["@secondary_category_2"].Value = spell.SecondaryCategory2.Value;
                spellCmd.Parameters["@target_type"].Value = (uint)spell.TargetType;
                spellCmd.Parameters["@cast_restriction"].Value = (uint)spell.CastRestriction;
                spellCmd.ExecuteNonQuery();

                foreach (SpellEffect effect in spell.Effects)
                {
                    effectCmd.Parameters["@spell_id"].Value = spellId;
                    effectCmd.Parameters["@slot"].Value = effect.Slot;
                    effectCmd.Parameters["@spa"].Value = (int)effect.Spa;
                    effectCmd.Parameters["@base1"].Value = effect.Base1;
                    effectCmd.Parameters["@base2"].Value = effect.Base2;
                    effectCmd.Parameters["@calc"].Value = effect.Calc;
                    effectCmd.Parameters["@max_value"].Value = effect.Max;
                    effectCmd.ExecuteNonQuery();
                    effectRows++;
                }

                for (uint classIndex = 0; classIndex < SpellRecord.ClassCount; classIndex++)
                {
                    byte classLevel = spell.ClassLevels[classIndex];
                    if (classLevel == SpellRecord.LevelUnusable)
                    {
                        continue;
                    }

                    classCmd.Parameters["@spell_id"].Value = spellId;
                    classCmd.Parameters["@class"].Value = classIndex + 1;
                    classCmd.Parameters["@level"].Value = classLevel;
                    classCmd.ExecuteNonQuery();
                    classRows++;
                }

                for (uint position = 0; position < 4; position++)
                {
                    int expendedItemId = spell.ReagentIds[position];
                    if (expendedItemId >= 0)
                    {
                        reagentCmd.Parameters["@spell_id"].Value = spellId;
                        reagentCmd.Parameters["@expended"].Value = 1;
                        reagentCmd.Parameters["@position"].Value = position;
                        reagentCmd.Parameters["@item_id"].Value = expendedItemId;
                        reagentCmd.Parameters["@quantity"].Value = spell.ReagentCounts[position];
                        reagentCmd.ExecuteNonQuery();
                        reagentRows++;
                    }

                    int keptItemId = spell.NoExpendReagentIds[position];
                    if (keptItemId >= 0)
                    {
                        reagentCmd.Parameters["@spell_id"].Value = spellId;
                        reagentCmd.Parameters["@expended"].Value = 0;
                        reagentCmd.Parameters["@position"].Value = position;
                        reagentCmd.Parameters["@item_id"].Value = keptItemId;
                        reagentCmd.Parameters["@quantity"].Value = 0;
                        reagentCmd.ExecuteNonQuery();
                        reagentRows++;
                    }
                }
            }

            tx.Commit();
            DebugLog.Write(LogChannel.Database, "SpellGateway.StoreSpells: committed " + spells.Count +
                " spells, " + effectRows + " effects, " + classRows + " class levels, " + reagentRows +
                " reagents.", LogLevel.Trace);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DebugLog.Write(LogChannel.Database, "SpellGateway.StoreSpells: failed, transaction rolled back: " +
                ex.Message, LogLevel.Error);
            throw;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // StoreCategories
    //
    // Stores a set of spell category names in one transaction.  A category with no stored row is
    // inserted.  A category whose stored name differs is updated.  A category whose stored name matches
    // is left alone.  A category without an identifier is skipped.  Stored categories that are absent
    // from the set are kept.  A failure rolls the transaction back and leaves the previous rows in place.
    //
    // categories:  The category names to store, keyed by category id.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void StoreCategories(IReadOnlyDictionary<SpellCategoryId, string> categories)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteTransaction tx = conn.BeginTransaction();
        try
        {
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO SpellCategories (id, name)
                VALUES (@id, @name)
                ON CONFLICT(id) DO UPDATE SET name = excluded.name
                WHERE SpellCategories.name <> excluded.name";
            SqliteParameter idParam = cmd.Parameters.Add("@id", SqliteType.Integer);
            SqliteParameter nameParam = cmd.Parameters.Add("@name", SqliteType.Text);

            uint written = 0;
            uint unchanged = 0;
            uint skipped = 0;

            foreach (KeyValuePair<SpellCategoryId, string> category in categories)
            {
                if (!category.Key.Exists)
                {
                    DebugLog.Write(LogChannel.Database, "SpellGateway.StoreCategories: skipping a category " +
                        "with no id, name '" + category.Value + "'.", LogLevel.Warn);
                    skipped++;
                    continue;
                }

                idParam.Value = category.Key.Value;
                nameParam.Value = category.Value;
                int rowsChanged = cmd.ExecuteNonQuery();
                if (rowsChanged > 0)
                {
                    written++;
                    DebugLog.Write(LogChannel.Database, "SpellGateway.StoreCategories: wrote category " +
                        category.Key + " '" + category.Value + "'.", LogLevel.Trace);
                }
                else
                {
                    unchanged++;
                }
            }

            tx.Commit();
            DebugLog.Write(LogChannel.Database, "SpellGateway.StoreCategories: committed. " + written +
                " written, " + unchanged + " unchanged, " + skipped + " skipped.", LogLevel.Trace);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DebugLog.Write(LogChannel.Database, "SpellGateway.StoreCategories: failed, transaction rolled " +
                "back: " + ex.Message, LogLevel.Error);
            throw;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadCategories
    //
    // Reads every spell category name from the database.
    //
    // Returns the category names keyed by category id; empty when the table has no rows.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Dictionary<SpellCategoryId, string> LoadCategories()
    {
        Dictionary<SpellCategoryId, string> categories = new Dictionary<SpellCategoryId, string>();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM SpellCategories";

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            SpellCategoryId id = (SpellCategoryId)(uint)reader.GetInt64(0);
            categories[id] = reader.GetString(1);
        }

        if (categories.Count == 0)
        {
            DebugLog.Write(LogChannel.Database, "SpellGateway.LoadCategories: the SpellCategories table is " +
                "empty.", LogLevel.Warn);
        }
        else
        {
            DebugLog.Write(LogChannel.Database, "SpellGateway.LoadCategories: read " + categories.Count +
                " category names.", LogLevel.Trace);
        }

        return categories;
    }
}