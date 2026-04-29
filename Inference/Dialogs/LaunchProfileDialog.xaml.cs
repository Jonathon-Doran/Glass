using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Glass.Data.Repositories;
using Glass.Data.Models;
using Inference.Core;

namespace Inference.Dialogs;

///////////////////////////////////////////////////////////////////////////////////////////////
// LaunchProfileDialog
//
// Modal dialog for selecting a profile to launch. The profile list is filtered
// by the current patch level's server type. Test patch levels show only profiles
// with characters on the Test server. Live patch levels show all other profiles.
///////////////////////////////////////////////////////////////////////////////////////////////
public partial class LaunchProfileDialog : Window
{
    ///////////////////////////////////////////////////////////////////////////////////////////
    // SelectedProfileName
    //
    // The name of the selected profile. Valid after the dialog closes with OK.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string SelectedProfileName { get; private set; } = "";

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LaunchProfileDialog
    //
    // Constructs the dialog, initializes the XAML-defined components, and populates
    // the profile list filtered by the specified server type.
    //
    // serverType:  "Test" or "Live", used to filter profiles.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public LaunchProfileDialog(string serverType)
    {
        InitializeComponent();
        LoadFilteredProfiles(serverType);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LoadFilteredProfiles
    //
    // Queries all profile names, loads each profile's slot assignments, checks
    // the first character's server name, and adds profiles that match the
    // specified server type to the list.
    //
    // serverType:  "Test" shows profiles on the Test server.
    //              "Live" shows profiles on any non-Test server.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void LoadFilteredProfiles(string serverType)
    {
        List<string> allNames = ProfileRepository.GetAllNames();
        int matchCount = 0;

        foreach (string name in allNames)
        {
            ProfileRepository profileRepo = new ProfileRepository(name);
            IReadOnlyList<SlotAssignment> slots = profileRepo.GetSlots();

            if (slots.Count == 0)
            {
                InferenceDebugLog.Write("LaunchProfileDialog.LoadFilteredProfiles: profile '"
                    + name + "' has no slots, skipping");
                continue;
            }

            Character? firstCharacter = CharacterRepository.Instance.GetById(slots[0].CharacterId);
            if (firstCharacter == null)
            {
                InferenceDebugLog.Write("LaunchProfileDialog.LoadFilteredProfiles: profile '"
                    + name + "' first character not found, skipping");
                continue;
            }

            bool isTestProfile = firstCharacter.Server == "Test";
            bool matches = (serverType == "Test" && isTestProfile)
                        || (serverType == "Live" && !isTestProfile);

            if (matches)
            {
                ProfileList.Items.Add(name);
                matchCount++;
            }
        }

        InferenceDebugLog.Write("LaunchProfileDialog.LoadFilteredProfiles: "
            + matchCount + " profiles matched server type " + serverType);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ProfileList_SelectionChanged
    //
    // Handles selection changes in the profile list. Enables the Launch button
    // when a profile is selected.
    //
    // sender:  The list box that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ButtonLaunch.IsEnabled = ProfileList.SelectedItem != null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_Launch_Click
    //
    // Handles the Launch button click. Sets the selected profile name and closes
    // the dialog with a positive result.
    //
    // sender:  The button that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_Launch_Click(object sender, RoutedEventArgs e)
    {
        SelectedProfileName = (string)ProfileList.SelectedItem;
        ProfileRepository profile = new ProfileRepository(SelectedProfileName);
        CharacterRepository.Instance.Load(profile.GetCharacterIds());
        InferenceDebugLog.Write("LaunchProfileDialog.Button_Launch_Click: selected '"
            + SelectedProfileName + "'");

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
        InferenceDebugLog.Write("LaunchProfileDialog.Button_Cancel_Click: cancelled");

        DialogResult = false;
        Close();
    }
}
