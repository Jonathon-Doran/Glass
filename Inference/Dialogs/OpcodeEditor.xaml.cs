using Glass.Core;
using Glass.Core.Logging;
using Glass.Data;
using Glass.Network.Protocol;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.Windows;
using System.Windows.Controls;

namespace Inference.Dialogs;

///////////////////////////////////////////////////////////////////////////////////////////////
// OpcodeEditor
//
// Modeless editor for the PatchOpcode table.  A patch level, opcode, and version are selected;
// the opcode's name, hex value, byte length, version, and gate are edited in a form.  The gate
// is the name of a Gate row, chosen from the patch's gates or typed freely.  Reads and writes via
// direct SQL through the Database singleton.
///////////////////////////////////////////////////////////////////////////////////////////////
public partial class OpcodeEditor : Window
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _connection
    //
    // Database connection held for the lifetime of the editor window.  Opened in the
    // constructor, used by every helper that reads or writes the PatchOpcode and Gate tables,
    // and disposed when the window closes.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private readonly SqliteConnection _connection;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _patchOpcodeKey
    //
    // PatchOpcode.id (database primary key) of the currently displayed opcode.  Cached when the
    // version selection commits, used as the row key when saving an existing opcode.  Zero when
    // the form represents a new opcode or a new version not yet inserted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private uint _patchOpcodeKey;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeDropdownItem
    //
    // Item bound to the opcode combobox.  Carries the opcode_name for real rows.  When IsSentinel
    // is true the item represents the "(New opcode...)" entry: OpcodeName is empty and ToString
    // returns the sentinel label so the combobox displays it in place of a name.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private class OpcodeDropdownItem
    {
        public string OpcodeName { get; set; } = string.Empty;
        public bool IsSentinel { get; set; }

        public override string ToString()
        {
            if (IsSentinel == true)
            {
                return "(New opcode...)";
            }

            return OpcodeName;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // VersionDropdownItem
    //
    // Item bound to the version combobox.  Carries the PatchOpcode primary key alongside the
    // version number for real rows.  When IsSentinel is true the item represents the
    // "(New version...)" entry: PatchOpcodeKey is 0, Version is 0, and ToString returns the
    // sentinel label so the combobox displays it in place of a number.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private class VersionDropdownItem
    {
        public uint PatchOpcodeKey { get; set; }
        public uint Version { get; set; }
        public bool IsSentinel { get; set; }

        public override string ToString()
        {
            if (IsSentinel == true)
            {
                return "(New version...)";
            }

            return Version.ToString();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeEditor (constructor)
    //
    // Initializes the window, opens the database connection held for the window's lifetime, and
    // populates the patch level dropdown.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeEditor()
    {
        InitializeComponent();

        OpcodeNameTextBox.Text = string.Empty;
        OpcodeHexTextBox.Text = string.Empty;
        OpcodeByteLengthTextBox.Text = string.Empty;
        OpcodeVersionTextBox.Text = string.Empty;
        OpcodeKeyTextBox.Text = string.Empty;
        GateComboBox.Text = string.Empty;

        _connection = Database.Instance.Connect();
        _connection.Open();

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
        DebugLog.Write(LogChannel.Fields, "OpcodeEditor.OnClosed: disposing connection");

        _connection.Dispose();

        base.OnClosed(e);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PopulatePatchLevelDropdown
    //
    // Reads the distinct (patch_date, server_type) pairs from PatchOpcode, builds a PatchLevel for
    // each, and populates the patch level combobox.  PatchLevel.ToString supplies the display
    // text.  Selects the current patch level from GlassContext as the initial value.
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

        DebugLog.Write(LogChannel.Fields, "OpcodeEditor.PopulatePatchLevelDropdown: "
            + patchLevels.Count + " patch level(s) loaded");

        PatchLevelComboBox.ItemsSource = patchLevels;

        PatchLevel currentPatchLevel = GlassContext.CurrentPatchLevel;
        PatchLevelComboBox.SelectedItem = currentPatchLevel;

        DebugLog.Write(LogChannel.Fields, "OpcodeEditor.PopulatePatchLevelDropdown: "
            + "default selection set to " + currentPatchLevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PopulateOpcodeDropdown
    //
    // Reads the opcode names from PatchOpcode for the given patch level, sorted, and binds them to
    // the opcode combobox as OpcodeDropdownItem instances.  Appends a sentinel item labeled
    // "(New opcode...)" so the user can choose to create a new opcode row.
    //
    // patchLevel:  The patch level whose opcode names to load.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PopulateOpcodeDropdown(PatchLevel patchLevel)
    {
        List<OpcodeDropdownItem> items = new List<OpcodeDropdownItem>();

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
            OpcodeDropdownItem item = new OpcodeDropdownItem();
            item.OpcodeName = reader.GetString(0);
            item.IsSentinel = false;
            items.Add(item);
        }

        OpcodeDropdownItem sentinel = new OpcodeDropdownItem();
        sentinel.OpcodeName = string.Empty;
        sentinel.IsSentinel = true;
        items.Add(sentinel);

        DebugLog.Write(LogChannel.Fields, "OpcodeEditor.PopulateOpcodeDropdown: "
            + (items.Count - 1) + " opcode(s) plus sentinel for patchLevel=" + patchLevel);

        OpcodeComboBox.ItemsSource = items;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PopulateVersionDropdown
    //
    // Reads the id and version columns from PatchOpcode for the given (patchLevel, opcodeName) and
    // binds them to the version combobox as VersionDropdownItem instances.  Appends a sentinel
    // item labeled "(New version...)" with PatchOpcodeKey = 0 so the user can choose to create a
    // new version row from the currently displayed one.  If exactly one real version exists,
    // selects it so the form populates immediately; otherwise the user picks.
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
            item.PatchOpcodeKey = (uint)reader.GetInt32(0);
            item.Version = (uint)reader.GetInt32(1);
            item.IsSentinel = false;
            items.Add(item);
        }

        VersionDropdownItem sentinel = new VersionDropdownItem();
        sentinel.PatchOpcodeKey = 0;
        sentinel.Version = 0;
        sentinel.IsSentinel = true;
        items.Add(sentinel);

        DebugLog.Write(LogChannel.Fields, "OpcodeEditor.PopulateVersionDropdown: "
            + (items.Count - 1) + " version(s) plus sentinel for " + patchLevel + " " + opcodeName);

        VersionComboBox.ItemsSource = items;

        if (items.Count > 1)
        {
            VersionComboBox.SelectedIndex = 0;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PopulateGateDropdown
    //
    // Reads the Gate names for the given patch level, sorted, and binds them to the gate combobox
    // as its dropdown list.  The combobox is editable, so a name typed that is not in the list is
    // still accepted; the list is a convenience for picking an existing gate.  Sets only the items,
    // not the text, so a loaded opcode's gate value is left untouched.
    //
    // patchLevel:  The patch level whose gate names to load.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PopulateGateDropdown(PatchLevel patchLevel)
    {
        List<string> names = new List<string>();

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
                names.Add(reader.GetString(0));
            }
        }

        DebugLog.Write(LogChannel.Fields, "OpcodeEditor.PopulateGateDropdown: "
            + names.Count + " gate(s) for patchLevel=" + patchLevel);

        GateComboBox.ItemsSource = names;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadOpcodeForm
    //
    // Reads the PatchOpcode row for the given (patchLevel, opcodeName, version) triple and
    // populates the opcode form's name, hex, byte length, version, key, and gate textboxes.  Hex
    // is displayed as "0xNNNN".  Byte length is the empty string when the column is null.  Key is
    // the database primary key, displayed read-only for diagnostic purposes.  Gate is the
    // gate_name column value.
    //
    // patchLevel:  The patch level whose opcode to load.
    // opcodeName:  The opcode_name column value.
    // version:     The version column value.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadOpcodeForm(PatchLevel patchLevel, string opcodeName, uint version)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, opcode_name, opcode_value, byte_length, version, gate_name"
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
            DebugLog.Write(LogChannel.Fields, "OpcodeEditor.LoadOpcodeForm: no PatchOpcode row "
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

        GateComboBox.Text = reader.GetString(5);

        DebugLog.Write(LogChannel.Fields, "OpcodeEditor.LoadOpcodeForm: loaded " + patchLevel
            + " " + opcodeName + " v" + version + " key=" + loadedKey
            + " hex=" + OpcodeHexTextBox.Text
            + " byteLength=" + OpcodeByteLengthTextBox.Text
            + " gate='" + GateComboBox.Text + "'");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PatchLevelComboBox_SelectionChanged
    //
    // Fires when the patch level selection changes.  Loads the opcode names and gate names for the
    // selected patch and clears the version dropdown and the opcode form, since the previously
    // selected opcode (if any) belongs to a different patch.
    //
    // sender:  The patch level combobox.
    // e:       Standard selection-changed event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PatchLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OpcodeComboBox.ItemsSource = null;
        VersionComboBox.ItemsSource = null;
        GateComboBox.Text = string.Empty;

        if (PatchLevelComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "OpcodeEditor.PatchLevelComboBox_SelectionChanged: "
                + "no selection");
            return;
        }

        PatchLevel patchLevel = (PatchLevel)PatchLevelComboBox.SelectedItem;
        DebugLog.Write(LogChannel.Fields, "OpcodeEditor.PatchLevelComboBox_SelectionChanged: "
            + "selected " + patchLevel);

        PopulateOpcodeDropdown(patchLevel);
        PopulateGateDropdown(patchLevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeComboBox_SelectionChanged
    //
    // Fires when the opcode selection changes.  For a real opcode item, loads the list of versions
    // for the selected (patchLevel, opcodeName) into the version dropdown.  For the sentinel item,
    // blanks the opcode form, sets _patchOpcodeKey to 0 so Save will INSERT a new PatchOpcode row,
    // and focuses the name textbox.
    //
    // sender:  The opcode combobox.
    // e:       Standard selection-changed event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void OpcodeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        VersionComboBox.ItemsSource = null;

        if (OpcodeComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "OpcodeEditor.OpcodeComboBox_SelectionChanged: "
                + "no selection");
            return;
        }

        if (PatchLevelComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "OpcodeEditor.OpcodeComboBox_SelectionChanged: "
                + "opcode selected but no patch level, ignoring");
            return;
        }

        PatchLevel patchLevel = (PatchLevel)PatchLevelComboBox.SelectedItem;
        OpcodeDropdownItem item = (OpcodeDropdownItem)OpcodeComboBox.SelectedItem;

        if (item.IsSentinel == true)
        {
            _patchOpcodeKey = 0;

            OpcodeNameTextBox.Text = string.Empty;
            OpcodeHexTextBox.Text = string.Empty;
            OpcodeByteLengthTextBox.Text = string.Empty;
            OpcodeVersionTextBox.Text = string.Empty;
            OpcodeKeyTextBox.Text = string.Empty;
            GateComboBox.Text = string.Empty;

            OpcodeNameTextBox.Focus();

            DebugLog.Write(LogChannel.Fields, "OpcodeEditor.OpcodeComboBox_SelectionChanged: "
                + "sentinel selected, armed for new opcode insert, form cleared");
            return;
        }

        DebugLog.Write(LogChannel.Fields, "OpcodeEditor.OpcodeComboBox_SelectionChanged: "
            + "patchLevel=" + patchLevel + " opcodeName=" + item.OpcodeName);

        PopulateVersionDropdown(patchLevel, item.OpcodeName);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // VersionComboBox_SelectionChanged
    //
    // Fires when the version selection changes.  For a real version item, caches the PatchOpcode
    // primary key from the selected VersionDropdownItem and loads the opcode form for the
    // (patchLevel, opcodeName, version) triple.
    //
    // For the sentinel item, leaves the form populated with whatever version was previously
    // displayed so the user can use it as a starting point, sets _patchOpcodeKey to 0 so Save will
    // INSERT a new PatchOpcode row, clears the version textbox, and gives it focus so the user can
    // type the new version number.
    //
    // sender:  The version combobox.
    // e:       Standard selection-changed event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void VersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VersionComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "OpcodeEditor.VersionComboBox_SelectionChanged: "
                + "no selection");
            return;
        }

        if (PatchLevelComboBox.SelectedItem == null || OpcodeComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "OpcodeEditor.VersionComboBox_SelectionChanged: "
                + "version selected but missing patch level or opcode name, ignoring");
            return;
        }

        VersionDropdownItem item = (VersionDropdownItem)VersionComboBox.SelectedItem;

        if (item.IsSentinel == true)
        {
            _patchOpcodeKey = 0;
            OpcodeVersionTextBox.Text = string.Empty;

            OpcodeVersionTextBox.Focus();

            DebugLog.Write(LogChannel.Fields, "OpcodeEditor.VersionComboBox_SelectionChanged: "
                + "sentinel selected, armed for new version insert");
            return;
        }

        OpcodeHexTextBox.Text = string.Empty;
        OpcodeByteLengthTextBox.Text = string.Empty;
        _patchOpcodeKey = 0;

        PatchLevel patchLevel = (PatchLevel)PatchLevelComboBox.SelectedItem;
        OpcodeDropdownItem opcodeItem = (OpcodeDropdownItem)OpcodeComboBox.SelectedItem;
        string opcodeName = opcodeItem.OpcodeName;
        uint version = item.Version;
        _patchOpcodeKey = item.PatchOpcodeKey;

        DebugLog.Write(LogChannel.Fields, "OpcodeEditor.VersionComboBox_SelectionChanged: "
            + "patchLevel=" + patchLevel + " opcodeName=" + opcodeName + " version=" + version
            + " patchOpcodeKey=" + _patchOpcodeKey);

        LoadOpcodeForm(patchLevel, opcodeName, version);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ClearButton_Click
    //
    // Resets the editor to the empty state for entering a new opcode.  Clears the opcode and
    // version selections, blanks the form textboxes and the gate, and sets _patchOpcodeKey to 0.
    // No-op when no patch level is selected.
    //
    // sender:  The Clear button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (PatchLevelComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "OpcodeEditor.ClearButton_Click: "
                + "no patch level selected, ignoring");
            return;
        }

        OpcodeComboBox.SelectedItem = null;
        VersionComboBox.ItemsSource = null;

        OpcodeNameTextBox.Text = string.Empty;
        OpcodeHexTextBox.Text = string.Empty;
        OpcodeByteLengthTextBox.Text = string.Empty;
        OpcodeKeyTextBox.Text = string.Empty;
        GateComboBox.Text = string.Empty;

        _patchOpcodeKey = 0;

        DebugLog.Write(LogChannel.Fields, "OpcodeEditor.ClearButton_Click: cleared to empty state");
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
        DebugLog.Write(LogChannel.Fields, "OpcodeEditor.CloseButton_Click: closing");
        Close();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SaveButton_Click
    //
    // Persists the opcode form to the database inside a single transaction.  When _patchOpcodeKey
    // is 0 the form represents a new opcode or a new version and InsertOpcodeRow runs; otherwise
    // SaveOpcodeForm updates the existing row.  A SQL error rolls the transaction back and reports
    // the message, leaving the database unchanged.  After a successful commit the opcode dropdown
    // is refreshed, the saved opcode reselected by matching its name, and the saved version
    // reselected by matching its number.
    //
    // sender:  The Save button.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (PatchLevelComboBox.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.Fields, "OpcodeEditor.SaveButton_Click: "
                + "no patch level selected, ignoring");
            return;
        }

        PatchLevel patchLevel = (PatchLevel)PatchLevelComboBox.SelectedItem;
        DebugLog.Write(LogChannel.Fields, "OpcodeEditor.SaveButton_Click: starting save");
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

            tx.Commit();
            DebugLog.Write(LogChannel.Fields, "OpcodeEditor.SaveButton_Click: committed");
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DebugLog.Write(LogChannel.Fields, "OpcodeEditor.SaveButton_Click: rolled back, "
                + ex.GetType().Name + ": " + ex.Message);
            MessageBox.Show(this, "Save failed: " + ex.Message, "Save Failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string opcodeName = OpcodeNameTextBox.Text.Trim();
        uint version = uint.Parse(OpcodeVersionTextBox.Text.Trim());

        PopulateOpcodeDropdown(patchLevel);

        if (OpcodeComboBox.ItemsSource is List<OpcodeDropdownItem> opcodeItems)
        {
            foreach (OpcodeDropdownItem opcodeItem in opcodeItems)
            {
                if (opcodeItem.IsSentinel == false && opcodeItem.OpcodeName == opcodeName)
                {
                    OpcodeComboBox.SelectedItem = opcodeItem;
                    break;
                }
            }
        }

        if (VersionComboBox.ItemsSource is List<VersionDropdownItem> versionItems)
        {
            foreach (VersionDropdownItem versionItem in versionItems)
            {
                if (versionItem.IsSentinel == false && versionItem.Version == version)
                {
                    VersionComboBox.SelectedItem = versionItem;
                    break;
                }
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // InsertOpcodeRow
    //
    // Inserts a new PatchOpcode row at the given patch level using the values in the opcode form
    // (name, hex, byte length, version, gate).  Captures the new id into _patchOpcodeKey.
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

        string gateName = GateComboBox.Text.Trim();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO PatchOpcode"
            + " (patch_date, server_type, opcode_value, opcode_name, version, byte_length, gate_name)"
            + " VALUES (@patchDate, @serverType, @opcodeValue, @opcodeName, @version, @byteLength, @gateName);"
            + " SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@patchDate", patchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", patchLevel.ServerType);
        cmd.Parameters.AddWithValue("@opcodeValue", (int)opcodeValue);
        cmd.Parameters.AddWithValue("@opcodeName", opcodeName);
        cmd.Parameters.AddWithValue("@version", version);
        cmd.Parameters.AddWithValue("@byteLength", byteLengthParam);
        cmd.Parameters.AddWithValue("@gateName", gateName);

        object? scalar = cmd.ExecuteScalar();
        _patchOpcodeKey = Convert.ToUInt32(scalar);

        DebugLog.Write(LogChannel.Fields, "OpcodeEditor.InsertOpcodeRow: inserted "
            + "PatchOpcode id=" + _patchOpcodeKey + " " + patchLevel + " " + opcodeName
            + " v" + version + " opcode_value=0x" + opcodeValue.ToString("x4")
            + " byte_length=" + byteLengthText + " gate='" + gateName + "'");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SaveOpcodeForm
    //
    // Updates the existing PatchOpcode row at _patchOpcodeKey with the values currently in the
    // opcode form (name, hex, byte length, version, gate).  Called from SaveButton_Click when the
    // form represents an existing opcode rather than a new one.
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

        string gateName = GateComboBox.Text.Trim();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE PatchOpcode"
            + " SET opcode_name = @opcodeName,"
            + "     opcode_value = @opcodeValue,"
            + "     byte_length = @byteLength,"
            + "     version = @version,"
            + "     gate_name = @gateName"
            + " WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", _patchOpcodeKey);
        cmd.Parameters.AddWithValue("@opcodeName", opcodeName);
        cmd.Parameters.AddWithValue("@opcodeValue", (int)opcodeValue);
        cmd.Parameters.AddWithValue("@byteLength", byteLengthParam);
        cmd.Parameters.AddWithValue("@version", version);
        cmd.Parameters.AddWithValue("@gateName", gateName);
        cmd.ExecuteNonQuery();

        DebugLog.Write(LogChannel.Fields, "OpcodeEditor.SaveOpcodeForm: updated "
            + "PatchOpcode id=" + _patchOpcodeKey + " name=" + opcodeName
            + " opcode_value=0x" + opcodeValue.ToString("x4")
            + " byte_length=" + byteLengthText + " version=" + version
            + " gate='" + gateName + "'");
    }
}