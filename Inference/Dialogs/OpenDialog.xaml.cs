using Inference.Core;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace Inference.Dialogs;

///////////////////////////////////////////////////////////////////////////////////////////////
// OpenDialog
//
// Modal dialog for selecting an existing patch level from the recent patches list.
// Returns the selected server type and patch date.
///////////////////////////////////////////////////////////////////////////////////////////////
public partial class OpenDialog : Window
{
    ///////////////////////////////////////////////////////////////////////////////////////////
    // ServerType
    //
    // The server type of the selected patch level. Valid after the dialog closes with OK.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string ServerType { get; private set; } = "";

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PatchDate
    //
    // The patch date of the selected patch level. Valid after the dialog closes with OK.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string PatchDate { get; private set; } = "";

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OpenDialog
    //
    // Constructs the dialog, initializes the XAML-defined components, and populates
    // the list from the RecentPatches setting.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OpenDialog()
    {
        InitializeComponent();
        LoadPatchLevels();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LoadPatchLevels
    //
    // Loads all distinct patch levels from the PatchOpcode table and populates
    // the PatchList with entries formatted as "2026-04-15 (Live)". Results are
    // ordered by patch_date descending so the most recent patch appears first.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void LoadPatchLevels()
    {
        InferenceDebugLog.Write("OpenDialog.LoadPatchLevels: loading patch levels from database");
        using (SqliteConnection connection = Glass.Data.Database.Instance.Connect())
        {
            connection.Open();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT DISTINCT patch_date, server_type FROM PatchOpcode"
                    + " ORDER BY patch_date DESC";
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string patchDate = reader.GetString(0);
                        string serverType = reader.GetString(1);
                        string displayServerType = serverType.Substring(0, 1).ToUpper()
                            + serverType.Substring(1);
                        string entry = patchDate + " (" + displayServerType + ")";
                        PatchList.Items.Add(entry);
                    }
                }
            }
        }
        InferenceDebugLog.Write("OpenDialog.LoadPatchLevels: loaded " + PatchList.Items.Count
            + " patch levels");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PatchList_SelectionChanged
    //
    // Handles selection changes in the patch list. Enables the Open button when
    // a patch is selected.
    //
    // sender:  The list box that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void PatchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ButtonOpen.IsEnabled = PatchList.SelectedItem != null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_Open_Click
    //
    // Handles the Open button click. Parses the selected patch level entry
    // and sets PatchDate and ServerType for the caller.
    //
    // sender:  The button that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_Open_Click(object sender, RoutedEventArgs e)
    {
        if (PatchList.SelectedItem == null)
        {
            return;
        }
        string selected = (string)PatchList.SelectedItem;
        int spaceIndex = selected.IndexOf(' ');
        int openParen = selected.IndexOf('(');
        int closeParen = selected.IndexOf(')');
        if (spaceIndex < 0 || openParen < 0 || closeParen < 0 || closeParen <= openParen + 1)
        {
            InferenceDebugLog.Write("OpenDialog.Button_Open_Click: malformed entry: " + selected);
            return;
        }
        PatchDate = selected.Substring(0, spaceIndex);
        ServerType = selected.Substring(openParen + 1, closeParen - openParen - 1);
        InferenceDebugLog.Write("OpenDialog.Button_Open_Click: opened ServerType=" + ServerType
            + " PatchDate=" + PatchDate);
        DialogResult = true;
        Close();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_Cancel_Click
    //
    // Handles the Cancel button click. Closes the dialog with a negative result.
    //
    // sender:  The button that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_Cancel_Click(object sender, RoutedEventArgs e)
    {
        InferenceDebugLog.Write("OpenDialog.Button_Cancel_Click: cancelled");

        DialogResult = false;
        Close();
    }
}
