using Glass.Core.Logging;
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

    private readonly Dictionary<uint, SpellRecord> _spellsById = new Dictionary<uint, SpellRecord>();

    // The SPA numbers retained at parse time.  An effect slot whose SPA is not in this set
    // is dropped from the record.  Adjust membership here; nothing else changes.
    private static readonly HashSet<uint> _spaWhitelist = new HashSet<uint>
    {
        0,      // HP: damage, heals, DOT ticks
        1,      // AC
        2,      // ATK
        3,      // movement rate (snare)
        11,     // attack speed (slow / haste)
        15,     // mana recovery / drain
        21,     // stun
        22,     // charm
        23,     // fear
        27,     // dispel
        31,     // mez
        35,     // disease counters
        36,     // poison counters
        46,     // fire resist
        47,     // cold resist
        48,     // poison resist
        49,     // disease resist
        50,     // magic resist
        59,     // damage shield
        69,     // max HP
        81,     // resurrect
        92,     // hate
        96,     // silence
        99,     // root
        100,    // heal over time
        101,    // complete heal
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
    // TryGet
    //
    // Looks up the SpellRecord for the given spell ID.
    //
    // spellId:  The spell ID to look up.
    // record:   Receives the record on success, null on a miss.
    //
    // Returns:  True if the spell is in the catalog, false otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool TryGet(uint spellId, [NotNullWhen(true)] out SpellRecord? record)
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
    private const int ColumnRecastTime = 10;
    private const int ColumnDurationFormula = 11;
    private const int ColumnDuration = 12;
    private const int ColumnMana = 14;
    private const int ColumnTargetType = 30;
    private const int ColumnCastRestriction = 136;

    // A line must have at least this many columns for the scalar reads above, plus the
    // packed effects in the final column.
    private const int MinimumColumns = 137;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ParseLine
    //
    // Parses one line of the spell data file into a SpellRecord.  The scalar fields are
    // read from fixed caret-delimited column positions; the packed effect slots are read
    // from the final column and filtered by the SPA whitelist.  Any structural failure —
    // too few columns, or an unparseable numeric field — is logged at Warn and yields
    // null so the caller can skip the line.
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
        uint duration = 0;
        uint mana = 0;
        uint targetType = 0;
        uint castRestriction = 0;

        bool parsed = uint.TryParse(columns[ColumnId], out id)
            && uint.TryParse(columns[ColumnRange], out range)
            && uint.TryParse(columns[ColumnCastTime], out castTime)
            && uint.TryParse(columns[ColumnRecastTime], out recastTime)
            && uint.TryParse(columns[ColumnDurationFormula], out durationFormula)
            && uint.TryParse(columns[ColumnDuration], out duration)
            && uint.TryParse(columns[ColumnMana], out mana)
            && uint.TryParse(columns[ColumnTargetType], out targetType)
            && uint.TryParse(columns[ColumnCastRestriction], out castRestriction);

        if (parsed == false)
        {
            DebugLog.Write(LogChannel.Reference, "SpellCatalog.ParseLine: line " + lineNumber
                + " has an unparseable numeric column, skipping", LogLevel.Warn);
            return null;
        }

        SpellRecord record = new SpellRecord();
        record.Id = id;
        record.Name = columns[ColumnName];
        record.Range = range;
        record.CastTimeMs = castTime;
        record.RecastTimeMs = recastTime;
        record.DurationFormula = durationFormula;
        record.DurationTicks = duration;
        record.Mana = mana;
        record.TargetType = (SpellTargetType)targetType;
        record.CastRestriction = (SpellCastRestriction)castRestriction;
        record.Effects = ParseEffects(columns[columns.Length - 1], id, lineNumber);

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
    private SpellEffect[] ParseEffects(string packed, uint spellId, uint lineNumber)
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
            uint spa = 0;
            int base1 = 0;
            int base2 = 0;
            uint calc = 0;
            int max = 0;

            bool parsed = uint.TryParse(parts[0], out slot)
                && uint.TryParse(parts[1], out spa)
                && int.TryParse(parts[2], out base1)
                && int.TryParse(parts[3], out base2)
                && uint.TryParse(parts[4], out calc)
                && int.TryParse(parts[5], out max);

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

    public string LookupSpell(uint spellId)
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
}