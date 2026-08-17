using Glass.Core.Logging;
using Glass.Data.Models;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Glass.World;

///////////////////////////////////////////////////////////////////////////////////////////////
// SpellCatalog
//
// Lookup of SpellRecords keyed by spell ID, loaded once from the client's spell data file.
// The catalog is immutable reference data: it is populated by Load and only read
// thereafter.  A catalog that failed to load is empty, and every lookup misses.
///////////////////////////////////////////////////////////////////////////////////////////////
public class SpellCatalog
{
    // Hard-coded spell data file location, pending a proper settings mechanism.
    private const string SpellFilePath = @"C:\Games\EverQuest\spells_us.txt";
    // Hard-coded database string file location, pending a proper settings mechanism.
    private const string DbStringFilePath = @"C:\Games\EverQuest\dbstr_us.txt";

    private readonly Dictionary<SpellId, SpellRecord> _spellsById = new Dictionary<SpellId, SpellRecord>();
    // The database string type whose entries are spell category names.
    private const uint DbStringTypeSpellCategory = 5;

    private readonly Dictionary<SpellCategoryId, string> _categoryNames = new Dictionary<SpellCategoryId, string>();


    // The SPA numbers retained at parse time.  An effect slot whose SPA is not in this set
    // is dropped from the record.  Adjust membership here; nothing else changes.
    private static readonly HashSet<SPAId> _spaWhitelist = new HashSet<SPAId>
    {
        SPAId.Hitpoints,
        SPAId.ArmorClass,
        SPAId.AttackPower,
        SPAId.MovementRate,
        SPAId.MeleeSpeed,
        SPAId.Mana,
        SPAId.Stun,
        SPAId.Charm,
        SPAId.Fear,
        SPAId.DispelMagic,
        SPAId.Mesmerize,
        SPAId.Disease,
        SPAId.Poison,
        SPAId.ResistFire,
        SPAId.ResistCold,
        SPAId.ResistPoison,
        SPAId.ResistDisease,
        SPAId.ResistMagic,
        SPAId.DamageShield,
        SPAId.MaxHitpoints,
        SPAId.Resurrect,
        SPAId.Hate,
        SPAId.Silence,
        SPAId.Root,
        SPAId.HealOverTime,
        SPAId.CompleteHeal
    };

    private static readonly SpellCatalog _instance = new SpellCatalog();

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SpellCatalog  (constructor)
    //
    // Loads the catalog from the hard-coded spell data file path.  A missing file leaves
    // the catalog empty; Load logs that condition.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private SpellCatalog()
    {
        Load(SpellFilePath);
        LoadCategoryNames(DbStringFilePath);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // The process-wide catalog instance.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static SpellCatalog Instance
    {
        get { return _instance; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Count
    //
    // The number of spells currently held by the catalog.  Zero before Load, or after a
    // load that failed.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public int Count
    {
        get { return _spellsById.Count; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CategoryNames
    //
    // The category names keyed by category ID, as loaded from the database string file.
    // Empty when the file was missing or held no spell category entries.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyDictionary<SpellCategoryId, string> CategoryNames
    {
        get { return _categoryNames; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TryGet
    //
    // Looks up the SpellRecord for the given spell ID.
    //
    // spellId:  The spell ID to look up.
    // record:   Receives the record on success, null on a miss.
    //
    // Returns:  True if the spell is in the catalog, false otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool TryGet(SpellId spellId, [NotNullWhen(true)] out SpellRecord? record)
    {
        return _spellsById.TryGetValue(spellId, out record);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Load
    //
    // Populates the catalog from the given spell data file, one spell per line.  Lines that
    // fail to parse are logged and skipped; a well-formed file loads completely.  A missing
    // file is logged at Warn and leaves the catalog empty — every lookup then misses, and
    // callers degrade to displaying raw spell IDs.  Calling Load again on a populated
    // catalog clears it first and reloads.
    //
    // filePath:  Full path to the spell data file (spells_us.txt).
    //
    // Returns:   The number of spells loaded.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public int Load(string filePath)
    {
        if (File.Exists(filePath) == false)
        {
            DebugLog.Write(LogChannel.Reference, "SpellCatalog.Load: file not found: "
                + filePath + ", catalog left empty", LogLevel.Warn);
            return 0;
        }

        if (_spellsById.Count > 0)
        {
            DebugLog.Write(LogChannel.Reference, "SpellCatalog.Load: reloading, clearing "
                + _spellsById.Count + " existing entries", LogLevel.Trace);
            _spellsById.Clear();
        }

        uint lineNumber = 0;
        uint skipped = 0;

        foreach (string line in File.ReadLines(filePath))
        {
            lineNumber++;

            if (line.Length == 0)
            {
                continue;
            }

            SpellRecord? record = ParseLine(line, lineNumber);
            if (record == null)
            {
                skipped++;
                continue;
            }

            if (_spellsById.ContainsKey(record.Id) == true)
            {
                DebugLog.Write(LogChannel.Reference, "SpellCatalog.Load: duplicate spell ID "
                    + record.Id + " ('" + record.Name + "') at line " + lineNumber
                    + ", replacing earlier entry", LogLevel.Warn);
            }

            _spellsById[record.Id] = record;
        }

        DebugLog.Write(LogChannel.Reference, "SpellCatalog.Load: loaded " + _spellsById.Count
            + " spells from " + lineNumber + " lines, skipped " + skipped
            + ", from " + filePath, LogLevel.Info);


        return _spellsById.Count;
    }


    ///////////////////////////////////////////////////////////////////////////////////////////
    // LogParseSpotCheck
    //
    // Logs the parsed target type and cast restriction for every spell whose name contains
    // the given substring.  Temporary verification aid; logs at Warn so the output is
    // visible under the normal log level.
    //
    // nameFragment:  Substring to match against spell names, case-insensitive.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void LogParseSpotCheck(string nameFragment)
    {
        foreach (SpellRecord record in _spellsById.Values)
        {
            if (record.Name.Contains(nameFragment, StringComparison.OrdinalIgnoreCase) == true)
            {
                DebugLog.Write(LogChannel.Reference, "SpellCatalog.LogParseSpotCheck: '"
                    + record.Name + "' id " + record.Id
                    + " targetType " + record.TargetType
                    + " castRestriction " + record.CastRestriction, LogLevel.Warn);
            }
        }
    }

    // Zero-based caret-delimited column positions in the spell data file.
    private const int ColumnId = 0;
    private const int ColumnName = 1;
    private const int ColumnRange = 4;
    private const int ColumnCastTime = 8;
    private const int ColumnRecastTime = 9;
    private const int ColumnDurationFormula = 11;
    private const int ColumnDurationCap = 12;
    private const int ColumnMana = 14;
    private const int ColumnReagentStart = 15;
    private const int ColumnReagentCountStart = 19;
    private const int ColumnNoExpendReagentStart = 23;
    private const int ColumnTargetType = 30;
    private const int ClassLevelStart = 36;
    private const int ColumnPrimaryCategory = 86;
    private const int ColumnSecondaryCategory = 87;
    private const int ColumnSecondaryCategory2 = 88;
    private const int ColumnCastRestriction = 136;

    // A line must have at least this many columns for the scalar reads above, plus the
    // packed effects in the final column.
    private const int MinimumColumns = 137;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ParseLine
    //
    // Parses one line of the spell data file into a SpellRecord.  The scalar fields are
    // read from fixed caret-delimited column positions; the class level, reagent, and
    // reagent count runs are read from their starting columns; the packed effect slots
    // are read from the final column and filtered by the SPA whitelist.  Any structural
    // failure — too few columns, or an unparseable numeric field — is logged at Warn and
    // yields null so the caller can skip the line.
    //
    // line:        One line of the file, without its line terminator.
    // lineNumber:  The line's position in the file, for log attribution.
    //
    // Returns:     The parsed record, or null if the line is malformed.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private SpellRecord? ParseLine(string line, uint lineNumber)
    {
        string[] columns = line.Split('^');

        if (columns.Length < MinimumColumns)
        {
            DebugLog.Write(LogChannel.Reference, "SpellCatalog.ParseLine: line " + lineNumber
                + " has " + columns.Length + " columns, need " + MinimumColumns
                + ", skipping", LogLevel.Warn);
            return null;
        }

        uint id = 0;
        uint range = 0;
        uint castTime = 0;
        uint recastTime = 0;
        uint durationFormula = 0;
        uint durationCap = 0;
        uint mana = 0;
        uint primaryCategory = 0;
        uint secondaryCategory = 0;
        uint secondaryCategory2 = 0;
        uint targetType = 0;
        uint castRestriction = 0;

        bool parsed = uint.TryParse(columns[ColumnId], out id)
            && uint.TryParse(columns[ColumnRange], out range)
            && uint.TryParse(columns[ColumnCastTime], out castTime)
            && uint.TryParse(columns[ColumnRecastTime], out recastTime)
            && uint.TryParse(columns[ColumnDurationFormula], out durationFormula)
            && uint.TryParse(columns[ColumnDurationCap], out durationCap)
            && uint.TryParse(columns[ColumnMana], out mana)
            && uint.TryParse(columns[ColumnPrimaryCategory], out primaryCategory)
            && uint.TryParse(columns[ColumnSecondaryCategory], out secondaryCategory)
            && uint.TryParse(columns[ColumnSecondaryCategory2], out secondaryCategory2)
            && uint.TryParse(columns[ColumnTargetType], out targetType)
            && uint.TryParse(columns[ColumnCastRestriction], out castRestriction);

        if (parsed == false)
        {
            DebugLog.Write(LogChannel.Reference, "SpellCatalog.ParseLine: line " + lineNumber
                + " has an unparseable numeric column, skipping", LogLevel.Warn);
            return null;
        }

        SpellRecord record = new SpellRecord();
        record.Id = (SpellId) id;
        record.Name = columns[ColumnName];
        record.Range = range;
        record.CastTimeMs = castTime;
        record.RecastTimeMs = recastTime;
        record.DurationFormula = durationFormula;
        record.DurationCapTicks = durationCap;
        record.Mana = mana;
        record.PrimaryCategory = (SpellCategoryId) primaryCategory;
        record.SecondaryCategory = (SpellCategoryId) secondaryCategory;
        record.SecondaryCategory2 = (SpellCategoryId) secondaryCategory2;

        for (uint classIndex = 0; classIndex < SpellRecord.ClassCount; classIndex++)
        {
            byte classLevel = 0;
            if (byte.TryParse(columns[ClassLevelStart + classIndex], out classLevel) == false)
            {
                DebugLog.Write(LogChannel.Reference, "SpellCatalog.ParseLine: line " + lineNumber
                    + " has an unparseable class level at class index " + classIndex
                    + ", skipping", LogLevel.Warn);
                return null;
            }

            record.ClassLevels[classIndex] = classLevel;
        }

        for (uint reagentIndex = 0; reagentIndex < 4; reagentIndex++)
        {
            int reagentId = 0;
            uint reagentCount = 0;
            int noExpendReagentId = 0;

            bool reagentParsed = int.TryParse(columns[ColumnReagentStart + reagentIndex], out reagentId)
                && uint.TryParse(columns[ColumnReagentCountStart + reagentIndex], out reagentCount)
                && int.TryParse(columns[ColumnNoExpendReagentStart + reagentIndex], out noExpendReagentId);

            if (reagentParsed == false)
            {
                DebugLog.Write(LogChannel.Reference, "SpellCatalog.ParseLine: line " + lineNumber
                    + " has an unparseable reagent column at reagent index " + reagentIndex
                    + ", skipping", LogLevel.Warn);
                return null;
            }

            record.ReagentIds[reagentIndex] = reagentId;
            record.ReagentCounts[reagentIndex] = reagentCount;
            record.NoExpendReagentIds[reagentIndex] = noExpendReagentId;
        }

        record.TargetType = (SpellTargetType)targetType;
        record.CastRestriction = (SpellCastRestriction)castRestriction;
        record.Effects = ParseEffects(columns[columns.Length - 1], (SpellId) id, lineNumber);

        return record;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ParseEffects
    //
    // Parses the packed effects column of one spell line: dollar-separated groups, each a
    // pipe-separated sextet of slot, SPA, base1, base2, calc, and max.  Groups whose SPA
    // is not in the whitelist are dropped.  A structurally bad group is logged at Warn and
    // skipped without failing the containing spell.  An empty column yields an empty array.
    //
    // packed:      The final column's raw text.
    // spellId:     The owning spell's ID, for log attribution.
    // lineNumber:  The owning line's position in the file, for log attribution.
    //
    // Returns:     The whitelisted effects in file order; empty if none survive.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private SpellEffect[] ParseEffects(string packed, SpellId spellId, uint lineNumber)
    {
        if (packed.Length == 0)
        {
            return Array.Empty<SpellEffect>();
        }

        string[] groups = packed.Split('$');
        List<SpellEffect> effects = new List<SpellEffect>();

        foreach (string group in groups)
        {
            string[] parts = group.Split('|');
            if (parts.Length < 6)
            {
                DebugLog.Write(LogChannel.Reference, "SpellCatalog.ParseEffects: spell "
                    + spellId + " line " + lineNumber + " effect group '" + group
                    + "' has " + parts.Length + " parts, need 6, skipping group", LogLevel.Warn);
                continue;
            }

            uint slot = 0;
            uint spaValue = 0;
            int base1 = 0;
            int base2 = 0;
            uint calc = 0;
            int max = 0;

            bool parsed = uint.TryParse(parts[0], out slot)
                && uint.TryParse(parts[1], out spaValue)
                && int.TryParse(parts[2], out base1)
                && int.TryParse(parts[3], out base2)
                && uint.TryParse(parts[4], out calc)
                && int.TryParse(parts[5], out max);

            SPAId spa = (SPAId)spaValue;

            if (parsed == false)
            {
                DebugLog.Write(LogChannel.Reference, "SpellCatalog.ParseEffects: spell "
                    + spellId + " line " + lineNumber + " effect group '" + group
                    + "' has an unparseable part, skipping group", LogLevel.Warn);
                continue;
            }

            if (_spaWhitelist.Contains(spa) == false)
            {
                continue;
            }

            SpellEffect effect = new SpellEffect();
            effect.Slot = slot;
            effect.Spa = spa;
            effect.Base1 = base1;
            effect.Base2 = base2;
            effect.Calc = calc;
            effect.Max = max;
            effects.Add(effect);
        }

        if (effects.Count == 0)
        {
            return Array.Empty<SpellEffect>();
        }

        return effects.ToArray();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LookupSpell
    //
    // A helper to lookup the spell name from ID
    //
    // spellId:  The ID to query
    //
    // Returns:   The name of the spell, or "unknown"
    ///////////////////////////////////////////////////////////////////////////////////////////////

    public string LookupSpell(SpellId spellId)
    {
        string spellName;
        SpellRecord? record;
        if (TryGet(spellId, out record) == true)
        {
            spellName = record.Name;
        }
        else
        {
            spellName = "unknown";
        }

        return spellName;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindSpells
    //
    // Returns every spell matching the given filter.  A name constraint matches as a
    // case-insensitive substring.  An SPA constraint matches if any retained effect slot
    // carries that SPA.  A class constraint matches if the class can cast the spell at
    // all, tightened by MaximumLevel when present.  A category constraint matches the
    // primary or either secondary category.
    //
    // filter:   The criteria to apply.
    //
    // Returns:  The matching records in no guaranteed order; empty if nothing matches.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<SpellRecord> FindSpells(SpellFilter filter)
    {
        List<SpellRecord> matches = new List<SpellRecord>();

        foreach (SpellRecord record in _spellsById.Values)
        {
            if (filter.NameContains != null
                && record.Name.Contains(filter.NameContains, StringComparison.OrdinalIgnoreCase) == false)
            {
                continue;
            }

            if (filter.Spa != null)
            {
                bool spaFound = false;
                foreach (SpellEffect effect in record.Effects)
                {
                    if (effect.Spa == filter.Spa.Value)
                    {
                        spaFound = true;
                        break;
                    }
                }

                if (spaFound == false)
                {
                    continue;
                }
            }

            if (filter.TargetType != null && record.TargetType != filter.TargetType.Value)
            {
                continue;
            }

            if (filter.CastableClass != null)
            {
                uint classIndex = (uint)filter.CastableClass.Value - 1;
                byte classLevel = record.ClassLevels[classIndex];

                if (classLevel == SpellRecord.LevelUnusable)
                {
                    continue;
                }

                if (filter.MaximumLevel != null && classLevel > filter.MaximumLevel.Value)
                {
                    continue;
                }
            }

            if (filter.Category.Exists && record.PrimaryCategory != filter.Category)
            {
                continue;
            }
            if (filter.Subcategory.Exists && record.SecondaryCategory != filter.Subcategory)
            {
                continue;
            }

            matches.Add(record);
        }

        DebugLog.Write(LogChannel.Reference, "SpellCatalog.FindSpells: " + matches.Count
            + " of " + _spellsById.Count + " spells matched", LogLevel.Trace);

        return matches;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LoadCategoryNames
    //
    // Populates the category name lookup from the given database string file, retaining
    // only the spell category entries.  Lines of other string types are ignored.  A
    // malformed line within the wanted type is logged at Warn and skipped.  A missing
    // file is logged at Warn and leaves the lookup empty — categories then display as
    // raw numbers.
    //
    // filePath:  Full path to the database string file (dbstr_us.txt).
    //
    // Returns:   The number of category names loaded.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public int LoadCategoryNames(string filePath)
    {
        if (File.Exists(filePath) == false)
        {
            DebugLog.Write(LogChannel.Reference, "SpellCatalog.LoadCategoryNames: file not found: "
                + filePath + ", category names unavailable", LogLevel.Warn);
            return 0;
        }

        if (_categoryNames.Count > 0)
        {
            DebugLog.Write(LogChannel.Reference, "SpellCatalog.LoadCategoryNames: reloading, clearing "
                + _categoryNames.Count + " existing entries", LogLevel.Trace);
            _categoryNames.Clear();
        }

        uint lineNumber = 0;

        foreach (string line in File.ReadLines(filePath))
        {
            lineNumber++;

            string[] columns = line.Split('^');
            if (columns.Length < 3)
            {
                continue;
            }

            uint stringType = 0;
            if (uint.TryParse(columns[1], out stringType) == false)
            {
                continue;
            }

            if (stringType != DbStringTypeSpellCategory)
            {
                continue;
            }

            uint categoryId = 0;
            if (uint.TryParse(columns[0], out categoryId) == false)
            {
                DebugLog.Write(LogChannel.Reference, "SpellCatalog.LoadCategoryNames: line "
                    + lineNumber + " has an unparseable category id, skipping", LogLevel.Warn);
                continue;
            }

            _categoryNames[(SpellCategoryId) categoryId] = columns[2];
        }

        DebugLog.Write(LogChannel.Reference, "SpellCatalog.LoadCategoryNames: loaded "
            + _categoryNames.Count + " category names from " + filePath, LogLevel.Trace);

        return _categoryNames.Count;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // DescribeCategory
    //
    // Formats a category for display as its name followed by the numeric value in
    // parentheses.  A category with no loaded name yields the numeric value alone.
    //
    // category:  The category to format.
    //
    // Returns:   The formatted text.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string DescribeCategory(SpellCategoryId category)
    {
        string? name;
        if (_categoryNames.TryGetValue(category, out name) == true)
        {
            return name + " (" + category.Value + ")";
        }
        return category.Value.ToString();
    }
}

///////////////////////////////////////////////////////////////////////////////////////////////
// SpellFilter
//
// Criteria for a catalog query.  Every field is optional; a null field does not constrain
// the result.  Populated fields combine conjunctively.  MaximumLevel is meaningful only
// when CastableClass is set: it further requires the class to receive the spell at or
// below that level.
///////////////////////////////////////////////////////////////////////////////////////////////
public class SpellFilter
{
    public string? NameContains;
    public SPAId? Spa;
    public SpellTargetType? TargetType;
    public EQClass? CastableClass;
    public byte? MaximumLevel;
    public SpellCategoryId Category = SpellCategoryId.None;
    public SpellCategoryId Subcategory = SpellCategoryId.None;
}