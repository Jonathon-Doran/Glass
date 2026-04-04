using Glass.Core;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.UI.Dialogs;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Glass;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ManageVideoDestinationsDialog
//
// Dialog for creating, editing, and deleting video destination regions.
// Destinations are keyed by name and UI skin, paired with a VideoSource of the same name.
// Coordinates are slot-relative absolute pixels.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public partial class ManageVideoDestinationsDialog : Window
{
    private List<VideoDestination> _destinations = new();
    private VideoDestination? _selectedDestination = null;
    private UISkin? _selectedSkin = null;
    private bool _initialized = false;
    private SlotPlacement? _slot1Placement = null;

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Win32Rect rect);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref Win32Point point);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point point);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ManageVideoDestinationsDialog
    //
    // Constructor. Loads UI skins and looks up slot 1's position for coordinate conversion.
    //
    // layoutId:  The active layout ID, used to convert overlay coordinates to slot-relative
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public ManageVideoDestinationsDialog(int layoutId)
    {
        InitializeComponent();

        if (layoutId > 0)
        {
            WindowLayoutRepository layoutRepo = new WindowLayoutRepository();
            IReadOnlyList<SlotPlacement> placements = layoutRepo.GetSlotPlacements(layoutId);
            _slot1Placement = placements.FirstOrDefault(p => p.SlotNumber == 1);

            if (_slot1Placement != null)
            {
                DebugLog.Write($"ManageVideoDestinationsDialog: slot 1 at ({_slot1Placement.X},{_slot1Placement.Y}) {_slot1Placement.Width}x{_slot1Placement.Height}.");
            }
            else
            {
                DebugLog.Write("ManageVideoDestinationsDialog: slot 1 not found in layout.");
            }
        }
        else
        {
            DebugLog.Write("ManageVideoDestinationsDialog: no layout ID provided, coordinate conversion disabled.");
        }

        LoadUISkins();
        _initialized = true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadUISkins
    //
    // Loads all UI skins and populates the skin dropdown.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadUISkins()
    {
        DebugLog.Write("ManageVideoDestinationsDialog.LoadUISkins: loading skins.");

        UISkinRepository skinRepo = new UISkinRepository();
        List<UISkin> skins = skinRepo.GetAll();

        UISkinComboBox.ItemsSource = skins;

        if (skins.Count > 0)
        {
            UISkinComboBox.SelectedIndex = 0;
        }

        DebugLog.Write($"ManageVideoDestinationsDialog.LoadUISkins: loaded {skins.Count} skins.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UISkinComboBox_SelectionChanged
    //
    // Fires when a UI skin is selected. Reloads sources and destinations for that skin.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void UISkinComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DestinationListView == null)
        {
            DebugLog.Write("ManageVideoDestinationsDialog.UISkinComboBox_SelectionChanged: not yet initialized, ignoring.");
            return;
        }

        if (UISkinComboBox.SelectedItem is not UISkin skin)
        {
            DebugLog.Write("ManageVideoDestinationsDialog.UISkinComboBox_SelectionChanged: no skin selected.");
            _selectedSkin = null;
            _destinations.Clear();
            DestinationListView.ItemsSource = null;
            SourceComboBox.ItemsSource = null;
            return;
        }

        DebugLog.Write($"ManageVideoDestinationsDialog.UISkinComboBox_SelectionChanged: selected '{skin.Name}'.");
        _selectedSkin = skin;
        LoadSources();
        LoadDestinations();
        ClearSelection();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadSources
    //
    // Loads video sources for the selected UI skin into the source dropdown.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadSources()
    {
        if (_selectedSkin == null)
        {
            DebugLog.Write("ManageVideoDestinationsDialog.LoadSources: no skin selected.");
            SourceComboBox.ItemsSource = null;
            return;
        }

        DebugLog.Write($"ManageVideoDestinationsDialog.LoadSources: loading sources for skin '{_selectedSkin.Name}'.");

        VideoSourceRepository repo = new VideoSourceRepository();
        List<VideoSource> sources = repo.GetByUISkin(_selectedSkin.Id);

        SourceComboBox.ItemsSource = null;
        SourceComboBox.ItemsSource = sources;

        DebugLog.Write($"ManageVideoDestinationsDialog.LoadSources: loaded {sources.Count} sources.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadDestinations
    //
    // Loads video destinations for the selected UI skin and populates the list view.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadDestinations()
    {
        if (_selectedSkin == null)
        {
            DebugLog.Write("ManageVideoDestinationsDialog.LoadDestinations: no skin selected.");
            _destinations.Clear();
            DestinationListView.ItemsSource = null;
            return;
        }

        DebugLog.Write($"ManageVideoDestinationsDialog.LoadDestinations: loading destinations for skin '{_selectedSkin.Name}'.");

        VideoDestinationRepository repo = new VideoDestinationRepository();
        _destinations = repo.GetByUISkin(_selectedSkin.Id).ToList();

        DestinationListView.ItemsSource = null;
        DestinationListView.ItemsSource = _destinations;

        DebugLog.Write($"ManageVideoDestinationsDialog.LoadDestinations: loaded {_destinations.Count} destinations.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DestinationListView_SelectionChanged
    //
    // Fires when the user selects a destination. Loads it into the edit controls.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void DestinationListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DestinationListView.SelectedItem is not VideoDestination destination)
        {
            DebugLog.Write("ManageVideoDestinationsDialog.DestinationListView_SelectionChanged: no destination selected.");
            ClearSelection();
            return;
        }

        DebugLog.Write($"ManageVideoDestinationsDialog.DestinationListView_SelectionChanged: destination='{destination.Name}'.");

        _selectedDestination = destination;

        // Select the matching source in the dropdown.
        if (SourceComboBox.ItemsSource is List<VideoSource> sources)
        {
            SourceComboBox.SelectedItem = sources.FirstOrDefault(s => s.Name == destination.Name);
        }

        XTextBox.Text = destination.X.ToString();
        YTextBox.Text = destination.Y.ToString();
        WidthTextBox.Text = destination.Width.ToString();
        HeightTextBox.Text = destination.Height.ToString();

        NewUpdateButton.Content = "Update";
        NewUpdateButton.IsEnabled = true;
        DeleteButton.IsEnabled = true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SourceComboBox_SelectionChanged
    //
    // Fires when the user selects a source. Enables the New/Update button.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void SourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        if (SourceComboBox.SelectedItem is VideoSource)
        {
            NewUpdateButton.IsEnabled = true;
        }
        else
        {
            NewUpdateButton.IsEnabled = false;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DestinationListView_KeyDown
    //
    // Clears selection when ESC is pressed in the list.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void DestinationListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DebugLog.Write("ManageVideoDestinationsDialog.DestinationListView_KeyDown: ESC pressed, clearing selection.");
            ClearSelection();
            e.Handled = true;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ClearSelection
    //
    // Clears the selected destination and resets the edit controls.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ClearSelection()
    {
        DebugLog.Write("ManageVideoDestinationsDialog.ClearSelection: clearing selection.");

        _selectedDestination = null;
        DestinationListView.SelectedItem = null;
        SourceComboBox.SelectedItem = null;
        XTextBox.Text = string.Empty;
        YTextBox.Text = string.Empty;
        WidthTextBox.Text = string.Empty;
        HeightTextBox.Text = string.Empty;

        NewUpdateButton.Content = "New";
        NewUpdateButton.IsEnabled = false;
        DeleteButton.IsEnabled = false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // NewUpdateButton_Click
    //
    // Creates a new destination or updates the selected destination.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void NewUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSkin == null)
        {
            DebugLog.Write("ManageVideoDestinationsDialog.NewUpdateButton_Click: no skin selected.");
            MessageBox.Show("Please select a UI skin first.", "No Skin Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SourceComboBox.SelectedItem is not VideoSource source)
        {
            DebugLog.Write("ManageVideoDestinationsDialog.NewUpdateButton_Click: no source selected.");
            MessageBox.Show("Please select a source first.", "No Source Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(XTextBox.Text, out int x))
        {
            DebugLog.Write("ManageVideoDestinationsDialog.NewUpdateButton_Click: invalid X value.");
            MessageBox.Show("Please enter a valid X coordinate.", "Invalid X", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(YTextBox.Text, out int y))
        {
            DebugLog.Write("ManageVideoDestinationsDialog.NewUpdateButton_Click: invalid Y value.");
            MessageBox.Show("Please enter a valid Y coordinate.", "Invalid Y", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(WidthTextBox.Text, out int width))
        {
            DebugLog.Write("ManageVideoDestinationsDialog.NewUpdateButton_Click: invalid Width value.");
            MessageBox.Show("Please enter a valid width.", "Invalid Width", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(HeightTextBox.Text, out int height))
        {
            DebugLog.Write("ManageVideoDestinationsDialog.NewUpdateButton_Click: invalid Height value.");
            MessageBox.Show("Please enter a valid height.", "Invalid Height", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        VideoDestinationRepository repo = new VideoDestinationRepository();

        if (_selectedDestination != null)
        {
            DebugLog.Write($"ManageVideoDestinationsDialog.NewUpdateButton_Click: updating destination id={_selectedDestination.Id}.");
            _selectedDestination.Name = source.Name;
            _selectedDestination.UISkinId = _selectedSkin.Id;
            _selectedDestination.X = x;
            _selectedDestination.Y = y;
            _selectedDestination.Width = width;
            _selectedDestination.Height = height;
            repo.Save(_selectedDestination);
            DebugLog.Write($"ManageVideoDestinationsDialog.NewUpdateButton_Click: saved destination id={_selectedDestination.Id}.");


            // Send updated region destination to GlassVideo immediately.
            if (GlassContext.GlassVideoPipe != null)
            {
                string cmd = $"region_dest {_selectedDestination.Name} {_selectedDestination.X} {_selectedDestination.Y} {_selectedDestination.Width} {_selectedDestination.Height}";
                DebugLog.Write($"ManageVideoDestinationsDialog.NewUpdateButton_Click: sending {cmd}");
                GlassContext.GlassVideoPipe.Send(cmd);
            }
        }
        else
        {
            DebugLog.Write("ManageVideoDestinationsDialog.NewUpdateButton_Click: creating new destination.");
            VideoDestination newDestination = new VideoDestination
            {
                Name = source.Name,
                UISkinId = _selectedSkin.Id,
                X = x,
                Y = y,
                Width = width,
                Height = height
            };
            repo.Save(newDestination);
            DebugLog.Write($"ManageVideoDestinationsDialog.NewUpdateButton_Click: saved new destination id={newDestination.Id}.");
            _destinations.Add(newDestination);

            // Send updated region destination to GlassVideo immediately.
            if (GlassContext.GlassVideoPipe != null)
            {
                string cmd = $"region_dest {newDestination.Name} {newDestination.X} {newDestination.Y} {newDestination.Width} {newDestination.Height}";
                DebugLog.Write($"ManageVideoDestinationsDialog.NewUpdateButton_Click: sending {cmd}");
                GlassContext.GlassVideoPipe.Send(cmd);
            }
        }

        LoadDestinations();
        ClearSelection();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DeleteButton_Click
    //
    // Deletes the selected destination after confirmation.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDestination == null)
        {
            DebugLog.Write("ManageVideoDestinationsDialog.DeleteButton_Click: no destination selected.");
            return;
        }

        DebugLog.Write($"ManageVideoDestinationsDialog.DeleteButton_Click: confirming delete of '{_selectedDestination.Name}'.");

        MessageBoxResult result = MessageBox.Show(
            $"Delete video destination '{_selectedDestination.Name}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            DebugLog.Write($"ManageVideoDestinationsDialog.DeleteButton_Click: deleting destination id={_selectedDestination.Id}.");

            VideoDestinationRepository repo = new VideoDestinationRepository();
            repo.Delete(_selectedDestination.Id);

            _destinations.Remove(_selectedDestination);
            LoadDestinations();
            ClearSelection();
        }
        else
        {
            DebugLog.Write("ManageVideoDestinationsDialog.DeleteButton_Click: delete cancelled.");
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PositionOverlayButton_Checked
    //
    // Opens the region overlay window when the toggle button is checked.
    // If a destination is selected, pre-populates the overlay from its stored coordinates.
    // If no destination is selected, positions the overlay near the mouse cursor.
    // If GlassVideo is not running, logs and unchecks the button without opening the overlay.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void PositionOverlayButton_Checked(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("ManageVideoDestinationsDialog.PositionOverlayButton_Checked: opening overlay window.");

        IntPtr glassVideoHwnd = FindWindow("GlassVideoWindow", null);
        if (glassVideoHwnd == IntPtr.Zero)
        {
            DebugLog.Write("ManageVideoDestinationsDialog.PositionOverlayButton_Checked: GlassVideo window not found, aborting.");
            PositionOverlayButton.IsChecked = false;
            return;
        }

        if (!GetWindowRect(glassVideoHwnd, out Win32Rect glassVideoRect))
        {
            DebugLog.Write("ManageVideoDestinationsDialog.PositionOverlayButton_Checked: GetWindowRect failed for GlassVideo, aborting.");
            PositionOverlayButton.IsChecked = false;
            return;
        }

        DebugLog.Write($"ManageVideoDestinationsDialog.PositionOverlayButton_Checked: GlassVideo window at ({glassVideoRect.Left},{glassVideoRect.Top}) {glassVideoRect.Right - glassVideoRect.Left}x{glassVideoRect.Bottom - glassVideoRect.Top}.");

        RegionOverlayWindow overlay = new RegionOverlayWindow();
        overlay.BorderColor = System.Windows.Media.Brushes.Teal;
        overlay.HandleColor = System.Windows.Media.Brushes.Teal;
        overlay.Owner = this;

        if (SourceComboBox.SelectedItem is VideoSource source && source.Width > 0 && source.Height > 0)
        {
            overlay.AspectRatio = source.Width / (double)source.Height;
            DebugLog.Write($"ManageVideoDestinationsDialog.PositionOverlayButton_Checked: aspect ratio set to {overlay.AspectRatio:F4} from source '{source.Name}' {source.Width}x{source.Height}.");
        }
        else
        {
            DebugLog.Write("ManageVideoDestinationsDialog.PositionOverlayButton_Checked: no source selected or invalid dimensions, aspect ratio unlocked.");
        }

        PresentationSource presentationSource = PresentationSource.FromVisual(this);
        if (presentationSource == null)
        {
            DebugLog.Write("ManageVideoDestinationsDialog.PositionOverlayButton_Checked: PresentationSource not available, aborting.");
            PositionOverlayButton.IsChecked = false;
            return;
        }

        Matrix transformFromDevice = presentationSource.CompositionTarget.TransformFromDevice;
        overlay.WindowStartupLocation = WindowStartupLocation.Manual;

        if (_selectedDestination != null && _selectedDestination.Width > 0 && _selectedDestination.Height > 0)
        {
            DebugLog.Write($"ManageVideoDestinationsDialog.PositionOverlayButton_Checked: pre-populating from destination '{_selectedDestination.Name}' at ({_selectedDestination.X},{_selectedDestination.Y}) {_selectedDestination.Width}x{_selectedDestination.Height}.");

            // Convert slot-relative physical pixels back to screen coordinates.
            // Screen = GlassVideo origin + slot 1 origin + destination offset.
            int slot1X = _slot1Placement?.X ?? 0;
            int slot1Y = _slot1Placement?.Y ?? 0;

            Win32Point clientOrigin = new Win32Point { X = 0, Y = 0 };
            ClientToScreen(glassVideoHwnd, ref clientOrigin);
            DebugLog.Write($"ManageVideoDestinationsDialog.PositionOverlayButton_Checked: GlassVideo client origin=({clientOrigin.X},{clientOrigin.Y}).");

            int screenPhysicalX = clientOrigin.X + slot1X + _selectedDestination.X;
            int screenPhysicalY = clientOrigin.Y + slot1Y + _selectedDestination.Y;

            DebugLog.Write($"ManageVideoDestinationsDialog.PositionOverlayButton_Checked: screen physical position ({screenPhysicalX},{screenPhysicalY}), slot1 offset ({slot1X},{slot1Y}).");
            Point logicalPosition = transformFromDevice.Transform(new Point(screenPhysicalX, screenPhysicalY));
            Point logicalSize = transformFromDevice.Transform(new Point(_selectedDestination.Width, _selectedDestination.Height));

            overlay.Left = logicalPosition.X;
            overlay.Top = logicalPosition.Y;
            overlay.Width = logicalSize.X;
            overlay.Height = logicalSize.Y;

            DebugLog.Write($"ManageVideoDestinationsDialog.PositionOverlayButton_Checked: overlay logical position ({overlay.Left:F1},{overlay.Top:F1}) size ({overlay.Width:F1}x{overlay.Height:F1}).");
        }
        else
        {
            DebugLog.Write("ManageVideoDestinationsDialog.PositionOverlayButton_Checked: no existing destination, sizing from source and positioning near cursor.");

            // Size from source dimensions if available.
            if (SourceComboBox.SelectedItem is VideoSource sourceForSize && sourceForSize.Width > 0 && sourceForSize.Height > 0)
            {
                Point logicalSize = transformFromDevice.Transform(new Point(sourceForSize.Width, sourceForSize.Height));
                overlay.Width = logicalSize.X;
                overlay.Height = logicalSize.Y;
                DebugLog.Write($"ManageVideoDestinationsDialog.PositionOverlayButton_Checked: overlay sized to {overlay.Width:F1}x{overlay.Height:F1} from source.");
            }

            // Position near mouse cursor.
            GetCursorPos(out Win32Point cursorPos);
            Point logicalCursor = transformFromDevice.Transform(new Point(cursorPos.X, cursorPos.Y));
            overlay.Left = logicalCursor.X;
            overlay.Top = logicalCursor.Y;

            DebugLog.Write($"ManageVideoDestinationsDialog.PositionOverlayButton_Checked: overlay positioned at cursor ({overlay.Left:F1},{overlay.Top:F1}).");
        }

        overlay.Show();

        _activeOverlay = overlay;

        overlay.Closed += (s, args) =>
        {
            DebugLog.Write("ManageVideoDestinationsDialog: overlay closed externally.");
            _activeOverlay = null;
        };

        DebugLog.Write("ManageVideoDestinationsDialog.PositionOverlayButton_Checked: overlay window opened.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PositionOverlayButton_Unchecked
    //
    // Closes the overlay window and captures the final coordinates when the toggle button is unchecked.
    // Converts WPF logical units to physical pixels using Win32 GetWindowRect.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void PositionOverlayButton_Unchecked(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("ManageVideoDestinationsDialog.PositionOverlayButton_Unchecked: closing overlay and capturing coordinates.");

        if (_activeOverlay == null)
        {
            DebugLog.Write("ManageVideoDestinationsDialog.PositionOverlayButton_Unchecked: no active overlay.");
            return;
        }

        WindowInteropHelper helper = new WindowInteropHelper(_activeOverlay);
        if (GetWindowRect(helper.Handle, out Win32Rect rect))
        {
            int x = rect.Left;
            int y = rect.Top;
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            DebugLog.Write($"ManageVideoDestinationsDialog.PositionOverlayButton_Unchecked: GetWindowRect=({rect.Left},{rect.Top},{rect.Right},{rect.Bottom}), overlay WPF Left={_activeOverlay.Left:F2} Top={_activeOverlay.Top:F2} Width={_activeOverlay.Width:F2} Height={_activeOverlay.Height:F2}.");

            // Convert virtual desktop coordinates to slot-relative.
            // Use ClientToScreen to get the true client area origin, excluding window chrome.
            IntPtr glassVideoHwnd = FindWindow("GlassVideoWindow", null);
            if (glassVideoHwnd != IntPtr.Zero)
            {
                Win32Point clientOrigin = new Win32Point { X = 0, Y = 0 };
                ClientToScreen(glassVideoHwnd, ref clientOrigin);
                DebugLog.Write($"ManageVideoDestinationsDialog.PositionOverlayButton_Unchecked: GlassVideo client origin=({clientOrigin.X},{clientOrigin.Y}).");

                if (_slot1Placement != null)
                {
                    x -= clientOrigin.X + _slot1Placement.X;
                    y -= clientOrigin.Y + _slot1Placement.Y;
                    DebugLog.Write($"ManageVideoDestinationsDialog.PositionOverlayButton_Unchecked: raw ({rect.Left},{rect.Top}) -> slot-relative ({x},{y}) {width}x{height}.");
                }
                else
                {
                    x -= clientOrigin.X;
                    y -= clientOrigin.Y;
                    DebugLog.Write($"ManageVideoDestinationsDialog.PositionOverlayButton_Unchecked: raw ({rect.Left},{rect.Top}) -> GlassVideo-relative ({x},{y}) {width}x{height} (no slot 1).");
                }
            }
            else
            {
                DebugLog.Write("ManageVideoDestinationsDialog.PositionOverlayButton_Unchecked: GlassVideo window not found, using raw coordinates.");
            }

            XTextBox.Text = x.ToString();
            YTextBox.Text = y.ToString();
            WidthTextBox.Text = width.ToString();
            HeightTextBox.Text = height.ToString();
        }
        else
        {
            DebugLog.Write("ManageVideoDestinationsDialog.PositionOverlayButton_Unchecked: GetWindowRect failed.");
        }

        _activeOverlay.Close();
        _activeOverlay = null;

        DebugLog.Write("ManageVideoDestinationsDialog.PositionOverlayButton_Unchecked: overlay closed.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Cancel_Click
    //
    // Closes the dialog.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("ManageVideoDestinationsDialog.Cancel_Click: closing dialog.");
        Close();
    }

    private RegionOverlayWindow? _activeOverlay = null;
}