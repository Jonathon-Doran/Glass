using System;
using System.Collections.Generic;
using System.Windows;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Inference.Core;
using Microsoft.Data.Sqlite;

namespace Inference.Dialogs;

///////////////////////////////////////////////////////////////////////////////////////////////
// ManagePatchLevelsDialog
//
// Dialog for creating, duplicating, renaming, and deleting patch levels.  Operations
// execute immediately against the database and the list refreshes after each.
///////////////////////////////////////////////////////////////////////////////////////////////
public partial class ManagePatchLevelsDialog : Window
{
    private readonly SqliteConnection _connection;
    private readonly PatchLevelManager _manager;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LevelRow
    //
    // One patch level as displayed in the list.  Properties rather than fields because
    // WPF bindings require properties.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class LevelRow
    {
        public string PatchDate { get; set; } = string.Empty;
        public string ServerType { get; set; } = string.Empty;
        public int OpcodeCount { get; set; }

        public PatchLevel Level
        {
            get { return new PatchLevel(PatchDate, ServerType); }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ManagePatchLevelsDialog (constructor)
    //
    // Constructs the dialog and populates the patch level list.
    //
    // connection:  An open SQLite connection to the Glass database.  The caller owns
    //              the connection lifetime, which must span the dialog's lifetime.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public ManagePatchLevelsDialog(SqliteConnection connection)
    {
        InitializeComponent();

        if (connection == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "ManagePatchLevelsDialog.ctor: connection is null", LogLevel.Error);
            throw new ArgumentNullException(nameof(connection));
        }

        _connection = connection;
        _manager = new PatchLevelManager(connection);

        DebugLog.Write(LogChannel.InferenceDebug, "ManagePatchLevelsDialog.ctor: constructed", LogLevel.Trace);

        RefreshLevelList();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RefreshLevelList
    //
    // Reloads the patch level list from the database and rebinds the ListView,
    // preserving the current selection when the selected level still exists.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void RefreshLevelList()
    {
        DebugLog.Write(LogChannel.InferenceDebug, "ManagePatchLevelsDialog.RefreshLevelList: refreshing", LogLevel.Trace);

        LevelRow? selected = LevelListView.SelectedItem as LevelRow;

        List<PatchLevelSummary> summaries = _manager.GetAllLevels();
        List<LevelRow> rows = new List<LevelRow>();

        foreach (PatchLevelSummary summary in summaries)
        {
            LevelRow row = new LevelRow();
            row.PatchDate = summary.Level.PatchDate;
            row.ServerType = summary.Level.ServerType;
            row.OpcodeCount = summary.OpcodeCount;
            rows.Add(row);
        }

        LevelListView.ItemsSource = rows;

        if (selected != null)
        {
            foreach (LevelRow row in rows)
            {
                if (row.Level == selected.Level)
                {
                    LevelListView.SelectedItem = row;
                    break;
                }
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "ManagePatchLevelsDialog.RefreshLevelList: " + rows.Count + " levels listed", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_New_Click
    //
    // Opens the new patch level dialog and creates the chosen level.  On failure
    // (typically a duplicate level) the error is shown in the status bar and the
    // list is left unchanged.
    //
    // sender:  The button that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_New_Click(object sender, RoutedEventArgs e)
    {
        NewPatchLevelDialog dialog = new NewPatchLevelDialog { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "ManagePatchLevelsDialog.New: cancelled", LogLevel.Trace);
            return;
        }

        PatchLevel level = new PatchLevel(dialog.PatchDate.ToString("yyyy-MM-dd"), dialog.ServerType);

        DebugLog.Write(LogChannel.InferenceDebug,
            "ManagePatchLevelsDialog.New: creating " + level, LogLevel.Trace);

        try
        {
            _manager.Create(level);
            StatusText.Text = "Created " + level + ".";
        }
        catch (Exception ex)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "ManagePatchLevelsDialog.New: create failed: " + ex.Message, LogLevel.Error);
            StatusText.Text = "Create failed: " + ex.Message;
            return;
        }

        RefreshLevelList();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_Duplicate_Click
    //
    // Duplicates the selected patch level into a target level collected from the
    // operator.  On failure the error is shown in the status bar and the list is
    // left unchanged.
    //
    // sender:  The button that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_Duplicate_Click(object sender, RoutedEventArgs e)
    {
        LevelRow? selected = LevelListView.SelectedItem as LevelRow;
        if (selected == null)
        {
            StatusText.Text = "Select a patch level to duplicate.";
            DebugLog.Write(LogChannel.InferenceDebug, "ManagePatchLevelsDialog.Duplicate: no selection", LogLevel.Trace);
            return;
        }

        PatchLevel target = _manager.GenerateUniqueLevel(selected.Level);
        DebugLog.Write(LogChannel.InferenceDebug,
            "ManagePatchLevelsDialog.Duplicate: source=" + selected.Level + " target=" + target, LogLevel.Trace);

        try
        {
            PatchLevelDuplicator duplicator = new PatchLevelDuplicator(_connection);
            int opcodeCount = duplicator.Duplicate(selected.PatchDate, selected.ServerType, target.PatchDate);
            StatusText.Text = "Duplicated " + opcodeCount + " opcodes to " + target + ".";
        }
        catch (Exception ex)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "ManagePatchLevelsDialog.Duplicate: failed: " + ex.Message, LogLevel.Error);
            StatusText.Text = "Duplicate failed: " + ex.Message;
            return;
        }

        RefreshLevelList();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_Rename_Click
    //
    // Renames the selected patch level to a target level collected from the operator.
    // On failure the error is shown in the status bar and the list is left unchanged.
    //
    // sender:  The button that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_Rename_Click(object sender, RoutedEventArgs e)
    {
        LevelRow? selected = LevelListView.SelectedItem as LevelRow;
        if (selected == null)
        {
            StatusText.Text = "Select a patch level to rename.";
            DebugLog.Write(LogChannel.InferenceDebug, "ManagePatchLevelsDialog.Rename: no selection", LogLevel.Trace);
            return;
        }

        NewPatchLevelDialog dialog = new NewPatchLevelDialog("Rename Patch Level") { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "ManagePatchLevelsDialog.Rename: cancelled", LogLevel.Trace);
            return;
        }

        PatchLevel newLevel = new PatchLevel(dialog.PatchDate.ToString("yyyy-MM-dd"), dialog.ServerType);

        DebugLog.Write(LogChannel.InferenceDebug,
            "ManagePatchLevelsDialog.Rename: level=" + selected.Level + " newLevel=" + newLevel, LogLevel.Trace);

        try
        {
            int updated = _manager.Rename(selected.Level, newLevel);
            StatusText.Text = "Renamed to " + newLevel + " (" + updated + " rows).";
        }
        catch (Exception ex)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "ManagePatchLevelsDialog.Rename: failed: " + ex.Message, LogLevel.Error);
            StatusText.Text = "Rename failed: " + ex.Message;
            return;
        }

        RefreshLevelList();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_Delete_Click
    //
    // Deletes the selected patch level after operator confirmation.  On failure the
    // error is shown in the status bar and the list is left unchanged.
    //
    // sender:  The button that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_Delete_Click(object sender, RoutedEventArgs e)
    {
        LevelRow? selected = LevelListView.SelectedItem as LevelRow;
        if (selected == null)
        {
            StatusText.Text = "Select a patch level to delete.";
            DebugLog.Write(LogChannel.InferenceDebug, "ManagePatchLevelsDialog.Delete: no selection", LogLevel.Trace);
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            "Delete patch level " + selected.Level + " (" + selected.OpcodeCount + " opcodes)?",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "ManagePatchLevelsDialog.Delete: cancelled", LogLevel.Trace);
            return;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "ManagePatchLevelsDialog.Delete: deleting " + selected.Level, LogLevel.Trace);

        try
        {
            int deleted = _manager.Delete(selected.Level);
            StatusText.Text = "Deleted " + selected.Level + " (" + deleted + " rows).";
        }
        catch (Exception ex)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "ManagePatchLevelsDialog.Delete: failed: " + ex.Message, LogLevel.Error);
            StatusText.Text = "Delete failed: " + ex.Message;
            return;
        }

        RefreshLevelList();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_Close_Click
    //
    // Closes the dialog.
    //
    // sender:  The button that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
