using Glass.ClientUI;
using Glass.Controls;
using Glass.Core;
using Glass.Core.Logging;
using Glass.Data;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.UI.ViewModels;
using ModernWpf.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using KeyBinding = Glass.Data.Models.KeyBinding;
using ListView = System.Windows.Controls.ListView;
using ListViewItem = System.Windows.Controls.ListViewItem;

namespace Glass;

/// <summary>
/// Interaction logic for ProfileDialog.xaml
/// </summary>
public partial class ProfileDialog : Window
{
    private readonly string? _profileName;
    private ObservableCollection<SlotAssignment> _slotAssignments = new();
    private Point _dragStartPoint;
    public ObservableCollection<LayoutMonitorViewModel> Monitors { get; set; } = new();
    public LayoutManager LayoutSettings { get; set; } = new();
    private int? _selectedLayoutId;
    private bool _initialized = false;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    /// ProfileDialog
    /// 
    /// A dialog box to edit profile content.  Used when creating or editing a profile.
    /// 
    /// profileName:   The profile to edit
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public ProfileDialog(string? profileName = null)
    {
        InitializeComponent();

        DataContext = this;
        LoadMachineComboBox();

        _profileName = profileName;
        Title = (profileName == null) ? "New Profile" : $"Edit Profile - {profileName}";

        if (profileName != null)
        {
            ProfileName.Text = profileName;
            ProfileRepository profileRepo = ProfileRepository.Instance;
            profileRepo.LoadProfile(profileName);

            CharacterRepository.Instance.Load();

            // Initialize selected layout ID from existing assignment.
            _selectedLayoutId = profileRepo.GetLayoutId();
            DebugLog.Write(LogChannel.Profiles, $"ProfileDialog: initialized _selectedLayoutId={_selectedLayoutId?.ToString() ?? "null"}.", LogLevel.Trace);

            foreach (SlotAssignment slot in profileRepo.GetSlots())
            {
                _slotAssignments.Add(slot);
                Character? character = CharacterRepository.Instance.GetByDatabaseId(slot.DatabaseId);
                DebugLog.Write(LogChannel.Profiles, $"ProfileDialog: slot={slot.SlotNumber} characterId={slot.DatabaseId} name='{character?.Name}'.", LogLevel.Trace);
            }

            CharacterSlotsListView.ItemsSource = _slotAssignments.Select(s =>
            {
                Character? character = CharacterRepository.Instance.GetByDatabaseId(s.DatabaseId);
                return new SlotAssignmentViewModel
                {
                    SlotNumber = s.SlotNumber,
                    DatabaseId = s.DatabaseId,
                    CharacterName = character?.Name ?? "(unknown)",
                    ClassName = character?.Class.ToString() ?? string.Empty,
                    AccountId = character?.AccountId ?? 0
                };
            }).ToList();

            string serverType = profileRepo.GetServerType();
            string server = profileRepo.GetServer();

            if (!string.IsNullOrEmpty(serverType))
            {
                ServerTypeComboBox.SelectedItem = ServerTypeComboBox.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(i => i.Content.ToString() == serverType);
            }
            if (!string.IsNullOrEmpty(server))
            {
                ServerComboBox.SelectedItem = ServerComboBox.Items
                    .OfType<string>()
                    .FirstOrDefault(s => s == server);
            }
            PopulateCharacterList(profileRepo.GetSlots());
        }
        else
        {
            PopulateCharacterList(new List<SlotAssignment>());
        }

        ProfileName.TextChanged += (s, e) => ValidateSave();
        _initialized = true;
    }
    ///////////////////////////////////////////////////////////////////////////////////////////
    // ServerTypeComboBox_SelectionChanged
    //
    // Handles changes to the server type selection. Populates the server combo box
    // with servers matching the selected type.
    //
    // sender:  The combo box that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ServerTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServerComboBox == null)
        {
            return;
        }

        ServerComboBox.Items.Clear();

        ComboBoxItem? selected = ServerTypeComboBox.SelectedItem as ComboBoxItem;
        if (selected == null)
        {
            return;
        }

        string serverType = selected.Content.ToString() ?? string.Empty;
        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.ServerTypeComboBox_SelectionChanged: serverType=" + serverType, LogLevel.Trace);

        switch (serverType)
        {
            case "Test":
                {
                    ServerComboBox.Items.Add("Test");
                    break;
                }
            case "Live":
                {
                    ServerComboBox.Items.Add("Antonius Bayle - Kane Bayle");
                    ServerComboBox.Items.Add("Bertoxxulous - Saryn");
                    ServerComboBox.Items.Add("Bristlebane - The Tribunal");
                    ServerComboBox.Items.Add("Cazic-Thule - Fennin Ro");
                    ServerComboBox.Items.Add("Drinal - Maelin Starpyre");
                    ServerComboBox.Items.Add("Erollisi Marr - The Nameless");
                    ServerComboBox.Items.Add("Firiona Vie");
                    ServerComboBox.Items.Add("Luclin - Stromm");
                    ServerComboBox.Items.Add("Mangler");
                    ServerComboBox.Items.Add("Mischief");
                    ServerComboBox.Items.Add("Povar - Quellious");
                    ServerComboBox.Items.Add("The Rathe - Prexus");
                    ServerComboBox.Items.Add("Teek");
                    ServerComboBox.Items.Add("Tunare - The Seventh Hammer");
                    ServerComboBox.Items.Add("Vox");
                    ServerComboBox.Items.Add("Xegony - Druzzil Ro");
                    ServerComboBox.Items.Add("Zek");
                    break;
                }
            case "Progression":
                {
                    ServerComboBox.Items.Add("Agnarr");
                    ServerComboBox.Items.Add("Aradune");
                    ServerComboBox.Items.Add("Fangbreaker"); 
                    ServerComboBox.Items.Add("Oakwynd");
                    ServerComboBox.Items.Add("Tormax");
                    ServerComboBox.Items.Add("Vaniki");
                    ServerComboBox.Items.Add("Yelinak");
                    break;
                }
        }

        if (!_initialized)
        {
            return;
        }
        PopulateCharacterList(_slotAssignments.ToList());
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PopulateCharacterList
    //
    // Load characters belonging to the named character set.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void PopulateCharacterList(IReadOnlyList<SlotAssignment> existingSlots)
    {
        IReadOnlyList<Character> allCharacters = CharacterRepository.Instance.GetAll();
        HashSet<DatabaseId> selectedIds = existingSlots.Select(s => s.DatabaseId).ToHashSet();

        DebugLog.Write(LogChannel.Profiles, "PopulateCharacterList: selectedServer='" + 
            (ServerComboBox.SelectedItem as string ?? "null") + 
            "' characterCount=" + allCharacters.Count, LogLevel.Trace);

        string? selectedServer = ServerComboBox.SelectedItem as string;

        CharactersListView.ItemsSource = allCharacters
            .Where(c => string.IsNullOrEmpty(selectedServer) || c.Server == selectedServer)
            .Select(c => new CharacterSelection
            {
                Character = c,
                IsSelected = selectedIds.Contains(c.DatabaseId)
            })
            .ToList();
        ValidateSave();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CharacterSelection
    //
    // A binding between a character and its selection state.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public class CharacterSelection : INotifyPropertyChanged
    {
        public Character Character { get; set; } = null!;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RebuildSlotAssignments
    //
    // Rebuilds the slot assignment list from the current character selection,
    // preserving existing assignments where possible and appending new characters.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private void RebuildSlotAssignments()
    {
        List<DatabaseId> selectedIds = (CharactersListView.ItemsSource as IEnumerable<CharacterSelection>)!
            .Where(c => c.IsSelected)
            .Select(c => (DatabaseId)(uint)c.Character.DatabaseId)
            .ToList();

        // Remove assignments for characters no longer selected.
        List<SlotAssignment> toRemove = _slotAssignments
            .Where(s => !selectedIds.Contains(s.DatabaseId))
            .ToList();
        foreach (SlotAssignment item in toRemove)
        {
            _slotAssignments.Remove(item);
        }

        // Append newly selected characters not yet assigned.
        foreach (DatabaseId selected in selectedIds)
        {
            if (!_slotAssignments.Any(s => s.DatabaseId == selected))
            {
                _slotAssignments.Add(new SlotAssignment { DatabaseId = selected });
            }
        }

        ReassignSlotNumbers();

        CharacterSlotsListView.ItemsSource = _slotAssignments.Select(s =>
        {
            Character? character = CharacterRepository.Instance.GetByDatabaseId(s.DatabaseId);
            return new SlotAssignmentViewModel
            {
                SlotNumber = s.SlotNumber,
                DatabaseId = s.DatabaseId,
                CharacterName = character?.Name ?? "(unknown)",
                ClassName = character?.Class.ToString() ?? string.Empty,
                AccountId = character?.AccountId ?? 0
            };
        }).ToList();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Save_Click
    //
    // Click handler for the save button on the profile editor dialog.
    // Persists the profile name, slot assignments, machine, and layout assignment.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.Save_Click: saving.", LogLevel.Trace);

        RebuildSlotAssignments();

        string profileName = ProfileName.Text.Trim();
        ProfileRepository profileRepo = ProfileRepository.Instance;
        bool existsInDatabase = profileRepo.ProfileExists(profileName);

        if (existsInDatabase)
        {
            MessageBoxResult result = MessageBox.Show($"A profile named '{profileName}' already exists. Overwrite it?",
                "Profile Exists", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.No)
            {
                DebugLog.Write(LogChannel.Profiles, "ProfileDialog.Save_Click: user declined overwrite, aborting.", LogLevel.Trace);
                return;
            }
        }

        if (profileName != _profileName)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.Save_Click: '" + profileName
                + "' is not the profile this dialog opened, creating an empty profile.", LogLevel.Trace);
            profileRepo.CreateProfile(profileName);
        }
        profileRepo.SetSlots(_slotAssignments.ToList());

        if (_selectedLayoutId.HasValue)
        {
            DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.Save_Click: assigning layoutId={_selectedLayoutId.Value}.", LogLevel.Trace);
            profileRepo.SetLayoutId(_selectedLayoutId.Value);
        }

        if (MachineComboBox.SelectedItem is ComboBoxItem machineItem && machineItem.Tag is int machineId)
        {
            profileRepo.SetMachineId(machineId);
        }

        ComboBoxItem? serverTypeItem = ServerTypeComboBox.SelectedItem as ComboBoxItem;
        if (serverTypeItem != null)
        {
            profileRepo.SetServerType(serverTypeItem.Content.ToString() ?? string.Empty);
        }
        string? selectedServer = ServerComboBox.SelectedItem as string;
        if (selectedServer != null)
        {
            profileRepo.SetServer(selectedServer);
        }

        ProfileId profileId = profileRepo.Save(overwrite: existsInDatabase);

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.Save_Click: saved profileId={profileId}.", LogLevel.Trace);
        RecentProfiles.Add(profileName);
        DialogResult = true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Cancel_Click
    //
    // Click handler for the cancel button on the profile editor dialog.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CharacterCheckBox_Click
    //
    // Click handler for a character checkbox when selecting a character for inclusion in the profile.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void CharacterCheckBox_Click(object sender, RoutedEventArgs e)
    {
        IEnumerable<CharacterSelection>? items = CharactersListView.ItemsSource as IEnumerable<CharacterSelection>;
        if (items == null)
        {
            return;
        }

        CharacterSelection? clicked = (sender as CheckBox)?.DataContext as CharacterSelection;
        if (clicked == null)
        {
            return;
        }

        if (clicked.IsSelected)
        {
            // Disable all other characters on the same account.
            foreach (CharacterSelection item in items)
            {
                if ((item != clicked) && (item.Character.AccountId == clicked.Character.AccountId))
                {
                    item.IsEnabled = false;
                }
            }
        }
        else
        {
            // Re-enable all characters on the same account.
            foreach (CharacterSelection item in items)
            {
                if (item.Character.AccountId == clicked.Character.AccountId)
                {
                    item.IsEnabled = true;
                }
            }
        }

        ValidateSave();
    }


    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ValidateSave
    //
    // Determine if the "Save" button should be enabled.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ValidateSave()
    {
        SaveButton.IsEnabled =
            !string.IsNullOrWhiteSpace(ProfileName.Text) &&
            CharactersListView.ItemsSource is IEnumerable<CharacterSelection> items &&
            items.Any(c => c.IsSelected);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // TabControl_SelectionChanged
    //
    // Fires when the user switches tabs. Blocks navigation away from Character Selection
    // if no profile name has been entered. Rebuilds slot assignments when the Slot Assignment
    // tab is selected. Loads the keyboard layout tab when it is selected.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (! _initialized)
        {
            return;
        }

        if (e.Source is not TabControl)
        {
            return;
        }

        if ((e.RemovedItems.Count > 0) &&
            (e.RemovedItems[0] is TabItem removed) &&
            (removed.Header.ToString() == "Character Selection"))
        {
            if (string.IsNullOrWhiteSpace(ProfileName.Text))
            {
                DebugLog.Write(LogChannel.Profiles, "ProfileDialog: profile name required before leaving character selection.", LogLevel.Trace);
                MessageBox.Show("Please enter a profile name before continuing.", "Profile Name Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                ((TabControl)sender).SelectedItem = e.RemovedItems[0];
                return;
            }
        }

        if (SlotAssignmentTab.IsSelected)
        {
            RebuildSlotAssignments();
            LoadSlotAssignmentLayoutComboBox();
        }

        if (KeyboardLayoutTab.IsSelected)
        {
            LoadTargetGroupComboBox();
            LoadPageComboBox();
            LoadCommandComboBox();
        }

        if (RelayGroupsTab.IsSelected)
        {
            LoadRelayGroupsTab();
        }

        if ((e.AddedItems.Count > 0) &&
            (e.AddedItems[0] is TabItem added) &&
            (added.Header.ToString() == "Keyboard Layout"))
        {
            LoadKeyboardLayoutTab();
        }

        if (WindowLayoutTab.IsSelected)
        {
            LoadWindowLayoutTab();
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CommandTypeComboBox_SelectionChanged
    //
    // Fires when the user selects a command. Opens Manage Commands if that option is selected.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void CommandTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!CommandTypeComboBox.IsDropDownOpen)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.CommandTypeComboBox_SelectionChanged: dropdown not open, ignoring.", LogLevel.Trace);
            return;
        }

        if (e.AddedItems.Count == 0)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.CommandTypeComboBox_SelectionChanged: no items added.", LogLevel.Trace);
            return;
        }

        if (CommandTypeComboBox.SelectedItem is ComboBoxItem selected && selected.Tag is int selectedCommandId)
        {
            Command? cmd = new CommandGateway().GetCommand(selectedCommandId);
            if (cmd != null)
            {
                LabelTextBox.Text = cmd.Label;
                DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.CommandTypeComboBox_SelectionChanged: populated label='{cmd.Label}' for commandId={selectedCommandId}.", LogLevel.Trace);
            }
        }

        if (CommandTypeComboBox.SelectedItem is not ComboBoxItem item ||
            item.Content.ToString() != "Manage Commands...")
        {
            return;
        }

        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.CommandTypeComboBox_SelectionChanged: opening ManageCommandsDialog.", LogLevel.Trace);

        CommandTypeComboBox.IsDropDownOpen = false;
        CommandTypeComboBox.SelectedIndex = 0;

        ManageCommandsDialog dialog = new ManageCommandsDialog { Owner = this };
        dialog.ShowDialog();
        LoadCommandComboBox();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadKeyboardLayoutTab
    //
    // Populates the page list on the Keyboard Layout tab with the pages associated with the
    // profile this dialog was opened on, including which page is the start page.  Clears the list
    // when the dialog was opened for a new profile.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadKeyboardLayoutTab()
    {
        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.LoadKeyboardLayoutTab: loading.", LogLevel.Trace);

        if (_profileName == null)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.LoadKeyboardLayoutTab: dialog is for a new profile, no pages to show.", LogLevel.Trace); PageListView.ItemsSource = null;
            return;
        }

        ProfileId profileId = ProfileRepository.Instance.GetId();

        if (!profileId.Exists)
        {
            DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadKeyboardLayoutTab: profile '{_profileName}' has no id, nothing to load.", LogLevel.Error);
            PageListView.ItemsSource = null;
            return;
        }

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadKeyboardLayoutTab: profileId={profileId}.", LogLevel.Trace);

        ProfilePageGateway pageGateway = new ProfilePageGateway();
        List<ProfilePage> pages = pageGateway.GetPagesForProfile(profileId);

        List<ProfilePageViewModel> items = pages.Select(p => new ProfilePageViewModel
        {
            KeyPageId = p.KeyPageId,
            PageName = p.PageName,
            Device = p.Device,
            InProfile = true,
            IsStartPage = p.IsStartPage
        }).ToList();

        PageListView.ItemsSource = items;
        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadKeyboardLayoutTab: loaded {items.Count} pages.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadMachineComboBox
    //
    // Populates the machine combo box with all known machines.
    // Selects the machine assigned to the current profile if one exists.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadMachineComboBox()
    {
        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.LoadMachineComboBox: loading.", LogLevel.Trace);

        MachineGateway machineGateway = new MachineGateway();
        List<Machine> machines = machineGateway.GetAll();

        MachineComboBox.Items.Clear();

        foreach (Machine machine in machines)
        {
            MachineComboBox.Items.Add(new ComboBoxItem
            {
                Content = machine.Name,
                Tag = machine.Id
            });
        }

        if (_profileName != null)
        {
            int? machineId = ProfileRepository.Instance.GetMachineId();
            if (machineId.HasValue)
            {
                MachineComboBox.SelectedItem = MachineComboBox.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(i => (int)i.Tag == machineId.Value);
            }
        }

        if ((MachineComboBox.SelectedItem == null) && (MachineComboBox.Items.Count > 0))
        {
            MachineComboBox.SelectedIndex = 0;
        }

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadMachineComboBox: loaded {machines.Count} machines.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadRelayGroupsTab
    //
    // Loads relay groups and character membership for the active profile into the matrix control.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadRelayGroupsTab()
    {
        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.LoadRelayGroupsTab: loading.", LogLevel.Trace);

        List<RelayGroup> groups = new RelayGroupGateway().GetAllGroups();
        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadRelayGroupsTab: {groups.Count} groups.", LogLevel.Trace);

        List<Character> characters = _slotAssignments
            .Select(s => CharacterRepository.Instance.GetByDatabaseId(s.DatabaseId))
            .Where(c => c != null)
            .Cast<Character>()
            .ToList();
        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadRelayGroupsTab: {characters.Count} characters.", LogLevel.Trace);

        HashSet<RelayGroupMatrix.Membership> membership = new HashSet<RelayGroupMatrix.Membership>();
        foreach (RelayGroup group in groups)
        {
            foreach (Character member in group.Characters)
            {
                RelayGroupMatrix.Membership item = new RelayGroupMatrix.Membership { GroupId = group.Id, DatabaseId = (DatabaseId)(uint)member.DatabaseId };
                membership.Add(item);
            }
        }
        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadRelayGroupsTab: {membership.Count} membership pairs.", LogLevel.Trace);

        RelayGroupMatrixControl.Load(groups, characters, membership);
        RelayGroupMatrixControl.MembershipChanged -= RelayGroupMatrixControl_MembershipChanged;         // prevent double-wiring
        RelayGroupMatrixControl.MembershipChanged += RelayGroupMatrixControl_MembershipChanged;

        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.LoadRelayGroupsTab: done.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MachineComboBox_SelectionChanged
    //
    // Fires when the user selects a machine.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void MachineComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MachineComboBox.SelectedItem is ComboBoxItem item && item.Tag is int machineId)
        {
            DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.MachineComboBox_SelectionChanged: machineId={machineId}.", LogLevel.Trace);
        }
    }

    // Records the start position for drag detection.
    private void CharacterSlotsListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CharacterSlotsListView_PreviewMouseMove
    //
    // Initiates a drag operation once the mouse has moved far enough from the
    // recorded drag start point. The dragged payload is the SlotAssignmentViewModel
    // instance bound to the row under the cursor; the Drop handler translates that
    // back to the underlying SlotAssignment in _slotAssignments by CharacterId.
    //
    // sender: The ListView that raised the event.
    // e:      Mouse event arguments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void CharacterSlotsListView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point mousePos = e.GetPosition(null);
        Vector diff = _dragStartPoint - mousePos;

        if ((Math.Abs(diff.X) <= SystemParameters.MinimumHorizontalDragDistance) &&
            (Math.Abs(diff.Y) <= SystemParameters.MinimumVerticalDragDistance))
        {
            return;
        }

        ListView listView = (ListView)sender;
        ListViewItem? listViewItem = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);
        if (listViewItem == null)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.CharacterSlotsListView_PreviewMouseMove: no ListViewItem under cursor, ignoring drag.", LogLevel.Trace);
            return;
        }

        SlotAssignmentViewModel? slot = listView.ItemContainerGenerator.ItemFromContainer(listViewItem) as SlotAssignmentViewModel;
        if (slot == null)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.CharacterSlotsListView_PreviewMouseMove: container did not resolve to a SlotAssignmentViewModel, ignoring drag.", LogLevel.Trace);
            return;
        }

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.CharacterSlotsListView_PreviewMouseMove: starting drag for characterId={slot.DatabaseId} slot={slot.SlotNumber}.", LogLevel.Trace);

        DataObject dragData = new DataObject("slotAssignment", slot);
        DragDrop.DoDragDrop(listViewItem, dragData, DragDropEffects.Move);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ServerComboBox_SelectionChanged
    //
    // Handles changes to the server selection. Repopulates the character list
    // filtered to the selected server.
    //
    // sender:  The combo box that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ServerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }
        PopulateCharacterList(_slotAssignments.ToList());
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_NewCharacter_Click
    //
    // Handles the New Character button click on the Character Selection tab.
    // Opens the character creation dialog and refreshes the character list
    // if a new character was created.
    //
    // sender:  The button that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_NewCharacter_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.Button_NewCharacter_Click", LogLevel.Trace);

        string? selectedServer = ServerComboBox.SelectedItem as string;
        if (string.IsNullOrEmpty(selectedServer))
        {
            MessageBox.Show("Please select a server before creating a character.",
                "No Server Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        NewCharacterDialog dialog = new NewCharacterDialog(selectedServer);
        dialog.Owner = this;

        if (dialog.ShowDialog() == true)
        {
            CharacterRepository.Instance.Add(dialog.CreatedCharacter);
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.Button_NewCharacter_Click: character added, refreshing list", LogLevel.Trace);
            PopulateCharacterList(_slotAssignments.ToList());
        }
    }
    private void CharacterSlotsListView_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CharacterSlotsListView_Drop
    //
    // Handles the drop end of a slot row drag. The dragged payload is a SlotAssignmentViewModel
    // produced by CharacterSlotsListView_PreviewMouseMove, and the row under the cursor at drop
    // time is also a SlotAssignmentViewModel (the ListView is bound to view models, not models).
    // Both must be translated back to the underlying SlotAssignment instances in _slotAssignments
    // by matching CharacterId before any reordering is done.
    //
    // If no row is under the cursor at drop time, the item is moved to the end of the list.
    //
    // sender:  The ListView that raised the event.
    // e:       Drag event arguments carrying the slotAssignment payload.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void CharacterSlotsListView_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("slotAssignment"))
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.CharacterSlotsListView_Drop: no slotAssignment payload, ignoring.", LogLevel.Warn);
            return;
        }

        SlotAssignmentViewModel? draggedView = e.Data.GetData("slotAssignment") as SlotAssignmentViewModel;
        if (draggedView == null)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.CharacterSlotsListView_Drop: payload was not a SlotAssignmentViewModel, ignoring.", LogLevel.Warn);
            return;
        }

        SlotAssignment? draggedSlot = _slotAssignments.FirstOrDefault(s => s.DatabaseId == draggedView.DatabaseId);
        if (draggedSlot == null)
        {
            DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.CharacterSlotsListView_Drop: no SlotAssignment found for dragged characterId={draggedView.DatabaseId}, ignoring.", LogLevel.Warn);
            return;
        }

        ListView listView = (ListView)sender;
        SlotAssignmentViewModel? targetView = GetObjectDataFromPoint(listView, e.GetPosition(listView)) as SlotAssignmentViewModel;

        SlotAssignment? targetSlot = null;
        if (targetView != null)
        {
            targetSlot = _slotAssignments.FirstOrDefault(s => s.DatabaseId == targetView.DatabaseId);
            if (targetSlot == null)
            {
                DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.CharacterSlotsListView_Drop: no SlotAssignment found for target characterId={targetView.DatabaseId}, treating as drop at end.", LogLevel.Warn);
            }
        }
        else
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.CharacterSlotsListView_Drop: no row under cursor, dropping at end.", LogLevel.Warn);
        }

        int removeIndex = _slotAssignments.IndexOf(draggedSlot);
        int insertIndex = (targetSlot != null) ? _slotAssignments.IndexOf(targetSlot) : _slotAssignments.Count;

        if (removeIndex < insertIndex)
        {
            insertIndex--;
        }

        if (removeIndex == insertIndex)
        {
            DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.CharacterSlotsListView_Drop: removeIndex == insertIndex ({removeIndex}), no move needed.", LogLevel.Trace);
            return;
        }

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.CharacterSlotsListView_Drop: moving characterId={draggedSlot.DatabaseId} from index={removeIndex} to index={insertIndex}.", LogLevel.Trace);
        _slotAssignments.Move(removeIndex, insertIndex);
        ReassignSlotNumbers();

        CharacterSlotsListView.ItemsSource = _slotAssignments.Select(s =>
        {
            Character? character = CharacterRepository.Instance.GetByDatabaseId(s.DatabaseId);
            return new SlotAssignmentViewModel
            {
                SlotNumber = s.SlotNumber,
                DatabaseId = s.DatabaseId,
                CharacterName = character?.Name ?? "(unknown)",
                ClassName = character?.Class.ToString() ?? string.Empty,
                AccountId = character?.AccountId ?? 0
            };
        }).ToList();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ReassignSlotNumbers
    //
    // Recalculate slot IDs after moving slots.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ReassignSlotNumbers()
    {
        for (int i = 0; i < _slotAssignments.Count; i++)
        {
            _slotAssignments[i].SlotNumber = i + 1;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadSlotAssignmentLayoutComboBox
    //
    // Populates the layout dropdown on the Slot Assignment tab with all available layouts.
    // Pre-selects the layout currently assigned to this profile, if any.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadSlotAssignmentLayoutComboBox()
    {
        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.LoadSlotAssignmentLayoutComboBox: loading.", LogLevel.Trace);

        SlotAssignmentLayoutComboBox.Items.Clear();

        WindowLayoutRepository layoutRepo = new WindowLayoutRepository();
        List<WindowLayout> layouts = layoutRepo.GetAllLayouts().ToList();

        foreach (WindowLayout layout in layouts)
        {
            ComboBoxItem item = new ComboBoxItem
            {
                Content = layout.DisplayName,
                Tag = layout.Id
            };

            SlotAssignmentLayoutComboBox.Items.Add(item);
            DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadSlotAssignmentLayoutComboBox: added layoutId={layout.Id} display='{layout.DisplayName}'.", LogLevel.Trace);
        }

        if (_profileName == null)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.LoadSlotAssignmentLayoutComboBox: no profile name, skipping pre-selection.", LogLevel.Trace);
            return;
        }

        int? assignedLayoutId = ProfileRepository.Instance.GetLayoutId();

        if (!assignedLayoutId.HasValue)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.LoadSlotAssignmentLayoutComboBox: no layout assigned to profile.", LogLevel.Trace);
            return;
        }

        ComboBoxItem? match = SlotAssignmentLayoutComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag is int id && id == assignedLayoutId.Value);

        if (match != null)
        {
            SlotAssignmentLayoutComboBox.SelectedItem = match;
            DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadSlotAssignmentLayoutComboBox: pre-selected layoutId={assignedLayoutId.Value}.", LogLevel.Trace);
        }
        else
        {
            DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadSlotAssignmentLayoutComboBox: assigned layoutId={assignedLayoutId.Value} not found in list.", LogLevel.Trace);
        }

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadSlotAssignmentLayoutComboBox: loaded {layouts.Count} layouts.", LogLevel.Trace);
    }


    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SlotAssignmentLayoutComboBox_SelectionChanged
    //
    // Fires when the user selects a layout from the Slot Assignment tab dropdown.
    // Records the selected layout ID as dirty state for Save_Click to persist.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void SlotAssignmentLayoutComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SlotAssignmentLayoutComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not int layoutId)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.SlotAssignmentLayoutComboBox_SelectionChanged: no layout selected.", LogLevel.Trace);
            _selectedLayoutId = null;
            return;
        }

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.SlotAssignmentLayoutComboBox_SelectionChanged: layoutId={layoutId}.", LogLevel.Trace);

        _selectedLayoutId = layoutId;
    }
    
    private T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T t)
            {
                return t;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private object? GetObjectDataFromPoint(ListView listView, Point point)
    {
        UIElement? element = listView.InputHitTest(point) as UIElement;
        if (element != null)
        {
            ListViewItem? item = FindAncestor<ListViewItem>(element);
            if (item != null)
            {
                return listView.ItemContainerGenerator.ItemFromContainer(item);
            }
        }
        return null;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CreateNewMonitorConfig
    //
    // Creates a new LayoutMonitorViewModel for the given layout position,
    // using the first available monitor from the database that is not already
    // in use by another position in this layout.
    //
    // layoutPosition:  The position of this monitor in the layout (1=primary, 2+=slot monitors).
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private LayoutMonitorViewModel CreateNewMonitorConfig(int layoutPosition)
    {
        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.CreateNewMonitorConfig: layoutPosition={layoutPosition}.", LogLevel.Trace);

        List<Glass.Data.Models.Monitor> usedMonitors = Monitors
            .Where(m => m.Monitor != null)
            .Select(m => m.Monitor)
            .ToList();

        MonitorGateway monitorRepo = new MonitorGateway();
        int machineId = GetSelectedMachineId();
        List<Glass.Data.Models.Monitor> available = monitorRepo.GetForMachine(machineId);

        Glass.Data.Models.Monitor? selectedMonitor = available
            .FirstOrDefault(m => !usedMonitors.Any(u => u.Id == m.Id));

        if (selectedMonitor == null)
        {
            DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.CreateNewMonitorConfig: no available monitor found, using defaults.", LogLevel.Trace);
            selectedMonitor = new Glass.Data.Models.Monitor
            {
                Width = 1920,
                Height = 1080
            };
        }

        LayoutMonitorViewModel layoutMonitor = new LayoutMonitorViewModel
        {
            LayoutPosition = layoutPosition,
            Monitor = selectedMonitor,
            SelectedResolution = $"{selectedMonitor.Width}x{selectedMonitor.Height}"
        };

        layoutMonitor.SlotWidth = selectedMonitor.Width / 4;

        return layoutMonitor;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetSelectedMachineId
    //
    // Returns the machine ID currently selected in the machine combo box, or 0 if none.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private int GetSelectedMachineId()
    {
        if (MachineComboBox.SelectedItem is ComboBoxItem item && item.Tag is int machineId)
        {
            return machineId;
        }
        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.GetSelectedMachineId: no machine selected, returning 0.", LogLevel.Warn);
        return 0;
    }

    // Recursively searches the visual tree for a child element with the given name.
    private T? FindVisualChildByName<T>(DependencyObject parent, string name) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);

            if ((child is FrameworkElement fe) && (fe.Name == name))
            {
                return (T)child;
            }

            T? result = FindVisualChildByName<T>(child, name);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }


    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadBindingList
    //
    // Loads and displays all key bindings for the given page in the binding list.
    // Builds command and relay group lookup maps to resolve display names.
    //
    // pageId:  The page whose bindings to load
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadBindingList(int pageId)
    {
        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadBindingList: pageId={pageId}.", LogLevel.Trace);

        List<KeyBinding> bindings = new KeyBindingGateway().GetBindingsForPage(pageId)
            .OrderBy(b => System.Text.RegularExpressions.Regex.Replace(b.Key, @"\d+", m => m.Value.PadLeft(4, '0')))
            .ToList();
        Dictionary<int, Command> commandMap = new CommandGateway().GetAllCommands().ToDictionary(c => c.Id, c => c);
        Dictionary<int, string> groupMap = new RelayGroupGateway().GetAllGroups().ToDictionary(g => g.Id, g => g.Name);

        List<KeyBindingViewModel> items = bindings.Select(b =>
        {
            string commandName = (b.CommandId.HasValue && commandMap.TryGetValue(b.CommandId.Value, out Command? cmd))
                ? cmd.Name
                : "(none)";

            string label = (b.CommandId.HasValue && commandMap.TryGetValue(b.CommandId.Value, out Command? cmd2))
                ? cmd2.Label
                : string.Empty;

            string targetName = b.Target switch
            {
                0 => "(none)",
                1 => "Self",
                2 => "All",
                3 => "Others",
                _ => groupMap.TryGetValue(b.Target, out string? gn) ? gn : "?"
            };

            return new KeyBindingViewModel
            {
                Binding = b,
                CommandTargetText = $"{b.Key}: {commandName}: {targetName}",
                Label = b.Label ?? label
            };
        }).ToList();

        BindingListView.ItemsSource = items;

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadBindingList: loaded {items.Count} bindings.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadTargetGroupComboBox
    //
    // Populates the target group combo box with the special target values and all relay groups.
    // Special targets use fixed IDs: 0=none, 1=self, 2=all, 3=others.
    // Relay groups use their database IDs directly (>=4).
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadTargetGroupComboBox()
    {
        TargetGroupComboBox.Items.Clear();

        TargetGroupComboBox.Items.Add(new ComboBoxItem { Content = "(none)", Tag = 0 });
        TargetGroupComboBox.Items.Add(new ComboBoxItem { Content = "Self", Tag = 1 });
        TargetGroupComboBox.Items.Add(new ComboBoxItem { Content = "All", Tag = 2 });
        TargetGroupComboBox.Items.Add(new ComboBoxItem { Content = "Others", Tag = 3 });

        RelayGroupGateway relayGroupRepo = new RelayGroupGateway();
        List<RelayGroup> groups = relayGroupRepo.GetAllGroups();

        foreach (RelayGroup group in groups)
        {
            TargetGroupComboBox.Items.Add(new ComboBoxItem
            {
                Content = group.Name,
                Tag = group.Id
            });
        }

        if (TargetGroupComboBox.Items.Count > 0)
        {
            TargetGroupComboBox.SelectedIndex = 0;
        }

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadTargetGroupComboBox: loaded {groups.Count} relay groups plus 4 special entries.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadPageComboBox
    //
    // Populates the PageComboBox with pages associated with this profile.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadPageComboBox()
    {
        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.LoadPageComboBox: loading.", LogLevel.Trace);

        PageComboBox.Items.Clear();

        PageComboBox.Items.Add(new ComboBoxItem { Content = "Manage Pages..." });
        PageComboBox.Items.Add(new Separator());

        if (_profileName == null)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.LoadPageComboBox: no profile name, skipping pages.", LogLevel.Trace);
            return;
        }

        ProfileId profileId = ProfileRepository.Instance.GetId();

        if (! profileId.Exists)
        {
            DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadPageComboBox: profile '{_profileName}' not found in database, skipping pages.", LogLevel.Error);
            return;
        }

        ProfilePageGateway pageRepo = new ProfilePageGateway();
        List<ProfilePage> pages = pageRepo.GetPagesForProfile(profileId);

        foreach (ProfilePage page in pages)
        {
            PageComboBox.Items.Add(new ProfilePageViewModel
            {
                KeyPageId = page.KeyPageId,
                PageName = page.PageName,
                Device = page.Device,
                InProfile = true,
                IsStartPage = page.IsStartPage
            });
        }

        if (PageComboBox.Items.Count > 2)
        {
            PageComboBox.SelectedIndex = 2;
            DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadPageComboBox: loaded {pages.Count} pages, defaulted to first.", LogLevel.Trace);
        }
        else
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.LoadPageComboBox: no pages found for profile.", LogLevel.Trace);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadWindowLayoutTab
    //
    // Populates the layout list on the Window Layout tab.
    // Pre-selects the layout currently assigned to this profile, if any.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadWindowLayoutTab()
    {
        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.LoadWindowLayoutTab: loading.", LogLevel.Trace);

        WindowLayoutRepository layoutRepo = new WindowLayoutRepository();
        List<WindowLayout> layouts = layoutRepo.GetAllLayouts().ToList();

        LayoutListView.ItemsSource = layouts;
        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadWindowLayoutTab: loaded {layouts.Count} layouts.", LogLevel.Trace);

        if (_profileName == null)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.LoadWindowLayoutTab: no profile name, skipping pre-selection.", LogLevel.Trace);
            return;
        }

        int? assignedLayoutId = ProfileRepository.Instance.GetLayoutId();

        if (!assignedLayoutId.HasValue)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.LoadWindowLayoutTab: no layout assigned to profile.", LogLevel.Trace);
            ClearLayoutSummary();
            return;
        }

        WindowLayout? assigned = layouts.FirstOrDefault(l => l.Id == assignedLayoutId.Value);

        if (assigned == null)
        {
            DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadWindowLayoutTab: assigned layoutId={assignedLayoutId.Value} not found in list.", LogLevel.Trace);
            ClearLayoutSummary();
            return;
        }

        LayoutListView.SelectedItem = assigned;
        UpdateLayoutSummary(assigned);
        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadWindowLayoutTab: pre-selected layoutId={assigned.Id} name='{assigned.Name}'.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadCommandComboBox
    //
    // Populates the CommandTypeComboBox from all commands in the database.
    // "Manage Commands..." is the first item.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadCommandComboBox()
    {
        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.LoadCommandComboBox: loading.");

        CommandGateway commandGateway = new CommandGateway();
        List<Command> commands = commandGateway.GetAllCommands();

        CommandTypeComboBox.Items.Clear();

        CommandTypeComboBox.Items.Add(new ComboBoxItem
        {
            Content = "Manage Commands..."
        });
        CommandTypeComboBox.Items.Add(new Separator());

        foreach (Command command in commands)
        {
            CommandTypeComboBox.Items.Add(new ComboBoxItem
            {
                Content = command.Name,
                Tag = command.Id
            });
        }

        if (CommandTypeComboBox.Items.Count > 2)
        {
            CommandTypeComboBox.SelectedIndex = 2;
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.LoadCommandComboBox: defaulted to first command.", LogLevel.Trace);
        }

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LoadCommandComboBox: loaded {commands.Count} commands.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RefreshKey
    //
    // Updates the display state of a single key in the keyboard layout control.
    // Builds a KeyDisplay from the current binding list for the given key.
    //
    // key:  The key to refresh
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void RefreshKey(string key)
    {
        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.RefreshKey: key='{key}'.", LogLevel.Trace);

        string selectedKey = SelectedKeyTextBlock.Text;

        KeyBindingViewModel? binding = (BindingListView.ItemsSource as List<KeyBindingViewModel>)
            ?.FirstOrDefault(b => b.Binding.Key == key);

        KeyDisplay keyDisplay = new KeyDisplay
        {
            KeyName = key,
            Label = binding?.Label ?? string.Empty,
            KeyType = KeyType.Momentary,
            IsSelected = (key == selectedKey)
        };

        KeyLayoutControl.UpdateKey(keyDisplay);

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.RefreshKey: key='{key}' label='{keyDisplay.Label}' isSelected={keyDisplay.IsSelected}.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RefreshKeyLayout
    //
    // Updates the keyboard layout control to reflect the current binding list.
    // Clears keys that no longer have bindings and refreshes keys that do.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void RefreshKeyLayout()
    {
        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.RefreshKeyLayout: refreshing.", LogLevel.Trace);

        if (KeyLayoutControl.Keys == null)
        {
            KeyLayoutControl.Keys = new Dictionary<string, KeyDisplay>();
        }

        List<KeyBindingViewModel> boundItems = (BindingListView.ItemsSource as List<KeyBindingViewModel>)
            ?? new List<KeyBindingViewModel>();

        HashSet<string> boundKeys = boundItems.Select(b => b.Binding.Key).ToHashSet();

        // Clear keys that are no longer bound
 
        foreach (string key in KeyLayoutControl.Keys.Keys.Where(k => !boundKeys.Contains(k)).ToList())
        {
            DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.RefreshKeyLayout: clearing key='{key}'.", LogLevel.Trace);
            KeyLayoutControl.ClearKey(key);
        }
 

        // Refresh all currently bound keys
        foreach (KeyBindingViewModel item in boundItems)
        {
            RefreshKey(item.Binding.Key);
        }

        // Ensure the selected key is marked as selected even if unbound
        string selectedKey = SelectedKeyTextBlock.Text;
        if (!string.IsNullOrEmpty(selectedKey) && !boundKeys.Contains(selectedKey))
        {
            RefreshKey(selectedKey);
        }

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.RefreshKeyLayout: complete.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ClearLayoutSummary
    //
    // Clears the read-only layout summary panel on the Window Layout tab.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ClearLayoutSummary()
    {
        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.ClearLayoutSummary: clearing.", LogLevel.Trace);

        LayoutSummaryName.Text = string.Empty;
        LayoutSummaryMonitors.Text = string.Empty;
        LayoutSummarySlots.Text = string.Empty;

        NewUpdateLayoutButton.Content = "New";
        NewUpdateLayoutButton.IsEnabled = true;
        DeleteLayoutButton.IsEnabled = false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UpdateLayoutSummary
    //
    // Populates the read-only layout summary panel on the Window Layout tab
    // with the details of the given layout, including profiles using it.
    //
    // layout:  The layout to summarize
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void UpdateLayoutSummary(WindowLayout layout)
    {
        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.UpdateLayoutSummary: layoutId={layout.Id} name='{layout.Name}'.", LogLevel.Trace);

        LayoutSummaryName.Text = layout.Name;
        LayoutSummaryMonitors.Text = layout.Monitors.Count.ToString();
        LayoutSummarySlots.Text = layout.Slots.Count.ToString();

        WindowLayoutRepository layoutRepo = new WindowLayoutRepository();
        List<Profile> profiles = layoutRepo.GetProfilesUsingLayout(layout.Id);

        if (profiles.Count == 0)
        {
            LayoutSummaryProfiles.Text = "(none)";
        }
        else
        {
            LayoutSummaryProfiles.Text = string.Join(", ", profiles.Select(p => p.Name));
        }

        NewUpdateLayoutButton.Content = "Update";
        NewUpdateLayoutButton.IsEnabled = true;
        DeleteLayoutButton.IsEnabled = true;

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.UpdateLayoutSummary: monitors={layout.Monitors.Count} slots={layout.Slots.Count} profiles={profiles.Count}.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LayoutListView_SelectionChanged
    //
    // Fires when the user selects a layout in the Window Layout tab list.
    // Records the selected layout ID as dirty state for Save_Click to persist.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LayoutListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LayoutListView.SelectedItem is not WindowLayout layout)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.LayoutListView_SelectionChanged: no layout selected.", LogLevel.Trace);
            ClearLayoutSummary();
            _selectedLayoutId = null;
            return;
        }

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.LayoutListView_SelectionChanged: layoutId={layout.Id} name='{layout.Name}'.", LogLevel.Trace);

        _selectedLayoutId = layout.Id;
        UpdateLayoutSummary(layout);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LayoutListView_KeyDown
    //
    // Escape clears the layout selection.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LayoutListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.LayoutListView_KeyDown: Escape pressed, clearing selection.", LogLevel.Trace);
            LayoutListView.SelectedItem = null;
            ClearLayoutSummary();
            e.Handled = true;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // NewUpdateLayout_Click
    //
    // Opens EditLayoutDialog to create a new layout or edit the selected one.
    // After the dialog closes successfully, refreshes the layout list and
    // re-selects the affected layout.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void NewUpdateLayout_Click(object sender, RoutedEventArgs e)
    {
        WindowLayout? selected = LayoutListView.SelectedItem as WindowLayout;

        if (selected == null)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.NewUpdateLayout_Click: no layout selected, creating new.", LogLevel.Trace);
        }
        else
        {
            DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.NewUpdateLayout_Click: editing layoutId={selected.Id} name='{selected.Name}'.", LogLevel.Trace);
        }

        EditLayoutDialog dialog = new EditLayoutDialog(selected) { Owner = this };

        if (dialog.ShowDialog() != true)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.NewUpdateLayout_Click: dialog cancelled.", LogLevel.Trace);
            return;
        }

        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.NewUpdateLayout_Click: dialog saved, refreshing layout list.", LogLevel.Trace);

        int? editedId = selected?.Id;
        LoadWindowLayoutTab();

        if (editedId.HasValue)
        {
            LayoutListView.SelectedItem = (LayoutListView.ItemsSource as List<WindowLayout>)
                ?.FirstOrDefault(l => l.Id == editedId.Value);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DeleteLayout_Click
    //
    // Deletes the selected layout after confirmation.
    // Refuses deletion if any profiles are still using the layout.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void DeleteLayout_Click(object sender, RoutedEventArgs e)
    {
        if (LayoutListView.SelectedItem is not WindowLayout layout)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.DeleteLayout_Click: no layout selected.", LogLevel.Trace);
            return;
        }

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.DeleteLayout_Click: layoutId={layout.Id} name='{layout.Name}'.", LogLevel.Trace);

        WindowLayoutRepository layoutRepo = new WindowLayoutRepository();
        List<Profile> affectedProfiles = layoutRepo.GetProfilesUsingLayout(layout.Id);

        if (affectedProfiles.Count > 0)
        {
            string profileList = string.Join(", ", affectedProfiles.Select(p => p.Name));
            DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.DeleteLayout_Click: layout in use by profiles: {profileList}.", LogLevel.Trace);
            MessageBox.Show(
                $"Layout '{layout.Name}' is used by the following profile(s) and cannot be deleted:\n\n{profileList}\n\nRemove the layout from these profiles first.",
                "Layout In Use", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBoxResult result = MessageBox.Show($"Delete layout '{layout.Name}'?",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.DeleteLayout_Click: cancelled.", LogLevel.Trace);
            return;
        }

        layoutRepo.Delete(layout.Id);
        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.DeleteLayout_Click: deleted layoutId={layout.Id}.", LogLevel.Trace);

        ClearLayoutSummary();
        LoadWindowLayoutTab();
    }
    private void NewPage_Click(object sender, RoutedEventArgs e)
    {
    }

    private void RenamePage_Click(object sender, RoutedEventArgs e)
    {
    }

    private void DeletePage_Click(object sender, RoutedEventArgs e)
    {
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PageListView_SelectionChanged
    //
    // Fires when the user selects a page in the page list.
    // Updates the keyboard layout control for the selected page's device and loads its bindings.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void PageListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageListView.SelectedItem is not ProfilePageViewModel page)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.PageListView_SelectionChanged: no page selected.", LogLevel.Trace);
            KeyLayoutControl.Visibility = Visibility.Collapsed;
            return;
        }

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.PageListView_SelectionChanged: page='{page.PageName}' device='{page.Device}'.", LogLevel.Trace);

        KeyLayoutControl.Visibility = Visibility.Visible;
        KeyLayoutControl.Device = page.Device;

        LoadBindingList(page.KeyPageId);
        RefreshKeyLayout();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // KeyLayoutControl_KeyPressed
    //
    // Fires when a key cell is clicked in the keyboard layout control.
    // Loads the binding for the selected key into the binding editor.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void KeyLayoutControl_KeyPressed(object sender, LayoutEventArgs e)
    {
        string key = e.KeyName;
        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.KeyLayoutControl_KeyPressed: key='{key}'.", LogLevel.Trace);

        SelectedKeyTextBlock.Text = key;
        RefreshKeyLayout();

        KeyBindingViewModel? binding = (BindingListView.ItemsSource as List<KeyBindingViewModel>)
            ?.FirstOrDefault(b => b.Binding.Key == key);

        if (binding != null)
        {
            DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.KeyLayoutControl_KeyPressed: found binding. commandId={binding.Binding.CommandId} target={binding.Binding.Target}.", LogLevel.Trace);

            CommandTypeComboBox.SelectedItem = CommandTypeComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(i => (int?)i.Tag == binding.Binding.CommandId);

            TargetGroupComboBox.SelectedItem = TargetGroupComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(i => i.Tag is int tag && tag == binding.Binding.Target);

            RoundRobinCheckBox.IsChecked = binding.Binding.RoundRobin;

            if (!string.IsNullOrEmpty(binding.Binding.Label))
            {
                LabelTextBox.Text = binding.Binding.Label;
            }
            else if (binding.Binding.CommandId.HasValue)
            {
                Command? cmd = new CommandGateway().GetCommand(binding.Binding.CommandId.Value);
                LabelTextBox.Text = cmd?.Label ?? string.Empty;
            }
            else
            {
                LabelTextBox.Text = string.Empty;
            }

            TriggerOnComboBox.SelectedItem = TriggerOnComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(i => i.Tag is int tag && tag == (int)binding.Binding.TriggerOn);

            RepeatCheckBox.IsChecked = binding.Binding.KeyType == KeyType.Toggle;
            RepeatIntervalTextBox.Text = (binding.Binding.RepeatIntervalMs / 1000.0).ToString();
        }
        else
        {
            DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.KeyLayoutControl_KeyPressed: no binding found for key='{key}'.", LogLevel.Trace);
            CommandTypeComboBox.SelectedIndex = 0;
            TargetGroupComboBox.SelectedIndex = 0;
            RoundRobinCheckBox.IsChecked = false;
            LabelTextBox.Text = string.Empty;
            TriggerOnComboBox.SelectedIndex = 0;
            RepeatCheckBox.IsChecked = false;
            RepeatIntervalTextBox.Text = "2";
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SaveBinding_Click
    //
    // Saves the current binding editor contents to the database for the selected key.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void SaveBinding_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedKeyTextBlock.Text))
        {
            MessageBox.Show("Please select a key first.", "No Key Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (PageComboBox.SelectedItem is not ProfilePageViewModel page)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.SaveBinding_Click: no page selected.", LogLevel.Trace);
            return;
        }

        string key = SelectedKeyTextBlock.Text;
        int? commandId = (CommandTypeComboBox.SelectedItem as ComboBoxItem)?.Tag is int cid ? cid : null;
        bool roundRobin = RoundRobinCheckBox.IsChecked == true;

        TriggerOn triggerOn = TriggerOn.Press;
        if (TriggerOnComboBox.SelectedItem is ComboBoxItem triggerItem && triggerItem.Tag is int triggerTag)
        {
            triggerOn = (TriggerOn)triggerTag;
        }

        int target = 0;

        if (TargetGroupComboBox.SelectedItem is ComboBoxItem targetItem && targetItem.Tag is int tag)
        {
            target = tag;
        }

        List<KeyBindingViewModel>? existingItems = BindingListView.ItemsSource as List<KeyBindingViewModel>;
        KeyBindingViewModel? existing = existingItems?.FirstOrDefault(b => b.Binding.Key == key);

        KeyBinding binding = existing?.Binding ?? new KeyBinding { KeyPageId = page.KeyPageId, Key = key };
        binding.CommandId = commandId;
        binding.Target = target;
        binding.RoundRobin = roundRobin;
        binding.TriggerOn = triggerOn;
        binding.Label = string.IsNullOrWhiteSpace(LabelTextBox.Text) ? null : LabelTextBox.Text.Trim();
        binding.KeyType = RepeatCheckBox.IsChecked == true ? KeyType.Toggle : KeyType.Momentary;
        binding.RepeatIntervalMs = (int)(double.TryParse(RepeatIntervalTextBox.Text, out double seconds) && seconds >= 2.0 ? seconds * 1000 : 2000);

        KeyBindingGateway keyBindingGateway = new KeyBindingGateway();
        keyBindingGateway.Save(binding);

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.SaveBinding_Click: saved. id={binding.Id}.", LogLevel.Trace);

        LoadBindingList(page.KeyPageId);
        RefreshKey(key);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ClearBinding_Click
    //
    // Deletes the binding for the selected key from the database.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ClearBinding_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedKeyTextBlock.Text))
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.ClearBinding_Click: no key selected.", LogLevel.Trace);
            return;
        }

        if (PageComboBox.SelectedItem is not ProfilePageViewModel page)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.ClearBinding_Click: no page selected.", LogLevel.Trace);
            return;
        }

        string key = SelectedKeyTextBlock.Text;
        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.ClearBinding_Click: page={page.KeyPageId} key='{key}'.", LogLevel.Trace);

        var existing = (BindingListView.ItemsSource as List<KeyBindingViewModel>)
            ?.FirstOrDefault(b => b.Binding.Key == key);

        if (existing == null)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.ClearBinding_Click: no binding found for key.", LogLevel.Trace);
            return;
        }

        var repo = new KeyBindingGateway();
        repo.Delete(existing.Binding.Id);

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.ClearBinding_Click: deleted. id={existing.Binding.Id}.", LogLevel.Trace);

        CommandTypeComboBox.SelectedIndex = 0;
        TargetGroupComboBox.SelectedIndex = 0;
        RoundRobinCheckBox.IsChecked = false;
        TriggerOnComboBox.SelectedIndex = 0;
        RepeatCheckBox.IsChecked = false;
        RepeatIntervalTextBox.Text = "2";

        LoadBindingList(page.KeyPageId);
        RefreshKeyLayout();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // BindingListView_SelectionChanged
    //
    // Fires when a binding is selected in the binding list.
    // Highlights the corresponding key in the grid and loads the binding into the editor.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void BindingListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BindingListView.SelectedItem is not KeyBindingViewModel item)
        {
            return;
        }

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.BindingListView_SelectionChanged: key='{item.Binding.Key}'.", LogLevel.Trace);

        SelectedKeyTextBlock.Text = item.Binding.Key;
        RefreshKeyLayout();

        CommandTypeComboBox.SelectedItem = CommandTypeComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => (int?)i.Tag == item.Binding.CommandId);

        TargetGroupComboBox.SelectedItem = TargetGroupComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag is int tag && tag == item.Binding.Target);

        RoundRobinCheckBox.IsChecked = item.Binding.RoundRobin;

        TriggerOnComboBox.SelectedItem = TriggerOnComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag is int tag && tag == (int)item.Binding.TriggerOn);

        RepeatCheckBox.IsChecked = item.Binding.KeyType == KeyType.Toggle;
        RepeatIntervalTextBox.Text = (item.Binding.RepeatIntervalMs / 1000.0).ToString();
    }


    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PageComboBox_SelectionChanged
    //
    // Fires when the user selects a page from the page dropdown.
    // Shows the key layout grid for the selected page's device and loads its bindings.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void PageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
        {
            return;
        }

        if (PageComboBox.SelectedItem is ComboBoxItem item)
        {
            if (item.Content.ToString() == "Manage Pages...")
            {
                DebugLog.Write(LogChannel.Profiles, "ProfileDialog.PageComboBox_SelectionChanged: Manage Pages selected.", LogLevel.Trace);
                PageComboBox.SelectedIndex = 0;
                ManagePages_Click(sender, e);
            }
            return;
        }

        if (PageComboBox.SelectedItem is not ProfilePageViewModel page)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileDialog.PageComboBox_SelectionChanged: no page selected.", LogLevel.Trace);
            KeyLayoutControl.Visibility = Visibility.Collapsed;
            return;
        }

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.PageComboBox_SelectionChanged: page='{page.PageName}' device='{page.Device}'.", LogLevel.Trace);

        KeyLayoutControl.Visibility = Visibility.Visible;
        KeyLayoutControl.Device = page.Device;

        LoadBindingList(page.KeyPageId);
        RefreshKeyLayout();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ManagePages_Click
    //
    // Opens the Manage Pages dialog (to create/delete pages) and refreshes the page combobox on return.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ManagePages_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.ManagePages_Click: opening ManagePagesDialog.", LogLevel.Trace);

        var dialog = new ManagePagesDialog { Owner = this };
        dialog.ShowDialog();

        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.ManagePages_Click: dialog closed, refreshing page list.", LogLevel.Trace);
        LoadPageComboBox();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AssignPages_Click
    //
    // Opens the Assign Pages dialog to manage page associations for this profile.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void AssignPages_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.AssignPages_Click: opening ProfilePagesDialog.");

        ProfileId profileId = ProfileRepository.Instance.GetId();

        if (! profileId.Exists)
        {
            MessageBox.Show("Please save the profile before assigning pages.", "Profile Not Saved", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ProfilePagesDialog dialog = new ProfilePagesDialog(profileId) { Owner = this };
        dialog.ShowDialog();

        LoadPageComboBox();
        LoadKeyboardLayoutTab();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RepeatCheckBox_Checked
    //
    // Shows the repeat interval field when Repeat is checked.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void RepeatCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.RepeatCheckBox_Checked: showing interval field.", LogLevel.Trace);
        RepeatIntervalLabel.Visibility = Visibility.Visible;
        RepeatIntervalTextBox.Visibility = Visibility.Visible;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RepeatCheckBox_Unchecked
    //
    // Hides the repeat interval field when Repeat is unchecked.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void RepeatCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.Profiles, "ProfileDialog.RepeatCheckBox_Unchecked: hiding interval field.", LogLevel.Trace);
        RepeatIntervalLabel.Visibility = Visibility.Collapsed;
        RepeatIntervalTextBox.Visibility = Visibility.Collapsed;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RelayGroupMatrixControl_MembershipChanged
    //
    // Fires when the user toggles a cell in the relay group matrix.
    // Persists the change to the database.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void RelayGroupMatrixControl_MembershipChanged(object? sender, RelayGroupMatrix.MembershipChangedEventArgs e)
    {
        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.RelayGroupMatrixControl_MembershipChanged: groupId={e.GroupId} characterId={e.CharacterDatabaseId} added={e.Added}.", LogLevel.Trace);

        RelayGroupGateway gateway = new RelayGroupGateway();

        if (e.Added)
        {
            gateway.AddMember(e.GroupId, e.CharacterDatabaseId);
        }
        else
        {
            gateway.RemoveMember(e.GroupId, e.CharacterDatabaseId);
        }

        DebugLog.Write(LogChannel.Profiles, $"ProfileDialog.RelayGroupMatrixControl_MembershipChanged: done.", LogLevel.Trace);
    }
}