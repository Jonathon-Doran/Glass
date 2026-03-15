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
}
