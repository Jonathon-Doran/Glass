using Glass.Core.Logging;
using Glass.Data.Repositories;
using Glass.World;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Inference.Dialogs;

///////////////////////////////////////////////////////////////////////////////////////////////
// SpawnBrowser
//
// Modeless read-only browser over the in-memory spawn records of one zone.  The zone combo
// lists every zone the zone repository knows; selecting one, pressing Refresh, or pressing
// Enter in the filter box re-reads that zone's records from the mob repository and shows
// the rows that pass the filter.  The window never mutates either repository.
///////////////////////////////////////////////////////////////////////////////////////////////
public partial class SpawnBrowser : Window
{
    ///////////////////////////////////////////////////////////////////////////////////////////
    // SpawnBrowser  (constructor)
    //
    // Fills the zone combo with every zone that has at least one sighting row in the database,
    // displayed as "name  [id]  N sightings" and carrying the boxed ZoneId in the item Tag.
    // No zone is selected initially, so the grid starts empty.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SpawnBrowser()
    {
        InitializeComponent();

        Dictionary<ZoneId, uint> counts = MobSightingGateway.Instance.CountByZone();
        List<KeyValuePair<string, ZoneId>> populated = new List<KeyValuePair<string, ZoneId>>();

        foreach (KeyValuePair<ZoneId, uint> entry in counts)
        {
            string label = ZoneRepository.Instance.GetZoneName(entry.Key) + "  [" + entry.Key.Value + "]  "
                + entry.Value + " sightings";
            populated.Add(new KeyValuePair<string, ZoneId>(label, entry.Key));
        }

        populated.Sort((KeyValuePair<string, ZoneId> left, KeyValuePair<string, ZoneId> right) =>
            string.Compare(left.Key, right.Key, StringComparison.OrdinalIgnoreCase));

        foreach (KeyValuePair<string, ZoneId> entry in populated)
        {
            ComboBoxItem item = new ComboBoxItem();
            item.Content = entry.Key;
            item.Tag = entry.Value;
            ZoneList.Items.Add(item);
        }

        DebugLog.Write(LogChannel.InferenceDebug, "SpawnBrowser: opened with " + populated.Count
            + " zone(s) that have sightings", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ZoneList_SelectionChanged
    //
    // Reloads the grid for the newly selected zone.
    //
    // sender:  The zone combo box.
    // e:       The selection changed event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ZoneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshRows();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FilterText_KeyDown
    //
    // Reloads the grid when Enter is pressed in the filter box.
    //
    // sender:  The filter text box.
    // e:       The key event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void FilterText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RefreshRows();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_Refresh_Click
    //
    // Reloads the grid for the selected zone with the current filter.
    //
    // sender:  The refresh button.
    // e:       The routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshRows();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RefreshRows
    //
    // Reads the selected zone's sightings and mob types from the database, fills each
    // record's Name from its mob type, keeps the records that pass the filter text, and
    // fills the grid in observed order.  A record whose mob type has no row is named
    // "<no type>".  With no zone selected the grid is emptied and the count line says so.
    // The count line reports rows shown against rows read.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void RefreshRows()
    {
        ComboBoxItem? zoneItem = ZoneList.SelectedItem as ComboBoxItem;

        if (zoneItem == null || zoneItem.Tag == null)
        {
            ResultGrid.ItemsSource = null;
            ResultCount.Text = "No zone selected";
            DebugLog.Write(LogChannel.InferenceDebug, "SpawnBrowser.RefreshRows: no zone selected, grid cleared",
                LogLevel.Trace);
            return;
        }

        ZoneId zoneId = (ZoneId)zoneItem.Tag;
        string filterText = FilterText.Text.Trim();
        string source;

        List<Spawn> spawns = MobRepository.Instance.GetSpawnsInZone(zoneId);

        if (spawns.Count > 0)
        {
            source = "cache";
        }
        else
        {
            source = "database";
            spawns = MobSightingGateway.Instance.LoadByZone(zoneId);
            List<MobType> mobTypes = MobTypeGateway.Instance.LoadByZone(zoneId);
            Dictionary<MobId, string> namesByMobId = new Dictionary<MobId, string>(mobTypes.Count);

            foreach (MobType mobType in mobTypes)
            {
                namesByMobId[mobType.Id] = mobType.BaseName;
            }

            uint unnamed = 0;

            foreach (Spawn spawn in spawns)
            {
                if (namesByMobId.TryGetValue(spawn.MobId, out string? baseName))
                {
                    spawn.Name = baseName;
                }
                else
                {
                    spawn.Name = "<no type>";
                    unnamed++;
                }
            }

            if (unnamed > 0)
            {
                DebugLog.Write(LogChannel.InferenceDebug, "SpawnBrowser.RefreshRows: zoneId=" + zoneId + " "
                    + unnamed + " sighting(s) reference a mob id with no MobTypes row", LogLevel.Warn);
            }
        }

        List<SpawnBrowserRow> rows = new List<SpawnBrowserRow>(spawns.Count);

        foreach (Spawn spawn in spawns)
        {
            if (MatchesFilter(spawn, filterText))
            {
                rows.Add(new SpawnBrowserRow(spawn));
            }
        }

        ResultGrid.ItemsSource = rows;
        ResultCount.Text = rows.Count + " of " + spawns.Count + " spawns (" + source + ")";

        DebugLog.Write(LogChannel.InferenceDebug, "SpawnBrowser.RefreshRows: zoneId=" + zoneId + " source=" + source
            + " read " + spawns.Count + " record(s), showing " + rows.Count + " with filter '" + filterText + "'",
            LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // MatchesFilter
    //
    // Tests one spawn record against the filter text.  Empty text matches everything.
    // Text that parses as a number (decimal, or hex with a 0x prefix) matches when it
    // equals the spawn id.  Independently, text that appears in the name, case-insensitive,
    // matches.  Either test passing is a match.
    //
    // spawn:       The record to test.
    // filterText:  The trimmed filter text.
    //
    // Returns:  True when the record should be shown.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool MatchesFilter(Spawn spawn, string filterText)
    {
        if (filterText.Length == 0)
        {
            return true;
        }

        uint numeric;
        bool parsed;

        if (filterText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            parsed = uint.TryParse(filterText.Substring(2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out numeric);
        }
        else
        {
            parsed = uint.TryParse(filterText, NumberStyles.None,
                CultureInfo.InvariantCulture, out numeric);
        }

        if (parsed && spawn.SpawnId.Value == numeric)
        {
            return true;
        }

        if (spawn.Name != null
            && spawn.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}

///////////////////////////////////////////////////////////////////////////////////////////////
// SpawnBrowserRow
//
// One row of the spawn browser grid: the display projections of a Spawn record, taken at
// the time the row was built.  ZoneEntry fields are shown under their slot names; ids in
// hex, the rest decimal.  A spawn id that does not exist shows as an empty string.
///////////////////////////////////////////////////////////////////////////////////////////////
public class SpawnBrowserRow
{
    public string SpawnIdHex { get; }
    public string Name { get; }
    public ushort Level { get; }
    public uint MobId { get; }
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public string Observed { get; }
    public uint A { get; }
    public string PersistentId { get; }
    public uint IsPlayer { get; }
    public uint Type { get; }
    public string Unknown1 { get; }
    public string Unknown2 { get; }
    public uint Unknown3 { get; }
    public uint C { get; }
    public uint Model { get; }
    public uint Class { get; }
    public string LastName { get; }
    public string OwnerId { get; }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SpawnBrowserRow  (constructor)
    //
    // Builds the display projections from the given record.  A null name shows as
    // "<null>"; a default Observed shows as an empty string.
    //
    // spawn:  The record to present.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SpawnBrowserRow(Spawn spawn)
    {
        if (spawn.SpawnId.Exists)
        {
            SpawnIdHex = "0x" + spawn.SpawnId.Value.ToString("X4");
        }
        else
        {
            SpawnIdHex = string.Empty;
        }

        Name = spawn.Name ?? "<null>";
        Level = spawn.Level;
        MobId = spawn.MobId.Value;
        X = spawn.X;
        Y = spawn.Y;
        Z = spawn.Z;

        if (spawn.Observed == default)
        {
            Observed = string.Empty;
        }
        else
        {
            Observed = spawn.Observed.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }

        A = spawn.A;
        PersistentId = "0x" + spawn.PersistentId.ToString("X");
        IsPlayer = spawn.IsPlayer;
        Type = spawn.Type;
        Unknown1 = spawn.Unknown1.ToString("F2");
        Unknown2 = "0x" + spawn.Unknown2.ToString("X8");
        Unknown3 = spawn.Unknown3;
        C = spawn.C;
        Model = spawn.Model;
        Class = spawn.Class;
        LastName = spawn.LastName ?? string.Empty;
        OwnerId = "0x" + spawn.OwnerId.ToString("X4");
    }
}
