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
    // Parses the database string file and stores its AA names, AA descriptions, and spell
    // category names in the database.  An exception raised while storing is not caught here.
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

        DbStringGateway.Instance.StoreStrings(DbStringType.AAName, file.AANames);
        DbStringGateway.Instance.StoreStrings(DbStringType.AADescription, file.AADescriptions);
        SpellGateway.Instance.StoreCategories(file.SpellCategories);

        DebugLog.Write(LogChannel.Reference, "ClientFileImporter.ImportDbStringFile: stored " +
            file.AANames.Count + " AA names, " + file.AADescriptions.Count + " AA descriptions, " +
            file.SpellCategories.Count + " spell categories from " + filePath + ".", LogLevel.Trace);
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