using Glass.Core;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.UI.ViewModels;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Glass;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// EditLayoutDialog
//
// Dialog for creating or editing a window layout.
// Pass a WindowLayout to edit an existing layout, or null to create a new one.
// On Save, writes directly to WindowLayoutRepository.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public partial class EditLayoutDialog : Window
{
    private readonly WindowLayout? _existingLayout;
    private bool _initialized = false;
    private LayoutMonitorViewModel? _selectedMonitor;

    public ObservableCollection<LayoutMonitorViewModel> Monitors { get; } = new();

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // EditLayoutDialog
    //
    // existingLayout:  The layout to edit, or null to create a new layout.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public EditLayoutDialog(WindowLayout? existingLayout)
    {
        InitializeComponent();
        DataContext = this;

        _existingLayout = existingLayout;

        DebugLog.Write($"EditLayoutDialog: opened. mode={(_existingLayout == null ? "new" : $"edit layoutId={_existingLayout.Id}")}");

        LoadMachineComboBox();

        _initialized = true;

        if (_existingLayout != null)
        {
            DebugLog.Write($"EditLayoutDialog: pre-populating from layoutId={_existingLayout.Id} name='{_existingLayout.Name}'.");
            LayoutNameTextBox.Text = _existingLayout.Name;
            LoadExistingLayout();
        }
        else
        {
            DebugLog.Write("EditLayoutDialog: new layout, defaulting to current machine.");
            MachineComboBox_SelectionChanged(this, null!);
        }


    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadMachineComboBox
    //
    // Populates the machine dropdown from MachineRepository.
    // Defaults selection to GlassContext.CurrentMachine.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadMachineComboBox()
    {
        DebugLog.Write("EditLayoutDialog.LoadMachineComboBox: loading.");

        MachineRepository machineRepo = new MachineRepository();
        List<Machine> machines = machineRepo.GetAll();

        MachineComboBox.Items.Clear();

        foreach (Machine machine in machines)
        {
            ComboBoxItem item = new ComboBoxItem
            {
                Content = machine.Name,
                Tag = machine.Id
            };

            MachineComboBox.Items.Add(item);

            if (GlassContext.CurrentMachine != null && machine.Id == GlassContext.CurrentMachine.Id)
            {
                MachineComboBox.SelectedItem = item;
                DebugLog.Write($"EditLayoutDialog.LoadMachineComboBox: defaulted to current machine id={machine.Id} name='{machine.Name}'.");
            }
        }

        if (MachineComboBox.SelectedItem == null && MachineComboBox.Items.Count > 0)
        {
            MachineComboBox.SelectedIndex = 0;
            DebugLog.Write("EditLayoutDialog.LoadMachineComboBox: no current machine match, defaulted to first.");
        }

        DebugLog.Write($"EditLayoutDialog.LoadMachineComboBox: loaded {machines.Count} machines.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadExistingLayout
    //
    // Pre-populates the dialog from the existing layout's monitor configuration.
    // Selects the layout's machine in the dropdown, then loads monitor cards
    // from LayoutMonitorSettings joined to the machine monitor catalog.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadExistingLayout()
    {
        DebugLog.Write($"EditLayoutDialog.LoadExistingLayout: layoutId={_existingLayout!.Id}.");

        if (_existingLayout.MachineId.HasValue)
        {
            foreach (ComboBoxItem item in MachineComboBox.Items)
            {
                if (item.Tag is int machineId && machineId == _existingLayout.MachineId.Value)
                {
                    MachineComboBox.SelectedItem = item;
                    DebugLog.Write($"EditLayoutDialog.LoadExistingLayout: selected machineId={machineId}.");
                    break;
                }
            }
        }

        Monitors.Clear();

        MonitorRepository monitorRepo = new MonitorRepository();
        int machineIdForLookup = _existingLayout.MachineId ?? GlassContext.CurrentMachine?.Id ?? 0;
        List<Glass.Data.Models.Monitor> machineMonitors = monitorRepo.GetForMachine(machineIdForLookup);

        foreach (LayoutMonitorSettings settings in _existingLayout.Monitors)
        {
            Glass.Data.Models.Monitor? monitor = machineMonitors.FirstOrDefault(m => m.Id == settings.MonitorId);

            if (monitor == null)
            {
                DebugLog.Write($"EditLayoutDialog.LoadExistingLayout: monitorId={settings.MonitorId} not found, creating placeholder.");
                monitor = new Glass.Data.Models.Monitor
                {
                    Id = settings.MonitorId,
                    Width = 1920,
                    Height = 1080
                };
            }

            LayoutMonitorViewModel vm = new LayoutMonitorViewModel
            {
                LayoutPosition = settings.LayoutPosition,
                SlotWidth = settings.SlotWidth,
                Monitor = monitor,
                SelectedResolution = $"{monitor.Width}x{monitor.Height}"
            };

            Monitors.Add(vm);
            DebugLog.Write($"EditLayoutDialog.LoadExistingLayout: added layoutPosition={settings.LayoutPosition} monitorId={settings.MonitorId} slotWidth={settings.SlotWidth}.");
        }

        int monitorCount = Monitors.Count;
        MonitorCountComboBox.SelectedItem = MonitorCountComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Content.ToString() == monitorCount.ToString());

        DebugLog.Write($"EditLayoutDialog.LoadExistingLayout: loaded {Monitors.Count} monitors.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MachineComboBox_SelectionChanged
    //
    // Fires when the user selects a different machine.
    // Clears the current monitor configuration and reloads suggestions
    // from the selected machine's monitor catalog.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void MachineComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized)
        {
            DebugLog.Write("EditLayoutDialog.MachineComboBox_SelectionChanged: not initialized, skipping.");
            return;
        }

        if (MachineComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not int machineId)
        {
            DebugLog.Write("EditLayoutDialog.MachineComboBox_SelectionChanged: no machine selected.");
            return;
        }

        DebugLog.Write($"EditLayoutDialog.MachineComboBox_SelectionChanged: machineId={machineId}.");

        Monitors.Clear();

        MonitorRepository monitorRepo = new MonitorRepository();
        List<Glass.Data.Models.Monitor> machineMonitors = monitorRepo.GetForMachine(machineId);

        int position = 1;
        foreach (Glass.Data.Models.Monitor monitor in machineMonitors)
        {
            LayoutMonitorViewModel vm = new LayoutMonitorViewModel
            {
                LayoutPosition = position,
                SlotWidth = monitor.Width / 4,
                Monitor = monitor,
                SelectedResolution = $"{monitor.Width}x{monitor.Height}"
            };

            Monitors.Add(vm);
            DebugLog.Write($"EditLayoutDialog.MachineComboBox_SelectionChanged: added layoutPosition={position} adapter='{monitor.AdapterName}' {monitor.Width}x{monitor.Height}.");
            position++;
        }

        MonitorCountComboBox.SelectedItem = MonitorCountComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Content.ToString() == Monitors.Count.ToString());

        DebugLog.Write($"EditLayoutDialog.MachineComboBox_SelectionChanged: loaded {Monitors.Count} monitors for machineId={machineId}.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MonitorCountChanged
    //
    // Fires when the user changes the monitor count combo box.
    // Adds or removes monitor cards to match the selected count.
    // New cards are populated from the selected machine's monitor catalog if available.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void MonitorCountChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized)
        {
            DebugLog.Write("EditLayoutDialog.MonitorCountChanged: not initialized, skipping.");
            return;
        }

        if (MonitorCountComboBox.SelectedItem == null)
        {
            DebugLog.Write("EditLayoutDialog.MonitorCountChanged: no item selected, skipping.");
            return;
        }

        int selectedCount = int.Parse(((ComboBoxItem)MonitorCountComboBox.SelectedItem).Content.ToString()!);
        DebugLog.Write($"EditLayoutDialog.MonitorCountChanged: selectedCount={selectedCount} current={Monitors.Count}.");

        int machineId = 0;
        if (MachineComboBox.SelectedItem is ComboBoxItem machineItem && machineItem.Tag is int mid)
        {
            machineId = mid;
        }

        List<Glass.Data.Models.Monitor> machineMonitors = new List<Glass.Data.Models.Monitor>();
        if (machineId > 0)
        {
            MonitorRepository monitorRepo = new MonitorRepository();
            machineMonitors = monitorRepo.GetForMachine(machineId);
        }

        while (Monitors.Count < selectedCount)
        {
            int position = Monitors.Count + 1;
            Glass.Data.Models.Monitor? suggestion = machineMonitors.Count >= position
                ? machineMonitors[position - 1]
                : null;

            LayoutMonitorViewModel vm = new LayoutMonitorViewModel
            {
                LayoutPosition = position,
                SlotWidth = suggestion != null ? suggestion.Width / 4 : 480,
                Monitor = suggestion ?? new Glass.Data.Models.Monitor { Width = 1920, Height = 1080 },
                SelectedResolution = suggestion != null ? $"{suggestion.Width}x{suggestion.Height}" : "1920x1080"
            };

            Monitors.Add(vm);
            DebugLog.Write($"EditLayoutDialog.MonitorCountChanged: added layoutPosition={position} slotWidth={vm.SlotWidth}.");
        }

        while (Monitors.Count > selectedCount)
        {
            DebugLog.Write($"EditLayoutDialog.MonitorCountChanged: removing layoutPosition={Monitors.Count}.");
            Monitors.RemoveAt(Monitors.Count - 1);
        }

        UpdateTotalSlotCount();
        DebugLog.Write($"EditLayoutDialog.MonitorCountChanged: Monitors.Count={Monitors.Count}.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MonitorSettingsChanged
    //
    // Fires when the user changes the resolution or orientation for a monitor card.
    // Updates the monitor dimensions and redraws the slot preview.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void MonitorSettingsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized)
        {
            DebugLog.Write("EditLayoutDialog.MonitorSettingsChanged: not initialized, skipping.");
            return;
        }

        if (sender is not ComboBox comboBox || comboBox.DataContext is not LayoutMonitorViewModel vm)
        {
            DebugLog.Write("EditLayoutDialog.MonitorSettingsChanged: could not resolve monitor view model.");
            return;
        }

        string value = comboBox.SelectedValue?.ToString() ?? string.Empty;

        if (comboBox.Name.Contains("Resolution"))
        {
            DebugLog.Write($"EditLayoutDialog.MonitorSettingsChanged: layoutPosition={vm.LayoutPosition} resolution='{value}'.");
            vm.SelectedResolution = value;
            vm.AdjustMonitorDimensions();
        }
        else if (comboBox.Name.Contains("Orientation"))
        {
            DebugLog.Write($"EditLayoutDialog.MonitorSettingsChanged: layoutPosition={vm.LayoutPosition} orientation='{value}'.");
            vm.Monitor.Orientation = value == "Portrait" ? MonitorOrientation.Portrait : MonitorOrientation.Landscape;
            vm.AdjustMonitorDimensions();
        }

        UpdateMonitorConfigurationUI(vm);
        UpdateTotalSlotCount();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MonitorRectangle_MouseLeftButtonDown
    //
    // Fires when the user clicks anywhere on a monitor card.
    // Resolves the view model from the sender's DataContext regardless of
    // whether the click landed on the Border, Rectangle, or any other child element.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void MonitorRectangle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DebugLog.Write($"EditLayoutDialog.MonitorRectangle_MouseLeftButtonDown: fired, sender={sender?.GetType().Name}.");

        LayoutMonitorViewModel? vm = null;

        if (sender is FrameworkElement element)
        {
            vm = element.DataContext as LayoutMonitorViewModel;
        }

        if (vm == null)
        {
            DebugLog.Write("EditLayoutDialog.MonitorRectangle_MouseLeftButtonDown: could not resolve monitor view model.");
            return;
        }

        foreach (LayoutMonitorViewModel monitor in Monitors)
        {
            monitor.IsSelected = (monitor.LayoutPosition == vm.LayoutPosition);
        }

        _selectedMonitor = vm;
        DebugLog.Write($"EditLayoutDialog.MonitorRectangle_MouseLeftButtonDown: selected layoutPosition={vm.LayoutPosition}.");

        UpdateMonitorConfigurationUI(vm);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UpdateMonitorConfigurationUI
    //
    // Updates the visibility and state of the strategy-dependent UI controls
    // for the given monitor view model.
    //
    // vm:  The monitor view model to update UI for
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void UpdateMonitorConfigurationUI(LayoutMonitorViewModel vm)
    {
        DebugLog.Write($"EditLayoutDialog.UpdateMonitorConfigurationUI: layoutPosition={vm.LayoutPosition} strategy={LayoutStrategyComboBox.SelectedIndex}.");

        LayoutStrategies strategy = LayoutStrategyComboBox.SelectedIndex switch
        {
            0 => LayoutStrategies.FixedWindowSize,
            1 => LayoutStrategies.SubsetAndSwap,
            2 => LayoutStrategies.SummaryWindows,
            _ => LayoutStrategies.FixedWindowSize
        };

        switch (strategy)
        {
            case LayoutStrategies.FixedWindowSize:
                {
                    WindowSizeInputs.Visibility = Visibility.Visible;
                    GridSizeInputs.Visibility = Visibility.Visible;

                    int maxWindowWidth = vm.Monitor.Width - LayoutConstants.HorizontalMargin;
                    int maxWindowHeight = vm.Monitor.Height - LayoutConstants.VerticalMargin;
                    int calculatedMaxWidth = (int)(maxWindowHeight * LayoutConstants.AspectRatio);

                    PreferredWidthSlider.Maximum = Math.Min(maxWindowWidth, calculatedMaxWidth);
                    PreferredWidthSlider.Value = vm.SlotWidth > 0 ? vm.SlotWidth : vm.Monitor.Width / 4;

                    UpdateGridSizeText(vm);
                    RedrawWindowsForMonitor(vm);
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

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UpdateGridSizeText
    //
    // Updates the grid size text block to show how many windows fit on the given monitor
    // based on the current slot width and monitor dimensions.
    //
    // vm:  The monitor view model to compute grid size for
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void UpdateGridSizeText(LayoutMonitorViewModel vm)
    {
        int slotWidth = vm.SlotWidth > 0 ? vm.SlotWidth : vm.Monitor.Width / 4;
        int slotHeight = (int)(slotWidth / LayoutConstants.AspectRatio);

        int columns = (int)(vm.Monitor.Width / (slotWidth + LayoutConstants.HorizontalMargin));
        int rows = (int)(vm.Monitor.Height / (slotHeight + LayoutConstants.VerticalMargin));

        GridSizeTextBlock.Text = $"{columns} x {rows}";

        DebugLog.Write($"EditLayoutDialog.UpdateGridSizeText: layoutPosition={vm.LayoutPosition} columns={columns} rows={rows} slotWidth={slotWidth}.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UpdateTotalSlotCount
    //
    // Computes and displays the total number of slots across all non-primary monitors.
    // Primary monitor (LayoutPosition == 1) is reserved for full-size client windows
    // and is excluded from the slot count.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void UpdateTotalSlotCount()
    {
        int totalSlots = 0;

        foreach (LayoutMonitorViewModel vm in Monitors)
        {
            if (vm.LayoutPosition == 1)
            {
                DebugLog.Write($"EditLayoutDialog.UpdateTotalSlotCount: skipping primary monitor at layoutPosition=1.");
                continue;
            }

            int slotWidth = vm.SlotWidth > 0 ? vm.SlotWidth : vm.Monitor.Width / 4;
            int slotHeight = (int)(slotWidth / LayoutConstants.AspectRatio);

            int columns = (int)(vm.Monitor.Width / (slotWidth + LayoutConstants.HorizontalMargin));
            int rows = (int)(vm.Monitor.Height / (slotHeight + LayoutConstants.VerticalMargin));

            int slots = rows * columns;
            totalSlots += slots;

            DebugLog.Write($"EditLayoutDialog.UpdateTotalSlotCount: layoutPosition={vm.LayoutPosition} columns={columns} rows={rows} slots={slots}.");
        }

        TotalSlotsTextBlock.Text = totalSlots.ToString();
        DebugLog.Write($"EditLayoutDialog.UpdateTotalSlotCount: totalSlots={totalSlots}.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RedrawWindowsForMonitor
    //
    // Redraws the slot preview canvas for the given monitor view model.
    // Computes slot rectangles based on monitor dimensions and slot width,
    // and draws scaled rectangles on the overlay canvas.
    //
    // vm:  The monitor view model to redraw
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void RedrawWindowsForMonitor(LayoutMonitorViewModel vm)
    {
        DebugLog.Write($"EditLayoutDialog.RedrawWindowsForMonitor: layoutPosition={vm.LayoutPosition}.");

        ContentPresenter? container = MonitorConfigItemsControl
            .ItemContainerGenerator
            .ContainerFromItem(vm) as ContentPresenter;

        if (container == null)
        {
            DebugLog.Write($"EditLayoutDialog.RedrawWindowsForMonitor: container not found for layoutPosition={vm.LayoutPosition}.");
            return;
        }

        Canvas? canvas = FindVisualChildByName<Canvas>(container, "OverlayCanvas");

        if (canvas == null)
        {
            DebugLog.Write($"EditLayoutDialog.RedrawWindowsForMonitor: OverlayCanvas not found for layoutPosition={vm.LayoutPosition}.");
            return;
        }

        DrawWindowsInMonitor(canvas, vm);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // FindVisualChildByName
    //
    // Recursively searches the visual tree for a child element with the given name.
    //
    // parent:  The root of the visual tree to search
    // name:    The name of the element to find
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
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
    // DrawWindowsInMonitor
    //
    // Computes and draws scaled slot rectangles on the overlay canvas for the given monitor.
    // Updates the view model's SlotRectangles and NumSlots with the computed values.
    //
    // canvas:  The canvas to draw on
    // vm:      The monitor view model to draw slots for
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void DrawWindowsInMonitor(Canvas canvas, LayoutMonitorViewModel vm)
    {
        if (vm.LayoutPosition == 1)
        {
            DebugLog.Write($"EditLayoutDialog.DrawWindowsInMonitor: skipping primary monitor at layoutPosition=1.");
            canvas.Children.Clear();
            vm.SlotRectangles.Clear();
            vm.NumSlots = 0;
            return;
        }

        int slotWidth = vm.SlotWidth > 0 ? vm.SlotWidth : vm.Monitor.Width / 4;
        int slotHeight = (int)(slotWidth / LayoutConstants.AspectRatio);

        canvas.Children.Clear();
        vm.SlotRectangles.Clear();

        double scaledSlotWidth = slotWidth / LayoutConstants.ScalingFactor;
        double scaledSlotHeight = slotHeight / LayoutConstants.ScalingFactor;

        int columns = (int)(vm.Monitor.Width / (slotWidth + LayoutConstants.HorizontalMargin));
        int rows = (int)(vm.Monitor.Height / (slotHeight + LayoutConstants.VerticalMargin));

        double fullSizeHorizontalMargin = (vm.Monitor.Width - (columns * slotWidth)) / (double)columns;
        double fullSizeVerticalMargin = (vm.Monitor.Height - (rows * slotHeight)) / (double)rows;

        double scaledHorizontalMargin = fullSizeHorizontalMargin / LayoutConstants.ScalingFactor;
        double scaledVerticalMargin = fullSizeVerticalMargin / LayoutConstants.ScalingFactor;

        vm.NumSlots = rows * columns;

        DebugLog.Write($"EditLayoutDialog.DrawWindowsInMonitor: layoutPosition={vm.LayoutPosition} columns={columns} rows={rows} slotWidth={slotWidth} slotHeight={slotHeight}.");

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                Rectangle windowRect = new Rectangle
                {
                    Width = scaledSlotWidth,
                    Height = scaledSlotHeight,
                    Fill = Brushes.Blue,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };

                int left = (int)Math.Round(col * (slotWidth + fullSizeHorizontalMargin));
                int top = (int)Math.Round(row * (slotHeight + fullSizeVerticalMargin));

                vm.SlotRectangles.Add(new Rect(left, top, slotWidth, slotHeight));

                Canvas.SetLeft(windowRect, col * (scaledSlotWidth + scaledHorizontalMargin));
                Canvas.SetTop(windowRect, row * (scaledSlotHeight + scaledVerticalMargin));

                canvas.Children.Add(windowRect);
            }
        }

        DebugLog.Write($"EditLayoutDialog.DrawWindowsInMonitor: drew {vm.NumSlots} slots for layoutPosition={vm.LayoutPosition}.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SlotWidthChanged
    //
    // Fires when the user edits the slot width text box for a monitor card.
    // Parses the value and updates the view model, then redraws the slot preview.
    //
    // sender:  The TextBox that changed
    // e:       The event arguments
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void SlotWidthChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized)
        {
            DebugLog.Write("EditLayoutDialog.SlotWidthChanged: not initialized, skipping.");
            return;
        }

        if (sender is not TextBox textBox || textBox.DataContext is not LayoutMonitorViewModel vm)
        {
            DebugLog.Write("EditLayoutDialog.SlotWidthChanged: could not resolve monitor view model.");
            return;
        }

        if (!int.TryParse(textBox.Text, out int slotWidth) || slotWidth <= 0)
        {
            DebugLog.Write($"EditLayoutDialog.SlotWidthChanged: invalid slot width '{textBox.Text}', skipping.");
            return;
        }

        if (slotWidth == vm.SlotWidth)
        {
            return;
        }

        DebugLog.Write($"EditLayoutDialog.SlotWidthChanged: layoutPosition={vm.LayoutPosition} slotWidth={slotWidth}.");

        vm.SlotWidth = slotWidth;

        UpdateGridSizeText(vm);
        RedrawWindowsForMonitor(vm);
        UpdateTotalSlotCount();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PreferredWidthSlider_ValueChanged
    //
    // Fires when the user moves the preferred width slider.
    // Updates the slot width on the selected monitor and redraws the slot preview.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void PreferredWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized)
        {
            DebugLog.Write("EditLayoutDialog.PreferredWidthSlider_ValueChanged: not initialized, skipping.");
            return;
        }

        if (PreferredSizeTextBlock == null)
        {
            return;
        }

        int slotWidth = (int)e.NewValue;
        int slotHeight = (int)(slotWidth / LayoutConstants.AspectRatio);

        PreferredSizeTextBlock.Text = $"{slotWidth} x {slotHeight}";

        if (_selectedMonitor == null)
        {
            DebugLog.Write("EditLayoutDialog.PreferredWidthSlider_ValueChanged: no monitor selected, skipping.");
            return;
        }

        DebugLog.Write($"EditLayoutDialog.PreferredWidthSlider_ValueChanged: slotWidth={slotWidth} layoutPosition={_selectedMonitor.LayoutPosition}.");

        _selectedMonitor.SlotWidth = slotWidth;

        UpdateGridSizeText(_selectedMonitor);
        RedrawWindowsForMonitor(_selectedMonitor);
        UpdateTotalSlotCount();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LayoutStrategyChanged
    //
    // Fires when the user changes the layout strategy combo box.
    // Updates visibility of strategy-dependent UI controls for all monitors.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LayoutStrategyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized)
        {
            DebugLog.Write("EditLayoutDialog.LayoutStrategyChanged: not initialized, skipping.");
            return;
        }

        if (LayoutStrategyComboBox.SelectedItem == null)
        {
            DebugLog.Write("EditLayoutDialog.LayoutStrategyChanged: no strategy selected, skipping.");
            return;
        }

        DebugLog.Write($"EditLayoutDialog.LayoutStrategyChanged: selectedIndex={LayoutStrategyComboBox.SelectedIndex}.");

        if (_selectedMonitor != null)
        {
            UpdateMonitorConfigurationUI(_selectedMonitor);
        }
        else if (Monitors.Count > 0)
        {
            UpdateMonitorConfigurationUI(Monitors[0]);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Save_Click
    //
    // Validates the layout name and monitor configuration, then persists to the database.
    // Creates a new layout or updates the existing one depending on constructor mode.
    // Converts view models to models before calling the repository.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("EditLayoutDialog.Save_Click: saving.");

        string layoutName = LayoutNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(layoutName))
        {
            DebugLog.Write("EditLayoutDialog.Save_Click: layout name is empty, aborting.");
            MessageBox.Show("Please enter a name for this layout.", "Name Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            LayoutNameTextBox.Focus();
            return;
        }

        if (MachineComboBox.SelectedItem is not ComboBoxItem machineItem || machineItem.Tag is not int machineId)
        {
            DebugLog.Write("EditLayoutDialog.Save_Click: no machine selected, aborting.");
            MessageBox.Show("Please select a machine for this layout.", "Machine Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Monitors.Count == 0)
        {
            DebugLog.Write("EditLayoutDialog.Save_Click: no monitors configured, aborting.");
            MessageBox.Show("Please configure at least one monitor.", "Monitors Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        WindowLayoutRepository layoutRepo = new WindowLayoutRepository();

        int layoutId;

        if (_existingLayout == null)
        {
            DebugLog.Write($"EditLayoutDialog.Save_Click: creating new layout name='{layoutName}' machineId={machineId}.");
            layoutId = layoutRepo.Create(layoutName, machineId);
            DebugLog.Write($"EditLayoutDialog.Save_Click: created layoutId={layoutId}.");
        }
        else
        {
            layoutId = _existingLayout.Id;

            if (_existingLayout.Name != layoutName)
            {
                layoutRepo.Rename(layoutId, layoutName);
                DebugLog.Write($"EditLayoutDialog.Save_Click: renamed layoutId={layoutId} to '{layoutName}'.");
            }

            DebugLog.Write($"EditLayoutDialog.Save_Click: updating layoutId={layoutId}.");
        }

        // Convert view models to LayoutMonitorSettings models.
        List<LayoutMonitorSettings> monitorSettings = new List<LayoutMonitorSettings>();
        foreach (LayoutMonitorViewModel vm in Monitors)
        {
            LayoutMonitorSettings settings = new LayoutMonitorSettings
            {
                LayoutId = layoutId,
                MonitorId = vm.Monitor.Id,
                LayoutPosition = vm.LayoutPosition,
                SlotWidth = vm.SlotWidth
            };
            monitorSettings.Add(settings);
            DebugLog.Write($"EditLayoutDialog.Save_Click: monitor layoutPosition={settings.LayoutPosition} monitorId={settings.MonitorId} slotWidth={settings.SlotWidth}.");
        }

        layoutRepo.SaveLayoutMonitors(layoutId, monitorSettings);

        // Convert slot rectangles from non-primary monitors to SlotPlacement models.
        List<SlotPlacement> placements = new List<SlotPlacement>();
        int slotNumber = 1;

        foreach (LayoutMonitorViewModel vm in Monitors)
        {
            if (vm.LayoutPosition == 1)
            {
                DebugLog.Write($"EditLayoutDialog.Save_Click: skipping primary monitor at layoutPosition=1.");
                continue;
            }

            foreach (Rect rect in vm.SlotRectangles)
            {
                SlotPlacement placement = new SlotPlacement
                {
                    LayoutId = layoutId,
                    MonitorId = vm.Monitor.Id,
                    SlotNumber = slotNumber,
                    X = (int)rect.X,
                    Y = (int)rect.Y,
                    Width = (int)rect.Width,
                    Height = (int)rect.Height
                };
                placements.Add(placement);
                DebugLog.Write($"EditLayoutDialog.Save_Click: slot={slotNumber} monitorId={vm.Monitor.Id} x={placement.X} y={placement.Y} w={placement.Width} h={placement.Height}.");
                slotNumber++;
            }
        }

        layoutRepo.SaveSlotPlacements(layoutId, placements);

        DebugLog.Write($"EditLayoutDialog.Save_Click: saved layoutId={layoutId} name='{layoutName}' monitors={monitorSettings.Count} placements={placements.Count}.");
        DialogResult = true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Cancel_Click
    //
    // Closes the dialog without saving.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("EditLayoutDialog.Cancel_Click: cancelled.");
        DialogResult = false;
    }
}