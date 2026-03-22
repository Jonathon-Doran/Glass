using Glass.Core;
using Glass.Data.Models;
using Glass.Data.Repositories;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Glass;

public partial class ManageKeyAliasesDialog : Window
{
    private KeyAlias? _selectedAlias;
    private bool _suppressNameLostFocus = false;
    private string _nameBeforeEdit = string.Empty;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ManageKeyAliasesDialog
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public ManageKeyAliasesDialog()
    {
        InitializeComponent();
        LoadAliasList();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadAliasList
    //
    // Loads all key aliases from the database into the alias list.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadAliasList()
    {
        DebugLog.Write("ManageKeyAliasesDialog.LoadAliasList: loading.");

        var repo = new KeyAliasRepository();
        var aliases = repo.GetAllAliases();

        AliasListView.ItemsSource = aliases;

        DebugLog.Write($"ManageKeyAliasesDialog.LoadAliasList: loaded {aliases.Count} aliases.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ClearSelection
    //
    // Deselects the current alias, clears the editor fields, and resets the button label to "New".
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ClearSelection()
    {
        DebugLog.Write("ManageKeyAliasesDialog.ClearSelection: clearing selection.");

        _selectedAlias = null;
        _nameBeforeEdit = string.Empty;

        AliasListView.SelectedItem = null;

        _suppressNameLostFocus = true;
        AliasNameTextBox.Text = string.Empty;
        _suppressNameLostFocus = false;

        AliasValueTextBox.Text = string.Empty;
        DeleteAliasButton.IsEnabled = false;
        NewRenameButton.Content = "New";
        NewRenameButton.IsEnabled = false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AliasListView_SelectionChanged
    //
    // Fires when the user selects an alias. Loads the alias into the editor.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void AliasListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AliasListView.SelectedItem is not KeyAlias alias)
        {
            DebugLog.Write("ManageKeyAliasesDialog.AliasListView_SelectionChanged: no alias selected.");
            _selectedAlias = null;
            AliasNameTextBox.Text = string.Empty;
            AliasValueTextBox.Text = string.Empty;
            return;
        }

        DebugLog.Write($"ManageKeyAliasesDialog.AliasListView_SelectionChanged: alias='{alias.Name}' value='{alias.Value}'.");

        _selectedAlias = alias;
        _nameBeforeEdit = alias.Name;
        DeleteAliasButton.IsEnabled = true;
        NewRenameButton.Content = "Rename";
        NewRenameButton.IsEnabled = true;

        _suppressNameLostFocus = true;
        AliasNameTextBox.Text = alias.Name;
        _suppressNameLostFocus = false;

        AliasValueTextBox.Text = alias.Value;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AliasListView_KeyDown
    //
    // Escape clears the selection.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void AliasListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DebugLog.Write("ManageKeyAliasesDialog.AliasListView_KeyDown: Escape pressed, clearing selection.");
            ClearSelection();
            e.Handled = true;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AliasNameTextBox_TextChanged
    //
    // Enables the New/Rename button when text is present.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void AliasNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        bool hasText = !string.IsNullOrWhiteSpace(AliasNameTextBox.Text);
        NewRenameButton.IsEnabled = hasText;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AliasNameTextBox_KeyDown
    //
    // Enter commits. Escape cancels and restores the original name.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void AliasNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DebugLog.Write("ManageKeyAliasesDialog.AliasNameTextBox_KeyDown: Enter pressed, committing.");
            _suppressNameLostFocus = true;
            CommitChanges();
            _suppressNameLostFocus = false;
            AliasListView.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DebugLog.Write("ManageKeyAliasesDialog.AliasNameTextBox_KeyDown: Escape pressed, cancelling.");
            _suppressNameLostFocus = true;
            AliasNameTextBox.Text = _nameBeforeEdit;
            _suppressNameLostFocus = false;
            AliasListView.Focus();
            e.Handled = true;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AliasNameTextBox_LostFocus
    //
    // Commits changes when the name field loses focus, unless suppressed.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void AliasNameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressNameLostFocus)
        {
            DebugLog.Write("ManageKeyAliasesDialog.AliasNameTextBox_LostFocus: suppressed.");
            return;
        }

        DebugLog.Write("ManageKeyAliasesDialog.AliasNameTextBox_LostFocus: committing.");
        CommitChanges();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AliasValueTextBox_LostFocus
    //
    // Commits changes when the value field loses focus.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void AliasValueTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_selectedAlias == null)
        {
            return;
        }

        DebugLog.Write("ManageKeyAliasesDialog.AliasValueTextBox_LostFocus: committing.");
        CommitChanges();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CommitChanges
    //
    // Validates and saves the current name and value fields for the selected alias.
    // Does nothing if no alias is selected, the name is empty, or nothing has changed.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void CommitChanges()
    {
        if (_selectedAlias == null)
        {
            DebugLog.Write("ManageKeyAliasesDialog.CommitChanges: no alias selected, nothing to commit.");
            return;
        }

        string newName = AliasNameTextBox.Text.Trim();
        string newValue = AliasValueTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(newName))
        {
            DebugLog.Write("ManageKeyAliasesDialog.CommitChanges: name is empty, restoring original.");
            AliasNameTextBox.Text = _nameBeforeEdit;
            return;
        }

        if ((newName == _selectedAlias.Name) && (newValue == _selectedAlias.Value))
        {
            DebugLog.Write("ManageKeyAliasesDialog.CommitChanges: no changes, skipping save.");
            return;
        }

        var repo = new KeyAliasRepository();
        var existing = repo.GetAllAliases();

        if (existing.Any(a => (a.Name == newName) && (a.Id != _selectedAlias.Id)))
        {
            DebugLog.Write($"ManageKeyAliasesDialog.CommitChanges: name '{newName}' already exists, restoring original.");
            MessageBox.Show($"An alias named '{newName}' already exists.", "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
            AliasNameTextBox.Text = _nameBeforeEdit;
            return;
        }

        _selectedAlias.Name = newName;
        _selectedAlias.Value = newValue;
        repo.Save(_selectedAlias);

        DebugLog.Write($"ManageKeyAliasesDialog.CommitChanges: saved name='{newName}' value='{newValue}'.");

        int savedId = _selectedAlias.Id;

        _suppressNameLostFocus = true;
        LoadAliasList();
        AliasListView.SelectedItem = (AliasListView.ItemsSource as List<KeyAlias>)
            ?.FirstOrDefault(a => a.Id == savedId);
        _suppressNameLostFocus = false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // NewRename_Click
    //
    // Creates a new alias when nothing is selected, or renames the selected alias.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void NewRename_Click(object sender, RoutedEventArgs e)
    {
        string name = AliasNameTextBox.Text.Trim();
        string value = AliasValueTextBox.Text.Trim();

        if (_selectedAlias == null)
        {
            DebugLog.Write($"ManageKeyAliasesDialog.NewRename_Click: creating alias name='{name}' value='{value}'.");

            if (string.IsNullOrWhiteSpace(value))
            {
                DebugLog.Write("ManageKeyAliasesDialog.NewRename_Click: value is empty.");
                MessageBox.Show("Please enter a keystroke value.", "No Value", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var repo = new KeyAliasRepository();
            var existing = repo.GetAllAliases();

            if (existing.Any(a => a.Name == name))
            {
                DebugLog.Write($"ManageKeyAliasesDialog.NewRename_Click: name '{name}' already exists.");
                MessageBox.Show($"An alias named '{name}' already exists.", "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var alias = new KeyAlias { Name = name, Value = value };
            repo.Save(alias);

            DebugLog.Write($"ManageKeyAliasesDialog.NewRename_Click: created. id={alias.Id}.");

            LoadAliasList();
            ClearSelection();
        }
        else
        {
            DebugLog.Write($"ManageKeyAliasesDialog.NewRename_Click: renaming '{_selectedAlias.Name}' to '{name}'.");
            CommitChanges();
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DeleteAlias_Click
    //
    // Deletes the selected alias.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void DeleteAlias_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAlias == null)
        {
            DebugLog.Write("ManageKeyAliasesDialog.DeleteAlias_Click: no alias selected.");
            return;
        }

        DebugLog.Write($"ManageKeyAliasesDialog.DeleteAlias_Click: deleting alias id={_selectedAlias.Id} name='{_selectedAlias.Name}'.");

        var result = MessageBox.Show($"Delete alias '{_selectedAlias.Name}'?",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            DebugLog.Write("ManageKeyAliasesDialog.DeleteAlias_Click: cancelled.");
            return;
        }

        var repo = new KeyAliasRepository();
        repo.Delete(_selectedAlias.Id);

        DebugLog.Write($"ManageKeyAliasesDialog.DeleteAlias_Click: deleted.");

        ClearSelection();
        LoadAliasList();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Close_Click
    //
    // Closes the dialog.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("ManageKeyAliasesDialog.Close_Click: closing.");
        Close();
    }
}
