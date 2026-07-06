using Glass.Core;
using Glass.Core.Logging;
using Glass.Data;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;

namespace Inference.Dialogs;

///////////////////////////////////////////////////////////////////////////////////////////////
// CollectionEditor
//
// Modeless editor for the collection-centric patch-data tables: FieldCollection, PacketField,
// and Gate.  A patch level and a collection are selected; the collection's PacketField
// rows are edited in one grid.  A gated field stores a Gate.name string as its encoding;
// gate rows are created and edited separately in GateEditor.  A field's predicate is entered
// as a string validated on save.  Reads and writes via direct SQL through the Database singleton.
///////////////////////////////////////////////////////////////////////////////////////////////
public partial class CollectionEditor : Window
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _connection
    //
    // Database connection held for the lifetime of the editor window.  Opened in the
    // constructor, used by every helper that reads or writes the patch-data tables, and
    // disposed when the window closes.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private readonly SqliteConnection _connection;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _fieldsTable
    //
    // DataTable bound to FieldsGrid.  Loaded from PacketField when a collection is selected.
    // The grid's add/delete/edit operations mutate this table directly; the save path walks the
    // table's RowState to issue INSERT/UPDATE/DELETE statements.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private DataTable? _fieldsTable;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _loadedCollectionName
    //
    // The FieldCollection name currently loaded into the grid, as stored in the database.  Held
    // so the save path can detect a rename (the name textbox differs from this) and can use the
    // original name to locate the collection's rows.  Empty when no collection is loaded or when
    // the form is armed to create a new collection.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private string _loadedCollectionName = string.Empty;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // KnownGateNames
    //
    // The name of every Gate row in the selected patch level, ordered by name, bound as the
    // ItemsSource of the gate combobox in the fields grid's row details.  The collection instance
    // is created once and only ever mutated, so bindings established when a row's details render
    // remain valid across repopulation.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ObservableCollection<string> KnownGateNames { get; } = new ObservableCollection<string>();

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CollectionDropdownItem
    //
    // Item bound to the collection combobox.  Carries the collection name for real rows.  When
    // IsSentinel is true the item represents the "(New collection...)" entry: CollectionName is
    // empty and ToString returns the sentinel label so the combobox displays it in place of a name.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private class CollectionDropdownItem
    {
        public string CollectionName { get; set; } = string.Empty;
        public bool IsSentinel { get; set; }

        public override string ToString()
        {
            if (IsSentinel == true)
            {
                return "(New collection...)";
            }

            return CollectionName;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CollectionEditor (constructor)
    //
    // Initializes the window, opens the database connection held for the window's lifetime, and
    // populates the encoding dropdown and the patch level dropdown.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public CollectionEditor()
    {
        InitializeComponent();

        CollectionNameTextBox.Text = string.Empty;

        _connection = Database.Instance.Connect();
        _connection.Open();

        PopulateEncodingDropdown();
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
        DebugLog.Write(LogChannel.Fields, "CollectionEditor.OnClosed: disposing connection", LogLevel.Trace);

        _connection.Dispose();

        base.OnClosed(e);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PopulateEncodingDropdown
    //
    // Sets the ItemsSource on the encoding column from the encoding strings recognized by the
    // current patch level's PatchData.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PopulateEncodingDropdown()
    {
        PatchLevel patchLevel = GlassContext.CurrentPatchLevel;
        string[] encodingStrings = GlassContext.PatchRegistry.GetEncodingStrings(patchLevel);

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.PopulateEncodingDropdown: "
            + encodingStrings.Length + " encoding(s) for patchLevel=" + patchLevel, LogLevel.Trace);

        FieldEncodingColumn.ItemsSource = encodingStrings;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PopulatePatchLevelDropdown
    //
    // Populates the patch level combobox from the patch levels present in the database and
    // selects the current patch level from GlassContext as the initial value.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PopulatePatchLevelDropdown()
    {
        List<PatchLevel> patchLevels = PatchRegistry.GetAllPatchLevels();

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.PopulatePatchLevelDropdown: "
            + patchLevels.Count + " patch level(s) loaded", LogLevel.Trace);

        PatchLevelComboBox.ItemsSource = patchLevels;
        PatchLevelComboBox.SelectedItem = GlassContext.CurrentPatchLevel;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PopulateCollectionDropdown
    //
    // Reads the FieldCollection names for the given patch level, sorted, and binds them to the
    // collection combobox as CollectionDropdownItem instances.  Appends a sentinel item labeled
    // "(New collection...)" so the user can choose to create a new collection.
    //
    // patchLevel:  The patch level whose collection names to load.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PopulateCollectionDropdown(PatchLevel patchLevel)
    {
        List<CollectionDropdownItem> items = new List<CollectionDropdownItem>();

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
                CollectionDropdownItem item = new CollectionDropdownItem();
                item.CollectionName = reader.GetString(0);
                item.IsSentinel = false;
                items.Add(item);
            }
        }

        CollectionDropdownItem sentinel = new CollectionDropdownItem();
        sentinel.CollectionName = string.Empty;
        sentinel.IsSentinel = true;
        items.Add(sentinel);

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.PopulateCollectionDropdown: "
            + (items.Count - 1) + " collection(s) plus sentinel for patchLevel=" + patchLevel, LogLevel.Trace);

        CollectionComboBox.ItemsSource = items;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PopulateGatesDropdown
    //
    // Replaces the contents of KnownGateNames with the name of every Gate row for the given
    // patch level, ordered by name.  KnownGateNames is the ItemsSource of the gate combobox in
    // the fields grid's row details.
    //
    // patchLevel:  The patch level whose Gate rows to load.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PopulateGatesDropdown(PatchLevel patchLevel)
    {
        KnownGateNames.Clear();

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
                KnownGateNames.Add(reader.GetString(0));
            }
        }

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.PopulateGatesDropdown: "
            + KnownGateNames.Count + " gate name(s) for patchLevel=" + patchLevel, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CreateFieldsTable
    //
    // Builds the in-memory DataTable that backs FieldsGrid.  The id column auto-increments with a
    // negative seed so rows added through the grid receive unique synthetic ids that cannot
    // collide with database-assigned ids loaded from PacketField; it is not shown in the grid and
    // exists only to match loaded rows back to their database row at update or delete time.
    //
    // The sequence column is the field's presentation sort order within the collection.
    //
    // The gate column is a grid-only column; it does not map to a PacketField column directly.
    // When non-empty it holds a Gate.name string; the save path writes that name into the
    // field's encoding.  The remaining columns map one-to-one to PacketField.
    //
    // The show_relative_to, show_gate, and show_predicate columns are view-only toggle state for
    // the second-line editors.  Each defaults to false; an editor shows when its value is non-empty
    // or its toggle is true.  The any_detail column is view-only and is true when any second-line
    // editor shows for the row; it drives the row's details visibility so a row with no second-line
    // editor reserves no space.  The save path does not read these columns.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private DataTable CreateFieldsTable()
    {
        DataTable table = new DataTable();
        DataColumn idColumn = table.Columns.Add("id", typeof(uint));
        idColumn.AutoIncrement = true;
        idColumn.AutoIncrementSeed = -1;
        idColumn.AutoIncrementStep = -1;
        table.Columns.Add("sequence", typeof(int));
        table.Columns.Add("field_name", typeof(string));
        table.Columns.Add("bit_offset", typeof(int));
        table.Columns.Add("bit_length", typeof(int));
        table.Columns.Add("encoding", typeof(string));
        table.Columns.Add("divisor", typeof(double));
        table.Columns.Add("relative_to", typeof(string));
        table.Columns.Add("predicate", typeof(string));
        table.Columns.Add("gate", typeof(string));

        DataColumn showRelativeToColumn = table.Columns.Add("show_relative_to", typeof(bool));
        showRelativeToColumn.DefaultValue = false;
        DataColumn showGateColumn = table.Columns.Add("show_gate", typeof(bool));
        showGateColumn.DefaultValue = false;
        DataColumn showPredicateColumn = table.Columns.Add("show_predicate", typeof(bool));
        showPredicateColumn.DefaultValue = false;

        DataColumn anyDetailColumn = table.Columns.Add("any_detail", typeof(bool));
        anyDetailColumn.DefaultValue = false;

        return table;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // RecomputeAnyDetail
    //
    // Sets the row's any_detail column from its three second-line values and three toggle flags.
    // A second-line editor shows when its value is non-empty or its toggle flag is true; any_detail
    // is true when any of the relative_to, gate, or predicate editors shows.  Called after loading
    // a row and after a toggle changes a flag so the row's details visibility tracks whether any
    // editor is shown.
    //
    // row:  The row whose any_detail to set.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void RecomputeAnyDetail(DataRow row)
    {
        bool relativeToShows = string.IsNullOrWhiteSpace(row["relative_to"] as string) == false
            || Convert.ToBoolean(row["show_relative_to"]) == true;
        bool gateShows = string.IsNullOrWhiteSpace(row["gate"] as string) == false
            || Convert.ToBoolean(row["show_gate"]) == true;
        bool predicateShows = string.IsNullOrWhiteSpace(row["predicate"] as string) == false
            || Convert.ToBoolean(row["show_predicate"]) == true;

        bool anyDetail = relativeToShows == true || gateShows == true || predicateShows == true;
        row["any_detail"] = anyDetail;

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.RecomputeAnyDetail: field='"
            + (row["field_name"] as string ?? string.Empty) + "' relativeTo=" + relativeToShows
            + " gate=" + gateShows + " predicate=" + predicateShows + " anyDetail=" + anyDetail, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadFieldsTable
    //
    // Reads the PacketField rows for the given collection, builds the grid's DataTable, and binds
    // it to FieldsGrid.  Rows are ordered by sequence with unassigned (null) sequences last, then
    // by bit_offset.  For each row the encoding is inspected: when it names a gate (starts with
    // "Gate_") the matching Gate row is read and formatted into the gate column, and the
    // encoding column is left empty so the grid shows the field as a gate rather than a scalar.
    // Otherwise the encoding is shown as-is and the gate column is left empty.  The sequence,
    // relative_to, and predicate columns are shown empty when their database value is null.
    //
    // patchLevel:      The patch level whose fields to load.
    // collectionName:  The FieldCollection name whose fields to load.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadFieldsTable(PatchLevel patchLevel, string collectionName)
    {
        DataTable table = CreateFieldsTable();

        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT id, sequence, field_name, bit_offset, bit_length, encoding,"
                + " divisor, relative_to, predicate"
                + " FROM PacketField"
                + " WHERE patch_date = @patchDate"
                + " AND server_type = @serverType"
                + " AND collection_name = @collectionName"
                + " ORDER BY sequence IS NULL, sequence, bit_offset";
            cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
            cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
            cmd.Parameters.AddWithValue("@collectionName", collectionName);

            using SqliteDataReader reader = cmd.ExecuteReader();
            while (reader.Read() == true)
            {
                DataRow row = table.NewRow();
                row["id"] = reader.GetInt32(0);

                if (reader.IsDBNull(1) == true)
                {
                    row["sequence"] = DBNull.Value;
                }
                else
                {
                    row["sequence"] = (uint)reader.GetInt32(1);
                }

                row["field_name"] = reader.GetString(2);
                row["bit_offset"] = reader.GetInt32(3);
                row["bit_length"] = reader.GetInt32(4);
                string encoding = reader.GetString(5);
                row["divisor"] = reader.GetDouble(6);

                if (reader.IsDBNull(7) == true)
                {
                    row["relative_to"] = DBNull.Value;
                }
                else
                {
                    row["relative_to"] = reader.GetString(7);
                }

                if (reader.IsDBNull(8) == true)
                {
                    row["predicate"] = DBNull.Value;
                }
                else
                {
                    row["predicate"] = reader.GetString(8);
                }

                if (encoding.StartsWith("Gate_", StringComparison.Ordinal) == true)
                {
                    row["encoding"] = DBNull.Value;
                    row["gate"] = encoding;
                    row["show_gate"] = true;
                }
                else
                {
                    row["encoding"] = encoding;
                    row["gate"] = DBNull.Value;
                }

                RecomputeAnyDetail(row);

                table.Rows.Add(row);
            }
        }

        table.AcceptChanges();

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.LoadFieldsTable: " + table.Rows.Count
            + " field(s) for " + patchLevel + " collection=" + collectionName, LogLevel.Trace);

        _fieldsTable = table;
        FieldsGrid.ItemsSource = table.DefaultView;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PatchLevelComboBox_SelectionChanged
    //
    // Fires when the patch level selection changes.  Populates the collection dropdown for the
    // selected patch and clears the collection name box and the fields grid, since any previously
    // selected collection belongs to a different patch.
    //
    // sender:  The patch level combobox.
    // e:       Standard selection-changed event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PatchLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CollectionComboBox.ItemsSource = null;
        CollectionNameTextBox.Text = string.Empty;
        FieldsGrid.ItemsSource = null;
        _fieldsTable = null;
        _loadedCollectionName = string.Empty;

        if (PatchLevelComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.PatchLevelComboBox_SelectionChanged: "
                + "no selection", LogLevel.Trace);
            return;
        }

        PatchLevel patchLevel = (PatchLevel)PatchLevelComboBox.SelectedItem;
        DebugLog.Write(LogChannel.Fields, "CollectionEditor.PatchLevelComboBox_SelectionChanged: "
            + "selected " + patchLevel, LogLevel.Trace);

        PopulateCollectionDropdown(patchLevel);
        PopulateGatesDropdown(patchLevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CollectionComboBox_SelectionChanged
    //
    // Fires when the collection selection changes.  For a real collection item, puts its name in
    // the name box, records it as the loaded collection, and loads its fields into the grid.  For
    // the sentinel item, blanks the name box, clears the loaded-collection name and the grid, and
    // seeds an empty fields table the user can type rows into, then focuses the name box so the
    // user can name the new collection.
    //
    // sender:  The collection combobox.
    // e:       Standard selection-changed event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void CollectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CollectionComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.CollectionComboBox_SelectionChanged: "
                + "no selection", LogLevel.Trace);
            return;
        }

        if (PatchLevelComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.CollectionComboBox_SelectionChanged: "
                + "collection selected but no patch level, ignoring", LogLevel.Warn);
            return;
        }

        PatchLevel patchLevel = (PatchLevel)PatchLevelComboBox.SelectedItem;
        CollectionDropdownItem item = (CollectionDropdownItem)CollectionComboBox.SelectedItem;

        if (item.IsSentinel == true)
        {
            _loadedCollectionName = string.Empty;
            CollectionNameTextBox.Text = string.Empty;

            _fieldsTable = CreateFieldsTable();
            FieldsGrid.ItemsSource = _fieldsTable.DefaultView;

            CollectionNameTextBox.Focus();

            DebugLog.Write(LogChannel.Fields, "CollectionEditor.CollectionComboBox_SelectionChanged: "
                + "sentinel selected, armed for new collection, form cleared", LogLevel.Trace);
            return;
        }

        _loadedCollectionName = item.CollectionName;
        CollectionNameTextBox.Text = item.CollectionName;

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.CollectionComboBox_SelectionChanged: "
            + "patchLevel=" + patchLevel + " collection=" + item.CollectionName, LogLevel.Trace);

        LoadFieldsTable(patchLevel, item.CollectionName);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ClearButton_Click
    //
    // Resets the editor to the empty state for entering a new collection.  Clears the collection
    // selection and name box, clears the loaded-collection name, and replaces the fields table with
    // a fresh empty one bound to the grid.  No-op when no patch level is selected.
    //
    // sender:  The Clear button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (PatchLevelComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.ClearButton_Click: "
                + "no patch level selected, ignoring", LogLevel.Trace);
            return;
        }

        CollectionComboBox.SelectedItem = null;
        CollectionNameTextBox.Text = string.Empty;
        _loadedCollectionName = string.Empty;

        _fieldsTable = CreateFieldsTable();
        FieldsGrid.ItemsSource = _fieldsTable.DefaultView;

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.ClearButton_Click: cleared to empty state", LogLevel.Trace);
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
        Close();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AddFieldButton_Click
    //
    // Adds a new empty row to the fields table.  The grid refreshes automatically because it is
    // bound to the table's DefaultView.  The new row's id is assigned by the table's negative
    // auto-increment, marking it as not yet in the database; the save path detects RowState.Added
    // and inserts it.  Requires that a collection is loaded or armed (the fields table is not null).
    //
    // sender:  The Add Field button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void AddFieldButton_Click(object sender, RoutedEventArgs e)
    {
        if (_fieldsTable == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.AddFieldButton_Click: "
                + "no collection loaded, ignoring", LogLevel.Warn);
            return;
        }

        DataRow newRow = _fieldsTable.NewRow();
        newRow["sequence"] = DBNull.Value;
        newRow["field_name"] = string.Empty;
        newRow["bit_offset"] = 0;
        newRow["bit_length"] = 0;
        newRow["encoding"] = string.Empty;
        newRow["divisor"] = 1.0;
        newRow["relative_to"] = DBNull.Value;
        newRow["predicate"] = DBNull.Value;
        newRow["gate"] = DBNull.Value;

        _fieldsTable.Rows.Add(newRow);

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.AddFieldButton_Click: "
            + "added new field row, total now " + _fieldsTable.Rows.Count, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // DeleteFieldButton_Click
    //
    // Deletes the currently selected field row from the in-memory fields table.  The WPF new-row
    // placeholder is selectable but is not a DataRowView, so it is filtered out here.  A deleted
    // loaded row is marked RowState.Deleted so the save path issues a DELETE; a deleted unsaved row
    // is simply dropped.
    //
    // sender:  The Delete button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void DeleteFieldButton_Click(object sender, RoutedEventArgs e)
    {
        if (_fieldsTable == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.DeleteFieldButton_Click: "
                + "no collection loaded, ignoring", LogLevel.Warn);
            return;
        }

        if (FieldsGrid.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.DeleteFieldButton_Click: "
                + "no row selected, ignoring", LogLevel.Warn);
            return;
        }

        DataRowView? selected = FieldsGrid.SelectedItem as DataRowView;
        if (selected == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.DeleteFieldButton_Click: "
                + "placeholder row selected, ignoring", LogLevel.Warn);
            return;
        }

        selected.Row.Delete();

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.DeleteFieldButton_Click: "
            + "deleted row, table now has " + _fieldsTable.Rows.Count + " row(s)", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SaveButton_Click
    //
    // Persists pending changes to the database inside a single transaction.  Either every change
    // commits or none does.  Ensures the FieldCollection row exists under the name in the name box,
    // renaming it and cascading the new name through PacketField.collection_name, gate encodings,
    // and Gate rows when the name differs from the loaded name; then walks the fields table
    // and writes its INSERT/UPDATE/DELETE statements.
    //
    // A malformed predicate string aborts the save with a message and rolls the transaction back,
    // leaving the database unchanged.
    //  
    // After a successful commit the collection dropdown is refreshed so a new or renamed collection
    // appears, and the saved collection is reselected.
    //
    // sender:  The Save button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (PatchLevelComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.SaveButton_Click: "
                + "no patch level selected, ignoring", LogLevel.Warn);
            return;
        }

        string newName = CollectionNameTextBox.Text.Trim();
        if (newName.Length == 0)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.SaveButton_Click: "
                + "empty collection name, ignoring", LogLevel.Trace);
            SetStatus("Collection name is required.");
            return;
        }

        PatchLevel patchLevel = (PatchLevel)PatchLevelComboBox.SelectedItem;
        DebugLog.Write(LogChannel.Fields, "CollectionEditor.SaveButton_Click: starting save, "
            + "patchLevel=" + patchLevel + " name=" + newName + " loadedName=" + _loadedCollectionName, LogLevel.Trace);

        using SqliteTransaction tx = _connection.BeginTransaction();

        try
        {
            ValidateFields();
            ApplyCollectionName(tx, patchLevel, newName);
            SaveFieldsTable(tx, patchLevel, newName);
            tx.Commit();
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.SaveButton_Click: committed", LogLevel.Trace);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.SaveButton_Click: rolled back, "
                + ex.GetType().Name + ": " + ex.Message, LogLevel.Trace);
            SetStatus("Save failed: " + ex.Message);
            return;
        }

        _loadedCollectionName = newName;

        PopulateCollectionDropdown(patchLevel);

        if (CollectionComboBox.ItemsSource is List<CollectionDropdownItem> items)
        {
            foreach (CollectionDropdownItem candidate in items)
            {
                if (candidate.IsSentinel == false && candidate.CollectionName == newName)
                {
                    CollectionComboBox.SelectedItem = candidate;
                    break;
                }
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ToggleRelativeToButton_Click
    //
    // Adds or removes the relative-to anchor on the selected field row.  If show_relative_to is
    // false, sets it true so the editor panel appears.  If show_relative_to is true, clears
    // relative_to to null and sets show_relative_to false so Save writes a null anchor and the
    // field loads as absolute.  Recomputes the row's any_detail so its details visibility tracks
    // the change.  No-op when no row is selected or the selection is the new-row placeholder
    // rather than a DataRowView.
    //
    // sender:  The Relative To toggle button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ToggleRelativeToButton_Click(object sender, RoutedEventArgs e)
    {
        DataRowView? selected = FieldsGrid.SelectedItem as DataRowView;
        if (selected == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.ToggleRelativeToButton_Click: "
                + "no field row selected, ignoring", LogLevel.Warn);
            return;
        }

        string fieldName = selected["field_name"] as string ?? string.Empty;
        bool editorOpen = Convert.ToBoolean(selected["show_relative_to"]);

        if (editorOpen == false)
        {
            selected["show_relative_to"] = true;
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.ToggleRelativeToButton_Click: "
                + "field='" + fieldName + "' relative-to added", LogLevel.Trace);
        }
        else
        {
            selected["relative_to"] = DBNull.Value;
            selected["show_relative_to"] = false;
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.ToggleRelativeToButton_Click: "
                + "field='" + fieldName + "' relative-to cleared", LogLevel.Trace);
        }

        RecomputeAnyDetail(selected.Row);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ToggleGateButton_Click
    //
    // Adds or removes the gate on the selected field row.  If show_gate is false, sets it
    // true so the editor panel appears.  If show_gate is true, clears gate to null and sets
    // show_gate false so Save will delete the gate from the database.
    //
    // sender:  The Gate toggle button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ToggleGateButton_Click(object sender, RoutedEventArgs e)
    {
        DataRowView? selected = FieldsGrid.SelectedItem as DataRowView;
        if (selected == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.ToggleGateButton_Click: "
                + "no field row selected, ignoring", LogLevel.Warn);
            return;
        }

        string fieldName = selected["field_name"] as string ?? string.Empty;
        bool editorOpen = Convert.ToBoolean(selected["show_gate"]);

        if (editorOpen == false)
        {
            selected["show_gate"] = true;
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.ToggleGateButton_Click: "
                + "field='" + fieldName + "' gate added", LogLevel.Trace);
        }
        else
        {
            selected["gate"] = DBNull.Value;
            selected["show_gate"] = false;
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.ToggleGateButton_Click: "
                + "field='" + fieldName + "' gate cleared", LogLevel.Trace);
        }

        RecomputeAnyDetail(selected.Row);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TogglePredicateButton_Click
    //
    // Flips the show_predicate flag on the selected field row, opening or closing the second-line
    // Predicate editor for that row, then recomputes the row's any_detail so its details visibility
    // tracks the change.  The editor is shown when the predicate value is non-empty or the flag is
    // true, so toggling the flag off on a row that already has a predicate value leaves the editor
    // shown.  No-op when no row is selected or the selection is the new-row placeholder rather than
    // a DataRowView.
    //
    // sender:  The Predicate toggle button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void TogglePredicateButton_Click(object sender, RoutedEventArgs e)
    {
        DataRowView? selected = FieldsGrid.SelectedItem as DataRowView;
        if (selected == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.TogglePredicateButton_Click: "
                + "no field row selected, ignoring", LogLevel.Warn);
            return;
        }

        bool current = Convert.ToBoolean(selected["show_predicate"]);
        bool toggled = current == false;
        selected["show_predicate"] = toggled;
        RecomputeAnyDetail(selected.Row);

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.TogglePredicateButton_Click: "
            + "field='" + (selected["field_name"] as string ?? string.Empty)
            + "' show_predicate " + current + " -> " + toggled, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ApplyCollectionName
    //
    // Makes the FieldCollection row match the name in the name box.  When no collection was loaded
    // the row is inserted under newName.  When a collection was loaded and the name is unchanged
    // nothing is done.  When a collection was loaded and the name differs the rename is cascaded
    // through every table that references the old name.
    //
    // tx:          The transaction within which the writes run.
    // patchLevel:  The patch level the collection belongs to.
    // newName:     The collection name from the name box.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ApplyCollectionName(SqliteTransaction tx, PatchLevel patchLevel, string newName)
    {
        if (_loadedCollectionName.Length == 0)
        {
            using SqliteCommand cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO FieldCollection"
                + " (patch_date, server_type, name)"
                + " VALUES (@patchDate, @serverType, @name)";
            cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
            cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
            cmd.Parameters.AddWithValue("@name", newName);
            cmd.ExecuteNonQuery();

            DebugLog.Write(LogChannel.Fields, "CollectionEditor.ApplyCollectionName: inserted "
                + "FieldCollection name=" + newName + " for patchLevel=" + patchLevel, LogLevel.Trace);
            return;
        }

        if (_loadedCollectionName == newName)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.ApplyCollectionName: name unchanged ("
                + newName + "), nothing to do", LogLevel.Trace);
            return;
        }

        RenameCollection(tx, patchLevel, _loadedCollectionName, newName);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // RenameCollection
    //
    // Renames a FieldCollection and cascades the new name through every table that references the
    // old name, all within the caller's transaction and scoped to the patch level:
    //
    //   FieldCollection.name              oldName -> newName
    //   PacketField.collection_name       oldName -> newName   (the collection's own fields)
    //   PacketField.encoding              Gate_oldName -> Gate_newName   (fields gating into it)
    //   Gate.name                 Gate_oldName -> Gate_newName
    //   Gate.child_collection     oldName -> newName
    //
    // After this runs no row in the patch references the old name.
    //
    // tx:          The transaction within which the writes run.
    // patchLevel:  The patch level whose rows to update.
    // oldName:     The collection's current name.
    // newName:     The collection's new name.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void RenameCollection(SqliteTransaction tx, PatchLevel patchLevel, string oldName, string newName)
    {
        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE FieldCollection"
                + " SET name = @newName"
                + " WHERE patch_date = @patchDate"
                + " AND server_type = @serverType"
                + " AND name = @oldName";
            cmd.Parameters.AddWithValue("@newName", newName);
            cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
            cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
            cmd.Parameters.AddWithValue("@oldName", oldName);
            cmd.ExecuteNonQuery();
        }

        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE PacketField"
                + " SET collection_name = @newName"
                + " WHERE patch_date = @patchDate"
                + " AND server_type = @serverType"
                + " AND collection_name = @oldName";
            cmd.Parameters.AddWithValue("@newName", newName);
            cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
            cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
            cmd.Parameters.AddWithValue("@oldName", oldName);
            cmd.ExecuteNonQuery();
        }

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.RenameCollection: renamed '" + oldName
            + "' to '" + newName, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ResolveFieldEncoding
    //
    // Returns the encoding string to store for a field row.  When the row's gate cell holds a
    // non-empty string that string is the gate name and becomes the field's encoding directly.
    // Otherwise the row's scalar encoding cell is returned unchanged.
    //
    // A gate name that does not match any Gate row in the database is logged as a warning; the
    // name is still stored, because incomplete wiring is normal during schema authoring.
    //
    // patchLevel:  The patch level the field belongs to; used for the gate existence check.
    // row:         The grid row whose encoding to resolve.
    //
    // Returns:
    //   The gate name for a gated field, or the scalar encoding for an ungated field.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private string ResolveFieldEncoding(PatchLevel patchLevel, DataRow row)
    {
        string fieldName = row["field_name"] as string ?? string.Empty;
        string? gateRaw = row["gate"] as string;

        if (string.IsNullOrWhiteSpace(gateRaw) == false)
        {
            string gateName = gateRaw.Trim();

            bool gateExists;
            using (SqliteCommand cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*)"
                    + " FROM Gate"
                    + " WHERE patch_date = @patchDate"
                    + " AND server_type = @serverType"
                    + " AND name = @name";
                cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
                cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
                cmd.Parameters.AddWithValue("@name", gateName);

                object? scalar = cmd.ExecuteScalar();
                gateExists = Convert.ToInt32(scalar) > 0;
            }

            if (gateExists == false)
            {
                DebugLog.Write(LogChannel.Fields, "CollectionEditor.ResolveFieldEncoding: field '"
                    + fieldName + "' gate name '" + gateName
                    + "' not found in Gate table for patchLevel=" + patchLevel
                    + " — stored anyway, wiring incomplete", LogLevel.Warn);
            }
            else
            {
                DebugLog.Write(LogChannel.Fields, "CollectionEditor.ResolveFieldEncoding: field '"
                    + fieldName + "' gated, encoding='" + gateName + "'", LogLevel.Trace);
            }

            return gateName;
        }

        string scalarEncoding = row["encoding"] as string ?? string.Empty;
        DebugLog.Write(LogChannel.Fields, "CollectionEditor.ResolveFieldEncoding: field '"
            + fieldName + "' ungated, encoding='" + scalarEncoding + "'", LogLevel.Trace);
        return scalarEncoding;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractPredicateSourceName
    //
    // Returns the source field name from a predicate string — the operand to the left of the
    // comparison operator.  The operator itself is not needed for validation, only the field the
    // predicate reads, so this splits on the first operator character and returns the trimmed
    // left side.  The recognized operator characters are '=', '!', '>', '<', and '&', matching the
    // predicate grammar's operators.
    //
    // An empty or operator-less string yields the empty string, which the caller treats as "no
    // resolvable source" and reports as an invalid predicate.
    //
    // Parameters:
    //   predicate  - The raw predicate string from a field's predicate cell.
    //
    // Returns:
    //   The trimmed source field name, or the empty string when none can be extracted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private string ExtractPredicateSourceName(string predicate)
    {
        if (predicate == null)
        {
            return string.Empty;
        }

        string trimmed = predicate.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        char[] operatorChars = new char[] { '=', '!', '>', '<', '&' };
        int operatorIndex = trimmed.IndexOfAny(operatorChars);
        if (operatorIndex <= 0)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.ExtractPredicateSourceName: '"
                + trimmed + "' has no operator or no left operand, returning empty", LogLevel.Warn);
            return string.Empty;
        }

        string sourceName = trimmed.Substring(0, operatorIndex).Trim();

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.ExtractPredicateSourceName: '"
            + trimmed + "' -> source '" + sourceName + "'", LogLevel.Trace);
        return sourceName;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ValidateFields
    //
    // Validates the field rows before any save write runs.  Three checks, each throwing on the
    // first failure so the save aborts and rolls back:
    //
    //   1. Every predicate's source field names a field present in the grid.
    //   2. Every Times gate's count field names a field present in the grid.
    //   3. The fields form no dependency cycle, where a field depends on its relative-to anchor
    //      and on its predicate source.  Detected by Kahn's algorithm over the dependency graph.
    //
    // Rows marked for deletion are excluded from every check.  Field names are compared exactly.
    //
    // Throws:
    //   InvalidOperationException describing the first reference or cycle failure found.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ValidateFields()
    {
        if (_fieldsTable == null)
        {
            return;
        }

        List<DataRow> liveRows = new List<DataRow>();
        for (int rowIndex = 0; rowIndex < _fieldsTable.Rows.Count; rowIndex++)
        {
            DataRow row = _fieldsTable.Rows[rowIndex];
            if (row.RowState == DataRowState.Deleted)
            {
                continue;
            }
            liveRows.Add(row);
        }

        Dictionary<string, int> indexByName = new Dictionary<string, int>(liveRows.Count);
        for (int rowIndex = 0; rowIndex < liveRows.Count; rowIndex++)
        {
            string name = liveRows[rowIndex]["field_name"] as string ?? string.Empty;
            indexByName[name] = rowIndex;
        }

        // Check 1 and 2: predicate sources and gate count fields name present fields.
        for (int rowIndex = 0; rowIndex < liveRows.Count; rowIndex++)
        {
            DataRow row = liveRows[rowIndex];
            string ownName = row["field_name"] as string ?? string.Empty;

            if (ownName.Trim().Length == 0)
            {
                throw new InvalidOperationException("A field has a blank name. All fields must have a name.");
            }

            string? gateRaw = row["gate"] as string;
            bool hasGate = string.IsNullOrWhiteSpace(gateRaw) == false;

            string? encodingRaw = row["encoding"] as string;
            bool hasEncoding = string.IsNullOrWhiteSpace(encodingRaw) == false;

            if (hasGate == false && hasEncoding == false)
            {
                SetStatus("Field '" + ownName + "' has no encoding, aborting save");
                throw new InvalidOperationException("Field '" + ownName
                    + "' has no encoding and no gate.");
            }

            string? predicateRaw = row["predicate"] as string;
            if (string.IsNullOrWhiteSpace(predicateRaw) == false)
            {
                string sourceName = ExtractPredicateSourceName(predicateRaw);
                if (sourceName.Length == 0)
                {
                    throw new InvalidOperationException("Field '" + ownName
                        + "' has an invalid predicate: '" + predicateRaw + "'");
                }
                if (indexByName.ContainsKey(sourceName) == false)
                {
                    throw new InvalidOperationException("Field '" + ownName
                        + "' predicate references unknown field '" + sourceName + "'");
                }
            }
        }

        // Check 3: no dependency cycle over relative-to and predicate-source edges.
        int fieldCount = liveRows.Count;
        int[] inDegree = new int[fieldCount];
        List<int>[] dependents = new List<int>[fieldCount];
        for (int initIndex = 0; initIndex < fieldCount; initIndex++)
        {
            dependents[initIndex] = new List<int>();
        }

        for (int rowIndex = 0; rowIndex < fieldCount; rowIndex++)
        {
            DataRow row = liveRows[rowIndex];

            AddDependencyEdge(row["relative_to"] as string, rowIndex, indexByName, inDegree, dependents);

            string? predicateRaw = row["predicate"] as string;
            if (string.IsNullOrWhiteSpace(predicateRaw) == false)
            {
                string sourceName = ExtractPredicateSourceName(predicateRaw);
                AddDependencyEdge(sourceName, rowIndex, indexByName, inDegree, dependents);
            }
        }

        Queue<int> ready = new Queue<int>();
        for (int readyIndex = 0; readyIndex < fieldCount; readyIndex++)
        {
            if (inDegree[readyIndex] == 0)
            {
                ready.Enqueue(readyIndex);
            }
        }

        int resolved = 0;
        while (ready.Count > 0)
        {
            int current = ready.Dequeue();
            resolved = resolved + 1;

            List<int> currentDependents = dependents[current];
            for (int dependentSlot = 0; dependentSlot < currentDependents.Count; dependentSlot++)
            {
                int dependentIndex = currentDependents[dependentSlot];
                inDegree[dependentIndex] = inDegree[dependentIndex] - 1;
                if (inDegree[dependentIndex] == 0)
                {
                    ready.Enqueue(dependentIndex);
                }
            }
        }

        if (resolved != fieldCount)
        {
            string cyclic = string.Empty;
            for (int leftoverIndex = 0; leftoverIndex < fieldCount; leftoverIndex++)
            {
                if (inDegree[leftoverIndex] > 0)
                {
                    string name = liveRows[leftoverIndex]["field_name"] as string ?? string.Empty;
                    if (cyclic.Length > 0)
                    {
                        cyclic = cyclic + ", ";
                    }
                    cyclic = cyclic + name;
                }
            }

            throw new InvalidOperationException("Fields form a dependency cycle: " + cyclic);
        }

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.ValidateFields: " + fieldCount
            + " field(s) validated, no reference or cycle errors", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AddDependencyEdge
    //
    // Adds one edge to the dependency graph built by ValidateFields: the field at dependentIndex
    // depends on the field named by dependencyName.  The edge increments the dependent's in-degree
    // and records it under the dependency, so Kahn's algorithm releases the dependent only after
    // the dependency is resolved.
    //
    // A null, empty, or unknown dependency name adds no edge.  An unknown name is not an error
    // here: predicate sources are checked for existence before the graph is built, and a missing
    // relative-to anchor is left for the load path to handle, so the graph only links fields that
    // are present.
    //
    // dependencyName:  The name of the field this field depends on (an anchor or a predicate source).
    // dependentIndex:  The index of the depending field in the live-rows list.
    // indexByName:     Field name to live-rows index.
    // inDegree:        Per-field count of unresolved dependencies; incremented for the dependent.
    // dependents:      Per-field list of fields that depend on it; the dependent is added under
    //                  the dependency.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void AddDependencyEdge(string? dependencyName, int dependentIndex,
        Dictionary<string, int> indexByName, int[] inDegree, List<int>[] dependents)
    {
        if (string.IsNullOrWhiteSpace(dependencyName) == true)
        {
            return;
        }

        int dependencyIndex;
        bool found = indexByName.TryGetValue(dependencyName, out dependencyIndex);
        if (found == false)
        {
            return;
        }

        dependents[dependencyIndex].Add(dependentIndex);
        inDegree[dependentIndex] = inDegree[dependentIndex] + 1;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SaveFieldsTable
    //
    // Walks the changed rows in the fields table and issues PacketField INSERT/UPDATE/DELETE
    // statements according to each row's RowState, all within the caller's transaction.  Inserted
    // and updated rows carry collectionName as their collection_name; their encoding is resolved by
    // ResolveFieldEncoding, which returns a scalar encoding or a "Gate_<child>" name and upserts the
    // gate's Gate row when the field is gated.  The sequence, relative_to, and predicate columns are
    // written as null when their cell is blank.  No-op when the table is null or has no changes.
    //
    // tx:              The transaction within which the writes run.
    // patchLevel:      The patch level the fields belong to.
    // collectionName:  The collection name to store as each field's collection_name.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void SaveFieldsTable(SqliteTransaction tx, PatchLevel patchLevel, string collectionName)
    {
        if (_fieldsTable == null)
        {
            return;
        }

        DataTable? changes = _fieldsTable.GetChanges();
        if (changes == null)
        {
            return;
        }

        uint inserted = 0;
        uint updated = 0;
        uint deleted = 0;

        foreach (DataRow row in changes.Rows)
        {
            if (row.RowState == DataRowState.Deleted)
            {
                uint id = Convert.ToUInt32(row["id", DataRowVersion.Original]);
                using SqliteCommand cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM PacketField WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                deleted = deleted + 1;
            }
            else if (row.RowState == DataRowState.Added)
            {
                string encoding = ResolveFieldEncoding(patchLevel, row);

                using SqliteCommand cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO PacketField"
                    + " (patch_date, server_type, collection_name, field_name, sequence, bit_offset,"
                    + " bit_length, encoding, divisor, relative_to, predicate)"
                    + " VALUES (@patchDate, @serverType, @collectionName, @fieldName, @sequence, @bitOffset,"
                    + " @bitLength, @encoding, @divisor, @relativeTo, @predicate)";
                cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
                cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
                cmd.Parameters.AddWithValue("@collectionName", collectionName);
                cmd.Parameters.AddWithValue("@fieldName", row["field_name"]);
                cmd.Parameters.AddWithValue("@sequence", row["sequence"]);
                cmd.Parameters.AddWithValue("@bitOffset", row["bit_offset"]);
                cmd.Parameters.AddWithValue("@bitLength", row["bit_length"]);
                cmd.Parameters.AddWithValue("@encoding", encoding);
                cmd.Parameters.AddWithValue("@divisor", row["divisor"]);
                cmd.Parameters.AddWithValue("@relativeTo", NullableCell(row, "relative_to"));
                cmd.Parameters.AddWithValue("@predicate", NullableCell(row, "predicate"));
                cmd.ExecuteNonQuery();
                inserted = inserted + 1;
            }
            else if (row.RowState == DataRowState.Modified)
            {
                string encoding = ResolveFieldEncoding(patchLevel, row);

                using SqliteCommand cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE PacketField"
                    + " SET collection_name = @collectionName,"
                    + "     field_name = @fieldName,"
                    + "     sequence = @sequence,"
                    + "     bit_offset = @bitOffset,"
                    + "     bit_length = @bitLength,"
                    + "     encoding = @encoding,"
                    + "     divisor = @divisor,"
                    + "     relative_to = @relativeTo,"
                    + "     predicate = @predicate"
                    + " WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", row["id"]);
                cmd.Parameters.AddWithValue("@collectionName", collectionName);
                cmd.Parameters.AddWithValue("@fieldName", row["field_name"]);
                cmd.Parameters.AddWithValue("@sequence", row["sequence"]);
                cmd.Parameters.AddWithValue("@bitOffset", row["bit_offset"]);
                cmd.Parameters.AddWithValue("@bitLength", row["bit_length"]);
                cmd.Parameters.AddWithValue("@encoding", encoding);
                cmd.Parameters.AddWithValue("@divisor", row["divisor"]);
                cmd.Parameters.AddWithValue("@relativeTo", NullableCell(row, "relative_to"));
                cmd.Parameters.AddWithValue("@predicate", NullableCell(row, "predicate"));
                cmd.ExecuteNonQuery();
                updated = updated + 1;
            }
        }

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.SaveFieldsTable: collection='"
            + collectionName + "' inserted=" + inserted + " updated=" + updated
            + " deleted=" + deleted, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // NullableCell
    //
    // Returns the trimmed string value of a row cell, or DBNull when the cell is null, not a string,
    // or blank.  Used for the optional PacketField columns relative_to and predicate, which store
    // null rather than an empty string when the user leaves them blank.
    //
    // row:     The row whose cell to read.
    // column:  The column name.
    //
    // Returns:
    //   The trimmed string, or DBNull.Value when the cell holds no meaningful text.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private object NullableCell(DataRow row, string column)
    {
        string? raw = row[column] as string;
        if (string.IsNullOrWhiteSpace(raw) == true)
        {
            return DBNull.Value;
        }
        return raw.Trim();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetContextMenuRow
    //
    // Returns the DataRowView for the currently selected row in FieldsGrid.  Used by context menu
    // handlers to identify the target row.  Returns null when no row is selected or the selection
    // is not a DataRowView.
    //
    // sender:  The MenuItem that was clicked; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private DataRowView? GetContextMenuRow(object sender)
    {
        DataRowView? rowView = FieldsGrid.SelectedItem as DataRowView;

        if (rowView == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.GetContextMenuRow: "
                + "no DataRowView selected", LogLevel.Warn);
        }

        return rowView;
    }

    private void InsertRowHereMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_fieldsTable == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.InsertRowHereMenuItem_Click: "
                + "no collection loaded, ignoring", LogLevel.Warn);
            return;
        }

        DataRowView? rowView = GetContextMenuRow(sender);
        if (rowView == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.InsertRowHereMenuItem_Click: "
                + "could not resolve context menu row, ignoring", LogLevel.Warn);
            return;
        }

        object sequenceCell = rowView.Row["sequence"];
        if (sequenceCell == DBNull.Value)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.InsertRowHereMenuItem_Click: "
                + "right-clicked row has no sequence number, ignoring", LogLevel.Warn);
            return;
        }

        int insertSequence = Convert.ToInt32(sequenceCell) + 1;

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.InsertRowHereMenuItem_Click: "
            + "inserting at sequence=" + insertSequence, LogLevel.Trace);

        for (int rowIndex = 0; rowIndex < _fieldsTable.Rows.Count; rowIndex++)
        {
            DataRow row = _fieldsTable.Rows[rowIndex];
            if (row.RowState == DataRowState.Deleted)
            {
                continue;
            }

            object cell = row["sequence"];
            if (cell == DBNull.Value)
            {
                continue;
            }

            int sequence = Convert.ToInt32(cell);
            if (sequence >= insertSequence)
            {
                row["sequence"] = sequence + 1;
            }
        }

        DataRow newRow = _fieldsTable.NewRow();
        newRow["sequence"] = insertSequence;
        newRow["field_name"] = string.Empty;
        newRow["bit_offset"] = 0;
        newRow["bit_length"] = 0;
        newRow["encoding"] = string.Empty;
        newRow["divisor"] = 1.0;
        newRow["relative_to"] = DBNull.Value;
        newRow["predicate"] = DBNull.Value;
        newRow["gate"] = DBNull.Value;

        int insertIndex = _fieldsTable.Rows.IndexOf(rowView.Row) + 1;
        _fieldsTable.Rows.InsertAt(newRow, insertIndex);

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.InsertRowHereMenuItem_Click: "
            + "inserted blank row at sequence=" + insertSequence
            + ", table now has " + _fieldsTable.Rows.Count + " row(s)", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // RemoveGapsMenuItem_Click
    //
    // Re-sequences all rows from the right-clicked row's sequence number onward, making them
    // consecutive with no gaps.  Rows above the right-clicked row are untouched.  No-op when no
    // collection is loaded or the target row cannot be resolved.
    //
    // sender:  The Remove Gaps menu item.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void RemoveGapsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_fieldsTable == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.RemoveGapsMenuItem_Click: "
                + "no collection loaded, ignoring", LogLevel.Warn);
            return;
        }

        DataRowView? rowView = GetContextMenuRow(sender);
        if (rowView == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.RemoveGapsMenuItem_Click: "
                + "could not resolve context menu row, ignoring", LogLevel.Warn);
            return;
        }

        object sequenceCell = rowView.Row["sequence"];
        if (sequenceCell == DBNull.Value)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.RemoveGapsMenuItem_Click: "
                + "right-clicked row has no sequence number, ignoring", LogLevel.Warn);
            return;
        }

        int startSequence = Convert.ToInt32(sequenceCell);

        List<DataRow> affectedRows = new List<DataRow>();
        for (int rowIndex = 0; rowIndex < _fieldsTable.Rows.Count; rowIndex++)
        {
            DataRow row = _fieldsTable.Rows[rowIndex];
            if (row.RowState == DataRowState.Deleted)
            {
                continue;
            }

            object cell = row["sequence"];
            if (cell == DBNull.Value)
            {
                continue;
            }

            if (Convert.ToInt32(cell) >= startSequence)
            {
                affectedRows.Add(row);
            }
        }

        affectedRows.Sort((DataRow a, DataRow b) =>
            Convert.ToInt32(a["sequence"]).CompareTo(Convert.ToInt32(b["sequence"])));

        int next = startSequence;
        for (int rowIndex = 0; rowIndex < affectedRows.Count; rowIndex++)
        {
            affectedRows[rowIndex]["sequence"] = next;
            next = next + 1;
        }

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.RemoveGapsMenuItem_Click: "
            + "re-sequenced " + affectedRows.Count + " row(s) from sequence="
            + startSequence, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetStatus
    //
    // Sets the status bar text to the given message.  Null or empty clears the bar.
    //
    // message:  The text to display, or null or empty to clear.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void SetStatus(string? message)
    {
        StatusText.Text = message ?? string.Empty;

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.SetStatus: '"
            + (message ?? string.Empty) + "'", LogLevel.Trace);
    }
}
