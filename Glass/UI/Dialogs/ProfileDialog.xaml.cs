using Glass.Core;
using Glass.Data;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.UI.ViewModels;
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

namespace Glass;

/// <summary>
/// Interaction logic for ProfileDialog.xaml
/// </summary>
public partial class ProfileDialog : Window
{
    private readonly string? _profileName;
    private ObservableCollection<SlotAssignment> _slotAssignments = new();
    private Point _dragStartPoint;
    public ObservableCollection<MonitorConfig> Monitors { get; set; } = new();
    public ObservableCollection<ComboBoxItem> MonitorComboBoxItems { get; set; } = new();
    public List<EnumeratedMonitor> EnumeratedDevices { get; set; } = new();
    public LayoutManager LayoutSettings { get; set; } = new();
    private readonly CharacterRepository _characterRepo = new CharacterRepository();
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
        PopulateEnumeratedDevices();

        _profileName = profileName;
        Title = (profileName == null) ? "New Profile" : $"Edit Profile - {profileName}";

        if (profileName != null)
        {
            ProfileName.Text = profileName;

            var repo = new CharacterSetRepository(profileName);
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
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        RebuildSlotAssignments();

        var profileName = ProfileName.Text.Trim();
        var repo = new CharacterSetRepository(profileName);
        repo.SetSlots(_slotAssignments.ToList());


        int profileId = repo.Save();
        if (profileId == -1)
        {
            var result = MessageBox.Show($"A profile named '{profileName}' already exists. Overwrite it?",
                                         "Profile Exists", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.No)
            {
                return;
            }
            profileId = repo.Save(overwrite: true);
        }

        // Save window layout if monitors are configured and a name is provided.
        string layoutName = LayoutNameTextBox.Text.Trim();
        if ((Monitors.Count > 0) && !string.IsNullOrWhiteSpace(layoutName))
        {
            var layoutRepo = new WindowLayoutRepository();
            layoutRepo.Save(profileId, layoutName, _slotAssignments.ToList(), Monitors.ToList());
        }

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
    // PopuplateEnumeratedDevices
    //
    // Enumerates physical monitors and populates the device list and combo box.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private void PopulateEnumeratedDevices()
    {
        int index = 0;
        MonitorInfoHelper.EnumerateMonitors((hMonitor, dpiScale, deviceName, width, height) =>
        {
            EnumeratedDevices.Add(new EnumeratedMonitor
            {
                DeviceName = deviceName,
                DpiScale = dpiScale,
                DeviceIndex = index
            });

            MonitorComboBoxItems.Add(new ComboBoxItem { Content = deviceName });
            index++;
        });

        DebugLog.Write($"ProfileDialog: enumerated {EnumeratedDevices.Count} monitors.");
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

        if ((e.AddedItems.Count > 0) &&
            (e.AddedItems[0] is TabItem added) &&
            (added.Header.ToString() == "Keyboard Layout"))
        {
            LoadKeyboardLayoutTab();
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
    // Populates the page list on the Keyboard Layout tab. Loads all key pages from the
    // database and marks the start page for the current profile.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadKeyboardLayoutTab()
    {
        DebugLog.Write("ProfileDialog.LoadKeyboardLayoutTab: loading.");

        var pageRepo = new KeyPageRepository();
        var profileRepo = new CharacterSetRepository(ProfileName.Text);
        int? startPageId = profileRepo.GetStartPageId();

        var pageNames = pageRepo.GetPageNames();
        var items = new List<KeyPageViewModel>();

        foreach (var name in pageNames)
        {
            var page = pageRepo.GetPage(name);
            if (page == null)
            {
                continue;
            }

            items.Add(new KeyPageViewModel
            {
                Id = page.Id,
                Name = page.Name,
                Device = page.Device,
                IsStartPage = startPageId.HasValue && (page.Id == startPageId.Value)
            });
        }

        PageListView.ItemsSource = items;
        DebugLog.Write($"ProfileDialog.LoadKeyboardLayoutTab: loaded {items.Count} pages.");
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

    // Adjusts the Monitors collection to match the selected monitor count.
    private void MonitorCountChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MonitorCountComboBox.SelectedItem == null)
        {
            return;
        }

        int selectedCount = int.Parse(((ComboBoxItem)MonitorCountComboBox.SelectedItem).Content.ToString()!);
        DebugLog.Write($"ProfileDialog.MonitorCountChanged: selectedCount={selectedCount}");

        while (Monitors.Count < selectedCount)
        {
            Monitors.Add(CreateNewMonitorConfig(Monitors.Count + 1));
        }

        while (Monitors.Count > selectedCount)
        {
            Monitors.RemoveAt(Monitors.Count - 1);
        }

        // Force each monitor's ComboBoxes to their correct values.
        MonitorConfigItemsControl.Items.Refresh();
        foreach (var item in MonitorConfigItemsControl.Items)
        {
            var container = MonitorConfigItemsControl.ItemContainerGenerator.ContainerFromItem(item) as ContentPresenter;
            if (container == null)
            {
                continue;
            }
            container.ApplyTemplate();

            var resolutionCombo = container.ContentTemplate.FindName("comboBoxResolution", container) as ComboBox;
            if (resolutionCombo != null)
            {
                resolutionCombo.SelectedIndex = 0;
            }

            var orientationCombo = container.ContentTemplate.FindName("comboBoxOrientation", container) as ComboBox;
            if (orientationCombo != null)
            {
                var monitor = item as MonitorConfig;
                orientationCombo.SelectedItem = orientationCombo.Items
                    .Cast<ComboBoxItem>()
                    .FirstOrDefault(i => i.Content.ToString() == monitor?.Orientation);
            }
        }
    }

    // Creates a new MonitorConfig for the given monitor number, using the first
    // available device name from the enumerated monitor list.
    private MonitorConfig CreateNewMonitorConfig(int id)
    {
        var usedDeviceNames = Monitors.Select(m => m.DeviceName).ToList();
        var availableDevice = EnumeratedDevices.FirstOrDefault(m => !usedDeviceNames.Contains(m.DeviceName));
        var availableDeviceName = availableDevice?.DeviceName ?? string.Empty;

        var monitor = new MonitorConfig
        {
            MonitorNumber = id,
            SelectedResolution = "1920x1080",
            MonitorWidth = 1920,
            MonitorHeight = 1080,
            PreferredWidth = 1920 / 4,
            Orientation = "Landscape",
            DeviceName = availableDeviceName,
            DpiScale = availableDevice?.DpiScale ?? 1.0f,
            DeviceIndex = availableDevice?.DeviceIndex ?? 0,
            SlotRectangles = new List<Rect>()
        };

        monitor.AdjustMonitorDimensions();
        DebugLog.Write($"ProfileDialog.CreateNewMonitorConfig: monitor={id} device={availableDeviceName}");
        return monitor;
    }

    // Handles resolution or orientation changes for a monitor.
    private void MonitorSettingsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.DataContext is MonitorConfig monitorConfig)
        {
            string value = comboBox.SelectedValue?.ToString() ?? string.Empty;

            if (comboBox.Name.Contains("Resolution"))
            {
                monitorConfig.SelectedResolution = value;
            }
            else if (comboBox.Name.Contains("Orientation"))
            {
                monitorConfig.Orientation = value;
            }

            DebugLog.Write($"ProfileDialog.MonitorSettingsChanged: monitor={monitorConfig.MonitorNumber} resolution={monitorConfig.SelectedResolution} orientation={monitorConfig.Orientation}");

            UpdateMonitorRectangles();
            UpdateMonitorConfigurationUI(monitorConfig);
        }
    }

    // Selects a monitor for configuration when its rectangle is clicked.
    private void MonitorRectangle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DebugLog.Write($"ProfileDialog.MonitorRectangle_MouseLeftButtonDown: fired, sender={sender?.GetType().Name}");

        if (sender is Rectangle rectangle && rectangle.DataContext is MonitorConfig monitorConfig)
        {
            foreach (var monitor in Monitors)
            {
                monitor.IsSelectedForConfiguration = (monitor.MonitorNumber == monitorConfig.MonitorNumber);
            }

            DebugLog.Write($"ProfileDialog.MonitorRectangle_MouseLeftButtonDown: selected monitor={monitorConfig.MonitorNumber}");
            UpdateMonitorConfigurationUI(monitorConfig);
            MonitorNameComboBox.SelectedIndex = monitorConfig.DeviceIndex;
        }
    }

    // Redraws the monitor rectangles and overlay canvases to reflect current
    // resolution and orientation settings.
    private void UpdateMonitorRectangles()
    {
        foreach (var monitor in Monitors)
        {
            var container = MonitorConfigItemsControl.ItemContainerGenerator.ContainerFromItem(monitor) as ContentPresenter;
            if (container == null)
            {
                continue;
            }

            var rectangle = FindVisualChildByName<Rectangle>(container, "MonitorRectangle");
            var canvas = FindVisualChildByName<Canvas>(container, "OverlayCanvas");

            if (rectangle != null)
            {
                rectangle.Width = monitor.MonitorWidth / LayoutConstants.ScalingFactor;
                rectangle.Height = monitor.MonitorHeight / LayoutConstants.ScalingFactor;
            }

            if (canvas != null)
            {
                canvas.Width = monitor.OverlayCanvasWidth;
                canvas.Height = monitor.OverlayCanvasHeight;
            }
        }
    }

    // Updates the UI controls for a monitor based on the current layout strategy.
    private void UpdateMonitorConfigurationUI(MonitorConfig monitor)
    {
        DebugLog.Write($"ProfileDialog.UpdateMonitorConfigurationUI: monitor={monitor.MonitorNumber} strategy={LayoutSettings.SelectedLayoutStrategy}");

        switch (LayoutSettings.SelectedLayoutStrategy)
        {
            case LayoutStrategies.FixedWindowSize:
                {
                    WindowSizeInputs.Visibility = Visibility.Visible;
                    GridSizeInputs.Visibility = Visibility.Visible;

                    int maxWindowWidth = monitor.MonitorWidth - LayoutConstants.HorizontalMargin;
                    int maxWindowHeight = monitor.MonitorHeight - LayoutConstants.VerticalMargin;
                    int calculatedMaxWidth = (int)(maxWindowHeight * LayoutConstants.AspectRatio);

                    PreferredWidthSlider.Maximum = Math.Min(maxWindowWidth, calculatedMaxWidth);
                    PreferredWidthSlider.Value = monitor.PreferredWidth > 0 ? monitor.PreferredWidth : monitor.MonitorWidth / 4;

                    UpdateGridSizeText(monitor.MonitorWidth, monitor.MonitorHeight, (int)PreferredWidthSlider.Value);
                    RedrawWindowsForMonitor(monitor);
                    break;
                }

            case LayoutStrategies.SubsetAndSwap:
            case LayoutStrategies.SummaryWindows:
                {
                    WindowSizeInputs.Visibility = Visibility.Collapsed;
                    GridSizeInputs.Visibility = Visibility.Collapsed;
                    break;
                }
        }
    }

    // Updates the grid size text block to show how many windows fit on the monitor.
    private void UpdateGridSizeText(int monitorWidth, int monitorHeight, int windowWidth)
    {
        int windowHeight = (int)(windowWidth / LayoutConstants.AspectRatio);
        int windowWidthWithMargin = windowWidth + LayoutConstants.HorizontalMargin;
        int windowHeightWithMargin = windowHeight + LayoutConstants.VerticalMargin;

        int gridWidth = monitorWidth / windowWidthWithMargin;
        int gridHeight = monitorHeight / windowHeightWithMargin;

        GridSizeTextBlock.Text = $"{gridWidth} x {gridHeight}";
    }

    // Redraws the slot rectangles on the overlay canvas for a given monitor.
    private void RedrawWindowsForMonitor(MonitorConfig monitorConfig)
    {
        var container = MonitorConfigItemsControl.ItemContainerGenerator.ContainerFromItem(monitorConfig) as ContentPresenter;
        if (container == null)
        {
            return;
        }

        var canvas = FindVisualChildByName<Canvas>(container, "OverlayCanvas");
        var rectangle = FindVisualChildByName<Rectangle>(container, "MonitorRectangle");

        if ((canvas == null) || (rectangle == null))
        {
            return;
        }

        if (LayoutSettings.Stacked && (monitorConfig.MonitorNumber == 1))
        {
            canvas.Children.Clear();
            monitorConfig.SlotRectangles.Clear();
            monitorConfig.NumSlots = 0;
        }
        else
        {
            DrawWindowsInMonitor(canvas, rectangle.Width, rectangle.Height, monitorConfig);
        }
    }

    // Draws scaled blue rectangles on the canvas representing slot positions,
    // and stores the full-size slot rectangles on the MonitorConfig.
    private void DrawWindowsInMonitor(Canvas canvas, double monitorWidth, double monitorHeight, MonitorConfig monitorConfig)
    {
        int windowWidth = monitorConfig.PreferredWidth;
        int windowHeight = (int)(windowWidth / LayoutConstants.AspectRatio);

        canvas.Children.Clear();
        monitorConfig.SlotRectangles.Clear();

        double scaledWindowWidth = windowWidth / LayoutConstants.ScalingFactor;
        double scaledWindowHeight = windowHeight / LayoutConstants.ScalingFactor;

        int columns = (int)(monitorWidth / (scaledWindowWidth + LayoutConstants.HorizontalMargin / LayoutConstants.ScalingFactor));
        int rows = (int)(monitorHeight / (scaledWindowHeight + LayoutConstants.VerticalMargin / LayoutConstants.ScalingFactor));

        double fullSizeHorizontalMargin = (monitorConfig.MonitorWidth - (columns * windowWidth)) / (double)columns;
        double fullSizeVerticalMargin = (monitorConfig.MonitorHeight - (rows * windowHeight)) / (double)rows;

        double scaledHorizontalMargin = fullSizeHorizontalMargin / LayoutConstants.ScalingFactor;
        double scaledVerticalMargin = fullSizeVerticalMargin / LayoutConstants.ScalingFactor;

        monitorConfig.NumSlots = rows * columns;

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                Rectangle windowRect = new Rectangle
                {
                    Width = scaledWindowWidth,
                    Height = scaledWindowHeight,
                    Fill = Brushes.Blue,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };

                int left = (int)Math.Round(col * (windowWidth + fullSizeHorizontalMargin));
                int top = (int)Math.Round(row * (windowHeight + fullSizeVerticalMargin));

                monitorConfig.SlotRectangles.Add(new Rect(left, top, windowWidth, windowHeight));

                Canvas.SetLeft(windowRect, col * (scaledWindowWidth + scaledHorizontalMargin));
                Canvas.SetTop(windowRect, row * (scaledWindowHeight + scaledVerticalMargin));

                canvas.Children.Add(windowRect);
            }
        }

        UpdateTotalSlotCount();
    }

    private void UpdateTotalSlotCount()
    {
        int totalSlots = Monitors
            .Where(m => !(LayoutSettings.Stacked && (m.MonitorNumber == 1)))
            .Sum(m => m.NumSlots);

        TotalSlotsTextBlock.Text = $"{totalSlots} total slots";
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

    // Updates the preferred width for the selected monitor and redraws its slot preview.
    private void PreferredWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PreferredSizeTextBlock == null || GridSizeTextBlock == null)
        {
            return;
        }

        int width = (int)e.NewValue;
        int height = (int)(width / LayoutConstants.AspectRatio);
        PreferredSizeTextBlock.Text = $"{width} x {height}";

        var selectedMonitor = Monitors.FirstOrDefault(m => m.IsSelectedForConfiguration);
        if (selectedMonitor != null)
        {
            selectedMonitor.PreferredWidth = width;
            UpdateGridSizeText(selectedMonitor.MonitorWidth, selectedMonitor.MonitorHeight, width);
            RedrawWindowsForMonitor(selectedMonitor);
        }
    }

    // Updates the layout strategy when the user changes the combo box selection.
    private void LayoutStrategyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LayoutStrategyComboBox.SelectedItem == null)
        {
            return;
        }

        LayoutSettings.SelectedLayoutStrategy = LayoutStrategyComboBox.SelectedIndex switch
        {
            0 => LayoutStrategies.FixedWindowSize,
            1 => LayoutStrategies.SubsetAndSwap,
            2 => LayoutStrategies.SummaryWindows,
            _ => LayoutStrategies.FixedWindowSize
        };

        DebugLog.Write($"ProfileDialog.LayoutStrategyChanged: strategy={LayoutSettings.SelectedLayoutStrategy}");

        var selectedMonitor = Monitors.FirstOrDefault(m => m.IsSelectedForConfiguration);
        if (selectedMonitor != null)
        {
            UpdateMonitorConfigurationUI(selectedMonitor);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadBindingList
    //
    // Loads and displays all key bindings for the given page in the binding list.
    //
    // pageId:  The page whose bindings to load
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadBindingList(int pageId)
    {
        DebugLog.Write($"ProfileDialog.LoadBindingList: pageId={pageId}.");

        var bindings = new KeyBindingRepository().GetBindingsForPage(pageId)
            .OrderBy(b => System.Text.RegularExpressions.Regex.Replace(b.Key, @"\d+", m => m.Value.PadLeft(4, '0')))
            .ToList();
        var commandMap = new CommandRepository().GetAllCommands().ToDictionary(c => c.Id, c => c.Name);
        var groupMap = new RelayGroupRepository().GetAllGroupNames().ToDictionary(g => g.Id, g => g.Name);

        var items = bindings.Select(b =>
        {
            string commandName = (b.CommandId.HasValue && commandMap.TryGetValue(b.CommandId.Value, out var cn))
                ? cn
                : "(none)";

            string targetName = b.Target switch
            {
                -1 => "(none)",
                0 => "Self",
                1 => "All Characters",
                2 => "All Others",
                3 => (b.RelayGroupId.HasValue && groupMap.TryGetValue(b.RelayGroupId.Value, out var gn))
                        ? gn
                        : "?",
                _ => "?"
            };

            return new KeyBindingViewModel
            {
                Binding = b,
                DisplayText = $"{b.Key}: {commandName}: {targetName}"
            };
        }).ToList();

        BindingListView.ItemsSource = items;

        DebugLog.Write($"ProfileDialog.LoadBindingList: loaded {items.Count} bindings.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadTargetGroupComboBox
    //
    // Populates the target group combo box with the special target values and all relay groups.
    // Each item's Tag is an integer: -1=None, 0=Self, 1=All Characters, 2=All Others, 3=RelayGroup.
    // For relay group items, the Tag is stored as a negative value offset to distinguish them —
    // actually, relay group items store their relay_group_id in a separate field.
    // Uses ComboBoxItem.Tag as int for special targets, and for relay groups stores the
    // relay_group_id in a field accessible at save time.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadTargetGroupComboBox()
    {
        DebugLog.Write("ProfileDialog.LoadTargetGroupComboBox: loading.");

        TargetGroupComboBox.Items.Clear();

        TargetGroupComboBox.Items.Add(new ComboBoxItem { Content = "(none)", Tag = -1 });
        TargetGroupComboBox.Items.Add(new ComboBoxItem { Content = "Self", Tag = 0 });
        TargetGroupComboBox.Items.Add(new ComboBoxItem { Content = "All Characters", Tag = 1 });
        TargetGroupComboBox.Items.Add(new ComboBoxItem { Content = "All Others", Tag = 2 });

        var repo = new RelayGroupRepository();
        var groups = repo.GetAllGroupNames();

        foreach (var group in groups)
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
    // Populates the PageComboBox from all key pages in the database.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadPageComboBox()
    {
        DebugLog.Write("ProfileDialog.LoadPageComboBox: loading.");

        var repo = new KeyPageRepository();
        var pages = repo.GetAllPages();

        PageComboBox.Items.Clear();

        PageComboBox.Items.Add(new ComboBoxItem
        {
            Content = "Manage Pages..."
        });
        PageComboBox.Items.Add(new Separator());

        foreach (var page in pages)
        {
            PageComboBox.Items.Add(new ComboBoxItem
            {
                Content = $"{page.Name} ({page.Device})",
                Tag = page.Name
            });
        }

        if (PageComboBox.Items.Count > 2)
        {
            PageComboBox.SelectedIndex = 2;
            DebugLog.Write("ProfileDialog.LoadPageComboBox: defaulted to first item.");
        }
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
    // UpdateKeyButtonColors
    //
    // Updates the background color of all key buttons in the visible grid.
    // Bound keys are green, unbound keys are grey.
    // The selected key is set to blue by the caller after this method returns.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void UpdateKeyButtonColors()
    {
        Grid? activeGrid = null;

        if (G13Grid.Visibility == Visibility.Visible)
        {
            activeGrid = G13Grid;
        }
        else if (G15Grid.Visibility == Visibility.Visible)
        {
            activeGrid = G15Grid;
        }
        else if (DominatorX36Grid.Visibility == Visibility.Visible)
        {
            activeGrid = DominatorX36Grid;
        }

        if (activeGrid == null)
        {
            return;
        }

        var boundKeys = (BindingListView.ItemsSource as List<KeyBindingViewModel>)
            ?.Select(b => b.Binding.Key)
            .ToHashSet() ?? new HashSet<string>();

        foreach (var child in activeGrid.Children.OfType<Button>())
        {
            string key = child.Tag?.ToString() ?? string.Empty;
            child.Background = boundKeys.Contains(key) ? Brushes.Green : new SolidColorBrush(Color.FromRgb(80, 80, 80));
        }
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
    // Shows the key layout grid for the selected page's device and hides the others.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void PageListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageListView.SelectedItem is not KeyPageViewModel page)
        {
            G13Grid.Visibility = Visibility.Collapsed;
            G15Grid.Visibility = Visibility.Collapsed;
            DominatorX36Grid.Visibility = Visibility.Collapsed;
            return;
        }

        DebugLog.Write($"ProfileDialog.PageListView_SelectionChanged: page='{page.Name}' device='{page.Device}'.");

        G13Grid.Visibility = page.Device == "G13" ? Visibility.Visible : Visibility.Collapsed;
        G15Grid.Visibility = page.Device == "G15" ? Visibility.Visible : Visibility.Collapsed;
        DominatorX36Grid.Visibility = page.Device == "DominatorX36" ? Visibility.Visible : Visibility.Collapsed;

        LoadBindingList(page.Id);
    }

    private void PageInProfile_Click(object sender, RoutedEventArgs e)
    {
    }

    private void PageIsStart_Click(object sender, RoutedEventArgs e)
    {
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // KeyButton_Click
    //
    // Fires when a key button is clicked in the key layout grid.
    // Highlights the selected key and loads its binding into the binding editor.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void KeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        string key = button.Tag?.ToString() ?? string.Empty;
        DebugLog.Write($"ProfileDialog.KeyButton_Click: key='{key}'.");

        UpdateKeyButtonColors();
        button.Background = Brushes.DodgerBlue;
        SelectedKeyTextBlock.Text = key;

        var binding = (BindingListView.ItemsSource as List<KeyBindingViewModel>)
            ?.FirstOrDefault(b => b.Binding.Key == key);

        if (binding != null)
        {
            DebugLog.Write($"ProfileDialog.KeyButton_Click: found binding. commandId={binding.Binding.CommandId} target={binding.Binding.Target} relayGroupId={binding.Binding.RelayGroupId}.");

            CommandTypeComboBox.SelectedItem = CommandTypeComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(i => (int?)i.Tag == binding.Binding.CommandId);

            TargetGroupComboBox.SelectedItem = TargetGroupComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(i => i.Tag is int tag &&
                                     (binding.Binding.Target <= 2
                                         ? tag == binding.Binding.Target
                                         : tag == binding.Binding.RelayGroupId));

            RoundRobinCheckBox.IsChecked = binding.Binding.RoundRobin;
        }
        else
        {
            DebugLog.Write($"ProfileDialog.KeyButton_Click: no binding found for key='{key}'.");
            CommandTypeComboBox.SelectedIndex = 0;
            TargetGroupComboBox.SelectedIndex = 0;
            RoundRobinCheckBox.IsChecked = false;
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

        if (PageComboBox.SelectedItem is not KeyPageViewModel page)
        {
            DebugLog.Write("ProfileDialog.SaveBinding_Click: no page selected.");
            return;
        }

        string key = SelectedKeyTextBlock.Text;
        int? commandId = (CommandTypeComboBox.SelectedItem as ComboBoxItem)?.Tag is int cid ? cid : null;
        bool roundRobin = RoundRobinCheckBox.IsChecked == true;

        int target = -1;
        int? relayGroupId = null;

        if (TargetGroupComboBox.SelectedItem is ComboBoxItem targetItem && targetItem.Tag is int tag)
        {
            if (tag <= 2)
            {
                target = tag;
                relayGroupId = null;
            }
            else
            {
                target = 3;
                relayGroupId = tag;
            }
        }

        DebugLog.Write($"ProfileDialog.SaveBinding_Click: page={page.Id} key='{key}' commandId={commandId} target={target} relayGroupId={relayGroupId} roundRobin={roundRobin}.");

        var existing = (BindingListView.ItemsSource as List<KeyBindingViewModel>)
            ?.FirstOrDefault(b => b.Binding.Key == key);

        var binding = existing?.Binding ?? new KeyBinding { KeyPageId = page.Id, Key = key };
        binding.CommandId = commandId;
        binding.Target = target;
        binding.RelayGroupId = relayGroupId;
        binding.RoundRobin = roundRobin;

        var repo = new KeyBindingRepository();
        repo.Save(binding);

        DebugLog.Write($"ProfileDialog.SaveBinding_Click: saved. id={binding.Id}.");

        LoadBindingList(page.Id);
        UpdateKeyButtonColors();
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

        if (PageComboBox.SelectedItem is not ComboBoxItem pageItem || pageItem.Tag == null)
        {
            DebugLog.Write("ProfileDialog.ClearBinding_Click: no page selected.");
            return;
        }

        string pageName = pageItem.Tag.ToString() ?? string.Empty;
        var pageRepo = new KeyPageRepository();
        var page = pageRepo.GetPage(pageName);

        if (page == null)
        {
            DebugLog.Write($"ProfileDialog.ClearBinding_Click: page '{pageName}' not found.");
            return;
        }

        string key = SelectedKeyTextBlock.Text;
        DebugLog.Write($"ProfileDialog.ClearBinding_Click: page={page.Id} key='{key}'.");

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

        LoadBindingList(page.Id);
        UpdateKeyButtonColors();
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

        UpdateKeyButtonColors();

        Grid? activeGrid = null;
        if (G13Grid.Visibility == Visibility.Visible)
        {
            activeGrid = G13Grid;
        }
        else if (G15Grid.Visibility == Visibility.Visible)
        {
            activeGrid = G15Grid;
        }
        else if (DominatorX36Grid.Visibility == Visibility.Visible)
        {
            activeGrid = DominatorX36Grid;
        }

        if (activeGrid != null)
        {
            var keyButton = activeGrid.Children.OfType<Button>()
                .FirstOrDefault(b => b.Tag?.ToString() == item.Binding.Key);
            if (keyButton != null)
            {
                keyButton.Background = Brushes.DodgerBlue;
            }
        }

        CommandTypeComboBox.SelectedItem = CommandTypeComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => (int?)((ComboBoxItem)i).Tag == item.Binding.CommandId);
        TargetGroupComboBox.SelectedItem = TargetGroupComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag is int tag &&
                                 (item.Binding.Target <= 2
                                     ? tag == item.Binding.Target
                                     : tag == item.Binding.RelayGroupId));
        RoundRobinCheckBox.IsChecked = item.Binding.RoundRobin;
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

        if (PageComboBox.SelectedItem is not ComboBoxItem item)
        {
            G13Grid.Visibility = Visibility.Collapsed;
            G15Grid.Visibility = Visibility.Collapsed;
            DominatorX36Grid.Visibility = Visibility.Collapsed;
            DeviceLabel.Visibility = Visibility.Collapsed;
            return;
        }

        if (item.Content.ToString() == "Manage Pages...")
        {
            DebugLog.Write("ProfileDialog.PageComboBox_SelectionChanged: Manage Pages selected.");
            PageComboBox.SelectedIndex = 0;
            ManagePages_Click(sender, e);
            return;
        }

        string name = item.Tag?.ToString() ?? string.Empty;
        var repo = new KeyPageRepository();
        var page = repo.GetPage(name);

        if (page == null)
        {
            DebugLog.Write($"ProfileDialog.PageComboBox_SelectionChanged: page '{name}' not found in database.");
            return;
        }

        DebugLog.Write($"ProfileDialog.PageComboBox_SelectionChanged: page='{page.Name}' device='{page.Device}'.");

        G13Grid.Visibility = page.Device == "G13" ? Visibility.Visible : Visibility.Collapsed;
        G15Grid.Visibility = page.Device == "G15" ? Visibility.Visible : Visibility.Collapsed;
        DominatorX36Grid.Visibility = page.Device == "Dominator X36" ? Visibility.Visible : Visibility.Collapsed;

        DeviceLabel.Text = page.Device;
        DeviceLabel.Visibility = Visibility.Visible;

        LoadBindingList(page.Id);
        UpdateKeyButtonColors();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ManagePages_Click
    //
    // Opens the Manage Pages dialog and refreshes the page combobox on return.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ManagePages_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("ProfileDialog.ManagePages_Click: opening ManagePagesDialog.");

        var dialog = new ManagePagesDialog { Owner = this };
        dialog.ShowDialog();

        DebugLog.Write("ProfileDialog.ManagePages_Click: dialog closed, refreshing page list.");
        LoadPageComboBox();
    }
}