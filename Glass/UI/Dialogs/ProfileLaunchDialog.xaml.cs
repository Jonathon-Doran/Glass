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
    private List<ProfileSummary> _profiles = new List<ProfileSummary>();
    private const string AllServers = "All servers";

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
    // Reads summaries of live profiles, limited by the server type filter when one was given, and fills
    // the server combo box with "All servers" followed by each server those profiles use.  With no
    // server type filter every live profile is kept.  A filter of "Test" keeps profiles whose server
    // type is Test; any other filter keeps profiles whose server type is not Test.  Selecting "All
    // servers" fills the list.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadProfiles()
    {
        List<ProfileSummary> summaries = ProfileRepository.GetSummaries(false);

        if (_serverType != null)
        {
            bool wantTest = (_serverType == "Test");
            summaries = summaries.Where(s => (s.ServerType == "Test") == wantTest).ToList();
        }

        _profiles = summaries;

        List<string> servers = _profiles
            .Select(s => s.Server)
            .Where(server => !string.IsNullOrEmpty(server))
            .Distinct()
            .OrderBy(server => server)
            .ToList();

        ServerComboBox.Items.Clear();
        ServerComboBox.Items.Add(AllServers);
        foreach (string server in servers)
        {
            ServerComboBox.Items.Add(server);
        }

        DebugLog.Write(LogChannel.Profiles, "ProfileLaunchDialog.LoadProfiles: " + _profiles.Count
            + " profiles on " + servers.Count + " servers, serverType filter='" + (_serverType ?? "none") + "'.", LogLevel.Trace);

        ServerComboBox.SelectedIndex = 0;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ApplyServerFilter
    //
    // Fills the list with the loaded profiles on the server chosen in the server combo box, or with all
    // loaded profiles when "All servers" is chosen.  Clears the selection, which disables Launch.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ApplyServerFilter()
    {
        string? server = ServerComboBox.SelectedItem as string;

        List<ProfileSummary> shown = ((server == null) || (server == AllServers))
            ? _profiles
            : _profiles.Where(s => s.Server == server).ToList();

        ProfileList.ItemsSource = shown;
        ProfileList.SelectedItem = null;
        SelectedProfileName = null;
        LaunchButton.IsEnabled = false;

        DebugLog.Write(LogChannel.Profiles, "ProfileLaunchDialog.ApplyServerFilter: server='" + (server ?? "none")
            + "' showing " + shown.Count + " of " + _profiles.Count + " profiles.", LogLevel.Trace);
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
    // ServerComboBox_SelectionChanged
    //
    // Refilters the profile list when a different server is chosen.
    //
    // sender:  The server combo box.
    // e:       Event arguments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ServerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DebugLog.Write(LogChannel.Profiles, "ProfileLaunchDialog.ServerComboBox_SelectionChanged: server='"
            + ((ServerComboBox.SelectedItem as string) ?? "none") + "'.", LogLevel.Trace);
        ApplyServerFilter();
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