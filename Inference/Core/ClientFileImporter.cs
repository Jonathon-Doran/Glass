using Glass.Core.Logging;
using Glass.Data.Repositories;
using Glass.World;
using System;
using System.Collections.Generic;
using System.IO;

namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// ClientFileImporter
//
// Imports the contents of the client's data files into the database.  The files are read
// from the folder named by the EverQuestFolder setting.
///////////////////////////////////////////////////////////////////////////////////////////////
public class ClientFileImporter
{
    // Name of the database string file within the EverQuest folder.
    private const string DbStringFileName = "dbstr_us.txt";

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ImportDbStringFile
    //
    // Parses the database string file and stores every retained string type in the
    // DbStrings table.  Spell category strings are also stored as spell categories.  An
    // exception raised while storing is not caught here.
    //
    // Returns:  True when the file was parsed and stored, false when the EverQuestFolder
    //           setting is empty or the file does not exist.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool ImportDbStringFile()
    {
        string folder = Properties.Settings.Default.EverQuestFolder;
        if (string.IsNullOrEmpty(folder) == true)
        {
            DebugLog.Write(LogChannel.Reference, "ClientFileImporter.ImportDbStringFile: the EverQuestFolder " +
                "setting is empty, nothing stored.", LogLevel.Warn);
            return false;
        }

        string filePath = Path.Combine(folder, DbStringFileName);

        DbStringFile file = new DbStringFile();
        if (file.ParseFile(filePath) == false)
        {
            DebugLog.Write(LogChannel.Reference, "ClientFileImporter.ImportDbStringFile: " + filePath +
                " could not be parsed, nothing stored.", LogLevel.Warn);
            return false;
        }

        uint stored = 0;

        foreach (KeyValuePair<DbStringType, Dictionary<uint, string>> entry in file.Strings)
        {
            DbStringGateway.Instance.StoreStrings(entry.Key, entry.Value);
            stored += (uint)entry.Value.Count;
            DebugLog.Write(LogChannel.Reference, "ClientFileImporter.ImportDbStringFile: stored " +
                entry.Value.Count + " strings of type " + entry.Key + ".", LogLevel.Trace);
        }

        Dictionary<uint, string>? categoryStrings = null;
        if (file.Strings.TryGetValue(DbStringType.SpellCategory, out categoryStrings) == true)
        {
            Dictionary<SpellCategoryId, string> categories = new Dictionary<SpellCategoryId, string>();
            foreach (KeyValuePair<uint, string> category in categoryStrings)
            {
                categories[(SpellCategoryId)category.Key] = category.Value;
            }

            SpellGateway.Instance.StoreCategories(categories);
            DebugLog.Write(LogChannel.Reference, "ClientFileImporter.ImportDbStringFile: stored " +
                categories.Count + " spell categories.", LogLevel.Trace);
        }
        else
        {
            DebugLog.Write(LogChannel.Reference, "ClientFileImporter.ImportDbStringFile: no spell category " +
                "strings in " + filePath + ".", LogLevel.Warn);
        }

        DebugLog.Write(LogChannel.Reference, "ClientFileImporter.ImportDbStringFile: stored " + stored +
            " strings of " + file.Strings.Count + " types from " + filePath + ".", LogLevel.Trace);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ImportSpells
    //
    // Stores every spell held by the spell catalog in the database, replacing the spells
    // stored before.  Nothing is stored when the catalog holds no spells, so an empty catalog
    // cannot erase the stored spells.  An exception raised while storing is not caught here.
    //
    // Returns:  True when the spells were stored, false when the catalog holds no spells.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool ImportSpells()
    {
        List<SpellRecord> spells = SpellCatalog.Instance.FindSpells(new SpellFilter());
        if (spells.Count == 0)
        {
            DebugLog.Write(LogChannel.Reference, "ClientFileImporter.ImportSpells: the spell catalog holds " +
                "no spells, nothing stored.", LogLevel.Warn);
            return false;
        }

        SpellGateway.Instance.StoreSpells(spells);

        DebugLog.Write(LogChannel.Reference, "ClientFileImporter.ImportSpells: stored " + spells.Count +
            " spells.", LogLevel.Trace);
        return true;
    }
}