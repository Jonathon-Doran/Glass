using Glass.Core.Logging;
using Glass.Data.Repositories;
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
    // Fills the zone combo with every known zone, displayed as "name (id)" and carrying the
    // boxed ZoneId in the item Tag.  No zone is selected initially, so the grid starts empty.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SpawnBrowser()
    {
        InitializeComponent();

        List<ZoneId> zoneIds = ZoneRepository.Instance.GetZoneIds();
        List<KeyValuePair<string, ZoneId>> populated = new List<KeyValuePair<string, ZoneId>>();

        foreach (ZoneId zoneId in zoneIds)
        {
            int spawnCount = MobRepository.Instance.GetSpawnsInZone(zoneId).Count;

            if (spawnCount == 0)
            {
                continue;
            }

            string label = ZoneRepository.Instance.GetZoneName(zoneId) + "  [" + zoneId.Value + "]  "
                + spawnCount + " spawns";
            populated.Add(new KeyValuePair<string, ZoneId>(label, zoneId));
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
            + " populated zone(s) in the list out of " + zoneIds.Count + " known", LogLevel.Trace);
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
    // Reads the selected zone's spawn records from the mob repository, keeps those that
    // pass the filter text, and fills the grid in the repository's order.  With no zone
    // selected the grid is emptied and the count line says so.  The count line reports
    // rows shown against rows read.
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
        List<Spawn> spawns = MobRepository.Instance.GetSpawnsInZone(zoneId);
        string filterText = FilterText.Text.Trim();

        List<SpawnBrowserRow> rows = new List<SpawnBrowserRow>(spawns.Count);

        foreach (Spawn spawn in spawns)
        {
            if (MatchesFilter(spawn, filterText))
            {
                rows.Add(new SpawnBrowserRow(spawn));
            }
        }

        ResultGrid.ItemsSource = rows;
        ResultCount.Text = rows.Count + " of " + spawns.Count + " spawns";

        DebugLog.Write(LogChannel.InferenceDebug, "SpawnBrowser.RefreshRows: zoneId=" + zoneId
            + " read " + spawns.Count + " record(s), showing " + rows.Count
            + " with filter '" + filterText + "'", LogLevel.Trace);
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
// the time the row was built.
///////////////////////////////////////////////////////////////////////////////////////////////
public class SpawnBrowserRow
{
    public uint SpawnIdDecimal { get; }
    public string SpawnIdHex { get; }
    public string Name { get; }
    public ushort Level { get; }
    public string LastSeen { get; }
    public uint A { get; }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SpawnBrowserRow  (constructor)
    //
    // Builds the display projections from the given record.  A null name shows as
    // "<null>"; a default LastSeen shows as an empty string.
    //
    // spawn:  The record to present.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SpawnBrowserRow(Spawn spawn)
    {
        SpawnIdDecimal = spawn.SpawnId.Value;
        SpawnIdHex = "0x" + spawn.SpawnId.Value.ToString("X4");
        Name = spawn.Name ?? "<null>";
        Level = spawn.Level;
        A = spawn.A;

        if (spawn.LastSeen == default)
        {
            LastSeen = string.Empty;
        }
        else
        {
            LastSeen = spawn.LastSeen.ToString("HH:mm:ss.fff");
        }
    }
}