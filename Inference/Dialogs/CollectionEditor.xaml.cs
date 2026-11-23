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
    // _problemPending
    //
    // True when a problem has been reported to the status bar and not yet cleared.  Set by
    // SetProblem.  Cleared when validation of the fields completes with no problem found.  While
    // true, no changes are written to the database.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private bool _problemPending = false;

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
    // FieldNamesVersionProperty
    //
    // Dependency property backing FieldNamesVersion.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static readonly DependencyProperty FieldNamesVersionProperty = DependencyProperty.Register(
        nameof(FieldNamesVersion), typeof(uint), typeof(CollectionEditor), new PropertyMetadata(0u));

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // FieldNamesVersion
    //
    // A counter that changes whenever the set of field names in the fields table changes: a name
    // edited, a row added, or a row deleted.  Bound into each row's relative-to color so the color
    // is recomputed when another row's name changes.  The value itself carries no meaning.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public uint FieldNamesVersion
    {
        get
        {
            return (uint)GetValue(FieldNamesVersionProperty);
        }
        set
        {
            SetValue(FieldNamesVersionProperty, value);
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
    // collection combobox.
    //
    // patchLevel:  The patch level whose collection names to load.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PopulateCollectionDropdown(PatchLevel patchLevel)
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

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.PopulateCollectionDropdown: "
            + names.Count + " collection(s) for patchLevel=" + patchLevel, LogLevel.Trace);

        CollectionComboBox.ItemsSource = names;
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
    //
    // The table's column-changed, row-changed, and row-deleted events are attached so that a field
    // name edit, a row add, or a row delete increments FieldNamesVersion.
    //
    // Returns:
    //   The new, empty fields table.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private DataTable CreateFieldsTable()
    {
        DataTable table = new DataTable();
        DataColumn idColumn = table.Columns.Add("id", typeof(long));
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

        table.ColumnChanged += FieldsTable_ColumnChanged;
        table.RowChanged += FieldsTable_RowChanged;
        table.RowDeleted += FieldsTable_RowDeleted;

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.CreateFieldsTable: table created, "
            + "field name change handlers attached", LogLevel.Trace);

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
    // FieldsTable_ColumnChanged
    //
    // Handles a column value change in the fields table.  When the changed column is field_name,
    // increments FieldNamesVersion so relative-to colors are recomputed.  Changes to any other
    // column are ignored.
    //
    // sender:  The fields table.
    // e:       Identifies the changed row and column.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void FieldsTable_ColumnChanged(object sender, DataColumnChangeEventArgs e)
    {
        if (e.Column == null || e.Column.ColumnName != "field_name")
        {
            return;
        }

        FieldNamesVersion = FieldNamesVersion + 1;

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.FieldsTable_ColumnChanged: field_name now '"
            + (e.ProposedValue as string ?? string.Empty) + "', FieldNamesVersion=" + FieldNamesVersion, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // FieldsTable_RowChanged
    //
    // Handles a row change in the fields table.  When the action is an add, increments
    // FieldNamesVersion so relative-to colors are recomputed.  Other actions are ignored.
    //
    // sender:  The fields table.
    // e:       Identifies the changed row and the action.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void FieldsTable_RowChanged(object sender, DataRowChangeEventArgs e)
    {
        if (e.Action != DataRowAction.Add)
        {
            return;
        }

        FieldNamesVersion = FieldNamesVersion + 1;

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.FieldsTable_RowChanged: row added, FieldNamesVersion="
            + FieldNamesVersion, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // FieldsTable_RowDeleted
    //
    // Handles a row deletion in the fields table.  Increments FieldNamesVersion so relative-to
    // colors are recomputed.
    //
    // sender:  The fields table.
    // e:       Identifies the deleted row; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void FieldsTable_RowDeleted(object sender, DataRowChangeEventArgs e)
    {
        FieldNamesVersion = FieldNamesVersion + 1;

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.FieldsTable_RowDeleted: row deleted, FieldNamesVersion="
            + FieldNamesVersion, LogLevel.Trace);
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
    // Fires when the collection selection changes.  Puts the selected collection's name in the
    // name box, records it as the loaded collection, and loads its fields into the grid.
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
        string collectionName = (string)CollectionComboBox.SelectedItem;

        _loadedCollectionName = collectionName;
        CollectionNameTextBox.Text = collectionName;

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.CollectionComboBox_SelectionChanged: "
            + "patchLevel=" + patchLevel + " collection=" + collectionName, LogLevel.Trace);

        LoadFieldsTable(patchLevel, collectionName);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // NewCollectionButton_Click
    //
    // Resets the editor to the empty state for entering a new collection.  Clears the collection
    // selection and name box, clears the loaded-collection name, replaces the fields table with a
    // fresh empty one bound to the grid, and focuses the name box so the new collection can be
    // named.  No-op when no patch level is selected.
    //
    // sender:  The New button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void NewCollectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (PatchLevelComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.NewCollectionButton_Click: "
                + "no patch level selected, ignoring", LogLevel.Trace);
            return;
        }

        CollectionComboBox.SelectedItem = null;
        CollectionNameTextBox.Text = string.Empty;
        _loadedCollectionName = string.Empty;

        _fieldsTable = CreateFieldsTable();
        FieldsGrid.ItemsSource = _fieldsTable.DefaultView;

        CollectionNameTextBox.Focus();

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.NewCollectionButton_Click: "
            + "cleared to empty state for a new collection", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // DeleteCollectionButton_Click
    //
    // Deletes the loaded collection and its fields from the database.  Refused, with the reason
    // reported through SetProblem, when no saved collection is loaded or when any Gate row uses the
    // collection as its child collection; the refusal names those gates.  Otherwise asks for
    // confirmation, then deletes within a single transaction.  A database failure rolls back,
    // leaving the database unchanged, and is reported through SetProblem.  After a successful
    // delete the collection dropdown is refreshed and the editor is left with no collection loaded.
    //
    // sender:  The Delete button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void DeleteCollectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (PatchLevelComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.DeleteCollectionButton_Click: "
                + "no patch level selected, ignoring", LogLevel.Warn);
            return;
        }

        if (_loadedCollectionName.Length == 0)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.DeleteCollectionButton_Click: "
                + "no saved collection loaded, not deleting", LogLevel.Trace);
            SetProblem("No saved collection is loaded to delete.");
            return;
        }

        PatchLevel patchLevel = (PatchLevel)PatchLevelComboBox.SelectedItem;
        string collectionName = _loadedCollectionName;

        List<string> gateNames = FindGatesUsingCollection(patchLevel, collectionName);
        if (gateNames.Count > 0)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.DeleteCollectionButton_Click: "
                + "collection '" + collectionName + "' used by " + gateNames.Count
                + " gate(s), not deleting", LogLevel.Trace);
            SetProblem("Collection '" + collectionName + "' is used by gate(s): "
                + string.Join(", ", gateNames) + ".  Delete those first.");
            return;
        }

        MessageBoxResult answer = MessageBox.Show(this,
            "Delete collection '" + collectionName + "' and all of its fields?  This cannot be undone.",
            "Delete Collection", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.DeleteCollectionButton_Click: "
                + "delete of '" + collectionName + "' declined", LogLevel.Trace);
            return;
        }

        using SqliteTransaction tx = _connection.BeginTransaction();

        try
        {
            DeleteCollection(tx, patchLevel, collectionName);
            tx.Commit();
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.DeleteCollectionButton_Click: "
                + "delete of '" + collectionName + "' committed", LogLevel.Trace);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.DeleteCollectionButton_Click: rolled back, "
                + ex.GetType().Name + ": " + ex.Message, LogLevel.Error);
            SetProblem("Delete failed: " + ex.Message);
            return;
        }

        PopulateCollectionDropdown(patchLevel);
        CollectionNameTextBox.Text = string.Empty;
        _loadedCollectionName = string.Empty;
        _fieldsTable = null;
        FieldsGrid.ItemsSource = null;

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.DeleteCollectionButton_Click: "
            + "editor cleared after delete of '" + collectionName + "'", LogLevel.Trace);
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
    // Validates the collection and, when no problem is pending, persists pending changes to the
    // database inside a single transaction.  Either every change commits or none does.
    //
    // A blank collection name or a failed field validation is reported to the status bar through
    // SetProblem and nothing is written.  Otherwise the FieldCollection row is made to match the
    // name in the name box, renaming it when the name differs from the loaded name, and the fields
    // table's INSERT/UPDATE/DELETE statements are written.  A database failure rolls the transaction
    // back, leaving the database unchanged, and is reported through SetProblem.
    //
    // After a successful commit the collection dropdown is refreshed so a new or renamed collection
    // appears, the saved collection is reselected, and its fields are reloaded from the database so
    // the grid holds no pending changes and every row carries its database id.
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
                + "empty collection name, not saving", LogLevel.Trace);
            SetProblem("Collection name is required.");
            return;
        }

        ValidateFields();
        if (_problemPending == true)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.SaveButton_Click: "
                + "problem pending after validation, not saving", LogLevel.Trace);
            return;
        }

        PatchLevel patchLevel = (PatchLevel)PatchLevelComboBox.SelectedItem;
        DebugLog.Write(LogChannel.Fields, "CollectionEditor.SaveButton_Click: starting save, "
            + "patchLevel=" + patchLevel + " name=" + newName + " loadedName=" + _loadedCollectionName, LogLevel.Trace);

        using SqliteTransaction tx = _connection.BeginTransaction();

        try
        {
            ApplyCollectionName(tx, patchLevel, newName);
            SaveFieldsTable(tx, patchLevel, newName);
            tx.Commit();
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.SaveButton_Click: committed", LogLevel.Trace);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.SaveButton_Click: rolled back, "
                + ex.GetType().Name + ": " + ex.Message, LogLevel.Error);
            SetProblem("Save failed: " + ex.Message);
            return;
        }

        _loadedCollectionName = newName;

        PopulateCollectionDropdown(patchLevel);
        CollectionComboBox.SelectedItem = newName;
        LoadFieldsTable(patchLevel, newName);

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.SaveButton_Click: reselected and reloaded collection '"
            + newName + "'", LogLevel.Trace);
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
    // Renames a FieldCollection and cascades the new name through every column that refers to a
    // collection by name, all within the caller's transaction and scoped to the patch level:
    //
    //   FieldCollection.name              oldName -> newName
    //   PacketField.collection_name       oldName -> newName   (the collection's own fields)
    //   Gate.child_collection             oldName -> newName   (gates that select the collection)
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

        int gatesUpdated;
        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE Gate"
                + " SET child_collection = @newName"
                + " WHERE patch_date = @patchDate"
                + " AND server_type = @serverType"
                + " AND child_collection = @oldName";
            cmd.Parameters.AddWithValue("@newName", newName);
            cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
            cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
            cmd.Parameters.AddWithValue("@oldName", oldName);
            gatesUpdated = cmd.ExecuteNonQuery();
        }

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.RenameCollection: renamed '" + oldName
            + "' to '" + newName + "', " + gatesUpdated + " gate(s) updated", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // FindGatesUsingCollection
    //
    // Returns the name of every Gate row in the patch level whose child_collection is the given
    // collection, ordered by name.
    //
    // patchLevel:      The patch level whose Gate rows to search.
    // collectionName:  The collection name to look for in child_collection.
    //
    // Returns:
    //   The matching gate names; empty when no gate uses the collection.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private List<string> FindGatesUsingCollection(PatchLevel patchLevel, string collectionName)
    {
        List<string> gateNames = new List<string>();

        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name"
                + " FROM Gate"
                + " WHERE patch_date = @patchDate"
                + " AND server_type = @serverType"
                + " AND child_collection = @collectionName"
                + " ORDER BY name";
            cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
            cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
            cmd.Parameters.AddWithValue("@collectionName", collectionName);

            using SqliteDataReader reader = cmd.ExecuteReader();
            while (reader.Read() == true)
            {
                gateNames.Add(reader.GetString(0));
            }
        }

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.FindGatesUsingCollection: "
            + gateNames.Count + " gate(s) use collection '" + collectionName + "' in patchLevel="
            + patchLevel, LogLevel.Trace);

        return gateNames;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // DeleteCollection
    //
    // Deletes a collection from the patch level within the given transaction: every PacketField
    // row whose collection_name is the collection, then the FieldCollection row itself.
    //
    // tx:              The transaction within which the deletes run.
    // patchLevel:      The patch level the collection belongs to.
    // collectionName:  The name of the collection to delete.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void DeleteCollection(SqliteTransaction tx, PatchLevel patchLevel, string collectionName)
    {
        int fieldsDeleted;
        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM PacketField"
                + " WHERE patch_date = @patchDate"
                + " AND server_type = @serverType"
                + " AND collection_name = @collectionName";
            cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
            cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
            cmd.Parameters.AddWithValue("@collectionName", collectionName);
            fieldsDeleted = cmd.ExecuteNonQuery();
        }

        int collectionsDeleted;
        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM FieldCollection"
                + " WHERE patch_date = @patchDate"
                + " AND server_type = @serverType"
                + " AND name = @collectionName";
            cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
            cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
            cmd.Parameters.AddWithValue("@collectionName", collectionName);
            collectionsDeleted = cmd.ExecuteNonQuery();
        }

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.DeleteCollection: collection '"
            + collectionName + "' in patchLevel=" + patchLevel + ", " + fieldsDeleted
            + " field row(s) and " + collectionsDeleted + " collection row(s) deleted", LogLevel.Trace);
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
    // ValidateFields
    //
    // Validates the field rows before any save write runs.  Checks, stopping at the first failure:
    //
    //   1. No two fields share a non-blank name.
    //   2. Every field has a non-blank name.
    //   3. Every field has an encoding or a gate.
    //   4. Every predicate parses under the patch data predicate expression grammar, and every
    //      source field named by its comparisons is present in the grid.
    //   5. Every relative-to anchor names a field present in the grid.
    //   6. The fields form no dependency cycle, where a field depends on its relative-to anchor
    //      and on every source field of its predicate.  Detected by Kahn's algorithm over
    //      the dependency graph.
    //
    // Rows marked for deletion are excluded from every check.  Field names are compared exactly;
    // the relative-to value is trimmed before comparison.  The first failure is reported through
    // SetProblem.  When every check passes, _problemPending is cleared and the status bar is
    // cleared.  When no fields table is loaded there is nothing to check and validation succeeds.
    //
    // Returns:
    //   True when every check passed; false when a problem was reported.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private bool ValidateFields()
    {
        if (_fieldsTable == null)
        {
            _problemPending = false;
            SetStatus(string.Empty);
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.ValidateFields: no fields table, "
                + "nothing to validate", LogLevel.Trace);
            return true;
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

        // Check 1: no duplicate non-blank names.
        Dictionary<string, int> indexByName = new Dictionary<string, int>(liveRows.Count);
        for (int rowIndex = 0; rowIndex < liveRows.Count; rowIndex++)
        {
            string name = liveRows[rowIndex]["field_name"] as string ?? string.Empty;
            if (name.Trim().Length > 0 && indexByName.ContainsKey(name) == true)
            {
                DebugLog.Write(LogChannel.Fields, "CollectionEditor.ValidateFields: field name '"
                    + name + "' used more than once", LogLevel.Trace);
                SetProblem("Field name '" + name + "' is used by more than one field.");
                return false;
            }
            indexByName[name] = rowIndex;
        }

        // Source field names of each row's predicate comparisons, empty when the row has none.
        List<string>[] predicateSources = new List<string>[liveRows.Count];

        // Checks 2 through 5: names, encodings, predicates, and relative-to anchors.
        for (int rowIndex = 0; rowIndex < liveRows.Count; rowIndex++)
        {
            DataRow row = liveRows[rowIndex];
            string ownName = row["field_name"] as string ?? string.Empty;
            predicateSources[rowIndex] = new List<string>();

            if (ownName.Trim().Length == 0)
            {
                DebugLog.Write(LogChannel.Fields, "CollectionEditor.ValidateFields: blank field name "
                    + "at live row " + rowIndex, LogLevel.Trace);
                SetProblem("A field has a blank name.  All fields must have a name.");
                return false;
            }

            string? gateRaw = row["gate"] as string;
            bool hasGate = string.IsNullOrWhiteSpace(gateRaw) == false;

            string? encodingRaw = row["encoding"] as string;
            bool hasEncoding = string.IsNullOrWhiteSpace(encodingRaw) == false;

            if (hasGate == false && hasEncoding == false)
            {
                DebugLog.Write(LogChannel.Fields, "CollectionEditor.ValidateFields: field '"
                    + ownName + "' has no encoding and no gate", LogLevel.Trace);
                SetProblem("Field '" + ownName + "' has no encoding and no gate.");
                return false;
            }

            string? predicateRaw = row["predicate"] as string;
            if (string.IsNullOrWhiteSpace(predicateRaw) == false)
            {
                PredicateProgram parsedProgram;
                bool parsed = PatchData.ParsePredicateExpression(predicateRaw, out parsedProgram);
                if (parsed == false)
                {
                    DebugLog.Write(LogChannel.Fields, "CollectionEditor.ValidateFields: field '"
                        + ownName + "' predicate '" + predicateRaw + "' does not parse", LogLevel.Trace);
                    SetProblem("Field '" + ownName + "' has an invalid predicate: '" + predicateRaw + "'");
                    return false;
                }

                string?[] sourceNames = parsedProgram.SourceNames!;
                for (int stepIndex = 0; stepIndex < sourceNames.Length; stepIndex++)
                {
                    string? sourceName = sourceNames[stepIndex];
                    if (sourceName == null)
                    {
                        continue;
                    }

                    if (indexByName.ContainsKey(sourceName) == false)
                    {
                        DebugLog.Write(LogChannel.Fields, "CollectionEditor.ValidateFields: field '"
                            + ownName + "' predicate source '" + sourceName + "' not in grid",
                            LogLevel.Trace);
                        SetProblem("Field '" + ownName + "' predicate references unknown field '"
                            + sourceName + "'");
                        return false;
                    }

                    predicateSources[rowIndex].Add(sourceName);
                }

                DebugLog.Write(LogChannel.Fields, "CollectionEditor.ValidateFields: field '"
                    + ownName + "' predicate '" + predicateRaw + "' names "
                    + predicateSources[rowIndex].Count + " source field(s)", LogLevel.Trace);
            }

            string? relativeToRaw = row["relative_to"] as string;
            if (string.IsNullOrWhiteSpace(relativeToRaw) == false)
            {
                string anchorName = relativeToRaw.Trim();
                if (indexByName.ContainsKey(anchorName) == false)
                {
                    DebugLog.Write(LogChannel.Fields, "CollectionEditor.ValidateFields: field '"
                        + ownName + "' relative-to anchor '" + anchorName + "' not in grid", LogLevel.Trace);
                    SetProblem("Field '" + ownName + "' is relative to unknown field '"
                        + anchorName + "'");
                    return false;
                }
            }
        }

        // Check 6: no dependency cycle over relative-to and predicate-source edges.
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

            string? relativeToRaw = row["relative_to"] as string;
            if (string.IsNullOrWhiteSpace(relativeToRaw) == false)
            {
                AddDependencyEdge(relativeToRaw.Trim(), rowIndex, indexByName, inDegree, dependents);
            }

            List<string> rowSources = predicateSources[rowIndex];
            for (int sourceIndex = 0; sourceIndex < rowSources.Count; sourceIndex++)
            {
                AddDependencyEdge(rowSources[sourceIndex], rowIndex, indexByName, inDegree, dependents);
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

            DebugLog.Write(LogChannel.Fields, "CollectionEditor.ValidateFields: dependency cycle among "
                + cyclic, LogLevel.Trace);
            SetProblem("Fields form a dependency cycle: " + cyclic);
            return false;
        }

        _problemPending = false;
        SetStatus(string.Empty);

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.ValidateFields: " + fieldCount
            + " field(s) validated, no problems, problem flag cleared", LogLevel.Trace);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AddDependencyEdge
    //
    // Adds one edge to a field dependency graph: the field at dependentIndex depends on the field
    // named by dependencyName.  The edge increments the dependent's in-degree and records it under
    // the dependency, so Kahn's algorithm releases the dependent only after the dependency is
    // resolved.
    //
    // A null, empty, or unknown dependency name adds no edge.  Predicate sources and relative-to
    // anchors are checked for existence before the graph is built, so the graph only links fields
    // that are present.
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
    // Writes the changed rows in the fields table to PacketField within the caller's transaction,
    // in three passes: every DELETE, then every UPDATE, then every INSERT, so a field name released
    // by a delete is free before an update or insert uses it.  Inserted and updated rows carry
    // collectionName as their collection_name, and their encoding is resolved by
    // ResolveFieldEncoding.  The relative_to and predicate columns are written as null when their
    // cell is blank.  No-op when the table is null or has no changes.
    //
    // A statement that fails is rethrown as an InvalidOperationException naming the action and the
    // field; an update also names the field's original name.
    //
    // tx:              The transaction within which the writes run.
    // patchLevel:      The patch level the fields belong to.
    // collectionName:  The collection name to store as each field's collection_name.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void SaveFieldsTable(SqliteTransaction tx, PatchLevel patchLevel, string collectionName)
    {
        if (_fieldsTable == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.SaveFieldsTable: no fields table, "
                + "nothing to write", LogLevel.Trace);
            return;
        }

        DataTable? changes = _fieldsTable.GetChanges();
        if (changes == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.SaveFieldsTable: no changes, "
                + "nothing to write", LogLevel.Trace);
            return;
        }

        uint inserted = 0;
        uint updated = 0;
        uint deleted = 0;

        // Pass 1: deletes.
        foreach (DataRow row in changes.Rows)
        {
            if (row.RowState != DataRowState.Deleted)
            {
                continue;
            }

            uint id = Convert.ToUInt32(row["id", DataRowVersion.Original]);
            string fieldName = row["field_name", DataRowVersion.Original] as string ?? string.Empty;

            using SqliteCommand cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM PacketField WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);

            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                DebugLog.Write(LogChannel.Fields, "CollectionEditor.SaveFieldsTable: delete of field '"
                    + fieldName + "' failed, " + ex.Message, LogLevel.Error);
                throw new InvalidOperationException("delete of field '" + fieldName + "' failed: "
                    + ex.Message, ex);
            }

            deleted = deleted + 1;
        }

        // Pass 2: updates.
        foreach (DataRow row in changes.Rows)
        {
            if (row.RowState != DataRowState.Modified)
            {
                continue;
            }

            string fieldName = row["field_name"] as string ?? string.Empty;
            string originalName = row["field_name", DataRowVersion.Original] as string ?? string.Empty;
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

            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                DebugLog.Write(LogChannel.Fields, "CollectionEditor.SaveFieldsTable: update of field '"
                    + fieldName + "' (was '" + originalName + "') failed, " + ex.Message, LogLevel.Error);
                throw new InvalidOperationException("update of field '" + fieldName + "' (was '"
                    + originalName + "') failed: " + ex.Message, ex);
            }

            updated = updated + 1;
        }

        // Pass 3: inserts.
        foreach (DataRow row in changes.Rows)
        {
            if (row.RowState != DataRowState.Added)
            {
                continue;
            }

            string fieldName = row["field_name"] as string ?? string.Empty;
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

            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                DebugLog.Write(LogChannel.Fields, "CollectionEditor.SaveFieldsTable: insert of field '"
                    + fieldName + "' failed, " + ex.Message, LogLevel.Error);
                throw new InvalidOperationException("insert of field '" + fieldName + "' failed: "
                    + ex.Message, ex);
            }

            inserted = inserted + 1;
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
    // RenumberButton_Click
    //
    // Renumbers every row in the collection with sequence numbers starting at 10 and stepping
    // by 10, keeping the current order.  Rows that have a sequence number are ordered by it,
    // with ties kept in table order.  Rows with no sequence number follow, in table order.
    // Deleted rows are skipped.  No-op when no collection is loaded.
    //
    // sender:  The Renumber button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void RenumberButton_Click(object sender, RoutedEventArgs e)
    {
        const int renumberStart = 10;
        const int renumberStep = 10;

        if (_fieldsTable == null)
        {
            DebugLog.Write(LogChannel.Fields, "CollectionEditor.RenumberButton_Click: "
                + "no collection loaded, ignoring", LogLevel.Warn);
            return;
        }

        DataTable table = _fieldsTable;

        List<int> sequencedIndexes = new List<int>();
        List<int> unsequencedIndexes = new List<int>();
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            DataRow row = table.Rows[rowIndex];
            if (row.RowState == DataRowState.Deleted)
            {
                continue;
            }

            if (row["sequence"] == DBNull.Value)
            {
                unsequencedIndexes.Add(rowIndex);
            }
            else
            {
                sequencedIndexes.Add(rowIndex);
            }
        }

        sequencedIndexes.Sort((int a, int b) =>
        {
            int sequenceA = Convert.ToInt32(table.Rows[a]["sequence"]);
            int sequenceB = Convert.ToInt32(table.Rows[b]["sequence"]);
            if (sequenceA != sequenceB)
            {
                return sequenceA.CompareTo(sequenceB);
            }
            return a.CompareTo(b);
        });

        int next = renumberStart;
        for (int listIndex = 0; listIndex < sequencedIndexes.Count; listIndex++)
        {
            table.Rows[sequencedIndexes[listIndex]]["sequence"] = next;
            next = next + renumberStep;
        }

        for (int listIndex = 0; listIndex < unsequencedIndexes.Count; listIndex++)
        {
            table.Rows[unsequencedIndexes[listIndex]]["sequence"] = next;
            next = next + renumberStep;
        }

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.RenumberButton_Click: "
            + "renumbered " + sequencedIndexes.Count + " sequenced and " + unsequencedIndexes.Count
            + " unsequenced row(s) from " + renumberStart + " by " + renumberStep, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetProblem
    //
    // Reports a problem with the collection being edited.  Replaces the status bar text with the
    // message and sets _problemPending so changes are not written until validation succeeds.
    //
    // message:  The problem description shown on the status bar.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void SetProblem(string message)
    {
        _problemPending = true;
        SetStatus(message);

        DebugLog.Write(LogChannel.Fields, "CollectionEditor.SetProblem: problem pending, '"
            + message + "'", LogLevel.Trace);
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
