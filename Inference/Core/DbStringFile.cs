using Glass.Core.Logging;
using System;
using System.Collections.Generic;
using System.IO;

namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// DbStringFile
//
// The contents of the client's database string file that Glass uses: AA name text, AA
// description text, and spell category names.  Each line of the file is a caret-delimited
// string id, string type, and text.  Lines of other string types are not retained.
///////////////////////////////////////////////////////////////////////////////////////////////
public class DbStringFile
{
    // String type numbers in the database string file.
    private const uint StringTypeAAName = 1;
    private const uint StringTypeAADescription = 4;
    private const uint StringTypeSpellCategory = 5;

    private readonly Dictionary<uint, string> _aaNames = new Dictionary<uint, string>();
    private readonly Dictionary<uint, string> _aaDescriptions = new Dictionary<uint, string>();
    private readonly Dictionary<SpellCategoryId, string> _spellCategories =
        new Dictionary<SpellCategoryId, string>();

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AANames
    //
    // AA name text keyed by string id.  Empty until a file has been parsed.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyDictionary<uint, string> AANames
    {
        get { return _aaNames; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AADescriptions
    //
    // AA description text keyed by string id.  Empty until a file has been parsed.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyDictionary<uint, string> AADescriptions
    {
        get { return _aaDescriptions; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SpellCategories
    //
    // Spell category names keyed by category id.  Empty until a file has been parsed.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyDictionary<SpellCategoryId, string> SpellCategories
    {
        get { return _spellCategories; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ParseFile
    //
    // Reads the given database string file and fills the three tables, replacing any earlier
    // contents.  A line with fewer than three columns, an unparseable string type, or a
    // string type that is not retained is skipped.  A line of a retained type with an
    // unparseable string id is logged at Warn and skipped.
    //
    // filePath:  Full path to the database string file (dbstr_us.txt).
    //
    // Returns:   True when the file was read, false when the file does not exist.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool ParseFile(string filePath)
    {
        if (File.Exists(filePath) == false)
        {
            DebugLog.Write(LogChannel.Reference, "DbStringFile.ParseFile: file not found: " + filePath,
                LogLevel.Warn);
            return false;
        }

        _aaNames.Clear();
        _aaDescriptions.Clear();
        _spellCategories.Clear();

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

            if ((stringType != StringTypeAAName) &&
                (stringType != StringTypeAADescription) &&
                (stringType != StringTypeSpellCategory))
            {
                continue;
            }

            uint stringId = 0;
            if (uint.TryParse(columns[0], out stringId) == false)
            {
                DebugLog.Write(LogChannel.Reference, "DbStringFile.ParseFile: line " + lineNumber +
                    " has an unparseable string id, skipping.", LogLevel.Warn);
                continue;
            }

            switch (stringType)
            {
                case StringTypeAAName:
                    {
                        _aaNames[stringId] = columns[2];
                        break;
                    }
                case StringTypeAADescription:
                    {
                        _aaDescriptions[stringId] = columns[2];
                        break;
                    }
                case StringTypeSpellCategory:
                    {
                        _spellCategories[(SpellCategoryId)stringId] = columns[2];
                        break;
                    }
            }
        }

        DebugLog.Write(LogChannel.Reference, "DbStringFile.ParseFile: read " + lineNumber + " lines from " +
            filePath + ": " + _aaNames.Count + " AA names, " + _aaDescriptions.Count + " AA descriptions, " +
            _spellCategories.Count + " spell categories.", LogLevel.Trace);
        return true;
    }
}
