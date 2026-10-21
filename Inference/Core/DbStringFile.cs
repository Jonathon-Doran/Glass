using Glass.Core.Logging;
using Glass.Data.Repositories;
using System;
using System.Collections.Generic;
using System.IO;

namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// DbStringFile
//
// The contents of the client's database string file, kept for every string type that is a
// member of DbStringType.  Each line of the file is a caret-delimited string id, string type,
// and text.  Lines of other string types are not retained.
///////////////////////////////////////////////////////////////////////////////////////////////
public class DbStringFile
{
    private readonly Dictionary<DbStringType, Dictionary<uint, string>> _strings =
        new Dictionary<DbStringType, Dictionary<uint, string>>();

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Strings
    //
    // Retained text keyed by string type, then by string id.  A type with no lines in the file
    // has no entry.  Empty until a file has been parsed.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyDictionary<DbStringType, Dictionary<uint, string>> Strings
    {
        get { return _strings; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ParseFile
    //
    // Reads the given database string file and fills the table, replacing any earlier
    // contents.  A line with fewer than three columns, an unparseable string type, or a
    // string type that is not a DbStringType member (or is None) is skipped.  A line of a
    // retained type with an unparseable string id is logged at Warn and skipped.
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

        _strings.Clear();

        uint lineNumber = 0;
        uint retained = 0;

        foreach (string line in File.ReadLines(filePath))
        {
            lineNumber++;

            string[] columns = line.Split('^');
            if (columns.Length < 3)
            {
                continue;
            }

            uint stringTypeNumber = 0;
            if (uint.TryParse(columns[1], out stringTypeNumber) == false)
            {
                continue;
            }

            if ((stringTypeNumber == (uint)DbStringType.None) ||
                (Enum.IsDefined(typeof(DbStringType), stringTypeNumber) == false))
            {
                continue;
            }

            DbStringType stringType = (DbStringType)stringTypeNumber;

            uint stringId = 0;
            if (uint.TryParse(columns[0], out stringId) == false)
            {
                DebugLog.Write(LogChannel.Reference, "DbStringFile.ParseFile: line " + lineNumber +
                    " has an unparseable string id, skipping.", LogLevel.Warn);
                continue;
            }

            Dictionary<uint, string>? table = null;
            if (_strings.TryGetValue(stringType, out table) == false)
            {
                table = new Dictionary<uint, string>();
                _strings[stringType] = table;
                DebugLog.Write(LogChannel.Reference, "DbStringFile.ParseFile: first line of type " + stringType +
                    " at line " + lineNumber + ".", LogLevel.Trace);
            }

            table[stringId] = columns[2];
            retained++;
        }

        DebugLog.Write(LogChannel.Reference, "DbStringFile.ParseFile: read " + lineNumber + " lines from " +
            filePath + ", retained " + retained + " strings of " + _strings.Count + " types.", LogLevel.Trace);
        return true;
    }
}