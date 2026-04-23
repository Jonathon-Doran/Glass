using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Inference.Core;

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
        LoadRecentPatches();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LoadRecentPatches
    //
    // Reads the RecentPatches setting and populates the list box. Each entry is
    // stored as "ServerType|PatchDate".
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void LoadRecentPatches()
    {
        StringCollection? recentPatches = Properties.Settings.Default.RecentPatches;
        if (recentPatches == null)
        {
            InferenceDebugLog.Write("OpenDialog.LoadRecentPatches: no recent patches found");
            return;
        }

        foreach (string? entry in recentPatches)
        {
            PatchList.Items.Add(entry);
        }

        InferenceDebugLog.Write("OpenDialog.LoadRecentPatches: loaded " + recentPatches.Count + " recent patches");
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
    // Handles the Open button click. Parses the selected entry and closes the
    // dialog with a positive result.
    //
    // sender:  The button that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_Open_Click(object sender, RoutedEventArgs e)
    {
        string selected = (string)PatchList.SelectedItem;
        string[] parts = selected.Split('|');
        if (parts.Length != 2)
        {
            InferenceDebugLog.Write("OpenDialog.Button_Open_Click: malformed entry: " + selected);
            return;
        }

        ServerType = parts[0];
        PatchDate = parts[1];

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
