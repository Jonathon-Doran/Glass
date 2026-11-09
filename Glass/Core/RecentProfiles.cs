using Glass.Core.Logging;
using Glass.Data;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;

namespace Glass.Core;

public static class RecentProfiles
{
    private const int MaxItems = 10;

    public static List<string> Get()
    {
        var collection = Properties.Settings.Default.RecentProfiles;
        if (collection == null) return new List<string>();
        return collection.Cast<string>().ToList();
    }

    public static void Add(string profileName)
    {
        var collection = Properties.Settings.Default.RecentProfiles
                         ?? new System.Collections.Specialized.StringCollection();
        if (collection.Contains(profileName))
            collection.Remove(profileName);
        collection.Insert(0, profileName);
        while (collection.Count > MaxItems)
            collection.RemoveAt(collection.Count - 1);
        Properties.Settings.Default.RecentProfiles = collection;
        Properties.Settings.Default.Save();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Remove
    //
    // Removes a profile name from the recent profiles list and saves the list.  Does nothing when the
    // name is not in the list.
    //
    // profileName:  The profile name to remove.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static void Remove(string profileName)
    {
        System.Collections.Specialized.StringCollection? collection = Properties.Settings.Default.RecentProfiles;

        if ((collection == null) || !collection.Contains(profileName))
        {
            DebugLog.Write(LogChannel.Profiles, "RecentProfiles.Remove: '" + profileName
                + "' is not in the recent list.", LogLevel.Trace);
            return;
        }

        collection.Remove(profileName);
        Properties.Settings.Default.RecentProfiles = collection;
        Properties.Settings.Default.Save();

        DebugLog.Write(LogChannel.Profiles, "RecentProfiles.Remove: removed '" + profileName + "'.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Rename
    //
    // Replaces a profile name in the recent profiles list with a new name, keeping its position, and
    // saves the list.  Does nothing when the old name is not in the list.
    //
    // oldName:  The profile name currently in the list.
    // newName:  The name to replace it with.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static void Rename(string oldName, string newName)
    {
        System.Collections.Specialized.StringCollection? collection = Properties.Settings.Default.RecentProfiles;

        if ((collection == null) || !collection.Contains(oldName))
        {
            DebugLog.Write(LogChannel.Profiles, "RecentProfiles.RenameProfile: '" + oldName
                + "' is not in the recent list.", LogLevel.Trace);
            return;
        }

        int position = collection.IndexOf(oldName);
        collection[position] = newName;
        Properties.Settings.Default.RecentProfiles = collection;
        Properties.Settings.Default.Save();

        DebugLog.Write(LogChannel.Profiles, "RecentProfiles.RenameProfile: renamed '" + oldName + "' to '"
            + newName + "' at position " + position + ".", LogLevel.Trace);
    }
}
