using Glass.Core;
using Glass.Data.Models;
using Glass.Data.Repositories;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Glass;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ManageVideoDestinationsDialog
//
// Dialog for creating, editing, and deleting video destination templates.
// Destinations are normalized to a reference width of 1920 and are global (not per-profile).
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public partial class ManageVideoDestinationsDialog : Window
{
    private List<VideoDestination> _destinations = new();
    private VideoDestination? _selectedDestination = null;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ManageVideoDestinationsDialog
    //
    // Constructor. Loads all video destinations from the database.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public ManageVideoDestinationsDialog()
    {
        InitializeComponent();
        LoadDestinations();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadDestinations
    //
    // Loads all video destinations from the database and populates the list view.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadDestinations()
    {
        DebugLog.Write("ManageVideoDestinationsDialog.LoadDestinations: loading destinations.");

        VideoDestinationRepository repo = new VideoDestinationRepository();
        _destinations = repo.GetAll().ToList();

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
        NameTextBox.Text = destination.Name;
        XTextBox.Text = destination.X.ToString();
        YTextBox.Text = destination.Y.ToString();
        WidthTextBox.Text = destination.Width.ToString();
        HeightTextBox.Text = destination.Height.ToString();

        NewUpdateButton.Content = "Update";
        NewUpdateButton.IsEnabled = true;
        DeleteButton.IsEnabled = true;
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
    // Clears the selected destination and edit controls.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ClearSelection()
    {
        DebugLog.Write("ManageVideoDestinationsDialog.ClearSelection: clearing selection.");

        _selectedDestination = null;
        DestinationListView.SelectedItem = null;
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
    // Creates a new destination or updates the selected destination, saving immediately to the database.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void NewUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        string name = NameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            DebugLog.Write("ManageVideoDestinationsDialog.NewUpdateButton_Click: name is empty.");
            MessageBox.Show("Please enter a name.", "Name Required", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            _selectedDestination.Name = name;
            _selectedDestination.X = x;
            _selectedDestination.Y = y;
            _selectedDestination.Width = width;
            _selectedDestination.Height = height;
            repo.Save(_selectedDestination);
            DebugLog.Write($"ManageVideoDestinationsDialog.NewUpdateButton_Click: saved destination id={_selectedDestination.Id}.");
        }
        else
        {
            DebugLog.Write("ManageVideoDestinationsDialog.NewUpdateButton_Click: creating new destination.");
            VideoDestination newDestination = new VideoDestination
            {
                Name = name,
                X = x,
                Y = y,
                Width = width,
                Height = height
            };
            repo.Save(newDestination);
            DebugLog.Write($"ManageVideoDestinationsDialog.NewUpdateButton_Click: saved new destination id={newDestination.Id}.");
            _destinations.Add(newDestination);
        }

        LoadDestinations();
        ClearSelection();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DeleteButton_Click
    //
    // Deletes the selected destination after confirmation, immediately removing from database.
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
    // Cancel_Click
    //
    // Closes the dialog.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("ManageVideoDestinationsDialog.Cancel_Click: closing dialog.");
        Close();
    }
}