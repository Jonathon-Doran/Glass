using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Glass;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ProfileLaunchDialog
//
// Modal dialog for choosing a live profile to launch.  The list shows each profile's name, server,
// character count and description, optionally limited to one server type.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public partial class ProfileLaunchDialog : Window
{
    private readonly string? _serverType;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SelectedProfileName
    //
    // The name of the chosen profile.  Valid after the dialog closes with a true result.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public string? SelectedProfileName { get; private set; }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ProfileLaunchDialog
    //
    // Builds the dialog and fills the profile list.
    //
    // serverType:  The server type to limit the list to, or null to list every live profile.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public ProfileLaunchDialog(string? serverType = null)
    {
        InitializeComponent();
        _serverType = serverType;

        DebugLog.Write(LogChannel.Profiles, "ProfileLaunchDialog: serverType filter='"
            + (_serverType ?? "none") + "'.", LogLevel.Trace);

        LoadProfiles();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadProfiles
    //
    // Fills the list with summaries of live profiles.  With no server type filter every live profile
    // is listed.  A filter of "Test" lists profiles whose server type is Test; any other filter lists
    // profiles whose server type is not Test.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadProfiles()
    {
        List<ProfileSummary> summaries = ProfileRepository.GetSummaries(false);

        if (_serverType != null)
        {
            bool wantTest = (_serverType == "Test");
            summaries = summaries.Where(s => (s.ServerType == "Test") == wantTest).ToList();
        }

        ProfileList.ItemsSource = summaries;

        DebugLog.Write(LogChannel.Profiles, "ProfileLaunchDialog.LoadProfiles: listed " + summaries.Count
            + " profiles, serverType filter='" + (_serverType ?? "none") + "'.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ProfileList_SelectionChanged
    //
    // Records the selected profile's name and enables Launch when a profile is selected.
    //
    // sender:  The profile list.
    // e:       Event arguments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ProfileSummary? selected = ProfileList.SelectedItem as ProfileSummary;
        SelectedProfileName = selected?.Name;
        LaunchButton.IsEnabled = (selected != null);

        DebugLog.Write(LogChannel.Profiles, "ProfileLaunchDialog.ProfileList_SelectionChanged: selected='"
            + (SelectedProfileName ?? "none") + "'.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Launch_Click
    //
    // Closes the dialog with a true result so the selected profile can be launched.  Does nothing
    // when no profile is selected.
    //
    // sender:  The Launch button.
    // e:       Event arguments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfileName == null)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileLaunchDialog.Launch_Click: no profile selected.", LogLevel.Warn);
            return;
        }

        DebugLog.Write(LogChannel.Profiles, "ProfileLaunchDialog.Launch_Click: launching '" + SelectedProfileName + "'.", LogLevel.Trace);
        DialogResult = true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Cancel_Click
    //
    // Closes the dialog with a false result, so nothing is launched.
    //
    // sender:  The Cancel button.
    // e:       Event arguments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.Profiles, "ProfileLaunchDialog.Cancel_Click: cancelled.", LogLevel.Trace);
        DialogResult = false;
    }
}