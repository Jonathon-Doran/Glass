using Glass.Controls;
using Glass.Core;
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
    private readonly CharacterRepository _characterRepo = new CharacterRepository();
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

            var repo = new ProfileRepository(profileName);
            foreach (var slot in repo.GetSlots())
            {
                _slotAssignments.Add(slot);
                var character = _characterRepo.GetById(slot.CharacterId);
                DebugLog.Write($"ProfileDialog: slot={slot.SlotNumber} characterId={slot.CharacterId} name='{character?.Name}'.");
            }

            CharacterSlotsListView.ItemsSource = _slotAssignments.Select(s =>
            {
                var character = _characterRepo.GetById(s.CharacterId);
                return new SlotAssignmentViewModel
                {
                    SlotNumber = s.SlotNumber,
                    CharacterId = s.CharacterId,
                    CharacterName = character?.Name ?? "(unknown)",
                    ClassName = character?.Class.ToString() ?? string.Empty,
                    AccountId = character?.AccountId ?? 0
                };
            }).ToList();
            PopulateCharacterList(repo.GetSlots());
        }
        else
        {
            PopulateCharacterList(new List<SlotAssignment>());
        }

        ProfileName.TextChanged += (s, e) => ValidateSave();
        _initialized = true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PopulateCharacterList
    //
    // Load characters belonging to the named character set.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void PopulateCharacterList(IReadOnlyList<SlotAssignment> existingSlots)
    {
        var charRepo = new CharacterRepository();
        var allCharacters = charRepo.GetAll();
        var selectedIds = existingSlots.Select(s => s.CharacterId).ToHashSet();

        CharactersListView.ItemsSource = allCharacters
            .Select(c => new CharacterSelection
            {
                Character = c,
                IsSelected = selectedIds.Contains(c.Id)
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
        var selectedIds = (CharactersListView.ItemsSource as IEnumerable<CharacterSelection>)!
            .Where(c => c.IsSelected)
            .Select(c => c.Character.Id)
            .ToList();

        // Remove assignments for characters no longer selected.
        var toRemove = _slotAssignments
            .Where(s => !selectedIds.Contains(s.CharacterId))
            .ToList();
        foreach (var item in toRemove)
        {
            _slotAssignments.Remove(item);
        }

        // Append newly selected characters not yet assigned.
        foreach (int characterId in selectedIds)
        {
            if (!_slotAssignments.Any(s => s.CharacterId == characterId))
            {
                _slotAssignments.Add(new SlotAssignment { CharacterId = characterId });
            }
        }

        ReassignSlotNumbers();

        CharacterSlotsListView.ItemsSource = _slotAssignments.Select(s =>
        {
            var character = _characterRepo.GetById(s.CharacterId);
            return new SlotAssignmentViewModel
            {
                SlotNumber = s.SlotNumber,
                CharacterId = s.CharacterId,
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
        DebugLog.Write("ProfileDialog.Save_Click: saving.");

        RebuildSlotAssignments();

        string profileName = ProfileName.Text.Trim();
        ProfileRepository repo = new ProfileRepository(profileName);
        repo.SetSlots(_slotAssignments.ToList());

        if (_selectedLayoutId.HasValue)
        {
            DebugLog.Write($"ProfileDialog.Save_Click: assigning layoutId={_selectedLayoutId.Value}.");
            repo.SetLayoutId(_selectedLayoutId.Value);
        }

        if (MachineComboBox.SelectedItem is ComboBoxItem machineItem && machineItem.Tag is int machineId)
        {
            repo.SetMachineId(machineId);
        }

        int profileId = repo.Save();

        if (profileId == -1)
        {
            MessageBoxResult result = MessageBox.Show($"A profile named '{profileName}' already exists. Overwrite it?",
                "Profile Exists", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.No)
            {
                DebugLog.Write("ProfileDialog.Save_Click: user declined overwrite, aborting.");
                return;
            }

            profileId = repo.Save(overwrite: true);
        }

        DebugLog.Write($"ProfileDialog.Save_Click: saved profileId={profileId}.");
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
        var items = CharactersListView.ItemsSource as IEnumerable<CharacterSelection>;
        if (items == null)
        {
            return;
        }

        var clicked = (sender as CheckBox)?.DataContext as CharacterSelection;
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
                DebugLog.Write("ProfileDialog: profile name required before leaving character selection.");
                MessageBox.Show("Please enter a profile name before continuing.", "Profile Name Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                ((TabControl)sender).SelectedItem = e.RemovedItems[0];
                return;
            }
        }

        if (SlotAssignmentTab.IsSelected)
        {
            RebuildSlotAssignments();
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
            DebugLog.Write("ProfileDialog.CommandTypeComboBox_SelectionChanged: dropdown not open, ignoring.");
            return;
        }

        if (e.AddedItems.Count == 0)
        {
            DebugLog.Write("ProfileDialog.CommandTypeComboBox_SelectionChanged: no items added.");
            return;
        }

        if (CommandTypeComboBox.SelectedItem is ComboBoxItem selected && selected.Tag is int selectedCommandId)
        {
            Command? cmd = new CommandRepository().GetCommand(selectedCommandId);
            if (cmd != null)
            {
                LabelTextBox.Text = cmd.Label;
                DebugLog.Write($"ProfileDialog.CommandTypeComboBox_SelectionChanged: populated label='{cmd.Label}' for commandId={selectedCommandId}.");
            }
        }

        if (CommandTypeComboBox.SelectedItem is not ComboBoxItem item ||
            item.Content.ToString() != "Manage Commands...")
        {
            return;
        }

        DebugLog.Write("ProfileDialog.CommandTypeComboBox_SelectionChanged: opening ManageCommandsDialog.");

        CommandTypeComboBox.IsDropDownOpen = false;
        CommandTypeComboBox.SelectedIndex = 0;

        var dialog = new ManageCommandsDialog { Owner = this };
        dialog.ShowDialog();
        LoadCommandComboBox();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadKeyboardLayoutTab
    //
    // Populates the page list on the Keyboard Layout tab with pages associated with
    // this profile.  Uses ProfilePageRepository to load only pages belonging to the
    // profile, including their in-profile and start-page state.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadKeyboardLayoutTab()
    {
        DebugLog.Write("ProfileDialog.LoadKeyboardLayoutTab: loading.");

        if (_profileName == null)
        {
            DebugLog.Write("ProfileDialog.LoadKeyboardLayoutTab: no profile name, nothing to load.");
            PageListView.ItemsSource = null;
            return;
        }

        var profileRepo = new ProfileRepository(_profileName);
        int profileId = profileRepo.GetId();

        if (profileId == 0)
        {
            DebugLog.Write($"ProfileDialog.LoadKeyboardLayoutTab: profile '{_profileName}' not found in database, nothing to load.");
            PageListView.ItemsSource = null;
            return;
        }

        DebugLog.Write($"ProfileDialog.LoadKeyboardLayoutTab: profileId={profileId}.");

        var pageRepo = new ProfilePageRepository();
        var pages = pageRepo.GetPagesForProfile(profileId);

        var items = pages.Select(p => new ProfilePageViewModel
        {
            KeyPageId = p.KeyPageId,
            PageName = p.PageName,
            Device = p.Device,
            InProfile = true,
            IsStartPage = p.IsStartPage
        }).ToList();

        PageListView.ItemsSource = items;
        DebugLog.Write($"ProfileDialog.LoadKeyboardLayoutTab: loaded {items.Count} pages.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadMachineComboBox
    //
    // Populates the machine combo box with all known machines.
    // Selects the machine assigned to the current profile if one exists.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadMachineComboBox()
    {
        DebugLog.Write("ProfileDialog.LoadMachineComboBox: loading.");

        var repo = new MachineRepository();
        var machines = repo.GetAll();

        MachineComboBox.Items.Clear();

        foreach (var machine in machines)
        {
            MachineComboBox.Items.Add(new ComboBoxItem
            {
                Content = machine.Name,
                Tag = machine.Id
            });
        }

        if (_profileName != null)
        {
            var profileRepo = new ProfileRepository(_profileName);
            int? machineId = profileRepo.GetMachineId();

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

        DebugLog.Write($"ProfileDialog.LoadMachineComboBox: loaded {machines.Count} machines.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadRelayGroupsTab
    //
    // Loads relay groups and character membership for the active profile into the matrix control.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadRelayGroupsTab()
    {
        DebugLog.Write("ProfileDialog.LoadRelayGroupsTab: loading.");

        List<RelayGroup> groups = new RelayGroupRepository().GetAllGroups();
        DebugLog.Write($"ProfileDialog.LoadRelayGroupsTab: {groups.Count} groups.");

        List<Character> characters = _slotAssignments
            .Select(s => _characterRepo.GetById(s.CharacterId))
            .Where(c => c != null)
            .Cast<Character>()
            .ToList();
        DebugLog.Write($"ProfileDialog.LoadRelayGroupsTab: {characters.Count} characters.");

        HashSet<(int GroupId, int CharacterId)> membership = new HashSet<(int, int)>();
        foreach (RelayGroup group in groups)
        {
            foreach (Character member in group.Characters)
            {
                membership.Add((group.Id, member.Id));
            }
        }
        DebugLog.Write($"ProfileDialog.LoadRelayGroupsTab: {membership.Count} membership pairs.");

        RelayGroupMatrixControl.Load(groups, characters, membership);
        RelayGroupMatrixControl.MembershipChanged -= RelayGroupMatrixControl_MembershipChanged;         // prevent double-wiring
        RelayGroupMatrixControl.MembershipChanged += RelayGroupMatrixControl_MembershipChanged;

        DebugLog.Write("ProfileDialog.LoadRelayGroupsTab: done.");
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
            DebugLog.Write($"ProfileDialog.MachineComboBox_SelectionChanged: machineId={machineId}.");
        }
    }

    // Records the start position for drag detection.
    private void CharacterSlotsListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    // Initiates a drag operation once the mouse has moved far enough.
    private void CharacterSlotsListView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        Point mousePos = e.GetPosition(null);
        Vector diff = _dragStartPoint - mousePos;

        if ((e.LeftButton == MouseButtonState.Pressed) &&
            ((Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance) ||
             (Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)))
        {
            ListView listView = (ListView)sender;
            ListViewItem? listViewItem = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);
            if (listViewItem != null)
            {
                SlotAssignment slot = (SlotAssignment)listView.ItemContainerGenerator.ItemFromContainer(listViewItem);
                DataObject dragData = new DataObject("slotAssignment", slot);
                DragDrop.DoDragDrop(listViewItem, dragData, DragDropEffects.Move);
            }
        }
    }

    private void CharacterSlotsListView_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    // Handles the drop — moves the dragged item to the drop position and renumbers.
    private void CharacterSlotsListView_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("slotAssignment"))
        {
            return;
        }

        SlotAssignment slot = (SlotAssignment)e.Data.GetData("slotAssignment");
        ListView listView = (ListView)sender;
        SlotAssignment? target = GetObjectDataFromPoint(listView, e.GetPosition(listView)) as SlotAssignment;

        int removeIndex = _slotAssignments.IndexOf(slot);
        int insertIndex = (target != null) ? _slotAssignments.IndexOf(target) : _slotAssignments.Count;

        if (removeIndex < insertIndex)
        {
            insertIndex--;
        }

        if (removeIndex != insertIndex)
        {
            _slotAssignments.Move(removeIndex, insertIndex);
            ReassignSlotNumbers();
            CharacterSlotsListView.Items.Refresh();
        }
    }

    private void ReassignSlotNumbers()
    {
        for (int i = 0; i < _slotAssignments.Count; i++)
        {
            _slotAssignments[i].SlotNumber = i + 1;
        }
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
        DebugLog.Write($"ProfileDialog.CreateNewMonitorConfig: layoutPosition={layoutPosition}.");

        List<Glass.Data.Models.Monitor> usedMonitors = Monitors
            .Where(m => m.Monitor != null)
            .Select(m => m.Monitor)
            .ToList();

        MonitorRepository monitorRepo = new MonitorRepository();
        int machineId = GetSelectedMachineId();
        List<Glass.Data.Models.Monitor> available = monitorRepo.GetForMachine(machineId);

        Glass.Data.Models.Monitor? selectedMonitor = available
            .FirstOrDefault(m => !usedMonitors.Any(u => u.Id == m.Id));

        if (selectedMonitor == null)
        {
            DebugLog.Write($"ProfileDialog.CreateNewMonitorConfig: no available monitor found, using defaults.");
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

        DebugLog.Write($"ProfileDialog.CreateNewMonitorConfig: created layoutPosition={layoutPosition} monitorId={selectedMonitor.Id} adapter='{selectedMonitor.AdapterName}' {selectedMonitor.Width}x{selectedMonitor.Height} slotWidth={layoutMonitor.SlotWidth}.");
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
        DebugLog.Write("ProfileDialog.GetSelectedMachineId: no machine selected, returning 0.");
        return 0;
    }

    // Recursively searches the visual tree for a child element with the given name.
    private T? FindVisualChildByName<T>(DependencyObject parent, string name) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if ((child is FrameworkElement fe) && (fe.Name == name))
            {
                return (T)child;
            }

            var result = FindVisualChildByName<T>(child, name);
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
        DebugLog.Write($"ProfileDialog.LoadBindingList: pageId={pageId}.");

        List<KeyBinding> bindings = new KeyBindingRepository().GetBindingsForPage(pageId)
            .OrderBy(b => System.Text.RegularExpressions.Regex.Replace(b.Key, @"\d+", m => m.Value.PadLeft(4, '0')))
            .ToList();
        Dictionary<int, Command> commandMap = new CommandRepository().GetAllCommands().ToDictionary(c => c.Id, c => c);
        Dictionary<int, string> groupMap = new RelayGroupRepository().GetAllGroups().ToDictionary(g => g.Id, g => g.Name);

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

        DebugLog.Write($"ProfileDialog.LoadBindingList: loaded {items.Count} bindings.");
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
        DebugLog.Write("ProfileDialog.LoadTargetGroupComboBox: loading.");

        TargetGroupComboBox.Items.Clear();

        TargetGroupComboBox.Items.Add(new ComboBoxItem { Content = "(none)", Tag = 0 });
        TargetGroupComboBox.Items.Add(new ComboBoxItem { Content = "Self", Tag = 1 });
        TargetGroupComboBox.Items.Add(new ComboBoxItem { Content = "All", Tag = 2 });
        TargetGroupComboBox.Items.Add(new ComboBoxItem { Content = "Others", Tag = 3 });

        RelayGroupRepository repo = new RelayGroupRepository();
        List<RelayGroup> groups = repo.GetAllGroups();

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

        DebugLog.Write($"ProfileDialog.LoadTargetGroupComboBox: loaded {groups.Count} relay groups plus 4 special entries.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadPageComboBox
    //
    // Populates the PageComboBox with pages associated with this profile.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadPageComboBox()
    {
        DebugLog.Write("ProfileDialog.LoadPageComboBox: loading.");

        PageComboBox.Items.Clear();

        PageComboBox.Items.Add(new ComboBoxItem { Content = "Manage Pages..." });
        PageComboBox.Items.Add(new Separator());

        if (_profileName == null)
        {
            DebugLog.Write("ProfileDialog.LoadPageComboBox: no profile name, skipping pages.");
            return;
        }

        ProfileRepository profileRepo = new ProfileRepository(_profileName);
        int profileId = profileRepo.GetId();

        if (profileId == 0)
        {
            DebugLog.Write($"ProfileDialog.LoadPageComboBox: profile '{_profileName}' not found in database, skipping pages.");
            return;
        }

        ProfilePageRepository pageRepo = new ProfilePageRepository();
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
            DebugLog.Write($"ProfileDialog.LoadPageComboBox: loaded {pages.Count} pages, defaulted to first.");
        }
        else
        {
            DebugLog.Write("ProfileDialog.LoadPageComboBox: no pages found for profile.");
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
        DebugLog.Write("ProfileDialog.LoadWindowLayoutTab: loading.");

        WindowLayoutRepository layoutRepo = new WindowLayoutRepository();
        List<WindowLayout> layouts = layoutRepo.GetAllLayouts().ToList();

        LayoutListView.ItemsSource = layouts;
        DebugLog.Write($"ProfileDialog.LoadWindowLayoutTab: loaded {layouts.Count} layouts.");

        if (_profileName == null)
        {
            DebugLog.Write("ProfileDialog.LoadWindowLayoutTab: no profile name, skipping pre-selection.");
            return;
        }

        ProfileRepository profileRepo = new ProfileRepository(_profileName);
        int? assignedLayoutId = profileRepo.GetLayoutId();

        if (!assignedLayoutId.HasValue)
        {
            DebugLog.Write("ProfileDialog.LoadWindowLayoutTab: no layout assigned to profile.");
            ClearLayoutSummary();
            return;
        }

        WindowLayout? assigned = layouts.FirstOrDefault(l => l.Id == assignedLayoutId.Value);

        if (assigned == null)
        {
            DebugLog.Write($"ProfileDialog.LoadWindowLayoutTab: assigned layoutId={assignedLayoutId.Value} not found in list.");
            ClearLayoutSummary();
            return;
        }

        LayoutListView.SelectedItem = assigned;
        UpdateLayoutSummary(assigned);
        DebugLog.Write($"ProfileDialog.LoadWindowLayoutTab: pre-selected layoutId={assigned.Id} name='{assigned.Name}'.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadCommandComboBox
    //
    // Populates the CommandTypeComboBox from all commands in the database.
    // "Manage Commands..." is the first item.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadCommandComboBox()
    {
        DebugLog.Write("ProfileDialog.LoadCommandComboBox: loading.");

        var repo = new CommandRepository();
        var commands = repo.GetAllCommands();

        CommandTypeComboBox.Items.Clear();

        CommandTypeComboBox.Items.Add(new ComboBoxItem
        {
            Content = "Manage Commands..."
        });
        CommandTypeComboBox.Items.Add(new Separator());

        foreach (var command in commands)
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
            DebugLog.Write("ProfileDialog.LoadCommandComboBox: defaulted to first command.");
        }

        DebugLog.Write($"ProfileDialog.LoadCommandComboBox: loaded {commands.Count} commands.");
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
        DebugLog.Write($"ProfileDialog.RefreshKey: key='{key}'.");

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

        DebugLog.Write($"ProfileDialog.RefreshKey: key='{key}' label='{keyDisplay.Label}' isSelected={keyDisplay.IsSelected}.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RefreshKeyLayout
    //
    // Updates the keyboard layout control to reflect the current binding list.
    // Clears keys that no longer have bindings and refreshes keys that do.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void RefreshKeyLayout()
    {
        DebugLog.Write("ProfileDialog.RefreshKeyLayout: refreshing.");

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
            DebugLog.Write($"ProfileDialog.RefreshKeyLayout: clearing key='{key}'.");
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

        DebugLog.Write($"ProfileDialog.RefreshKeyLayout: complete.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ClearLayoutSummary
    //
    // Clears the read-only layout summary panel on the Window Layout tab.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ClearLayoutSummary()
    {
        DebugLog.Write("ProfileDialog.ClearLayoutSummary: clearing.");

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
        DebugLog.Write($"ProfileDialog.UpdateLayoutSummary: layoutId={layout.Id} name='{layout.Name}'.");

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

        DebugLog.Write($"ProfileDialog.UpdateLayoutSummary: monitors={layout.Monitors.Count} slots={layout.Slots.Count} profiles={profiles.Count}.");
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
            DebugLog.Write("ProfileDialog.LayoutListView_SelectionChanged: no layout selected.");
            ClearLayoutSummary();
            _selectedLayoutId = null;
            return;
        }

        DebugLog.Write($"ProfileDialog.LayoutListView_SelectionChanged: layoutId={layout.Id} name='{layout.Name}'.");

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
            DebugLog.Write("ProfileDialog.LayoutListView_KeyDown: Escape pressed, clearing selection.");
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
            DebugLog.Write("ProfileDialog.NewUpdateLayout_Click: no layout selected, creating new.");
        }
        else
        {
            DebugLog.Write($"ProfileDialog.NewUpdateLayout_Click: editing layoutId={selected.Id} name='{selected.Name}'.");
        }

        EditLayoutDialog dialog = new EditLayoutDialog(selected) { Owner = this };

        if (dialog.ShowDialog() != true)
        {
            DebugLog.Write("ProfileDialog.NewUpdateLayout_Click: dialog cancelled.");
            return;
        }

        DebugLog.Write("ProfileDialog.NewUpdateLayout_Click: dialog saved, refreshing layout list.");

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
            DebugLog.Write("ProfileDialog.DeleteLayout_Click: no layout selected.");
            return;
        }

        DebugLog.Write($"ProfileDialog.DeleteLayout_Click: layoutId={layout.Id} name='{layout.Name}'.");

        WindowLayoutRepository layoutRepo = new WindowLayoutRepository();
        List<Profile> affectedProfiles = layoutRepo.GetProfilesUsingLayout(layout.Id);

        if (affectedProfiles.Count > 0)
        {
            string profileList = string.Join(", ", affectedProfiles.Select(p => p.Name));
            DebugLog.Write($"ProfileDialog.DeleteLayout_Click: layout in use by profiles: {profileList}.");
            MessageBox.Show(
                $"Layout '{layout.Name}' is used by the following profile(s) and cannot be deleted:\n\n{profileList}\n\nRemove the layout from these profiles first.",
                "Layout In Use", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBoxResult result = MessageBox.Show($"Delete layout '{layout.Name}'?",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            DebugLog.Write("ProfileDialog.DeleteLayout_Click: cancelled.");
            return;
        }

        layoutRepo.Delete(layout.Id);
        DebugLog.Write($"ProfileDialog.DeleteLayout_Click: deleted layoutId={layout.Id}.");

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
            DebugLog.Write("ProfileDialog.PageListView_SelectionChanged: no page selected.");
            KeyLayoutControl.Visibility = Visibility.Collapsed;
            return;
        }

        DebugLog.Write($"ProfileDialog.PageListView_SelectionChanged: page='{page.PageName}' device='{page.Device}'.");

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
        DebugLog.Write($"ProfileDialog.KeyLayoutControl_KeyPressed: key='{key}'.");

        SelectedKeyTextBlock.Text = key;
        RefreshKeyLayout();

        KeyBindingViewModel? binding = (BindingListView.ItemsSource as List<KeyBindingViewModel>)
            ?.FirstOrDefault(b => b.Binding.Key == key);

        if (binding != null)
        {
            DebugLog.Write($"ProfileDialog.KeyLayoutControl_KeyPressed: found binding. commandId={binding.Binding.CommandId} target={binding.Binding.Target}.");

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
                Command? cmd = new CommandRepository().GetCommand(binding.Binding.CommandId.Value);
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
            DebugLog.Write($"ProfileDialog.KeyLayoutControl_KeyPressed: no binding found for key='{key}'.");
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
            DebugLog.Write("ProfileDialog.SaveBinding_Click: no key selected.");
            MessageBox.Show("Please select a key first.", "No Key Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (PageComboBox.SelectedItem is not ProfilePageViewModel page)
        {
            DebugLog.Write("ProfileDialog.SaveBinding_Click: no page selected.");
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

        DebugLog.Write($"ProfileDialog.SaveBinding_Click: page={page.KeyPageId} key='{key}' commandId={commandId} target={target} roundRobin={roundRobin} triggerOn={triggerOn}.");
     
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

        KeyBindingRepository repo = new KeyBindingRepository();
        repo.Save(binding);

        DebugLog.Write($"ProfileDialog.SaveBinding_Click: saved. id={binding.Id}.");

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
            DebugLog.Write("ProfileDialog.ClearBinding_Click: no key selected.");
            return;
        }

        if (PageComboBox.SelectedItem is not ProfilePageViewModel page)
        {
            DebugLog.Write("ProfileDialog.ClearBinding_Click: no page selected.");
            return;
        }

        string key = SelectedKeyTextBlock.Text;
        DebugLog.Write($"ProfileDialog.ClearBinding_Click: page={page.KeyPageId} key='{key}'.");

        var existing = (BindingListView.ItemsSource as List<KeyBindingViewModel>)
            ?.FirstOrDefault(b => b.Binding.Key == key);

        if (existing == null)
        {
            DebugLog.Write("ProfileDialog.ClearBinding_Click: no binding found for key.");
            return;
        }

        var repo = new KeyBindingRepository();
        repo.Delete(existing.Binding.Id);

        DebugLog.Write($"ProfileDialog.ClearBinding_Click: deleted. id={existing.Binding.Id}.");

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

        DebugLog.Write($"ProfileDialog.BindingListView_SelectionChanged: key='{item.Binding.Key}'.");

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
                DebugLog.Write("ProfileDialog.PageComboBox_SelectionChanged: Manage Pages selected.");
                PageComboBox.SelectedIndex = 0;
                ManagePages_Click(sender, e);
            }
            return;
        }

        if (PageComboBox.SelectedItem is not ProfilePageViewModel page)
        {
            DebugLog.Write("ProfileDialog.PageComboBox_SelectionChanged: no page selected.");
            KeyLayoutControl.Visibility = Visibility.Collapsed;
            return;
        }

        DebugLog.Write($"ProfileDialog.PageComboBox_SelectionChanged: page='{page.PageName}' device='{page.Device}'.");

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
        DebugLog.Write("ProfileDialog.ManagePages_Click: opening ManagePagesDialog.");

        var dialog = new ManagePagesDialog { Owner = this };
        dialog.ShowDialog();

        DebugLog.Write("ProfileDialog.ManagePages_Click: dialog closed, refreshing page list.");
        LoadPageComboBox();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AssignPages_Click
    //
    // Opens the Assign Pages dialog to manage page associations for this profile.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void AssignPages_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("ProfileDialog.AssignPages_Click: opening ProfilePagesDialog.");

        var repo = new ProfileRepository(ProfileName.Text);
        int id = repo.GetId();

        if (id == 0)
        {
            DebugLog.Write("ProfileDialog.AssignPages_Click: profile not saved yet.");
            MessageBox.Show("Please save the profile before assigning pages.", "Profile Not Saved", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new ProfilePagesDialog(id) { Owner = this };
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
        DebugLog.Write("ProfileDialog.RepeatCheckBox_Checked: showing interval field.");
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
        DebugLog.Write("ProfileDialog.RepeatCheckBox_Unchecked: hiding interval field.");
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
        DebugLog.Write($"ProfileDialog.RelayGroupMatrixControl_MembershipChanged: groupId={e.GroupId} characterId={e.CharacterId} added={e.Added}.");

        RelayGroupRepository repo = new RelayGroupRepository();

        if (e.Added)
        {
            repo.AddMember(e.GroupId, e.CharacterId);
        }
        else
        {
            repo.RemoveMember(e.GroupId, e.CharacterId);
        }

        DebugLog.Write($"ProfileDialog.RelayGroupMatrixControl_MembershipChanged: done.");
    }
}