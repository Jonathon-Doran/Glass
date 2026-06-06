using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.UI.Dialogs;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Glass;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ManageVideoSourcesDialog
//
// Dialog for creating, editing, and deleting video source subsample regions.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public partial class ManageVideoSourcesDialog : Window
{
    private List<VideoSource> _sources = new();
    private VideoSource? _selectedSource = null;
    private UISkin? _selectedSkin = null;
    private RegionOverlayWindow? _activeOverlay = null;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ManageVideoSourcesDialog constructor
    //
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public ManageVideoSourcesDialog()
    {
        InitializeComponent();
        LoadUISkins();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadUISkins
    //
    // Loads all UI skins and populates the dropdown.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadUISkins()
    {
        DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.LoadUISkins: loading skins.");

        UISkinRepository skinRepo = new UISkinRepository();
        List<UISkin> skins = skinRepo.GetAll();

        UISkinComboBox.ItemsSource = skins;

        if (skins.Count > 0)
        {
            UISkinComboBox.SelectedIndex = 0;
        }

        DebugLog.Write(LogChannel.General, $"ManageVideoSourcesDialog.LoadUISkins: loaded {skins.Count} skins.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UISkinComboBox_SelectionChanged
    //
    // Fires when a UI skin is selected. Loads sources for that skin.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void UISkinComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UISkinComboBox.SelectedItem is not UISkin skin)
        {
            DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.UISkinComboBox_SelectionChanged: no skin selected.");
            _selectedSkin = null;
            _sources.Clear();
            SourceListView.ItemsSource = null;
            return;
        }

        DebugLog.Write(LogChannel.General, $"ManageVideoSourcesDialog.UISkinComboBox_SelectionChanged: selected '{skin.Name}'.");
        _selectedSkin = skin;
        LoadSources();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadSources
    //
    // Loads video sources for the selected UI skin and populates the list view.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadSources()
    {
        if (_selectedSkin == null)
        {
            DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.LoadSources: no skin selected.");
            _sources.Clear();
            SourceListView.ItemsSource = null;
            return;
        }

        DebugLog.Write(LogChannel.General, $"ManageVideoSourcesDialog.LoadSources: loading sources for skin '{_selectedSkin.Name}'.");

        VideoSourceRepository repo = new VideoSourceRepository();
        _sources = repo.GetByUISkin(_selectedSkin.Id).ToList();

        SourceListView.ItemsSource = null;
        SourceListView.ItemsSource = _sources;

        DebugLog.Write(LogChannel.General, $"ManageVideoSourcesDialog.LoadSources: loaded {_sources.Count} sources.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SourceListView_KeyDown
    //
    // Clears selection when ESC is pressed in the list.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void SourceListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.SourceListView_KeyDown: ESC pressed, clearing selection.");
            ClearSelection();
            e.Handled = true;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SourceListView_SelectionChanged
    //
    // Fires when the user selects a source in the list. Loads the source into the edit controls.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void SourceListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceListView.SelectedItem is not VideoSource source)
        {
            DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.SourceListView_SelectionChanged: no source selected.");
            ClearSelection();
            return;
        }

        DebugLog.Write(LogChannel.General, $"ManageVideoSourcesDialog.SourceListView_SelectionChanged: source='{source.Name}'.");

        _selectedSource = source;
        NameTextBox.Text = source.Name;
        XTextBox.Text = source.X.ToString();
        YTextBox.Text = source.Y.ToString();
        WidthTextBox.Text = source.Width.ToString();
        HeightTextBox.Text = source.Height.ToString();

        NewUpdateButton.Content = "Update";
        NewUpdateButton.IsEnabled = true;
        DeleteButton.IsEnabled = true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ClearSelection
    //
    // Clears the selected source and edit controls.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ClearSelection()
    {
        DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.ClearSelection: clearing selection.");

        _selectedSource = null;
        SourceListView.SelectedItem = null;
        NameTextBox.Text = string.Empty;
        XTextBox.Text = string.Empty;
        YTextBox.Text = string.Empty;
        WidthTextBox.Text = string.Empty;
        HeightTextBox.Text = string.Empty;

        NewUpdateButton.Content = "New";
        NewUpdateButton.IsEnabled = false;
        DeleteButton.IsEnabled = false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // NameTextBox_TextChanged
    //
    // Fires when the name text changes. Enables the New/Update button if name is not empty.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void NameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        bool hasName = !string.IsNullOrWhiteSpace(NameTextBox.Text);
        NewUpdateButton.IsEnabled = hasName;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // NewUpdateButton_Click
    //
    // Creates a new source or updates the selected source, saving immediately to the database.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void NewUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSkin == null)
        {
            DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.NewUpdateButton_Click: no skin selected.");
            MessageBox.Show("Please select a UI skin first.", "No Skin Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string name = NameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.NewUpdateButton_Click: name is empty.");
            MessageBox.Show("Please enter a name.", "Name Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(XTextBox.Text, out int x))
        {
            DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.NewUpdateButton_Click: invalid X value.");
            MessageBox.Show("Please enter a valid X coordinate.", "Invalid X", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(YTextBox.Text, out int y))
        {
            DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.NewUpdateButton_Click: invalid Y value.");
            MessageBox.Show("Please enter a valid Y coordinate.", "Invalid Y", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(WidthTextBox.Text, out int width))
        {
            DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.NewUpdateButton_Click: invalid Width value.");
            MessageBox.Show("Please enter a valid width.", "Invalid Width", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(HeightTextBox.Text, out int height))
        {
            DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.NewUpdateButton_Click: invalid Height value.");
            MessageBox.Show("Please enter a valid height.", "Invalid Height", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        VideoSourceRepository repo = new VideoSourceRepository();

        if (_selectedSource != null)
        {
            DebugLog.Write(LogChannel.General, $"ManageVideoSourcesDialog.NewUpdateButton_Click: updating source id={_selectedSource.Id}.");
            _selectedSource.Name = name;
            _selectedSource.UISkinId = _selectedSkin.Id;
            _selectedSource.X = x;
            _selectedSource.Y = y;
            _selectedSource.Width = width;
            _selectedSource.Height = height;
            repo.Save(_selectedSource);
            DebugLog.Write(LogChannel.General, $"ManageVideoSourcesDialog.NewUpdateButton_Click: saved source id={_selectedSource.Id}.");
        }
        else
        {
            DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.NewUpdateButton_Click: creating new source.");
            VideoSource newSource = new VideoSource
            {
                Name = name,
                UISkinId = _selectedSkin.Id,
                X = x,
                Y = y,
                Width = width,
                Height = height
            };
            repo.Save(newSource);
            DebugLog.Write(LogChannel.General, $"ManageVideoSourcesDialog.NewUpdateButton_Click: saved new source id={newSource.Id}.");
            _sources.Add(newSource);
        }

        LoadSources();
        ClearSelection();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DeleteButton_Click
    //
    // Deletes the selected source after confirmation, immediately removing from database.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSource == null)
        {
            DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.DeleteButton_Click: no source selected.");
            return;
        }

        DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.DeleteButton_Click: confirming delete of '{_selectedSource.Name}'.");

        MessageBoxResult result = MessageBox.Show(
            $"Delete video source '{_selectedSource.Name}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            DebugLog.Write(LogChannel.General, $"ManageVideoSourcesDialog.DeleteButton_Click: deleting source id={_selectedSource.Id}.");

            VideoSourceRepository repo = new VideoSourceRepository();
            repo.Delete(_selectedSource.Id);

            _sources.Remove(_selectedSource);
            LoadSources();
            ClearSelection();
        }
        else
        {
            DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.DeleteButton_Click: delete cancelled.");
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Cancel_Click
    //
    // Closes the dialog.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.Cancel_Click: closing dialog.");
        Close();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PositionOverlayButton_Checked
    //
    // Opens the region overlay window when the toggle button is checked.
    // The overlay allows the user to visually position and size a source region.
    // The overlay is configured with teal colors to match Glass's UI theme.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void PositionOverlayButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_activeOverlay != null)
        {
            DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.PositionOverlayButton_Checked: overlay already exists, closing it first.");
            _activeOverlay.Close();
            _activeOverlay = null;
        }

        _activeOverlay = new RegionOverlayWindow();

        // Configure colors to match Glass theme
        _activeOverlay.BorderColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2AFFD7"));
        _activeOverlay.HandleColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2AFFD7"));

        // Opcode overlay closure from X button or other means
        _activeOverlay.Closed += (s, args) =>
        {
            DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog: overlay closed externally.");
            _activeOverlay = null;
            PositionOverlayButton.IsChecked = false;
        };

        // Show the overlay
        _activeOverlay.Owner = this;
        _activeOverlay.Show();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PositionOverlayButton_Unchecked
    //
    // Closes the overlay window and captures the final coordinates when the toggle button is unchecked.
    // Converts WPF logical units to physical pixels using the DPI transform.
    // Updates the coordinate fields with the raw pixel values from the overlay.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void PositionOverlayButton_Unchecked(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.PositionOverlayButton_Unchecked: closing overlay and capturing coordinates.");

        if (_activeOverlay == null)
        {
            DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.PositionOverlayButton_Unchecked: no active overlay.");
            return;
        }

        PresentationSource source = PresentationSource.FromVisual(_activeOverlay);
        if (source != null)
        {
            Matrix transform = source.CompositionTarget.TransformToDevice;

            Point topLeft = transform.Transform(new Point(_activeOverlay.Left, _activeOverlay.Top));
            Point sizePoint = transform.Transform(new Point(_activeOverlay.Width, _activeOverlay.Height));

            DebugLog.Write(LogChannel.General, $"ManageVideoSourcesDialog.PositionOverlayButton_Unchecked: captured ({topLeft.X},{topLeft.Y}) {sizePoint.X}x{sizePoint.Y}.");

            XTextBox.Text = ((int)topLeft.X).ToString();
            YTextBox.Text = ((int)topLeft.Y).ToString();
            WidthTextBox.Text = ((int)sizePoint.X).ToString();
            HeightTextBox.Text = ((int)sizePoint.Y).ToString();
        }
        else
        {
            DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.PositionOverlayButton_Unchecked: PresentationSource unavailable, coordinates not captured.");
        }

        _activeOverlay.Close();
        _activeOverlay = null;

        DebugLog.Write(LogChannel.General, "ManageVideoSourcesDialog.PositionOverlayButton_Unchecked: overlay closed.");
    }
}