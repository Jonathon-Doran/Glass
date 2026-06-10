using Glass.Core;
using Glass.Core.Logging;
using Glass.Data;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.Windows;
using System.Windows.Controls;

namespace Inference.Dialogs;

///////////////////////////////////////////////////////////////////////////////////////////////
// GateEditor
//
// Modeless editor for the Gate table.  A patch level and a gate are selected; the gate's name,
// multiplicity kind, child collection, and count field are edited in a form.  The count field
// applies only to the Times kind and is disabled for Once and UntilEnd.  Reads and writes via
// direct SQL through the Database singleton.
///////////////////////////////////////////////////////////////////////////////////////////////
public partial class GateEditor : Window
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _connection
    //
    // Database connection held for the lifetime of the editor window.  Opened in the
    // constructor, used by every helper that reads or writes the Gate table, and disposed when
    // the window closes.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private readonly SqliteConnection _connection;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _loadedGateName
    //
    // The Gate name currently loaded into the form, as stored in the database.  Held so the save
    // path can detect a rename (the name textbox differs from this) and can use the original name
    // to locate the row.  Empty when no gate is loaded or when the form is armed to create a new
    // gate.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private string _loadedGateName = string.Empty;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GateDropdownItem
    //
    // Item bound to the gate combobox.  Carries the gate name for real rows.  When IsSentinel is
    // true the item represents the "(New gate...)" entry: GateName is empty and ToString returns
    // the sentinel label so the combobox displays it in place of a name.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private class GateDropdownItem
    {
        public string GateName { get; set; } = string.Empty;
        public bool IsSentinel { get; set; }

        public override string ToString()
        {
            if (IsSentinel == true)
            {
                return "(New gate...)";
            }

            return GateName;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GateEditor (constructor)
    //
    // Initializes the window, opens the database connection held for the window's lifetime, and
    // populates the kind dropdown and the patch level dropdown.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public GateEditor()
    {
        InitializeComponent();

        GateNameTextBox.Text = string.Empty;
        ChildCollectionComboBox.Text = string.Empty;
        CountFieldTextBox.Text = string.Empty;

        _connection = Database.Instance.Connect();
        _connection.Open();

        PopulateKindDropdown();
        PopulatePatchLevelDropdown();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OnClosed
    //
    // Disposes the database connection held for the lifetime of the window.
    //
    // e:  Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    protected override void OnClosed(EventArgs e)
    {
        DebugLog.Write(LogChannel.Fields, "GateEditor.OnClosed: disposing connection");

        _connection.Dispose();

        base.OnClosed(e);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PopulateKindDropdown
    //
    // Sets the ItemsSource on the kind combobox to the MultiplicityKind enum values, so the
    // dropdown offers exactly the kinds the model defines.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PopulateKindDropdown()
    {
        MultiplicityKind[] kinds = (MultiplicityKind[])Enum.GetValues(typeof(MultiplicityKind));

        DebugLog.Write(LogChannel.Fields, "GateEditor.PopulateKindDropdown: " + kinds.Length
            + " kind(s)");

        KindComboBox.ItemsSource = kinds;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PopulatePatchLevelDropdown
    //
    // Populates the patch level combobox from the patch levels present in the database and selects
    // the current patch level from GlassContext as the initial value.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PopulatePatchLevelDropdown()
    {
        List<PatchLevel> patchLevels = PatchRegistry.GetAllPatchLevels();

        DebugLog.Write(LogChannel.Fields, "GateEditor.PopulatePatchLevelDropdown: "
            + patchLevels.Count + " patch level(s) loaded");

        PatchLevelComboBox.ItemsSource = patchLevels;
        PatchLevelComboBox.SelectedItem = GlassContext.CurrentPatchLevel;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PopulateGateDropdown
    //
    // Reads the Gate names for the given patch level, sorted, and binds them to the gate combobox
    // as GateDropdownItem instances.  Appends a sentinel item labeled "(New gate...)" so the user
    // can choose to create a new gate.
    //
    // patchLevel:  The patch level whose gate names to load.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PopulateGateDropdown(PatchLevel patchLevel)
    {
        List<GateDropdownItem> items = new List<GateDropdownItem>();

        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name"
                + " FROM Gate"
                + " WHERE patch_date = @patchDate"
                + " AND server_type = @serverType"
                + " ORDER BY name";
            cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
            cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);

            using SqliteDataReader reader = cmd.ExecuteReader();
            while (reader.Read() == true)
            {
                GateDropdownItem item = new GateDropdownItem();
                item.GateName = reader.GetString(0);
                item.IsSentinel = false;
                items.Add(item);
            }
        }

        GateDropdownItem sentinel = new GateDropdownItem();
        sentinel.GateName = string.Empty;
        sentinel.IsSentinel = true;
        items.Add(sentinel);

        DebugLog.Write(LogChannel.Fields, "GateEditor.PopulateGateDropdown: "
            + (items.Count - 1) + " gate(s) plus sentinel for patchLevel=" + patchLevel);

        GateComboBox.ItemsSource = items;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PatchLevelComboBox_SelectionChanged
    //
    // Fires when the patch level selection changes.  Populates the gate dropdown for the selected
    // patch and clears the gate selection and the form, since any previously selected gate belongs
    // to a different patch.
    //
    // sender:  The patch level combobox.
    // e:       Standard selection-changed event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PatchLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        GateComboBox.ItemsSource = null;
        GateNameTextBox.Text = string.Empty;
        ChildCollectionComboBox.Text = string.Empty;
        CountFieldTextBox.Text = string.Empty;
        _loadedGateName = string.Empty;

        if (PatchLevelComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "GateEditor.PatchLevelComboBox_SelectionChanged: "
                + "no selection");
            return;
        }

        PatchLevel patchLevel = (PatchLevel)PatchLevelComboBox.SelectedItem;
        DebugLog.Write(LogChannel.Fields, "GateEditor.PatchLevelComboBox_SelectionChanged: "
            + "selected " + patchLevel);

        PopulateGateDropdown(patchLevel);
        PopulateChildCollectionDropdown(patchLevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GateComboBox_SelectionChanged
    //
    // Fires when the gate selection changes.  For a real gate item, records it as the loaded gate
    // and loads its fields into the form.  For the sentinel item, blanks the form, clears the
    // loaded-gate name, selects the Once kind as the default, and focuses the name box so the user
    // can name the new gate.
    //
    // sender:  The gate combobox.
    // e:       Standard selection-changed event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void GateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GateComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "GateEditor.GateComboBox_SelectionChanged: "
                + "no selection");
            return;
        }

        if (PatchLevelComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "GateEditor.GateComboBox_SelectionChanged: "
                + "gate selected but no patch level, ignoring");
            return;
        }

        PatchLevel patchLevel = (PatchLevel)PatchLevelComboBox.SelectedItem;
        GateDropdownItem item = (GateDropdownItem)GateComboBox.SelectedItem;

        if (item.IsSentinel == true)
        {
            _loadedGateName = string.Empty;
            GateNameTextBox.Text = string.Empty;
            ChildCollectionComboBox.Text = string.Empty;
            CountFieldTextBox.Text = string.Empty;
            CountConstantTextBox.Text = string.Empty;
            KindComboBox.SelectedItem = MultiplicityKind.Once;

            GateNameTextBox.Focus();

            DebugLog.Write(LogChannel.Fields, "GateEditor.GateComboBox_SelectionChanged: "
                + "sentinel selected, armed for new gate, form cleared");
            return;
        }

        _loadedGateName = item.GateName;

        DebugLog.Write(LogChannel.Fields, "GateEditor.GateComboBox_SelectionChanged: "
            + "patchLevel=" + patchLevel + " gate=" + item.GateName);

        LoadGateForm(patchLevel, item.GateName);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadGateForm
    //
    // Reads the Gate row for the given (patchLevel, gateName) and populates the form's name, kind,
    // child collection, count field, and constant count.  The kind string is parsed to a
    // MultiplicityKind; a value that does not parse falls back to Once and is logged, so re-saving
    // the gate stores a valid kind.  The count field and constant count are shown empty when their
    // columns are null; a Times gate carries one or the other.
    //
    // patchLevel:  The patch level whose gate to load.
    // gateName:    The Gate.name to load.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadGateForm(PatchLevel patchLevel, string gateName)
    {
        string kindString = string.Empty;
        string childCollection = string.Empty;
        string countFieldName = string.Empty;
        string countConstant = string.Empty;
        bool rowFound = false;

        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT kind, child_collection, field_name, count"
                + " FROM Gate"
                + " WHERE patch_date = @patchDate"
                + " AND server_type = @serverType"
                + " AND name = @name";
            cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
            cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
            cmd.Parameters.AddWithValue("@name", gateName);

            using SqliteDataReader reader = cmd.ExecuteReader();
            if (reader.Read() == true)
            {
                rowFound = true;
                kindString = reader.GetString(0);
                childCollection = reader.GetString(1);
                if (reader.IsDBNull(2) == false)
                {
                    countFieldName = reader.GetString(2);
                }
                if (reader.IsDBNull(3) == false)
                {
                    uint countValue = (uint)reader.GetInt32(3);
                    countConstant = countValue.ToString();
                }
            }
        }

        if (rowFound == false)
        {
            DebugLog.Write(LogChannel.Fields, "GateEditor.LoadGateForm: no Gate row named '"
                + gateName + "' for patchLevel=" + patchLevel);
            return;
        }

        MultiplicityKind kind;
        bool kindParsed = Enum.TryParse<MultiplicityKind>(kindString, out kind);
        if (kindParsed == false)
        {
            DebugLog.Write(LogChannel.Fields, "GateEditor.LoadGateForm: gate '" + gateName
                + "' has unparsable kind '" + kindString + "', defaulting to Once");
            kind = MultiplicityKind.Once;
        }

        GateNameTextBox.Text = gateName;
        KindComboBox.SelectedItem = kind;
        ChildCollectionComboBox.Text = childCollection;
        CountFieldTextBox.Text = countFieldName;
        CountConstantTextBox.Text = countConstant;

        DebugLog.Write(LogChannel.Fields, "GateEditor.LoadGateForm: loaded '" + gateName
            + "' kind=" + kind + " child='" + childCollection + "' countField='"
            + countFieldName + "' countConstant='" + countConstant + "' for patchLevel=" + patchLevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // KindComboBox_SelectionChanged
    //
    // Fires when the kind selection changes.  Shows the count field and count editors only for the
    // Times kind, since only Times reads a count.  For Once and UntilEnd both count editors and
    // their labels are hidden and cleared so the form shows and carries no count where none applies.
    //
    // sender:  The kind combobox.
    // e:       Standard selection-changed event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void KindComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool showCount;

        if (KindComboBox.SelectedItem == null)
        {
            showCount = false;

            DebugLog.Write(LogChannel.Fields, "GateEditor.KindComboBox_SelectionChanged: "
                + "no selection, hiding count editors");
        }
        else
        {
            MultiplicityKind kind = (MultiplicityKind)KindComboBox.SelectedItem;
            showCount = kind == MultiplicityKind.Times;

            DebugLog.Write(LogChannel.Fields, "GateEditor.KindComboBox_SelectionChanged: "
                + "kind=" + kind + " showCount=" + showCount);
        }

        if (showCount == true)
        {
            CountFieldLabel.Visibility = Visibility.Visible;
            CountFieldTextBox.Visibility = Visibility.Visible;
            CountConstantLabel.Visibility = Visibility.Visible;
            CountConstantTextBox.Visibility = Visibility.Visible;
            return;
        }

        CountFieldLabel.Visibility = Visibility.Collapsed;
        CountFieldTextBox.Visibility = Visibility.Collapsed;
        CountConstantLabel.Visibility = Visibility.Collapsed;
        CountConstantTextBox.Visibility = Visibility.Collapsed;

        CountFieldTextBox.Text = string.Empty;
        CountConstantTextBox.Text = string.Empty;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ClearButton_Click
    //
    // Resets the editor to the empty state for entering a new gate.  Clears the gate selection,
    // blanks the name, child collection, and count field, clears the loaded-gate name, and selects
    // the Once kind as the default.  No-op when no patch level is selected.
    //
    // sender:  The Clear button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (PatchLevelComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "GateEditor.ClearButton_Click: "
                + "no patch level selected, ignoring");
            return;
        }

        GateComboBox.SelectedItem = null;
        GateNameTextBox.Text = string.Empty;
        ChildCollectionComboBox.Text = string.Empty;
        CountFieldTextBox.Text = string.Empty;
        CountConstantTextBox.Text = string.Empty;
        _loadedGateName = string.Empty;
        KindComboBox.SelectedItem = MultiplicityKind.Once;

        DebugLog.Write(LogChannel.Fields, "GateEditor.ClearButton_Click: cleared to empty state");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CloseButton_Click
    //
    // Closes the editor window.  Pending unsaved edits are discarded; the user is expected to click
    // Save first if they want changes persisted.
    //
    // sender:  The Close button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.Fields, "GateEditor.CloseButton_Click: closing");
        Close();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SaveButton_Click
    //
    // Persists the gate form to the database inside a single transaction.  Validates that the name
    // is present and, for a Times gate, that exactly one of the count field or the constant count is
    // supplied and that a supplied constant parses as a non-negative integer.  When no gate was
    // loaded the form is inserted as a new row; when a gate was loaded the row is updated, keyed on
    // the loaded name so a changed name renames the row.  The count field and constant count are
    // written as null for the Once and UntilEnd kinds, and for a Times gate the unused one of the
    // two is written as null.  A validation failure or SQL error rolls the transaction back and
    // reports the message, leaving the database unchanged.  After a successful commit the gate
    // dropdown is refreshed and the saved gate reselected.
    //
    // sender:  The Save button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (PatchLevelComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "GateEditor.SaveButton_Click: "
                + "no patch level selected, ignoring");
            return;
        }

        if (KindComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "GateEditor.SaveButton_Click: "
                + "no kind selected, ignoring");
            MessageBox.Show(this, "Kind is required.", "Save Failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string newName = GateNameTextBox.Text.Trim();
        if (newName.Length == 0)
        {
            DebugLog.Write(LogChannel.Fields, "GateEditor.SaveButton_Click: "
                + "empty gate name, ignoring");
            MessageBox.Show(this, "Gate name is required.", "Save Failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        PatchLevel patchLevel = (PatchLevel)PatchLevelComboBox.SelectedItem;
        MultiplicityKind kind = (MultiplicityKind)KindComboBox.SelectedItem;
        string childCollection = ChildCollectionComboBox.Text.Trim();
        string countFieldName = CountFieldTextBox.Text.Trim();
        string countConstantText = CountConstantTextBox.Text.Trim();

        object countFieldParam = DBNull.Value;
        object countConstantParam = DBNull.Value;

        if (kind == MultiplicityKind.Times)
        {
            bool hasField = countFieldName.Length > 0;
            bool hasConstant = countConstantText.Length > 0;

            if (hasField == false && hasConstant == false)
            {
                DebugLog.Write(LogChannel.Fields, "GateEditor.SaveButton_Click: "
                    + "Times gate '" + newName + "' has neither count field nor constant, ignoring");
                MessageBox.Show(this, "A Times gate requires a count field or a constant count.",
                    "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (hasField == true && hasConstant == true)
            {
                DebugLog.Write(LogChannel.Fields, "GateEditor.SaveButton_Click: "
                    + "Times gate '" + newName + "' has both count field and constant, ignoring");
                MessageBox.Show(this, "A Times gate takes either a count field or a constant count, not both.",
                    "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (hasField == true)
            {
                countFieldParam = countFieldName;
            }
            else
            {
                uint countValue;
                bool constantParsed = uint.TryParse(countConstantText, out countValue);
                if (constantParsed == false)
                {
                    DebugLog.Write(LogChannel.Fields, "GateEditor.SaveButton_Click: "
                        + "Times gate '" + newName + "' constant '" + countConstantText
                        + "' is not a non-negative integer, ignoring");
                    MessageBox.Show(this, "The constant count must be a non-negative whole number.",
                        "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                countConstantParam = countValue;
            }
        }

        DebugLog.Write(LogChannel.Fields, "GateEditor.SaveButton_Click: starting save, "
            + "patchLevel=" + patchLevel + " name=" + newName + " loadedName=" + _loadedGateName
            + " kind=" + kind + " child='" + childCollection + "' countField='" + countFieldName
            + "' countConstant='" + countConstantText + "'");

        using SqliteTransaction tx = _connection.BeginTransaction();

        try
        {
            if (_loadedGateName.Length == 0)
            {
                using SqliteCommand cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO Gate"
                    + " (patch_date, server_type, name, kind, child_collection, field_name, count)"
                    + " VALUES (@patchDate, @serverType, @name, @kind, @childCollection, @fieldName, @count)";
                cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
                cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
                cmd.Parameters.AddWithValue("@name", newName);
                cmd.Parameters.AddWithValue("@kind", kind.ToString());
                cmd.Parameters.AddWithValue("@childCollection", childCollection);
                cmd.Parameters.AddWithValue("@fieldName", countFieldParam);
                cmd.Parameters.AddWithValue("@count", countConstantParam);
                cmd.ExecuteNonQuery();

                DebugLog.Write(LogChannel.Fields, "GateEditor.SaveButton_Click: inserted gate '"
                    + newName + "'");
            }
            else
            {
                using SqliteCommand cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE Gate"
                    + " SET name = @newName,"
                    + "     kind = @kind,"
                    + "     child_collection = @childCollection,"
                    + "     field_name = @fieldName,"
                    + "     count = @count"
                    + " WHERE patch_date = @patchDate"
                    + " AND server_type = @serverType"
                    + " AND name = @oldName";
                cmd.Parameters.AddWithValue("@newName", newName);
                cmd.Parameters.AddWithValue("@kind", kind.ToString());
                cmd.Parameters.AddWithValue("@childCollection", childCollection);
                cmd.Parameters.AddWithValue("@fieldName", countFieldParam);
                cmd.Parameters.AddWithValue("@count", countConstantParam);
                cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
                cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
                cmd.Parameters.AddWithValue("@oldName", _loadedGateName);
                cmd.ExecuteNonQuery();

                DebugLog.Write(LogChannel.Fields, "GateEditor.SaveButton_Click: updated gate '"
                    + _loadedGateName + "' -> '" + newName + "'");
            }

            tx.Commit();
            DebugLog.Write(LogChannel.Fields, "GateEditor.SaveButton_Click: committed");
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DebugLog.Write(LogChannel.Fields, "GateEditor.SaveButton_Click: rolled back, "
                + ex.GetType().Name + ": " + ex.Message);
            MessageBox.Show(this, "Save failed: " + ex.Message, "Save Failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _loadedGateName = newName;

        PopulateGateDropdown(patchLevel);

        if (GateComboBox.ItemsSource is List<GateDropdownItem> items)
        {
            foreach (GateDropdownItem candidate in items)
            {
                if (candidate.IsSentinel == false && candidate.GateName == newName)
                {
                    GateComboBox.SelectedItem = candidate;
                    break;
                }
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // DeleteButton_Click
    //
    // Deletes the loaded gate row from the database, keyed on the loaded name and the selected
    // patch level, after a confirmation prompt.  No-op when no real gate is loaded.  Only the Gate
    // row is removed; references to its name elsewhere are not touched.  After deletion the gate
    // dropdown is refreshed and the form cleared to the new-gate state.
    //
    // sender:  The Delete button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (PatchLevelComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "GateEditor.DeleteButton_Click: "
                + "no patch level selected, ignoring");
            return;
        }

        if (_loadedGateName.Length == 0)
        {
            DebugLog.Write(LogChannel.Fields, "GateEditor.DeleteButton_Click: "
                + "no gate loaded, ignoring");
            return;
        }

        PatchLevel patchLevel = (PatchLevel)PatchLevelComboBox.SelectedItem;

        MessageBoxResult confirm = MessageBox.Show(this,
            "Delete gate '" + _loadedGateName + "'?", "Delete Gate",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            DebugLog.Write(LogChannel.Fields, "GateEditor.DeleteButton_Click: "
                + "user cancelled delete of '" + _loadedGateName + "'");
            return;
        }

        using SqliteTransaction tx = _connection.BeginTransaction();

        try
        {
            using SqliteCommand cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM Gate"
                + " WHERE patch_date = @patchDate"
                + " AND server_type = @serverType"
                + " AND name = @name";
            cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
            cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
            cmd.Parameters.AddWithValue("@name", _loadedGateName);
            int affected = cmd.ExecuteNonQuery();

            tx.Commit();

            DebugLog.Write(LogChannel.Fields, "GateEditor.DeleteButton_Click: deleted gate '"
                + _loadedGateName + "' for patchLevel=" + patchLevel + " rows=" + affected);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DebugLog.Write(LogChannel.Fields, "GateEditor.DeleteButton_Click: rolled back, "
                + ex.GetType().Name + ": " + ex.Message);
            MessageBox.Show(this, "Delete failed: " + ex.Message, "Delete Failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        GateComboBox.SelectedItem = null;
        GateNameTextBox.Text = string.Empty;
        ChildCollectionComboBox.Text = string.Empty;
        CountFieldTextBox.Text = string.Empty;
        CountConstantTextBox.Text = string.Empty;
        _loadedGateName = string.Empty;
        KindComboBox.SelectedItem = MultiplicityKind.Once;

        PopulateGateDropdown(patchLevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PopulateChildCollectionDropdown
    //
    // Reads the FieldCollection names for the given patch level, sorted, and binds them to the
    // child collection combobox as its dropdown list.  The combobox is editable, so a name typed
    // that is not in the list is still accepted; the list is a convenience for picking an existing
    // collection.  Sets only the items, not the text, so a loaded gate's child value is left
    // untouched.
    //
    // patchLevel:  The patch level whose collection names to load.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PopulateChildCollectionDropdown(PatchLevel patchLevel)
    {
        List<string> names = new List<string>();

        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name"
                + " FROM FieldCollection"
                + " WHERE patch_date = @patchDate"
                + " AND server_type = @serverType"
                + " ORDER BY name";
            cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
            cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);

            using SqliteDataReader reader = cmd.ExecuteReader();
            while (reader.Read() == true)
            {
                names.Add(reader.GetString(0));
            }
        }

        DebugLog.Write(LogChannel.Fields, "GateEditor.PopulateChildCollectionDropdown: "
            + names.Count + " collection(s) for patchLevel=" + patchLevel);

        ChildCollectionComboBox.ItemsSource = names;
    }
}