using Glass.Core;
using Glass.Core.Logging;
using Glass.Data;
using Glass.Network.Protocol;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Inference.Dialogs;

///////////////////////////////////////////////////////////////////////////////////////////////
// PatchDataEditor
//
// Modeless editor for the four patch-data tables: PatchOpcode, PacketField,
// PacketOptionalGroup, PacketOptionalField.  Reads and writes via direct SQL through
// the Database singleton.
///////////////////////////////////////////////////////////////////////////////////////////////
public partial class PatchDataEditor : Window
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _connection
    //
    // Database connection held for the lifetime of the editor window.  Opened in the
    // constructor, used by every helper that reads or writes the four patch-data tables,
    // and disposed when the window closes.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private readonly SqliteConnection _connection;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _fieldsTable
    //
    // DataTable bound to FieldsGrid.  Loaded from PacketField when an opcode is selected.
    // The grid's add/delete/edit operations mutate this table directly; SaveButton_Click
    // walks the table's RowState to issue INSERT/UPDATE/DELETE statements.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private DataTable? _fieldsTable;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _optionalGroupsTable
    //
    // DataTable bound to OptionalGroupsGrid.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private DataTable? _optionalGroupsTable;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _optionalFieldsTable
    //
    // DataTable bound to OptionalFieldsGrid.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private DataTable? _optionalFieldsTable;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _patchOpcodeKey
    //
    // PatchOpcode.id (database primary key) of the currently displayed opcode.  Cached when
    // the version selection commits, used as the foreign key when saving new rows in
    // _fieldsTable and _optionalGroupsTable.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private uint _patchOpcodeKey;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PatchDataEditor (constructor)
    //
    // Initializes the window and opens the database connection held for the lifetime of the
    // window.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchDataEditor()
    {
        InitializeComponent();

        OpcodeNameTextBox.Text = string.Empty;
        OpcodeHexTextBox.Text = string.Empty;
        OpcodeByteLengthTextBox.Text = string.Empty;
        OpcodeVersionTextBox.Text = string.Empty;
        OpcodeKeyTextBox.Text = string.Empty;

        _connection = Database.Instance.Connect();
        _connection.Open();

        PopulateEncodingDropdowns();
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
        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.OnClosed: disposing connection");

        _connection.Dispose();

        base.OnClosed(e);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PopulateEncodingDropdowns
    //
    // Sets the ItemsSource on both encoding DataGridComboBoxColumns from the encoding strings
    // recognized by the current patch level's PatchData.  Both the required-fields grid and
    // the optional-fields grid use the same set of encoding strings.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PopulateEncodingDropdowns()
    {
        PatchLevel patchLevel = GlassContext.CurrentPatchLevel;
        string[] encodingStrings = GlassContext.PatchRegistry.GetEncodingStrings(patchLevel);

        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.PopulateEncodingDropdowns: "
            + encodingStrings.Length + " encoding(s) for patchLevel=" + patchLevel);

        FieldEncodingColumn.ItemsSource = encodingStrings;
        OptionalFieldEncodingColumn.ItemsSource = encodingStrings;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PopulatePatchLevelDropdown
    //
    // Reads the distinct (patch_date, server_type) pairs from PatchOpcode, builds a PatchLevel
    // for each, and populates the patch level combobox.  PatchLevel.ToString supplies the
    // display text.  Selects the current patch level from GlassContext as the initial value.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PopulatePatchLevelDropdown()
    {
        List<PatchLevel> patchLevels = new List<PatchLevel>();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT patch_date, server_type"
            + " FROM PatchOpcode"
            + " ORDER BY patch_date, server_type";

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string patchDate = reader.GetString(0);
            string serverType = reader.GetString(1);
            PatchLevel patchLevel = new PatchLevel(patchDate, serverType);
            patchLevels.Add(patchLevel);
        }

        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.PopulatePatchLevelDropdown: "
            + patchLevels.Count + " patch level(s) loaded");

        PatchLevelComboBox.ItemsSource = patchLevels;

        PatchLevel currentPatchLevel = GlassContext.CurrentPatchLevel;
        PatchLevelComboBox.SelectedItem = currentPatchLevel;

        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.PopulatePatchLevelDropdown: "
            + "default selection set to " + currentPatchLevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PopulateOpcodeDropdown
    //
    // Reads the opcode names from PatchOpcode for the given patch level, sorted, and populates
    // the opcode combobox.
    //
    // patchLevel:  The patch level whose opcode names to load.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PopulateOpcodeDropdown(PatchLevel patchLevel)
    {
        List<string> opcodeNames = new List<string>();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT opcode_name"
            + " FROM PatchOpcode"
            + " WHERE patch_date = @patchDate"
            + " AND server_type = @serverType"
            + " ORDER BY opcode_name";
        cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string opcodeName = reader.GetString(0);
            opcodeNames.Add(opcodeName);
        }

        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.PopulateOpcodeDropdown: "
            + opcodeNames.Count + " opcode(s) for patchLevel=" + patchLevel);

        OpcodeComboBox.ItemsSource = opcodeNames;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PopulateVersionDropdown
    //
    // Reads the id and version columns from PatchOpcode for the given (patchLevel, opcodeName)
    // and binds them to the version combobox as VersionDropdownItem instances.  If exactly one
    // version exists, selects it so the form populates immediately; otherwise the user picks.
    //
    // patchLevel:  The patch level whose versions to load.
    // opcodeName:  The opcode_name column value.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PopulateVersionDropdown(PatchLevel patchLevel, string opcodeName)
    {
        List<VersionDropdownItem> items = new List<VersionDropdownItem>();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, version"
            + " FROM PatchOpcode"
            + " WHERE patch_date = @patchDate"
            + " AND server_type = @serverType"
            + " AND opcode_name = @opcodeName"
            + " ORDER BY version";
        cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
        cmd.Parameters.AddWithValue("@opcodeName", opcodeName);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            VersionDropdownItem item = new VersionDropdownItem();
            item.PatchOpcodeKey = (uint) reader.GetInt32(0);
            item.Version = (uint) reader.GetInt32(1);
            items.Add(item);
        }

        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.PopulateVersionDropdown: "
            + items.Count + " version(s) for " + patchLevel + " " + opcodeName);

        VersionComboBox.ItemsSource = items;

        if (items.Count > 0)
        {
            VersionComboBox.SelectedIndex = 0;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadOpcodeForm
    //
    // Reads the PatchOpcode row for the given (patchLevel, opcodeName, version) triple and
    // populates the opcode form's name, hex, byte length, version, and key textboxes.  Hex is
    // displayed as "0xNNNN".  Byte length is the empty string when the column is null.  Key
    // is the database primary key, displayed read-only for diagnostic purposes.
    //
    // patchLevel:  The patch level whose opcode to load.
    // opcodeName:  The opcode_name column value.
    // version:     The version column value.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadOpcodeForm(PatchLevel patchLevel, string opcodeName, uint version)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, opcode_name, opcode_value, byte_length, version"
            + " FROM PatchOpcode"
            + " WHERE patch_date = @patchDate"
            + " AND server_type = @serverType"
            + " AND opcode_name = @opcodeName"
            + " AND version = @version";
        cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
        cmd.Parameters.AddWithValue("@opcodeName", opcodeName);
        cmd.Parameters.AddWithValue("@version", version);

        using SqliteDataReader reader = cmd.ExecuteReader();
        if (reader.Read() == false)
        {
            DebugLog.Write(LogChannel.Fields, "PatchDataEditor.LoadOpcodeForm: no PatchOpcode row "
                + "for " + patchLevel + " " + opcodeName + " v" + version);
            return;
        }

        uint loadedKey = (uint)reader.GetInt32(0);
        OpcodeKeyTextBox.Text = loadedKey.ToString();

        OpcodeNameTextBox.Text = reader.GetString(1);
        ushort opcodeValue = (ushort)reader.GetInt32(2);
        OpcodeHexTextBox.Text = "0x" + opcodeValue.ToString("x4");

        if (reader.IsDBNull(3) == true)
        {
            OpcodeByteLengthTextBox.Text = string.Empty;
        }
        else
        {
            int byteLength = reader.GetInt32(3);
            OpcodeByteLengthTextBox.Text = byteLength.ToString();
        }

        uint loadedVersion = (uint)reader.GetInt32(4);
        OpcodeVersionTextBox.Text = loadedVersion.ToString();

        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.LoadOpcodeForm: loaded " + patchLevel
            + " " + opcodeName + " v" + version + " key=" + loadedKey
            + " hex=" + OpcodeHexTextBox.Text
            + " byteLength=" + OpcodeByteLengthTextBox.Text);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadFieldsTable
    //
    // Reads the PacketField rows for the given (patchLevel, opcodeName, version) triple,
    // joining through PatchOpcode to find the parent row's id.  Builds a DataTable, populates
    // it from the reader, sets its primary key on the id column so RowState tracking works,
    // and binds it to FieldsGrid.
    //
    // patchLevel:  The patch level whose fields to load.
    // opcodeName:  The opcode_name column value.
    // version:     The version column value.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadFieldsTable(PatchLevel patchLevel, string opcodeName, uint version)
    {
        DataTable table = CreateFieldsTable();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT pf.id, pf.patch_opcode_id, pf.field_name, pf.bit_offset,"
            + " pf.bit_length, pf.encoding, pf.divisor"
            + " FROM PacketField pf"
            + " JOIN PatchOpcode po ON pf.patch_opcode_id = po.id"
            + " WHERE po.patch_date = @patchDate"
            + " AND po.server_type = @serverType"
            + " AND po.opcode_name = @opcodeName"
            + " AND po.version = @version"
            + " ORDER BY pf.bit_offset";
        cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
        cmd.Parameters.AddWithValue("@opcodeName", opcodeName);
        cmd.Parameters.AddWithValue("@version", version);

        using SqliteDataReader reader = cmd.ExecuteReader();
        table.Load(reader);
        table.AcceptChanges();

        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.LoadFieldsTable: " + table.Rows.Count
            + " field(s) for " + patchLevel + " " + opcodeName + " v" + version);

        _fieldsTable = table;
        FieldsGrid.ItemsSource = table.DefaultView;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CreateFieldsTable
    //
    // Builds the in-memory DataTable that backs FieldsGrid.  The id column is configured for
    // auto-increment with a negative seed so rows added through the grid receive unique
    // synthetic ids that cannot collide with database-assigned ids loaded from PacketField.
    // The id column is not bound in the grid; it exists only so loaded rows can be matched
    // back to their database row at update or delete time.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private DataTable CreateFieldsTable()
    {
        DataTable table = new DataTable();
        DataColumn idColumn = table.Columns.Add("id", typeof(int));
        idColumn.AutoIncrement = true;
        idColumn.AutoIncrementSeed = -1;
        idColumn.AutoIncrementStep = -1;
        table.Columns.Add("patch_opcode_id", typeof(int));
        table.Columns.Add("field_name", typeof(string));
        table.Columns.Add("bit_offset", typeof(int));
        table.Columns.Add("bit_length", typeof(int));
        table.Columns.Add("encoding", typeof(string));
        table.Columns.Add("divisor", typeof(double));
        return table;
    }
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadOptionalGroupsTable
    //
    // Reads the PacketOptionalGroup rows for the given (patchLevel, opcodeName, version)
    // triple, joining through PatchOpcode to find the parent row's id.  Builds a DataTable,
    // populates it from the reader, sets its primary key on the id column so RowState
    // tracking works, and binds it to OptionalGroupsGrid.
    //
    // patchLevel:  The patch level whose optional groups to load.
    // opcodeName:  The opcode_name column value.
    // version:     The version column value.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadOptionalGroupsTable(PatchLevel patchLevel, string opcodeName, uint version)
    {
        DataTable table = CreateOptionalGroupsTable();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT pog.id, pog.patch_opcode_id, pog.bit_offset,"
            + " pog.flags_bit_length, pog.flag_field_name"
            + " FROM PacketOptionalGroup pog"
            + " JOIN PatchOpcode po ON pog.patch_opcode_id = po.id"
            + " WHERE po.patch_date = @patchDate"
            + " AND po.server_type = @serverType"
            + " AND po.opcode_name = @opcodeName"
            + " AND po.version = @version"
            + " ORDER BY pog.bit_offset";
        cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
        cmd.Parameters.AddWithValue("@opcodeName", opcodeName);
        cmd.Parameters.AddWithValue("@version", version);

        using SqliteDataReader reader = cmd.ExecuteReader();
        table.Load(reader);

        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.LoadOptionalGroupsTable: "
            + table.Rows.Count + " group(s) for " + patchLevel + " " + opcodeName + " v" + version);

        _optionalGroupsTable = table;
        OptionalGroupsGrid.ItemsSource = table.DefaultView;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CreateOptionalGroupsTable
    //
    // Builds an empty DataTable with the PacketOptionalGroup schema and primary key.  Used by
    // LoadOptionalGroupsTable to populate from the database, and by AddOpcodeButton_Click to
    // seed an empty grid the user can type rows into.
    //
    // Returns the empty DataTable.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private DataTable CreateOptionalGroupsTable()
    {
        DataTable table = new DataTable();
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("patch_opcode_id", typeof(int));
        table.Columns.Add("bit_offset", typeof(int));
        table.Columns.Add("flags_bit_length", typeof(int));
        table.Columns.Add("flag_field_name", typeof(string));
        return table;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadOptionalFieldsTable
    //
    // Reads the PacketOptionalField rows for the given group id, builds a DataTable, populates
    // it from the reader, sets its primary key on the id column so RowState tracking works,
    // and binds it to OptionalFieldsGrid.
    //
    // groupId:  The PacketOptionalGroup row id whose fields to load.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadOptionalFieldsTable(int groupId)
    {
        DataTable table = CreateOptionalFieldsTable();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, group_id, field_name, flag_mask, sequence_order,"
            + " bit_length, encoding, divisor"
            + " FROM PacketOptionalField"
            + " WHERE group_id = @groupId"
            + " ORDER BY sequence_order";
        cmd.Parameters.AddWithValue("@groupId", groupId);

        using SqliteDataReader reader = cmd.ExecuteReader();
        table.Load(reader);

        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.LoadOptionalFieldsTable: "
            + table.Rows.Count + " field(s) for groupId=" + groupId);

        _optionalFieldsTable = table;
        OptionalFieldsGrid.ItemsSource = table.DefaultView;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CreateOptionalFieldsTable
    //
    // Builds an empty DataTable with the PacketOptionalField schema and primary key.  Used by
    // LoadOptionalFieldsTable to populate from the database, and by AddOpcodeButton_Click to
    // seed an empty grid the user can type rows into.
    //
    // Returns the empty DataTable.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private DataTable CreateOptionalFieldsTable()
    {
        DataTable table = new DataTable();
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("group_id", typeof(int));
        table.Columns.Add("field_name", typeof(string));
        table.Columns.Add("flag_mask", typeof(int));
        table.Columns.Add("sequence_order", typeof(int));
        table.Columns.Add("bit_length", typeof(int));
        table.Columns.Add("encoding", typeof(string));
        table.Columns.Add("divisor", typeof(double));
        return table;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SaveButton_Click
    //
    // Persists pending changes to the database inside a single transaction.  Either every
    // change commits or none does.
    //
    // When _patchOpcodeKey is 0, the form represents a new opcode that has not yet been
    // inserted; InsertOpcodeRow runs first and captures the new key so the grid saves can
    // reference it as their foreign key.  Otherwise SaveOpcodeForm updates the existing row.
    //
    // After a successful commit the three grid tables are reloaded from the database so they
    // pick up newly assigned primary keys, and the opcode dropdown is refreshed so a newly
    // inserted opcode shows up.
    //
    // sender:  The Save button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (PatchLevelComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchDataEditor.SaveButton_Click: "
                + "no patch level selected, ignoring");
            return;
        }

        PatchLevel patchLevel = (PatchLevel)PatchLevelComboBox.SelectedItem;
        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.SaveButton_Click: starting save");
        using SqliteTransaction tx = _connection.BeginTransaction();

        try
        {
            if (_patchOpcodeKey == 0)
            {
                InsertOpcodeRow(tx, patchLevel);
            }
            else
            {
                SaveOpcodeForm(tx);
            }

            SaveFieldsTable(tx);
            SaveOptionalGroupsTable(tx);
            SaveOptionalFieldsTable(tx);

            tx.Commit();
            DebugLog.Write(LogChannel.Fields, "PatchDataEditor.SaveButton_Click: committed");
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DebugLog.Write(LogChannel.Fields, "PatchDataEditor.SaveButton_Click: rolled back, "
                + ex.GetType().Name + ": " + ex.Message);
            MessageBox.Show(this, "Save failed: " + ex.Message, "Save Failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string opcodeName = OpcodeNameTextBox.Text.Trim();
        uint version = uint.Parse(OpcodeVersionTextBox.Text.Trim());

        PopulateOpcodeDropdown(patchLevel);
        OpcodeComboBox.SelectedItem = opcodeName;

        if (VersionComboBox.ItemsSource is List<VersionDropdownItem> items)
        {
            foreach (VersionDropdownItem item in items)
            {
                if (item.Version == version)
                {
                    VersionComboBox.SelectedItem = item;
                    break;
                }
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CloseButton_Click
    //
    // Closes the editor window.  Pending unsaved edits are discarded; the user is expected to
    // click Save first if they want changes persisted.
    //
    // sender:  The Close button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.CloseButton_Click: closing");
        Close();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AddOpcodeButton_Click
    //
    // Clears the opcode form and version dropdown, seeds the three grids with empty
    // DataTables, and sets _patchOpcodeKey to 0 so SaveButton_Click knows to insert a new
    // PatchOpcode row instead of updating an existing one.  No database write happens here —
    // the insert is deferred to Save, which inserts the PatchOpcode row first and uses its
    // new id as the foreign key for the field, group, and optional-field rows the user has
    // added in the grids.
    //
    // sender:  The Add Opcode button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void AddOpcodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (PatchLevelComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchDataEditor.AddOpcodeButton_Click: "
                + "no patch level selected, ignoring");
            return;
        }

        OpcodeComboBox.SelectedItem = null;
        VersionComboBox.ItemsSource = null;

        OpcodeNameTextBox.Text = string.Empty;
        OpcodeHexTextBox.Text = string.Empty;
        OpcodeByteLengthTextBox.Text = string.Empty;
        OpcodeKeyTextBox.Text = string.Empty;

        _patchOpcodeKey = 0;

        _fieldsTable = CreateFieldsTable();
        _optionalGroupsTable = CreateOptionalGroupsTable();
        _optionalFieldsTable = CreateOptionalFieldsTable();

        FieldsGrid.ItemsSource = _fieldsTable.DefaultView;
        OptionalGroupsGrid.ItemsSource = _optionalGroupsTable.DefaultView;
        OptionalFieldsGrid.ItemsSource = _optionalFieldsTable.DefaultView;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // InsertOpcodeRow
    //
    // Inserts a new PatchOpcode row at the given patch level using the values in the opcode
    // form (name, hex, byte length, version).  Captures the new id into _patchOpcodeKey so
    // subsequent grid saves use it as the foreign key.
    //
    // tx:          The transaction within which the insert runs.
    // patchLevel:  The patch level the new opcode belongs to.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void InsertOpcodeRow(SqliteTransaction tx, PatchLevel patchLevel)
    {
        string opcodeName = OpcodeNameTextBox.Text.Trim();

        string hexText = OpcodeHexTextBox.Text.Trim();
        if (hexText.StartsWith("0x", StringComparison.OrdinalIgnoreCase) == true)
        {
            hexText = hexText.Substring(2);
        }
        ushort opcodeValue = Convert.ToUInt16(hexText, 16);

        string byteLengthText = OpcodeByteLengthTextBox.Text.Trim();
        object byteLengthParam;
        if (string.IsNullOrEmpty(byteLengthText) == true)
        {
            byteLengthParam = DBNull.Value;
        }
        else
        {
            byteLengthParam = int.Parse(byteLengthText);
        }

        string versionText = OpcodeVersionTextBox.Text.Trim();
        uint version = uint.Parse(versionText);

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO PatchOpcode"
            + " (patch_date, server_type, opcode_value, opcode_name, version, byte_length)"
            + " VALUES (@patchDate, @serverType, @opcodeValue, @opcodeName, @version, @byteLength);"
            + " SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
        cmd.Parameters.AddWithValue("@opcodeValue", (int)opcodeValue);
        cmd.Parameters.AddWithValue("@opcodeName", opcodeName);
        cmd.Parameters.AddWithValue("@version", version);
        cmd.Parameters.AddWithValue("@byteLength", byteLengthParam);

        object? scalar = cmd.ExecuteScalar();
        _patchOpcodeKey = Convert.ToUInt32(scalar);

        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.InsertOpcodeRow: inserted "
            + "PatchOpcode id=" + _patchOpcodeKey + " " + patchLevel + " " + opcodeName
            + " v" + version + " opcode_value=0x" + opcodeValue.ToString("x4")
            + " byte_length=" + byteLengthText);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SaveOpcodeForm
    //
    // Updates the existing PatchOpcode row at _patchOpcodeKey with the values currently in
    // the opcode form (name, hex, byte length, version).  Called from SaveButton_Click when
    // the form represents an existing opcode rather than a new one.
    //
    // tx:  The transaction within which the update runs.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void SaveOpcodeForm(SqliteTransaction tx)
    {
        string opcodeName = OpcodeNameTextBox.Text.Trim();

        string hexText = OpcodeHexTextBox.Text.Trim();
        if (hexText.StartsWith("0x", StringComparison.OrdinalIgnoreCase) == true)
        {
            hexText = hexText.Substring(2);
        }
        ushort opcodeValue = Convert.ToUInt16(hexText, 16);

        string byteLengthText = OpcodeByteLengthTextBox.Text.Trim();
        object byteLengthParam;
        if (string.IsNullOrEmpty(byteLengthText) == true)
        {
            byteLengthParam = DBNull.Value;
        }
        else
        {
            byteLengthParam = int.Parse(byteLengthText);
        }

        string versionText = OpcodeVersionTextBox.Text.Trim();
        uint version = uint.Parse(versionText);

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE PatchOpcode"
            + " SET opcode_name = @opcodeName,"
            + "     opcode_value = @opcodeValue,"
            + "     byte_length = @byteLength,"
            + "     version = @version"
            + " WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", _patchOpcodeKey);
        cmd.Parameters.AddWithValue("@opcodeName", opcodeName);
        cmd.Parameters.AddWithValue("@opcodeValue", (int)opcodeValue);
        cmd.Parameters.AddWithValue("@byteLength", byteLengthParam);
        cmd.Parameters.AddWithValue("@version", version);
        cmd.ExecuteNonQuery();

        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.SaveOpcodeForm: updated "
            + "PatchOpcode id=" + _patchOpcodeKey + " name=" + opcodeName
            + " opcode_value=0x" + opcodeValue.ToString("x4")
            + " byte_length=" + byteLengthText + " version=" + version);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AddFieldButton_Click
    //
    // Adds a new empty row to _fieldsTable.  The grid refreshes automatically because it is
    // bound to _fieldsTable.DefaultView.  The new row's id stays at 0 (the DataTable's default
    // for the unset primary key); SaveFieldsTable detects RowState.Added and INSERTs.  The
    // patch_opcode_id foreign key is injected at save time from _patchOpcodeKey.  Requires
    // that an opcode is loaded (i.e. _fieldsTable is not null).
    //
    // sender:  The Add Field button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void AddFieldButton_Click(object sender, RoutedEventArgs e)
    {
        if (_fieldsTable == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchDataEditor.AddFieldButton_Click: "
                + "no opcode loaded, ignoring");
            return;
        }

        DataRow newRow = _fieldsTable.NewRow();
        newRow["field_name"] = string.Empty;
        newRow["bit_offset"] = 0;
        newRow["bit_length"] = 0;
        newRow["encoding"] = string.Empty;
        newRow["divisor"] = 1.0;

        _fieldsTable.Rows.Add(newRow);

        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.AddFieldButton_Click: "
            + "added new field row, total now " + _fieldsTable.Rows.Count);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // DeleteFieldButton_Click
    //
    // Deletes the currently selected field row from the in-memory fields table.
    // The WPF new-row placeholder is selectable when CanUserAddRows is enabled but
    // is not a DataRowView, so it is filtered out here.
    //
    // sender:  The Delete button that raised the event.
    // e:       Routed event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void DeleteFieldButton_Click(object sender, RoutedEventArgs e)
    {
        if (_fieldsTable == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchDataEditor.DeleteFieldButton_Click: "
                + "no opcode loaded, ignoring");
            return;
        }
        if (FieldsGrid.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchDataEditor.DeleteFieldButton_Click: "
                + "no row selected, ignoring");
            return;
        }
        DataRowView selected = FieldsGrid.SelectedItem as DataRowView;
        if (selected == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchDataEditor.DeleteFieldButton_Click: "
                + "placeholder row selected, ignoring");
            return;
        }
        selected.Row.Delete();
        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.DeleteFieldButton_Click: "
            + "deleted row, table now has " + _fieldsTable.Rows.Count + " row(s)");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SaveFieldsTable
    //
    // Walks the changed rows in _fieldsTable and issues INSERT/UPDATE/DELETE statements
    // according to each row's RowState.  No-op when the table is null or has no changes.
    //
    // tx:  The transaction within which the writes run.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void SaveFieldsTable(SqliteTransaction tx)
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
                using SqliteCommand cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO PacketField"
                    + " (patch_opcode_id, field_name, bit_offset, bit_length, encoding, divisor)"
                    + " VALUES (@patchOpcodeId, @fieldName, @bitOffset, @bitLength, @encoding,"
                    + " @divisor);"
                    + " SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@patchOpcodeId", _patchOpcodeKey);
                cmd.Parameters.AddWithValue("@fieldName", row["field_name"]);
                cmd.Parameters.AddWithValue("@bitOffset", row["bit_offset"]);
                cmd.Parameters.AddWithValue("@bitLength", row["bit_length"]);
                cmd.Parameters.AddWithValue("@encoding", row["encoding"]);
                cmd.Parameters.AddWithValue("@divisor", row["divisor"]);

                object? scalar = cmd.ExecuteScalar();

                inserted = inserted + 1;
            }
            else if (row.RowState == DataRowState.Modified)
            {
                using SqliteCommand cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE PacketField"
                    + " SET field_name = @fieldName,"
                    + "     bit_offset = @bitOffset,"
                    + "     bit_length = @bitLength,"
                    + "     encoding = @encoding,"
                    + "     divisor = @divisor"
                    + " WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", row["id"]);
                cmd.Parameters.AddWithValue("@fieldName", row["field_name"]);
                cmd.Parameters.AddWithValue("@bitOffset", row["bit_offset"]);
                cmd.Parameters.AddWithValue("@bitLength", row["bit_length"]);
                cmd.Parameters.AddWithValue("@encoding", row["encoding"]);
                cmd.Parameters.AddWithValue("@divisor", row["divisor"]);
                cmd.ExecuteNonQuery();
                updated = updated + 1;
            }
        }

        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.SaveFieldsTable: "
            + "inserted=" + inserted + " updated=" + updated + " deleted=" + deleted);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SaveOptionalGroupsTable
    //
    // Walks the changed rows in _optionalGroupsTable and issues INSERT/UPDATE/DELETE statements
    // according to each row's RowState.  No-op when the table is null or has no changes.
    //
    // tx:  The transaction within which the writes run.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void SaveOptionalGroupsTable(SqliteTransaction tx)
    {
        if (_optionalGroupsTable == null)
        {
            return;
        }

        DataTable? changes = _optionalGroupsTable.GetChanges();
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
                cmd.CommandText = "DELETE FROM PacketOptionalGroup WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                deleted = deleted + 1;
            }
            else if (row.RowState == DataRowState.Added)
            {
                using SqliteCommand cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO PacketOptionalGroup"
                    + " (patch_opcode_id, bit_offset, flags_bit_length, flag_field_name)"
                    + " VALUES (@patchOpcodeId, @bitOffset, @flagsBitLength, @flagFieldName);"
                    + " SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@patchOpcodeId", _patchOpcodeKey);
                cmd.Parameters.AddWithValue("@bitOffset", row["bit_offset"]);
                cmd.Parameters.AddWithValue("@flagsBitLength", row["flags_bit_length"]);
                cmd.Parameters.AddWithValue("@flagFieldName", row["flag_field_name"]);

                object? scalar = cmd.ExecuteScalar();

                inserted = inserted + 1;
            }
            else if (row.RowState == DataRowState.Modified)
            {
                using SqliteCommand cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE PacketOptionalGroup"
                    + " SET bit_offset = @bitOffset,"
                    + "     flags_bit_length = @flagsBitLength,"
                    + "     flag_field_name = @flagFieldName"
                    + " WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", row["id"]);
                cmd.Parameters.AddWithValue("@bitOffset", row["bit_offset"]);
                cmd.Parameters.AddWithValue("@flagsBitLength", row["flags_bit_length"]);
                cmd.Parameters.AddWithValue("@flagFieldName", row["flag_field_name"]);
                cmd.ExecuteNonQuery();
                updated = updated + 1;
            }
        }

        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.SaveOptionalGroupsTable: "
            + "inserted=" + inserted + " updated=" + updated + " deleted=" + deleted);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SaveOptionalFieldsTable
    //
    // Walks the changed rows in _optionalFieldsTable and issues INSERT/UPDATE/DELETE statements
    // according to each row's RowState.  No-op when the table is null or has no changes.  The
    // group_id foreign key on Added rows comes from the currently selected row in
    // OptionalGroupsGrid; if no group is selected when a row is added, the save fails on the
    // NOT NULL constraint, and the transaction rolls back.
    //
    // tx:  The transaction within which the writes run.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void SaveOptionalFieldsTable(SqliteTransaction tx)
    {
        if (_optionalFieldsTable == null)
        {
            return;
        }

        DataTable? changes = _optionalFieldsTable.GetChanges();
        if (changes == null)
        {
            return;
        }

        uint groupKey = 0;
        if (OptionalGroupsGrid.SelectedItem != null)
        {
            DataRowView selectedGroup = (DataRowView)OptionalGroupsGrid.SelectedItem;
            groupKey = Convert.ToUInt32(selectedGroup["id"]);
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
                cmd.CommandText = "DELETE FROM PacketOptionalField WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                deleted = deleted + 1;
            }
            else if (row.RowState == DataRowState.Added)
            {
                using SqliteCommand cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO PacketOptionalField"
                    + " (group_id, field_name, flag_mask, sequence_order, bit_length, encoding,"
                    + " divisor)"
                    + " VALUES (@groupId, @fieldName, @flagMask, @sequenceOrder, @bitLength,"
                    + " @encoding, @divisor);"
                    + " SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@groupId", groupKey);
                cmd.Parameters.AddWithValue("@fieldName", row["field_name"]);
                cmd.Parameters.AddWithValue("@flagMask", row["flag_mask"]);
                cmd.Parameters.AddWithValue("@sequenceOrder", row["sequence_order"]);
                cmd.Parameters.AddWithValue("@bitLength", row["bit_length"]);
                cmd.Parameters.AddWithValue("@encoding", row["encoding"]);
                cmd.Parameters.AddWithValue("@divisor", row["divisor"]);

                object? scalar = cmd.ExecuteScalar();

                inserted = inserted + 1;
            }
            else if (row.RowState == DataRowState.Modified)
            {
                using SqliteCommand cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE PacketOptionalField"
                    + " SET field_name = @fieldName,"
                    + "     flag_mask = @flagMask,"
                    + "     sequence_order = @sequenceOrder,"
                    + "     bit_length = @bitLength,"
                    + "     encoding = @encoding,"
                    + "     divisor = @divisor"
                    + " WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", row["id"]);
                cmd.Parameters.AddWithValue("@fieldName", row["field_name"]);
                cmd.Parameters.AddWithValue("@flagMask", row["flag_mask"]);
                cmd.Parameters.AddWithValue("@sequenceOrder", row["sequence_order"]);
                cmd.Parameters.AddWithValue("@bitLength", row["bit_length"]);
                cmd.Parameters.AddWithValue("@encoding", row["encoding"]);
                cmd.Parameters.AddWithValue("@divisor", row["divisor"]);
                cmd.ExecuteNonQuery();
                updated = updated + 1;
            }
        }

        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.SaveOptionalFieldsTable: "
            + "inserted=" + inserted + " updated=" + updated + " deleted=" + deleted);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PatchLevelComboBox_SelectionChanged
    //
    // Fires when the patch level selection changes.  Loads the list of opcode names for the
    // selected patch into the opcode dropdown and clears the version dropdown and the three
    // grids, since the previously selected opcode (if any) belongs to a different patch.
    //
    // sender:  The patch level combobox.
    // e:       Standard selection-changed event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PatchLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OpcodeComboBox.ItemsSource = null;
        VersionComboBox.ItemsSource = null;
        FieldsGrid.ItemsSource = null;
        OptionalGroupsGrid.ItemsSource = null;
        OptionalFieldsGrid.ItemsSource = null;

        if (PatchLevelComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchDataEditor.PatchLevelComboBox_SelectionChanged: "
                + "no selection");
            return;
        }

        PatchLevel patchLevel = (PatchLevel)PatchLevelComboBox.SelectedItem;
        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.PatchLevelComboBox_SelectionChanged: "
            + "selected " + patchLevel);

        PopulateOpcodeDropdown(patchLevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeComboBox_SelectionChanged
    //
    // Fires when the opcode name selection changes.  Loads the list of versions for the
    // selected (patchLevel, opcodeName) into the version dropdown.  Clears the version
    // dropdown and the three grids when the opcode selection clears.
    //
    // sender:  The opcode combobox.
    // e:       Standard selection-changed event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void OpcodeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        VersionComboBox.ItemsSource = null;
        FieldsGrid.ItemsSource = null;
        OptionalGroupsGrid.ItemsSource = null;
        OptionalFieldsGrid.ItemsSource = null;

        if (OpcodeComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchDataEditor.OpcodeComboBox_SelectionChanged: "
                + "no selection");
            return;
        }

        if (PatchLevelComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchDataEditor.OpcodeComboBox_SelectionChanged: "
                + "opcode selected but no patch level, ignoring");
            return;
        }

        PatchLevel patchLevel = (PatchLevel)PatchLevelComboBox.SelectedItem;
        string opcodeName = (string)OpcodeComboBox.SelectedItem;

        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.OpcodeComboBox_SelectionChanged: "
            + "patchLevel=" + patchLevel + " opcodeName=" + opcodeName);

        PopulateVersionDropdown(patchLevel, opcodeName);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // VersionComboBox_SelectionChanged
    //
    // Fires when the version selection changes.  Caches the PatchOpcode primary key from the
    // selected VersionDropdownItem and loads the opcode form, the fields grid, and the
    // optional groups grid for the (patchLevel, opcodeName, version) triple.
    //
    // sender:  The version combobox.
    // e:       Standard selection-changed event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void VersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        FieldsGrid.ItemsSource = null;
        OptionalGroupsGrid.ItemsSource = null;
        OptionalFieldsGrid.ItemsSource = null;
        OpcodeHexTextBox.Text = string.Empty;
        OpcodeByteLengthTextBox.Text = string.Empty;
        _patchOpcodeKey = 0;

        if (VersionComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchDataEditor.VersionComboBox_SelectionChanged: "
                + "no selection");
            return;
        }

        if (PatchLevelComboBox.SelectedItem == null || OpcodeComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchDataEditor.VersionComboBox_SelectionChanged: "
                + "version selected but missing patch level or opcode name, ignoring");
            return;
        }

        PatchLevel patchLevel = (PatchLevel)PatchLevelComboBox.SelectedItem;
        string opcodeName = (string)OpcodeComboBox.SelectedItem;
        VersionDropdownItem item = (VersionDropdownItem)VersionComboBox.SelectedItem;
        uint version = item.Version;
        _patchOpcodeKey = item.PatchOpcodeKey;

        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.VersionComboBox_SelectionChanged: "
            + "patchLevel=" + patchLevel + " opcodeName=" + opcodeName + " version=" + version
            + " patchOpcodeKey=" + _patchOpcodeKey);

        LoadOpcodeForm(patchLevel, opcodeName, version);
        LoadFieldsTable(patchLevel, opcodeName, version);
        LoadOptionalGroupsTable(patchLevel, opcodeName, version);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OptionalGroupsGrid_SelectionChanged
    //
    // Fires when the optional group selection changes.  Loads the PacketOptionalField rows
    // belonging to the selected group into OptionalFieldsGrid.  Clears the optional fields
    // grid when no group is selected.
    //
    // sender:  The optional groups DataGrid.
    // e:       Standard selection-changed event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void OptionalGroupsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OptionalFieldsGrid.ItemsSource = null;

        if (OptionalGroupsGrid.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchDataEditor.OptionalGroupsGrid_SelectionChanged: "
                + "no selection");
            return;
        }

        DataRowView rowView = (DataRowView)OptionalGroupsGrid.SelectedItem;
        int groupId = (int)rowView["id"];

        DebugLog.Write(LogChannel.Fields, "PatchDataEditor.OptionalGroupsGrid_SelectionChanged: "
            + "selected group id=" + groupId);

        LoadOptionalFieldsTable(groupId);
    }



    ///////////////////////////////////////////////////////////////////////////////////////////////
    // VersionDropdownItem
    //
    // Item bound to the version combobox.  Carries the PatchOpcode primary key alongside the
    // version number.  ToString returns just the version so the combobox displays "1", "2",
    // etc., while the handler reads PatchOpcodeKey directly off the selected item.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private class VersionDropdownItem
    {
        public uint PatchOpcodeKey { get; set; }
        public uint Version { get; set; }

        public override string ToString()
        {
            return Version.ToString();
        }
    }
}