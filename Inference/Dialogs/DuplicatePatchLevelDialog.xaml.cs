using System;
using System.Collections.Generic;
using System.Windows;
using Glass.Core.Logging;
using Microsoft.Data.Sqlite;

namespace Inference.Dialogs;

///////////////////////////////////////////////////////////////////////////////////////////////
// DuplicatePatchLevelDialog
//
// Modal dialog for duplicating an existing patch level into a new target patch date.
// The source combobox lists every (patch_date, server_type) pair present in
// PatchOpcode, with the most recent entry selected by default.  Server type carries
// over from the chosen source row.
//
// The caller owns the SqliteConnection lifetime; the dialog only reads from it during
// construction to populate the source list.
///////////////////////////////////////////////////////////////////////////////////////////////
public partial class DuplicatePatchLevelDialog : Window
{
    private readonly List<PatchLevelEntry> _entries;


    ///////////////////////////////////////////////////////////////////////////////////////////
    // SourcePatchDate
    //
    // The selected source patch date.  Valid after the dialog closes with OK.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string SourcePatchDate { get; private set; } = string.Empty;


    ///////////////////////////////////////////////////////////////////////////////////////////
    // SourceServerType
    //
    // The selected source server type.  Valid after the dialog closes with OK.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string SourceServerType { get; private set; } = string.Empty;


    ///////////////////////////////////////////////////////////////////////////////////////////
    // TargetPatchDate
    //
    // The target patch date in yyyy-MM-dd form.  Valid after the dialog closes with OK.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string TargetPatchDate { get; private set; } = string.Empty;


    ///////////////////////////////////////////////////////////////////////////////////////////
    // DuplicatePatchLevelDialog (constructor)
    //
    // Constructs the dialog, initializes XAML components, and populates the source
    // combobox by querying PatchOpcode for distinct (patch_date, server_type) pairs.
    //
    // connection:  An open SQLite connection to the Glass database.  The dialog
    //              reads from it only during this constructor.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public DuplicatePatchLevelDialog(SqliteConnection connection)
    {
        InitializeComponent();

        if (connection == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "DuplicatePatchLevelDialog.ctor: connection is null", LogLevel.Error);
            throw new ArgumentNullException(nameof(connection));
        }

        _entries = LoadPatchLevels(connection);
        DebugLog.Write(LogChannel.InferenceDebug,
            "DuplicatePatchLevelDialog.ctor: loaded " + _entries.Count + " patch levels", LogLevel.Trace);

        for (int entryIndex = 0; entryIndex < _entries.Count; entryIndex++)
        {
            PatchLevelEntry entry = _entries[entryIndex];
            SourceComboBox.Items.Add(entry.PatchDate + " (" + entry.ServerType + ")");
        }

        if (_entries.Count > 0)
        {
            SourceComboBox.SelectedIndex = 0;
        }
        else
        {
            StatusText.Text = "No patch levels found in the database.";
            DebugLog.Write(LogChannel.InferenceDebug,
                "DuplicatePatchLevelDialog.ctor: PatchOpcode contains no rows", LogLevel.Error);
        }
    }


    ///////////////////////////////////////////////////////////////////////////////////////////
    // LoadPatchLevels
    //
    // Reads every distinct (patch_date, server_type) pair from PatchOpcode, sorted with
    // the most recent patch date first.
    //
    // connection:  An open SQLite connection to the Glass database.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private List<PatchLevelEntry> LoadPatchLevels(SqliteConnection connection)
    {
        List<PatchLevelEntry> entries = new List<PatchLevelEntry>();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT DISTINCT patch_date, server_type " +
            "FROM PatchOpcode " +
            "ORDER BY patch_date DESC, server_type ASC";

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            PatchLevelEntry entry = new PatchLevelEntry();
            entry.PatchDate = reader.GetString(0);
            entry.ServerType = reader.GetString(1);
            entries.Add(entry);
        }

        return entries;
    }


    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_OK_Click
    //
    // Handles the OK button click.  Validates the selection, reads the target date,
    // and closes the dialog with a positive result.  On any validation failure,
    // updates StatusText and leaves the dialog open.
    //
    // sender:  The button that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_OK_Click(object sender, RoutedEventArgs e)
    {
        int selectedIndex = SourceComboBox.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= _entries.Count)
        {
            StatusText.Text = "Select a source patch level.";
            DebugLog.Write(LogChannel.InferenceDebug,
                "DuplicatePatchLevelDialog.OK: no source selected", LogLevel.Info);
            return;
        }

        DateTime? selectedDate = TargetDatePicker.SelectedDate;
        if (!selectedDate.HasValue)
        {
            StatusText.Text = "Select a target patch date.";
            DebugLog.Write(LogChannel.InferenceDebug,
                "DuplicatePatchLevelDialog.OK: no target date selected", LogLevel.Info);
            return;
        }

        PatchLevelEntry source = _entries[selectedIndex];
        string targetDateText = selectedDate.Value.ToString("yyyy-MM-dd");

        if (string.Equals(targetDateText, source.PatchDate, StringComparison.Ordinal))
        {
            StatusText.Text = "Target date must differ from the source patch date.";
            DebugLog.Write(LogChannel.InferenceDebug,
                "DuplicatePatchLevelDialog.OK: target date equals source date (" + targetDateText + ")", LogLevel.Info);
            return;
        }

        SourcePatchDate = source.PatchDate;
        SourceServerType = source.ServerType;
        TargetPatchDate = targetDateText;

        DebugLog.Write(LogChannel.InferenceDebug,
            "DuplicatePatchLevelDialog.OK: source=(" + SourcePatchDate + "," + SourceServerType
            + ") target=" + TargetPatchDate, LogLevel.Trace);

        DialogResult = true;
        Close();
    }


    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_Cancel_Click
    //
    // Handles the Cancel button click.  Closes the dialog with a negative result.
    //
    // sender:  The button that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }


    ///////////////////////////////////////////////////////////////////////////////////////////
    // PatchLevelEntry
    //
    // One (patch_date, server_type) pair read from PatchOpcode.  Used both as the data
    // source for the combobox and as the post-selection lookup target.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class PatchLevelEntry
    {
        public string PatchDate = string.Empty;
        public string ServerType = string.Empty;
    }
}