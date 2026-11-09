using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using System.Windows;

namespace Glass;

public partial class SelectProfileDialog : Window
{
    public string? SelectedProfileName { get; private set; }
    private ProfileSummary? _selectedSummary = null;

    public SelectProfileDialog()
    {
        InitializeComponent();
        LoadProfiles();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadProfiles
    //
    // Fills the profile list with summaries of live profiles, or of deleted profiles when Show deleted
    // is checked, clears the selection, name box and description box, and shows the buttons that
    // apply to that list.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadProfiles()
    {
        bool showDeleted = (ShowDeletedCheckBox.IsChecked == true);

        List<ProfileSummary> summaries = ProfileRepository.GetSummaries(showDeleted);
        ProfileList.ItemsSource = summaries;
        _selectedSummary = null;
        SelectedProfileName = null;
        NameTextBox.Text = string.Empty;
        DescriptionTextBox.Text = string.Empty;

        Visibility liveVisibility = showDeleted ? Visibility.Collapsed : Visibility.Visible;
        Visibility deletedVisibility = showDeleted ? Visibility.Visible : Visibility.Collapsed;

        EditButton.Visibility = liveVisibility;
        RenameButton.Visibility = liveVisibility;
        DeleteButton.Visibility = liveVisibility;
        RestoreButton.Visibility = deletedVisibility;
        EmptyDeletedButton.Visibility = deletedVisibility;
        NameTextBox.IsEnabled = !showDeleted;
        EmptyDeletedButton.IsEnabled = showDeleted && (summaries.Count > 0);

        DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.LoadProfiles: showDeleted=" + showDeleted
            + " count=" + summaries.Count + ".", LogLevel.Trace);

        UpdateButtons();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ProfileList_SelectionChanged
    //
    // Records the selected profile, copies its name into the name box as the starting point for a
    // rename and its description into the description box, and updates which buttons are enabled.
    //
    // sender:  The profile list.
    // e:       Event arguments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ProfileList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _selectedSummary = ProfileList.SelectedItem as ProfileSummary;
        SelectedProfileName = _selectedSummary?.Name;
        NameTextBox.Text = _selectedSummary?.Name ?? string.Empty;
        DescriptionTextBox.Text = _selectedSummary?.Description ?? string.Empty;

        DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.ProfileList_SelectionChanged: selected='"
            + (SelectedProfileName ?? "none") + "'.", LogLevel.Trace);

        UpdateButtons();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UpdateButtons
    //
    // Enables the buttons that act on a selected profile when one is selected.  Rename is enabled only
    // when the name box holds a non-blank name different from the selected profile's name.  Save
    // Description is enabled only when the description box differs from the selected profile's
    // stored description.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void UpdateButtons()
    {
        bool hasSelection = (_selectedSummary != null);
        string newName = NameTextBox.Text.Trim();
        bool renameReady = hasSelection && (newName.Length > 0) && (newName != SelectedProfileName);
        bool descriptionChanged = hasSelection && (DescriptionTextBox.Text.Trim() != _selectedSummary!.Description);

        EditButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
        RestoreButton.IsEnabled = hasSelection;
        RenameButton.IsEnabled = renameReady;
        SaveDescriptionButton.IsEnabled = descriptionChanged;

        DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.UpdateButtons: selected='"
            + (SelectedProfileName ?? "none") + "' renameReady=" + renameReady
            + " descriptionChanged=" + descriptionChanged + ".", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // NameTextBox_TextChanged
    //
    // Updates which buttons are enabled when the name box changes, so Rename tracks the typed name.
    //
    // sender:  The name box.
    // e:       Event arguments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void NameTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.NameTextBox_TextChanged: text='"
            + NameTextBox.Text + "'.", LogLevel.Trace);
        UpdateButtons();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ShowDeletedCheckBox_Changed
    //
    // Switches the list between live and deleted profiles when Show deleted is checked or unchecked.
    //
    // sender:  The Show deleted check box.
    // e:       Event arguments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ShowDeletedCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.ShowDeletedCheckBox_Changed: showDeleted="
            + (ShowDeletedCheckBox.IsChecked == true) + ".", LogLevel.Trace);
        LoadProfiles();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DescriptionTextBox_TextChanged
    //
    // Updates which buttons are enabled when the description box changes, so Save Description tracks
    // whether the text differs from the stored description.
    //
    // sender:  The description box.
    // e:       Event arguments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void DescriptionTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.DescriptionTextBox_TextChanged: length="
            + DescriptionTextBox.Text.Length + ".", LogLevel.Trace);
        UpdateButtons();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SaveDescription_Click
    //
    // Stores the description box text as the selected profile's description, updates the selected
    // row to match, and refreshes the list display.
    //
    // sender:  The Save Description button.
    // e:       Event arguments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void SaveDescription_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSummary == null)
        {
            DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.SaveDescription_Click: no profile selected.", LogLevel.Warn);
            return;
        }

        string description = DescriptionTextBox.Text.Trim();

        if (!ProfileRepository.Instance.SetDescription(_selectedSummary.Name, description))
        {
            DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.SaveDescription_Click: could not store description for '"
                + _selectedSummary.Name + "'.", LogLevel.Error);
            MessageBox.Show($"Could not save the description for '{_selectedSummary.Name}'.", "Save Failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _selectedSummary.Description = description;
        ProfileList.Items.Refresh();
        DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.SaveDescription_Click: description saved for '"
            + _selectedSummary.Name + "'.", LogLevel.Trace);

        UpdateButtons();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Edit_Click
    //
    // Closes the dialog with a true result so the selected profile can be opened for editing.  Does
    // nothing when no profile is selected.
    //
    // sender:  The Edit button.
    // e:       Event arguments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfileName == null)
        {
            DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.Edit_Click: no profile selected.", LogLevel.Warn);
            return;
        }

        DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.Edit_Click: editing '" + SelectedProfileName + "'.", LogLevel.Trace);
        DialogResult = true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Rename_Click
    //
    // Renames the selected profile to the name in the name box.  Refuses a name held by a live
    // profile.  When a deleted profile holds the name, asks whether to remove it permanently and
    // continue.  Updates the recent profiles list and reselects the renamed profile.
    //
    // sender:  The Rename button.
    // e:       Event arguments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfileName == null)
        {
            DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.Rename_Click: no profile selected.", LogLevel.Warn);
            return;
        }

        string oldName = SelectedProfileName;
        string newName = NameTextBox.Text.Trim();
        ProfileRepository profileRepo = ProfileRepository.Instance;

        if (profileRepo.ProfileExists(newName))
        {
            DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.Rename_Click: '" + newName
                + "' is held by a live profile, '" + oldName + "' not renamed.", LogLevel.Trace);
            MessageBox.Show($"A profile named '{newName}' already exists.", "Name In Use",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (profileRepo.DeletedProfileExists(newName))
        {
            MessageBoxResult result = MessageBox.Show(
                $"A deleted profile named '{newName}' exists. Permanently remove it and continue?",
                "Deleted Profile Exists", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.Rename_Click: user kept deleted profile '"
                    + newName + "', '" + oldName + "' not renamed.", LogLevel.Trace);
                return;
            }

            if (!profileRepo.PurgeProfile(newName))
            {
                DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.Rename_Click: could not remove deleted profile '"
                    + newName + "', '" + oldName + "' not renamed.", LogLevel.Error);
                return;
            }

            DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.Rename_Click: removed deleted profile '"
                + newName + "'.", LogLevel.Trace);
        }

        if (!profileRepo.RenameProfile(oldName, newName))
        {
            DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.Rename_Click: rename of '" + oldName
                + "' to '" + newName + "' failed.", LogLevel.Error);
            MessageBox.Show($"Could not rename '{oldName}' to '{newName}'.", "Rename Failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RecentProfiles.Rename(oldName, newName);
        DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.Rename_Click: renamed '" + oldName
            + "' to '" + newName + "'.", LogLevel.Trace);

        LoadProfiles();
        ProfileList.SelectedItem = (ProfileList.ItemsSource as List<ProfileSummary>)?
            .FirstOrDefault(s => s.Name == newName);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Delete_Click
    //
    // After confirmation, marks the selected profile deleted, removes it from the recent profiles
    // list and reloads the profile list.  The profile can be restored from the deleted list.
    //
    // sender:  The Delete button.
    // e:       Event arguments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfileName == null)
        {
            DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.Delete_Click: no profile selected.", LogLevel.Warn);
            return;
        }

        string profileName = SelectedProfileName;
        MessageBoxResult result = MessageBox.Show(
            $"Delete profile '{profileName}'? It can be restored from the deleted list.",
            "Delete Profile", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.Delete_Click: user cancelled delete of '"
                + profileName + "'.", LogLevel.Trace);
            return;
        }

        if (!ProfileRepository.Instance.DeleteProfile(profileName))
        {
            DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.Delete_Click: delete of '" + profileName
                + "' failed.", LogLevel.Error);
            MessageBox.Show($"Could not delete '{profileName}'.", "Delete Failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RecentProfiles.Remove(profileName);
        DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.Delete_Click: deleted '" + profileName + "'.", LogLevel.Trace);
        LoadProfiles();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Restore_Click
    //
    // Restores the selected deleted profile and reloads the deleted profile list.
    //
    // sender:  The Restore button.
    // e:       Event arguments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfileName == null)
        {
            DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.Restore_Click: no profile selected.", LogLevel.Warn);
            return;
        }

        string profileName = SelectedProfileName;

        if (!ProfileRepository.Instance.RestoreProfile(profileName))
        {
            DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.Restore_Click: restore of '" + profileName
                + "' failed.", LogLevel.Error);
            MessageBox.Show($"Could not restore '{profileName}'.", "Restore Failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.Restore_Click: restored '" + profileName + "'.", LogLevel.Trace);
        LoadProfiles();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // EmptyDeleted_Click
    //
    // After confirmation, permanently removes every deleted profile and reloads the deleted profile
    // list.  Reports any profile that could not be removed.
    //
    // sender:  The Empty Deleted button.
    // e:       Event arguments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void EmptyDeleted_Click(object sender, RoutedEventArgs e)
    {
        List<string> deletedNames = ProfileRepository.GetDeletedNames();

        if (deletedNames.Count == 0)
        {
            DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.EmptyDeleted_Click: no deleted profiles.", LogLevel.Trace);
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            $"Permanently remove {deletedNames.Count} deleted profile(s)? This cannot be undone.",
            "Empty Deleted Profiles", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.EmptyDeleted_Click: user cancelled.", LogLevel.Trace);
            return;
        }

        List<string> failedNames = new List<string>();
        foreach (string profileName in deletedNames)
        {
            if (!ProfileRepository.Instance.PurgeProfile(profileName))
            {
                DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.EmptyDeleted_Click: could not remove '"
                    + profileName + "'.", LogLevel.Error);
                failedNames.Add(profileName);
            }
        }

        DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.EmptyDeleted_Click: removed "
            + (deletedNames.Count - failedNames.Count) + " of " + deletedNames.Count + " deleted profiles.", LogLevel.Trace);

        if (failedNames.Count > 0)
        {
            MessageBox.Show("Could not remove: " + string.Join(", ", failedNames), "Empty Deleted Profiles",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        LoadProfiles();

    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Close_Click
    //
    // Closes the dialog with a false result, so no profile is opened for editing.
    //
    // sender:  The Close button.
    // e:       Event arguments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.Profiles, "SelectProfileDialog.Close_Click: closed.", LogLevel.Trace);
        DialogResult = false;
    }
}