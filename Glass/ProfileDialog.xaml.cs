using Glass.Core;
using Glass.Data;
using Glass.Data.Models;
using Glass.Data.Repositories;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

namespace Glass;

/// <summary>
/// Interaction logic for ProfileDialog.xaml
/// </summary>
public partial class ProfileDialog : Window
{
    private readonly string? _profileName;
    private readonly CharacterSet? _profile;
    private ObservableCollection<SlotAssignment> _slotAssignments = new();
    private Point _dragStartPoint;
    public ObservableCollection<MonitorConfig> Monitors { get; set; } = new();
    public ObservableCollection<ComboBoxItem> MonitorComboBoxItems { get; set; } = new();
    public List<EnumeratedMonitor> EnumeratedDevices { get; set; } = new();
    public LayoutManager LayoutSettings { get; set; } = new();

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
            }
            PopulateCharacterList(repo.GetSlots());
        }

        ProfileName.TextChanged += (s, e) => ValidateSave();
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

        var selectedIds = existingSlots.Select(s => s.Character.Id).ToHashSet();

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

    public class CharacterSelection
    {
        public Character Character { get; set; } = null!;
        public bool IsSelected { get; set; }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RebuildSlotAssignments
    //
    // Rebuilds the slot assignment list from the current character selection,
    // preserving existing assignments where possible and appending new characters.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private void RebuildSlotAssignments()
    {
        var selected = (CharactersListView.ItemsSource as IEnumerable<CharacterSelection>)!
            .Where(c => c.IsSelected)
            .Select(c => c.Character)
            .ToList();

        // Remove assignments for characters no longer selected.
        var toRemove = _slotAssignments
            .Where(s => !selected.Any(c => c.Id == s.Character.Id))
            .ToList();
        foreach (var item in toRemove)
        {
            _slotAssignments.Remove(item);
        }

        // Append newly selected characters not yet assigned.
        foreach (var character in selected)
        {
            if (!_slotAssignments.Any(s => s.Character.Id == character.Id))
            {
                _slotAssignments.Add(new SlotAssignment { Character = character });
            }
        }

        ReassignSlotNumbers();

        CharacterSlotsListView.ItemsSource = _slotAssignments;
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

    // Rebuild slot assignments whenever the user switches to the Slot Assignment tab.
    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not TabControl)
        {
            return;
        }
        if (SlotAssignmentTab.IsSelected)
        {
            RebuildSlotAssignments();
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
}