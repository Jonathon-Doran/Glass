using Glass.Core;
using Glass.Data.Models;
using Glass.Data.Repositories;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Glass;

public partial class ManagePagesDialog : Window
{
    private KeyPage? _selectedPage;
    private bool _suppressNameLostFocus = false;
    private string _nameBeforeEdit = string.Empty;

    // Known device types. Extend this list as new devices are supported.
    public static readonly string[] KnownDevices = { "G13", "G15", "Dominator X36" };

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ManagePagesDialog
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public ManagePagesDialog()
    {
        InitializeComponent();
        LoadPageList();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadPageList
    //
    // Loads all pages from the database into the page list.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadPageList()
    {
        DebugLog.Write("ManagePagesDialog.LoadPageList: loading.");

        var repo = new KeyPageRepository();
        var pages = repo.GetAllPages();

        PageListView.ItemsSource = pages;

        DebugLog.Write($"ManagePagesDialog.LoadPageList: loaded {pages.Count} pages.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ClearSelection
    //
    // Deselects the current page, clears the editor fields, and resets the button label to "New".
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ClearSelection()
    {
        DebugLog.Write("ManagePagesDialog.ClearSelection: clearing selection.");

        _selectedPage = null;
        _nameBeforeEdit = string.Empty;

        PageListView.SelectedItem = null;

        _suppressNameLostFocus = true;
        PageNameTextBox.Text = string.Empty;
        _suppressNameLostFocus = false;

        DeviceComboBox.SelectedIndex = 0;
        DeletePageButton.IsEnabled = false;
        NewRenameButton.Content = "New";
        NewRenameButton.IsEnabled = false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PageListView_SelectionChanged
    //
    // Fires when the user selects a page. Loads the page name and device into the editor.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void PageListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageListView.SelectedItem is not KeyPage page)
        {
            DebugLog.Write("ManagePagesDialog.PageListView_SelectionChanged: no page selected.");
            _selectedPage = null;
            PageNameTextBox.Text = string.Empty;
            DeviceComboBox.SelectedIndex = 0;
            return;
        }

        DebugLog.Write($"ManagePagesDialog.PageListView_SelectionChanged: page='{page.Name}' device='{page.Device}'.");

        _selectedPage = page;
        _nameBeforeEdit = page.Name;
        DeletePageButton.IsEnabled = true;
        NewRenameButton.Content = "Rename";
        NewRenameButton.IsEnabled = true;

        _suppressNameLostFocus = true;
        PageNameTextBox.Text = page.Name;
        _suppressNameLostFocus = false;

        DeviceComboBox.SelectedItem = DeviceComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Content.ToString() == page.Device);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PageListView_KeyDown
    //
    // Escape clears the selection.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void PageListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DebugLog.Write("ManagePagesDialog.PageListView_KeyDown: Escape pressed, clearing selection.");
            ClearSelection();
            e.Handled = true;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PageNameTextBox_TextChanged
    //
    // Enables the New/Rename button when text is present.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void PageNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        bool hasText = !string.IsNullOrWhiteSpace(PageNameTextBox.Text);
        NewRenameButton.IsEnabled = hasText;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PageNameTextBox_KeyDown
    //
    // Enter commits. Escape cancels and restores the original name.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void PageNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DebugLog.Write("ManagePagesDialog.PageNameTextBox_KeyDown: Enter pressed, committing.");
            _suppressNameLostFocus = true;
            CommitRename();
            _suppressNameLostFocus = false;
            PageListView.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DebugLog.Write("ManagePagesDialog.PageNameTextBox_KeyDown: Escape pressed, cancelling.");
            _suppressNameLostFocus = true;
            PageNameTextBox.Text = _nameBeforeEdit;
            _suppressNameLostFocus = false;
            PageListView.Focus();
            e.Handled = true;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PageNameTextBox_LostFocus
    //
    // Commits the rename when the name field loses focus, unless suppressed.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void PageNameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressNameLostFocus)
        {
            DebugLog.Write("ManagePagesDialog.PageNameTextBox_LostFocus: suppressed.");
            return;
        }

        DebugLog.Write("ManagePagesDialog.PageNameTextBox_LostFocus: committing.");
        CommitRename();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // BeginRename
    //
    // Captures the current name and moves focus to the name field with all text selected.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void BeginRename()
    {
        if (_selectedPage == null)
        {
            DebugLog.Write("ManagePagesDialog.BeginRename: no page selected.");
            return;
        }

        _nameBeforeEdit = _selectedPage.Name;
        DebugLog.Write($"ManagePagesDialog.BeginRename: captured original name='{_nameBeforeEdit}'.");

        PageNameTextBox.Focus();
        PageNameTextBox.SelectAll();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CommitRename
    //
    // Validates and saves the current name and device as the selected page's properties.
    // Does nothing if no page is selected, the name is empty, or nothing has changed.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void CommitRename()
    {
        if (_selectedPage == null)
        {
            DebugLog.Write("ManagePagesDialog.CommitRename: no page selected, nothing to commit.");
            return;
        }

        string newName = PageNameTextBox.Text.Trim();
        string newDevice = (DeviceComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? KnownDevices[0];

        if (string.IsNullOrWhiteSpace(newName))
        {
            DebugLog.Write("ManagePagesDialog.CommitRename: name is empty, restoring original.");
            PageNameTextBox.Text = _nameBeforeEdit;
            return;
        }

        if ((newName == _selectedPage.Name) && (newDevice == _selectedPage.Device))
        {
            DebugLog.Write("ManagePagesDialog.CommitRename: no changes, skipping save.");
            return;
        }

        var repo = new KeyPageRepository();
        var existing = repo.GetAllPages();

        if (existing.Any(p => (p.Name == newName) && (p.Id != _selectedPage.Id)))
        {
            DebugLog.Write($"ManagePagesDialog.CommitRename: name '{newName}' already exists, restoring original.");
            MessageBox.Show($"A page named '{newName}' already exists.", "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
            PageNameTextBox.Text = _nameBeforeEdit;
            return;
        }

        _selectedPage.Name = newName;
        _selectedPage.Device = newDevice;
        repo.Save(_selectedPage);

        DebugLog.Write($"ManagePagesDialog.CommitRename: saved name='{newName}' device='{newDevice}'.");

        int savedId = _selectedPage.Id;

        _suppressNameLostFocus = true;
        LoadPageList();
        PageListView.SelectedItem = (PageListView.ItemsSource as List<KeyPage>)
            ?.FirstOrDefault(p => p.Id == savedId);
        _suppressNameLostFocus = false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // NewRename_Click
    //
    // Creates a new page when nothing is selected, or renames the selected page.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void NewRename_Click(object sender, RoutedEventArgs e)
    {
        string name = PageNameTextBox.Text.Trim();
        string device = (DeviceComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? KnownDevices[0];

        if (_selectedPage == null)
        {
            DebugLog.Write($"ManagePagesDialog.NewRename_Click: creating page name='{name}' device='{device}'.");

            var repo = new KeyPageRepository();
            var existing = repo.GetAllPages();

            if (existing.Any(p => p.Name == name))
            {
                DebugLog.Write($"ManagePagesDialog.NewRename_Click: name '{name}' already exists.");
                MessageBox.Show($"A page named '{name}' already exists.", "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var page = new KeyPage { Name = name, Device = device };
            repo.Save(page);

            DebugLog.Write($"ManagePagesDialog.NewRename_Click: created. id={page.Id}.");

            LoadPageList();

            _suppressNameLostFocus = true;
            PageListView.SelectedItem = (PageListView.ItemsSource as List<KeyPage>)
                ?.FirstOrDefault(p => p.Id == page.Id);
            _suppressNameLostFocus = false;
        }
        else
        {
            DebugLog.Write($"ManagePagesDialog.NewRename_Click: renaming '{_selectedPage.Name}' to '{name}'.");
            CommitRename();
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DeletePage_Click
    //
    // Deletes the selected page.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void DeletePage_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPage == null)
        {
            DebugLog.Write("ManagePagesDialog.DeletePage_Click: no page selected.");
            return;
        }

        DebugLog.Write($"ManagePagesDialog.DeletePage_Click: deleting page id={_selectedPage.Id} name='{_selectedPage.Name}'.");

        var result = MessageBox.Show($"Delete page '{_selectedPage.Name}'?",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            DebugLog.Write("ManagePagesDialog.DeletePage_Click: cancelled.");
            return;
        }

        var repo = new KeyPageRepository();
        repo.Delete(_selectedPage.Id);

        DebugLog.Write($"ManagePagesDialog.DeletePage_Click: deleted.");

        ClearSelection();
        LoadPageList();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Close_Click
    //
    // Closes the dialog.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("ManagePagesDialog.Close_Click: closing.");
        Close();
    }
}
