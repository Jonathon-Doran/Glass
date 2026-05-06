using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Capture;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Inference.Core;
using Inference.Dialogs;
using Inference.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using static Glass.Network.Protocol.SoeConstants;

namespace Inference;

///////////////////////////////////////////////////////////////////////////////////////////////
// MainWindow
//
// Main window for the Inference tool.
///////////////////////////////////////////////////////////////////////////////////////////////
public partial class MainWindow : Window
{
    private struct HexDumpByte
    {
        public byte Value;
        public bool IsConstant;
    }

    private struct HexDumpLine
    {
        public string Offset;
        public HexDumpByte[] Bytes;
    }

    private struct HexDumpSample
    {
        public string Header;
        public List<HexDumpLine> Lines;
    }

    private PatchLevel? _currentPatchLevel = null;

    private bool _hasUnsavedChanges = false;
    private readonly Stack<object> _undoStack = new Stack<object>();
    private SessionDemux? _sessionDemux;
    private PacketCapture? _packetCapture;
    private readonly ObservableCollection<OpcodeEntry> _opcodeEntries;
    private readonly Dictionary<uint, OpcodeEntry> _opcodeLookup;
    private Dictionary<uint, string> _patchOpcodes;
    private readonly List<CapturedPacket> _capturedPackets = new List<CapturedPacket>();
    private readonly object _payloadLock;

    // analysis packet filtering fields
    private SoeConstants.StreamId? _analysisFilterChannel;
    private int? _analysisFilterSessionId;
    private int _analysisMaxPackets = 20;
    private int _analysisMaxHexBytes = 256;


    ///////////////////////////////////////////////////////////////////////////////////////////
    // MainWindow
    //
    // Constructs the main window and initializes the XAML-defined components.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public MainWindow()
    {
        InitializeComponent();

        GlassContext.ProfileManager = new ProfileManager();
        InitializeLogging();
        DebugLog.Write(LogChannel.InferenceDebug, "Inference application started");
        DebugLog.Write(LogChannel.Inference, "Inference log initialized");

        _opcodeEntries = new ObservableCollection<OpcodeEntry>();
        _opcodeLookup = new Dictionary<uint, OpcodeEntry>();
        OpcodeGrid.ItemsSource = _opcodeEntries;

        DebugLog.Write(LogChannel.InferenceDebug, "MainWindow: OpcodeGrid bound to collection");

        _patchOpcodes = new Dictionary<uint, string>();
        _payloadLock = new object();

        InitializeAnalysisFilters();
        AddDummyCandidates();
        InitializePipes();
        GlassContext.FocusTracker = new FocusTracker();

        OpenDatabase();
        BuildRecentPatchesMenu();
        RestoreLastPatchLevel();
        GlassContext.PatchRegistry = new PatchRegistry(_currentPatchLevel!.Value);
        UpdateControlStates();
    }

    private void InitializeLogging()
    {
        GlassDebugLogHandler glassDebugLogHandler = new GlassDebugLogHandler("glass.log");
        DebugLog.AddHandler(LogSink.GlassDebugLogfile, glassDebugLogHandler);

        DebugLog.Route(LogChannel.General, LogSink.GlassDebugLogfile);
        DebugLog.Route(LogChannel.ISXGlass, LogSink.GlassDebugLogfile);
        DebugLog.Route(LogChannel.Pipes, LogSink.GlassDebugLogfile);

        DebugLog.Route(LogChannel.Video, LogSink.GlassDebugLogfile);
        DebugLog.Route(LogChannel.Sessions, LogSink.GlassDebugLogfile);
        DebugLog.Route(LogChannel.Profiles, LogSink.GlassDebugLogfile);
        DebugLog.Route(LogChannel.Input, LogSink.GlassDebugLogfile);
        DebugLog.Route(LogChannel.Database, LogSink.GlassDebugLogfile);
        DebugLog.Route(LogChannel.LowNetwork, LogSink.GlassDebugLogfile);
        DebugLog.Route(LogChannel.Network, LogSink.GlassDebugLogfile);

        // The inference debug log.  Debug messages for the inference app
        GlassDebugLogHandler debugLogHandler = new GlassDebugLogHandler("debug.log");
        DebugLog.AddHandler(LogSink.InferenceDebugLogfile, debugLogHandler);
        DebugLog.Route(LogChannel.InferenceDebug, LogSink.InferenceDebugLogfile);

        // The debug tab, just debug messages for inference
        GlassConsoleLogHandler debugTabHandler = new GlassConsoleLogHandler(DebugLogOutput, DebugLogScroller);
        DebugLog.AddHandler(LogSink.InferenceDebugTab, debugTabHandler);
        DebugLog.Route(LogChannel.InferenceDebug, LogSink.InferenceDebugTab);

        // The inference logfile.  Just inference messages
        GlassDebugLogHandler inferenceLogHandler = new GlassDebugLogHandler("inference.log");
        DebugLog.AddHandler(LogSink.InferenceLogfile, inferenceLogHandler);
        DebugLog.Route(LogChannel.Inference, LogSink.InferenceLogfile);
        DebugLog.Route(LogChannel.Opcodes, LogSink.InferenceLogfile);

        GlassDebugLogHandler fieldsLogHandler = new GlassDebugLogHandler("fields.log");
        DebugLog.AddHandler(LogSink.Aux1LogFile, fieldsLogHandler);
        DebugLog.Route(LogChannel.Fields, LogSink.Aux1LogFile);

        GlassDebugLogHandler lowNetLogHandler = new GlassDebugLogHandler("lowNet.log");
        DebugLog.AddHandler(LogSink.Aux2LogFile, lowNetLogHandler);
        DebugLog.Route(LogChannel.LowNetwork, LogSink.Aux2LogFile);

        // The inference tab, just inference messages
        GlassConsoleLogHandler inferenceTabHandler = new GlassConsoleLogHandler(InferenceLogOutput, InferenceLogScroller);
        DebugLog.AddHandler(LogSink.InferenceTab, inferenceTabHandler);
        DebugLog.Route(LogChannel.Inference, LogSink.InferenceTab);
    }

    private void AddDummyCandidates()
    {
        // ----- Dummy candidates for UI review (remove when real analysis is wired) -----
        ObservableCollection<AnalysisCandidate> dummyCandidates
            = new ObservableCollection<AnalysisCandidate>();

        dummyCandidates.Add(new AnalysisCandidate
        {
            Name = "OP_PlayerProfile",
            Confidence = "High",
            Evidence = "Size 23471, seen once per zone-in, contains character name at offset 904"
        });

        dummyCandidates.Add(new AnalysisCandidate
        {
            Name = "OP_ZoneEntry",
            Confidence = "Medium",
            Evidence = "Size 1204, seen once per zone-in, direction Z2C, precedes spawn burst"
        });

        CandidateGrid.ItemsSource = dummyCandidates;
        DebugLog.Write(LogChannel.InferenceDebug, "MainWindow: loaded 2 dummy candidates for UI review");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RestoreLastPatchLevel
    //
    // Reads the LastOpenedPatchDate and LastOpenedPatchServerType settings and
    // restores the working patch level if both are present.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void RestoreLastPatchLevel()
    {
        string? savedPatchDate = Properties.Settings.Default.LastOpenedPatchDate;
        string? savedServerType = Properties.Settings.Default.LastOpenedPatchServerType;
        if (string.IsNullOrEmpty(savedPatchDate) || string.IsNullOrEmpty(savedServerType))
        {
            DebugLog.Write(LogChannel.InferenceDebug, "RestoreLastPatchLevel: no previous patch level found");
            return;
        }

        _currentPatchLevel = new PatchLevel(savedPatchDate, savedServerType);
        _patchOpcodes = LoadPatchOpcodes(savedPatchDate, savedServerType);

        string displayServerType = savedServerType.Substring(0, 1).ToUpper() + savedServerType.Substring(1);
        StatusPatchLevel.Text = savedPatchDate + " (" + displayServerType + ")";
        UpdateRecentPatches(savedPatchDate, savedServerType);
        BuildRecentPatchesMenu();
        DebugLog.Write(LogChannel.InferenceDebug, "RestoreLastPatchLevel: restored "
            + savedServerType + " " + savedPatchDate);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Window_Closing
    //
    // Handles the window closing event. Shuts down logging.
    //
    // sender:  The window being closed.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "Inference application closing");
      //  InferenceLog.Shutdown();
       // InferenceDebugLog.Shutdown();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // InitializePipes
    //
    // Creates and starts the named pipe connections to ISXGlass and GlassVideo.
    // Wires up status update handlers for the status bar.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void InitializePipes()
    {
        GlassContext.ISXGlassPipe = new PipeManager("ISXGlass", "ISXGlass_Commands", "ISXGlass_Notify");
        GlassContext.ISXGlassPipe.Connected += () => Dispatcher.Invoke(() =>
        {
            StatusIsx.Text = "ISX: Connected";
            DebugLog.Write(LogChannel.InferenceDebug, "ISXGlass pipe connected");
        });
        GlassContext.ISXGlassPipe.Disconnected += () => Dispatcher.Invoke(() =>
        {
            StatusIsx.Text = "ISX: Disconnected";
            DebugLog.Write(LogChannel.InferenceDebug, "ISXGlass pipe disconnected");
        });
        GlassContext.ISXGlassPipe.MessageReceived += msg => Dispatcher.Invoke(() => HandleISXGlassMessage(msg));
        GlassContext.ISXGlassPipe.Start();

        GlassContext.GlassVideoPipe = new PipeManager("GlassVideo", "GlassVideo_Cmd", "GlassVideo_Notify");
        GlassContext.GlassVideoPipe.Connected += () => Dispatcher.Invoke(() =>
        {
            StatusGlassVideo.Text = "GlassVideo: Connected";
            DebugLog.Write(LogChannel.InferenceDebug, "GlassVideo pipe connected");
        });
        GlassContext.GlassVideoPipe.Disconnected += () => Dispatcher.Invoke(() =>
        {
            StatusGlassVideo.Text = "GlassVideo: Not Running";
            DebugLog.Write(LogChannel.InferenceDebug, "GlassVideo pipe disconnected");
        });
        GlassContext.GlassVideoPipe.MessageReceived += msg => Dispatcher.Invoke(() =>
        {
            DebugLog.Write(LogChannel.InferenceDebug, "GlassVideo message: " + msg);
        });
        GlassContext.GlassVideoPipe.Start();

        DebugLog.Write(LogChannel.InferenceDebug, "InitializePipes: pipes started");
        
        DebugLog.Write(LogChannel.InferenceDebug, "InitializePipes: session registry and focus tracker initialized");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // InitializeAnalysisFilters
    //
    // Populates the Session and Channel filter dropdowns on the Analysis tab
    // with initial values.  Session is populated dynamically as clients connect.
    // Channel is populated with the four stream types plus an "All" option.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void InitializeAnalysisFilters()
    {
        ComboBoxItem allChannels = new ComboBoxItem();
        allChannels.Content = "All";
        allChannels.Tag = null;
        AnalysisChannelFilter.Items.Add(allChannels);

        ComboBoxItem c2w = new ComboBoxItem();
        c2w.Content = "Client -> World";
        c2w.Tag = SoeConstants.StreamId.StreamClientToWorld;
        AnalysisChannelFilter.Items.Add(c2w);

        ComboBoxItem w2c = new ComboBoxItem();
        w2c.Content = "World -> Client";
        w2c.Tag = SoeConstants.StreamId.StreamWorldToClient;
        AnalysisChannelFilter.Items.Add(w2c);

        ComboBoxItem c2z = new ComboBoxItem();
        c2z.Content = "Client -> Zone";
        c2z.Tag = SoeConstants.StreamId.StreamClientToZone;
        AnalysisChannelFilter.Items.Add(c2z);

        ComboBoxItem z2c = new ComboBoxItem();
        z2c.Content = "Zone -> Client";
        z2c.Tag = SoeConstants.StreamId.StreamZoneToClient;
        AnalysisChannelFilter.Items.Add(z2c);

        AnalysisChannelFilter.SelectedIndex = 0;

        DebugLog.Write(LogChannel.InferenceDebug, "InitializeAnalysisFilters: channel filter populated with 5 entries");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // UpdateControlStates
    //
    // Evaluates the current application state and enables or disables controls
    // according to context rules. Called whenever state changes that could affect
    // control availability.
    //
    // Rules:
    //   Launch Profile:  enabled when a patch level is loaded
    //   Save:            enabled when a patch level is loaded and unsaved changes exist
    //   Undo:            enabled when the undo stack is not empty
    //   Analyze:         enabled when a patch level is loaded and an opcode row is selected
    //   Accept:          enabled when a candidate row is selected
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void UpdateControlStates()
    {
        bool hasPatchLevel = (_currentPatchLevel != null);
        bool hasOpcodeSelected = OpcodeGrid.SelectedItem != null;
        bool hasCandidateSelected = CandidateGrid.SelectedItem != null;
        bool hasUndoHistory = _undoStack.Count > 0;

        MenuProfile.IsEnabled = hasPatchLevel;
        MenuSave.IsEnabled = hasPatchLevel && _hasUnsavedChanges;
        MenuUndo.IsEnabled = hasUndoHistory;
        ButtonAnalyze.IsEnabled = hasPatchLevel && hasOpcodeSelected;
        ToggleAccept.IsEnabled = hasCandidateSelected;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OpenDatabase
    //
    // Opens the Glass database at its default path. The database contains the
    // PatchOpcode, PacketField, and related tables used by inference.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void OpenDatabase()
    {
        string dbPath = Glass.Data.Database.DefaultPath;
        if (!System.IO.File.Exists(dbPath))
        {
            DebugLog.Write(LogChannel.InferenceDebug, "OpenDatabase: database not found at " + dbPath);
            return;
        }

        Glass.Data.Database.Open(dbPath);
        DebugLog.Write(LogChannel.InferenceDebug, "OpenDatabase: opened " + dbPath);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LoadPatchOpcodes
    //
    // Loads all PatchOpcode rows for the given patch level and returns a mapping of
    // opcode_value to opcode_name for identifying opcodes in the packet grid.
    //
    // When multiple versions of an opcode share an opcode_value (as variants of the
    // same logical opcode like OP_Tracking v1/v2/v3), they collapse to a single
    // dictionary entry with the shared opcode_name. The grid will display that name
    // for any matching packet without distinguishing which version it is; callers
    // needing version precision must query PatchOpcode differently.
    //
    // patchDate:  Patch date string, e.g. "2026-04-15".
    // serverType: "live" or "test".
    // Returns:    Dictionary of opcode_value to opcode_name.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private Dictionary<uint, string> LoadPatchOpcodes(string patchDate, string serverType)
    {
        Dictionary<uint, string> result = new Dictionary<uint, string>();
        using (SqliteConnection connection = Glass.Data.Database.Instance.Connect())
        {
            connection.Open();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT opcode_value, opcode_name, version FROM PatchOpcode "
                    + "WHERE patch_date = @patchDate AND server_type = @serverType";
                command.Parameters.AddWithValue("@patchDate", patchDate);
                command.Parameters.AddWithValue("@serverType", serverType);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int opcodeValue = reader.GetInt32(0);
                        string opcodeName = reader.GetString(1);
                        int version = reader.GetInt32(2);
                        result[(uint)opcodeValue] = opcodeName;
                        DebugLog.Write(LogChannel.InferenceDebug, "LoadPatchOpcodes: loaded 0x"
                            + opcodeValue.ToString("x4") + " " + opcodeName
                            + " version=" + version);
                    }
                }
            }
        }
        DebugLog.Write(LogChannel.InferenceDebug, "LoadPatchOpcodes: loaded "
            + result.Count + " distinct opcodes");
        return result;
    }

    private void UpdateRecentPatches(string patchDate, string serverType)
    {
        string displayServerType = serverType.Substring(0, 1).ToUpper() + serverType.Substring(1);
        string entry = patchDate + " (" + displayServerType + ")";
        if (Properties.Settings.Default.RecentPatches == null)
        {
            Properties.Settings.Default.RecentPatches = new System.Collections.Specialized.StringCollection();
        }
        if (Properties.Settings.Default.RecentPatches.Contains(entry))
        {
            Properties.Settings.Default.RecentPatches.Remove(entry);
        }
        while (Properties.Settings.Default.RecentPatches.Count >= 5)
        {
            Properties.Settings.Default.RecentPatches.RemoveAt(Properties.Settings.Default.RecentPatches.Count - 1);
        }
        Properties.Settings.Default.RecentPatches.Insert(0, entry);
        Properties.Settings.Default.Save();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BuildRecentPatchesMenu
    //
    // Rebuilds the Recent Patches section of the File menu by reading the
    // RecentPatches setting and validating each entry against the database.
    // An entry is valid only if at least one PatchOpcode row exists for that
    // patch_date and server_type combination. Invalid entries (no opcodes in
    // the database, or malformed strings) are pruned from the setting.
    //
    // Valid entries are inserted as MenuItems into the File menu immediately
    // after MenuOpenPatchLevel, preceded by a Separator. Entries appear in
    // setting order, which is most-recent first (index 0 is most-recent).
    // If no entries are valid, nothing is inserted and no Separator appears.
    //
    // Previously inserted Recent items and their Separator are removed before
    // rebuilding, so this method is safe to call repeatedly.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void BuildRecentPatchesMenu()
    {
        MenuItem fileMenu = (MenuItem)MenuOpenPatchLevel.Parent;

        // Remove previously inserted Recent items and their separator.
        // This is to handle the case where patch levels are modified during the run
        for (int i = fileMenu.Items.Count - 1; i >= 0; i--)
        {
            object item = fileMenu.Items[i];
            if (item is MenuItem menuItem && menuItem.Tag is string tag && tag == "RecentPatch")
            {
                fileMenu.Items.RemoveAt(i);
            }
            else if (item is Separator separator && separator.Tag is string sepTag && sepTag == "RecentPatch")
            {
                fileMenu.Items.RemoveAt(i);
            }
        }

        if (Properties.Settings.Default.RecentPatches == null
            || Properties.Settings.Default.RecentPatches.Count == 0)
        {
            return;
        }

        // Validate each entry against the database. Walk forward through the
        // setting, collecting valid entries and preserving their order.
        // Prune invalid or malformed entries.
        List<string> validEntries = new List<string>();
        bool pruned = false;
        using (SqliteConnection connection = Glass.Data.Database.Instance.Connect())
        {
            connection.Open();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT 1 FROM PatchOpcode WHERE patch_date = @patchDate"
                    + " AND server_type = @serverType LIMIT 1";
                SqliteParameter paramPatchDate = command.Parameters.Add("@patchDate", SqliteType.Text);
                SqliteParameter paramServerType = command.Parameters.Add("@serverType", SqliteType.Text);
                for (int i = 0; i < Properties.Settings.Default.RecentPatches.Count; i++)
                {
                    string? entry = Properties.Settings.Default.RecentPatches[i];
                    if (entry == null)
                    {
                        Properties.Settings.Default.RecentPatches.RemoveAt(i);
                        i--;
                        pruned = true;
                        continue;
                    }
                    string? patchDate = ParseRecentPatchDate(entry);
                    string? serverType = ParseRecentServerType(entry);
                    if (patchDate == null || serverType == null)
                    {
                        Properties.Settings.Default.RecentPatches.RemoveAt(i);
                        i--;
                        pruned = true;
                        continue;
                    }
                    paramPatchDate.Value = patchDate;
                    paramServerType.Value = serverType;
                    object? result = command.ExecuteScalar();
                    if (result != null)
                    {
                        validEntries.Add(entry);
                    }
                    else
                    {
                        Properties.Settings.Default.RecentPatches.RemoveAt(i);
                        i--;
                        pruned = true;
                    }
                }
            }
        }

        if (pruned)
        {
            Properties.Settings.Default.Save();
        }

        if (validEntries.Count == 0)
        {
            return;
        }

        // Insert into the menu in setting order (most-recent first).
        // Each item is inserted at an incrementing position after
        // MenuOpenPatchLevel, so the menu matches the setting order.
        int insertIndex = fileMenu.Items.IndexOf(MenuOpenPatchLevel) + 1;

        Separator recentSeparator = new Separator();
        recentSeparator.Tag = "RecentPatch";
        fileMenu.Items.Insert(insertIndex, recentSeparator);
        insertIndex++;

        foreach (string entry in validEntries)
        {
            string? patchDate = ParseRecentPatchDate(entry);
            string? serverType = ParseRecentServerType(entry);
            MenuItem recentItem = new MenuItem();
            recentItem.Header = entry;
            recentItem.Tag = "RecentPatch";
            string capturedPatchDate = patchDate!;
            string capturedServerType = serverType!;
            recentItem.Click += (object sender, RoutedEventArgs e) =>
            {
                _currentPatchLevel = new PatchLevel(capturedPatchDate, capturedServerType);
                StatusPatchLevel.Text = entry;
                Properties.Settings.Default.LastOpenedPatchDate = capturedPatchDate;
                Properties.Settings.Default.LastOpenedPatchServerType = capturedServerType;
                UpdateRecentPatches(capturedPatchDate, capturedServerType);
                BuildRecentPatchesMenu();
                UpdateControlStates();
            };
            fileMenu.Items.Insert(insertIndex, recentItem);
            insertIndex++;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ParseRecentPatchDate
    //
    // Extracts the patch date from a Recent Patches entry string.
    // Expected format: "2026-04-15 (Live)"
    //
    // entry:   The Recent Patches entry string.
    // Returns: The patch date string, or null if the format is invalid.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private string? ParseRecentPatchDate(string entry)
    {
        int spaceIndex = entry.IndexOf(' ');
        if (spaceIndex < 0)
        {
            return null;
        }
        return entry.Substring(0, spaceIndex);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ParseRecentServerType
    //
    // Extracts the server type from a Recent Patches entry string.
    // Expected format: "2026-04-15 (Live)"
    //
    // entry:   The Recent Patches entry string.
    // Returns: The server type string in lowercase (e.g. "live"), or null if invalid.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private string? ParseRecentServerType(string entry)
    {
        int openParen = entry.IndexOf('(');
        int closeParen = entry.IndexOf(')');
        if (openParen < 0 || closeParen < 0 || closeParen <= openParen + 1)
        {
            return null;
        }
        return entry.Substring(openParen + 1, closeParen - openParen - 1);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // RefreshAnalysis
    //
    // Re-runs the analysis for the currently selected opcode using the current
    // filter settings.  Called by Button_Analyze_Click and by the filter
    // selection changed handlers.  Does nothing if no opcode is selected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void RefreshAnalysis()
    {
        if (OpcodeGrid.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "RefreshAnalysis: no opcode selected, skipping");
            return;
        }

        OpcodeEntry selected = (OpcodeEntry)OpcodeGrid.SelectedItem;

        List<CapturedPacket> packets = GetFilteredPackets(
            selected.RawOpcode, _analysisFilterChannel, _analysisFilterSessionId,
            _analysisMaxPackets, _analysisMaxHexBytes);

        if (packets.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "RefreshAnalysis: no packets for "
                + selected.Opcode + " with current filters");
            return;
        }

        DebugLog.Write(LogChannel.InferenceDebug, "RefreshAnalysis: analyzing " + selected.Opcode
            + " packets=" + packets.Count
            + " channel=" + (_analysisFilterChannel?.ToString() ?? "All")
            + " session=" + (_analysisFilterSessionId?.ToString() ?? "All")
            + " maxHex=" + _analysisMaxHexBytes);

        Thread analysisThread = new Thread(() => AnalyzeOpcode(packets));
        analysisThread.IsBackground = true;
        analysisThread.Name = "AnalysisThread";
        analysisThread.Start();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeGrid_SelectionChanged
    //
    // Handles selection changes in the Opcodes data grid.
    // Updates control states to reflect whether an opcode is selected.
    //
    // sender:  The data grid that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void OpcodeGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateControlStates();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CandidateGrid_SelectionChanged
    //
    // Handles selection changes in the Candidate data grid.
    // Updates control states to reflect whether a candidate is selected.
    //
    // sender:  The data grid that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void CandidateGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateControlStates();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_NewPatchLevel_Click
    //
    // Handles the File > New Patch Level menu item click.
    // Opens the New Patch Level dialog. If the user confirms, creates a new patch
    // level entry, adds it to recent patches, and sets it as the current working
    // patch level.
    //
    // sender:  The menu item that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_NewPatchLevel_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "MenuItem_NewPatchLevel_Click");

        NewPatchLevelDialog dialog = new NewPatchLevelDialog();
        dialog.Owner = this;

        if (dialog.ShowDialog() == true)
        {
            string patchDate = dialog.PatchDate.ToString("yyyy-MM-dd");
            string serverType = dialog.ServerType;
            string entry = patchDate + " (" + serverType + ")";

            DebugLog.Write(LogChannel.InferenceDebug, "New patch level created: " + entry);

            _currentPatchLevel = new PatchLevel(patchDate, serverType);
            StatusPatchLevel.Text = patchDate + " (" + serverType.Substring(0, 1).ToUpper() + serverType.Substring(1) + ")";

            if (Properties.Settings.Default.RecentPatches == null)
            {
                Properties.Settings.Default.RecentPatches = new System.Collections.Specialized.StringCollection();
            }

            if (!Properties.Settings.Default.RecentPatches.Contains(entry))
            {
                Properties.Settings.Default.RecentPatches.Add(entry);
            }

            Properties.Settings.Default.LastOpenedPatchDate = patchDate;
            Properties.Settings.Default.LastOpenedPatchServerType = serverType;
            Properties.Settings.Default.Save();

            UpdateControlStates();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_OpenPatchLevel_Click
    //
    // Handles the File > Open Patch Level menu item click.
    // Opens the Open Patch Level dialog. If the user selects a patch level,
    // sets it as the current working patch level and saves the selection to settings.
    //
    // sender:  The menu item that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_OpenPatchLevel_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "MenuItem_OpenPatchLevel_Click");

        OpenDialog dialog = new OpenDialog();
        dialog.Owner = this;

        if (dialog.ShowDialog() == true)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "Opened patch level: ServerType="
                + dialog.ServerType + " PatchDate=" + dialog.PatchDate);

            _currentPatchLevel = new PatchLevel(dialog.PatchDate, dialog.ServerType);
            StatusPatchLevel.Text = dialog.PatchDate + " (" + dialog.ServerType.Substring(0, 1).ToUpper() + dialog.ServerType.Substring(1) + ")";

            Properties.Settings.Default.LastOpenedPatchDate = dialog.PatchDate;
            Properties.Settings.Default.LastOpenedPatchServerType = dialog.ServerType;
            Properties.Settings.Default.Save();

            UpdateRecentPatches(dialog.PatchDate, dialog.ServerType);
            BuildRecentPatchesMenu();
            UpdateControlStates();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_LaunchProfile_Click
    //
    // Handles the Profile > Launch Profile menu item click.
    // Opens the Launch Profile dialog filtered by the current patch level's
    // server type. If the user selects a profile, starts packet capture and
    // then launches the profile.
    //
    // sender:  The menu item that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private async void MenuItem_LaunchProfile_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "MenuItem_LaunchProfile_Click");

        if (_currentPatchLevel == null)
        {
            DebugLog.Write(LogChannel.General, "No patch level before launch.");
            return;
        }

        string serverType = _currentPatchLevel.Value.ServerType;

        DebugLog.Write(LogChannel.Inference, "launching profile for serverType: " + 
            serverType);
        LaunchProfileDialog dialog = new LaunchProfileDialog(serverType);

        dialog.Owner = this;

        GlassContext.SessionRegistry = new SessionRegistry(HandleAppPacket);
        GlassContext.SessionRegistry.AllSessionsDisconnected += OnAllSessionsDisconnected;

        GlassContext.FieldExtractor = new FieldExtractor();
        GlassContext.CurrentPatchLevel = _currentPatchLevel.Value;

        if (dialog.ShowDialog() == true)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "Profile selected: " + dialog.SelectedProfileName);

            string? localIp = PacketCapture.GetLocalIP();
            if (localIp == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug, "MenuItem_LaunchProfile_Click: no capture device found");
                MessageBox.Show("No suitable capture device found. Is Npcap installed?",
                    "Capture Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            // TODO:  Throttle network log
            // DebugLog.Log_Network = false;


            _sessionDemux = new SessionDemux(localIp, HandleAppPacket);
            _packetCapture = new PacketCapture(_sessionDemux);

            // string bpfFilter = "udp and (net 64.37.128.0/18 or net 69.174.192.0/19 or net 209.0.234.0/23)";
            string bpfFilter = "udp and (net 69.174.0.0/16 or net 64.37.0.0/16 or net 209.0.0.0/16)";
            if (!_packetCapture.Start(bpfFilter))
            {
                DebugLog.Write(LogChannel.InferenceDebug, "MenuItem_LaunchProfile_Click: capture failed to start");
                MessageBox.Show("Failed to start packet capture.",
                    "Capture Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            DebugLog.Write(LogChannel.InferenceDebug, "MenuItem_LaunchProfile_Click: capture started");
            StatusCapture.Text = "Capture: Active";

            await GlassContext.ProfileManager.LaunchProfile(dialog.SelectedProfileName);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnAllSessionsDisconnected
    //
    // Called when all EQ sessions have disconnected.
    // Clears the active profile and stops focus tracking.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void OnAllSessionsDisconnected()
    {
        DebugLog.Write(LogChannel.Sessions, "MainWindow.OnAllSessionsDisconnected: all sessions disconnected, clearing active profile.");
        GlassContext.ProfileManager.ClearActiveProfile();
        GlassContext.FocusTracker.Stop();
        GlassContext.FocusTracker.ClearActiveSession();
        GlassContext.GlassVideoPipe.Send("clear_all");
        OpcodeDispatch.Instance.Dispose();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_Save_Click
    //
    // Handles the File > Save menu item click.
    // Persists the current patch level state to the database.
    //
    // sender:  The menu item that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_Save_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "MenuItem_Save_Click");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_Exit_Click
    //
    // Handles the File > Exit menu item click.
    // Closes the application.
    //
    // sender:  The menu item that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_Exit_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "MenuItem_Exit_Click");
        Close();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_Undo_Click
    //
    // Handles the Edit > Undo menu item click.
    // Reverses the most recent edit operation.
    //
    // sender:  The menu item that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_Undo_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "MenuItem_Undo_Click");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_Analyze_Click
    //
    // Handles the Analyze button click. Retrieves filtered packets for the selected
    // opcode from the session buffer and launches the analysis on a background thread.
    //
    // sender:  The button that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_Analyze_Click(object sender, RoutedEventArgs e)
    {
        RefreshAnalysis();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AnalysisSessionFilter_SelectionChanged
    //
    // Handles selection changes in the Session filter dropdown on the Analysis tab.
    // Sets _analysisFilterSessionId to the selected client's local port, or null for "All".
    //
    // sender:  The ComboBox that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void AnalysisSessionFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ComboBoxItem? selected = AnalysisSessionFilter.SelectedItem as ComboBoxItem;

        if (selected == null)
        {
            _analysisFilterSessionId = null;
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisSessionFilter_SelectionChanged: no selection, filter cleared");
            RefreshAnalysis();
            return;
        }

        if (selected.Tag is int sessionId)
        {
            _analysisFilterSessionId = sessionId;
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisSessionFilter_SelectionChanged: filter set to session " + sessionId);
        }
        else
        {
            _analysisFilterSessionId = null;
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisSessionFilter_SelectionChanged: filter cleared (All)");
        }

        RefreshAnalysis();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AnalysisChannelFilter_SelectionChanged
    //
    // Handles selection changes in the Channel filter dropdown on the Analysis tab.
    // Sets _analysisFilterChannel to the selected stream type, or null for "All".
    //
    // sender:  The ComboBox that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void AnalysisChannelFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ComboBoxItem? selected = AnalysisChannelFilter.SelectedItem as ComboBoxItem;

        if (selected == null)
        {
            _analysisFilterChannel = null;
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisChannelFilter_SelectionChanged: no selection, filter cleared");
            RefreshAnalysis();
            return;
        }

        string value = selected.Content as string ?? "";

        if (selected.Tag is SoeConstants.StreamId streamId)
        {
            _analysisFilterChannel = streamId;
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisChannelFilter_SelectionChanged: filter set to " + streamId);
        }
        else
        {
            _analysisFilterChannel = null;
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisChannelFilter_SelectionChanged: filter cleared (All)");
        }
        RefreshAnalysis();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AnalysisPacketCount_SelectionChanged
    //
    // Handles selection changes in the Packets filter dropdown on the Analysis tab.
    // Sets _analysisMaxPackets to the selected value.  "All" sets int.MaxValue.
    //
    // sender:  The ComboBox that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void AnalysisPacketCount_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ComboBoxItem? selected = AnalysisPacketCount.SelectedItem as ComboBoxItem;

        if (selected == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisPacketCount_SelectionChanged: no selection");
            return;
        }

        string value = selected.Content as string ?? "";

        if (value == "All")
        {
            _analysisMaxPackets = int.MaxValue;
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisPacketCount_SelectionChanged: set to All");
        }
        else if (int.TryParse(value, out int count))
        {
            _analysisMaxPackets = count;
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisPacketCount_SelectionChanged: set to " + count);
        }
        else
        {
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisPacketCount_SelectionChanged: unexpected value '" + value + "'");
        }
        RefreshAnalysis();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AnalysisHexLength_SelectionChanged
    //
    // Handles selection changes in the Hex bytes filter dropdown on the Analysis tab.
    // Sets _analysisMaxHexBytes to the selected value.  "Full" sets int.MaxValue.
    //
    // sender:  The ComboBox that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void AnalysisHexLength_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ComboBoxItem? selected = AnalysisHexLength.SelectedItem as ComboBoxItem;

        if (selected == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisHexLength_SelectionChanged: no selection");
            return;
        }

        string value = selected.Content as string ?? "";

        if (value == "Full")
        {
            _analysisMaxHexBytes = int.MaxValue;
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisHexLength_SelectionChanged: set to Full");
        }
        else if (int.TryParse(value, out int bytes))
        {
            _analysisMaxHexBytes = bytes;
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisHexLength_SelectionChanged: set to " + bytes);
        }
        else
        {
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisHexLength_SelectionChanged: unexpected value '" + value + "'");
        }
        RefreshAnalysis();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ToggleButton_AcceptCandidate_Click
    //
    // Handles the Accept toggle button click on the Analysis tab.
    // Toggles acceptance of the selected candidate identification. When toggled on,
    // the candidate's logical name is applied to the opcode. When toggled off,
    // the identification is reverted.
    //
    // sender:  The toggle button that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ToggleButton_AcceptCandidate_Click(object sender, RoutedEventArgs e)
    {
        if (CandidateGrid.SelectedItem == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "ToggleButton_AcceptCandidate_Click: no candidate selected");
            return;
        }
        System.Windows.Controls.Primitives.ToggleButton toggle = (System.Windows.Controls.Primitives.ToggleButton)sender;
        bool isAccepted = toggle.IsChecked == true;
        DebugLog.Write(LogChannel.InferenceDebug, "ToggleButton_AcceptCandidate_Click: accepted=" + isAccepted);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeTraceList_SelectionChanged
    //
    // Selection handler for the Opcode Trace list.  Updates enabled state of the
    // Hide and Expand toolbar controls based on whether anything is selected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void OpcodeTraceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool hasSelection = OpcodeTraceList.SelectedItem != null;

        DebugLog.Write(LogChannel.Opcodes, "OpcodeTraceList_SelectionChanged: hasSelection=" + hasSelection);

        ButtonOpcodeTraceHide.IsEnabled = hasSelection;
        ToggleOpcodeTraceExpand.IsEnabled = hasSelection;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeTraceHexLength_SelectionChanged
    //
    // Hex length cap selector on the Opcode Trace toolbar.  Controls the maximum bytes
    // shown in an expanded entry's hex dump.  Stub — implementation pending hex render.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void OpcodeTraceHexLength_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ComboBoxItem? selectedItem = OpcodeTraceHexLength.SelectedItem as ComboBoxItem;
        string selection = selectedItem?.Content?.ToString() ?? "(none)";
        DebugLog.Write(LogChannel.Opcodes, "OpcodeTraceHexLength_SelectionChanged: selection=" + selection + " (stub, not yet implemented)");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Toggle_OpcodeTraceExpand_Click
    //
    // Expand/Collapse toggle on the Opcode Trace toolbar.  Toggles hex dump display
    // for the selected entry.  Stub — implementation pending expand state.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Toggle_OpcodeTraceExpand_Click(object sender, RoutedEventArgs e)
    {
        bool isChecked = ToggleOpcodeTraceExpand.IsChecked == true;
        DebugLog.Write(LogChannel.Opcodes, "Toggle_OpcodeTraceExpand_Click: expand=" + isChecked + " (stub, not yet implemented)");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ColorPatch_MouseLeftButtonUp
    //
    // Click handler shared by all color patches on the Opcode Trace toolbar.  Reads the
    // patch's Tag (an ARGB hex string) and applies the color to either the selected entry
    // or, if "Color opcode" is armed, the opcode of the selected entry.
    // Stub — implementation pending entry view model and color state.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ColorPatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        Border? patch = sender as Border;

        if (patch == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "ColorPatch_MouseLeftButtonUp: sender was not a Border, ignoring");
            return;
        }

        string? tag = patch.Tag as string;
        bool armed = ToggleOpcodeTraceColorOpcode.IsChecked == true;

        DebugLog.Write(LogChannel.Opcodes, "ColorPatch_MouseLeftButtonUp: tag='" + tag + "' armedForOpcode=" + armed + " (stub, not yet implemented)");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Button_OpcodeTraceRefresh_Click
    //
    // Refresh button for the Opcode Trace tab.  Walks Inference's CapturedPacket array,
    // decodes each packet via OpcodeDispatch.Extract, and rebuilds the list.
    // Stub — implementation pending the view model and refresh logic.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Button_OpcodeTraceRefresh_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.Opcodes, "Button_OpcodeTraceRefresh_Click: refresh requested (stub, not yet implemented)");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Button_OpcodeTraceHide_Click
    //
    // Hide button on the Opcode Trace toolbar.  Hides the opcode of the currently
    // selected entry from the list.  Stub — implementation pending hide-set state.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Button_OpcodeTraceHide_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.Opcodes, "Button_OpcodeTraceHide_Click: hide requested (stub, not yet implemented)");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Button_OpcodeTraceManage_Click
    //
    // Manage button on the Opcode Trace toolbar.  Opens the hide/unhide management
    // dialog for opcodes seen this session.  Stub — implementation pending dialog.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Button_OpcodeTraceManage_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.Opcodes, "Button_OpcodeTraceManage_Click: manage requested (stub, not yet implemented)");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AnalyzeOpcode
    //
    // Computes constant-byte analysis across the given packets and builds hex dump
    // data for display. Each packet's payload is formatted as a hex dump with
    // constant bytes highlighted. Results are dispatched to the UI thread for
    // rendering.
    //
    // packets:  List of captured packets with payloads (possibly truncated by the
    //           caller via GetFilteredPackets).
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AnalyzeOpcode(List<CapturedPacket> packets)
    {
        bool[] isConstant = ComputeConstantBytes(packets);
        List<HexDumpSample> dumpData = new List<HexDumpSample>();
        for (int packetIndex = 0; packetIndex < packets.Count; packetIndex++)
        {
            CapturedPacket packet = packets[packetIndex];
            string name = "(unknown)";

            if (packet.Metadata.SessionId >= 0)
            {
                name = "(" + GlassContext.SessionRegistry.CharacterNameFromSession(packet.Metadata.SessionId) + ")";
            }
            DebugLog.Write(LogChannel.Fields, "session " + packet.Metadata.SessionId + " is [" +
                name + "] and using metadata we get " + GlassContext.SessionRegistry.CharacterNameFromMetadata(packet.Metadata));

            int displayLength = Math.Min(packet.Payload.Length, _analysisMaxHexBytes);
            bool truncated = packet.OriginalLength > _analysisMaxHexBytes;
            HexDumpSample dumpSample = new HexDumpSample();
            dumpSample.Header = "--- Packet " + (packetIndex + 1)
                + "  " + packet.Metadata.Timestamp.ToString("HH:mm:ss.fff")
                + "  (" + packet.OriginalLength + " bytes)"
                + (truncated ? "  [showing first " + _analysisMaxHexBytes + "]" : "")
                + " ---" + Environment.NewLine;
            dumpSample.Header += packet.Metadata.SourceIp + ":" + packet.Metadata.SourcePort + " -> " +
                packet.Metadata.DestIp + ":" + packet.Metadata.DestPort + Environment.NewLine;
            dumpSample.Header += "Session " + packet.Metadata.SessionId + " " + name + ", Channel " + StreamAbbrev[packet.Metadata.Channel];

            dumpSample.Lines = new List<HexDumpLine>();

            int offset = 0;
            while (offset < displayLength)
            {
                int bytesThisRow = Math.Min(16, displayLength - offset);
                HexDumpLine line = new HexDumpLine();
                line.Offset = offset.ToString("x8");
                line.Bytes = new HexDumpByte[bytesThisRow];
                for (int i = 0; i < bytesThisRow; i++)
                {
                    line.Bytes[i].Value = packet.Payload[offset + i];
                    line.Bytes[i].IsConstant = isConstant[offset + i];
                }
                dumpSample.Lines.Add(line);
                offset = offset + 16;
            }
            dumpData.Add(dumpSample);
        }

        Dispatcher.Invoke(() => RenderHexDump(dumpData));
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // RenderHexDump
    //
    // Renders pre-computed hex dump data into the HexDumpDisplay RichTextBox.
    // Called on the UI thread via Dispatcher.Invoke.  Bytes that are constant
    // across all samples are highlighted in cyan.
    //
    // dumpData:  Pre-computed hex dump lines from the analysis thread
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void RenderHexDump(List<HexDumpSample> dumpData)
    {
        HexDumpDisplay.Document.Blocks.Clear();

        SolidColorBrush constantBrush = new SolidColorBrush(Colors.Cyan);
        SolidColorBrush normalBrush = (SolidColorBrush)HexDumpDisplay.Foreground;

        for (int sampleIndex = 0; sampleIndex < dumpData.Count; sampleIndex++)
        {
            HexDumpSample dumpSample = dumpData[sampleIndex];

            Paragraph header = new Paragraph();
            header.Margin = new Thickness(0, sampleIndex > 0 ? 8 : 0, 0, 2);
            header.Inlines.Add(new Run(dumpSample.Header)
            {
                Foreground = normalBrush
            });
            HexDumpDisplay.Document.Blocks.Add(header);

            for (int lineIndex = 0; lineIndex < dumpSample.Lines.Count; lineIndex++)
            {
                HexDumpLine line = dumpSample.Lines[lineIndex];
                StringBuilder sb = new StringBuilder(80);
                sb.Append(line.Offset);
                sb.Append("  ");

                for (int i = 0; i < 16; i++)
                {
                    if (i == 8)
                    {
                        sb.Append(' ');
                    }

                    if (i < line.Bytes.Length)
                    {
                        sb.Append(line.Bytes[i].Value.ToString("x2"));
                        sb.Append(' ');
                    }
                    else
                    {
                        sb.Append("   ");
                    }
                }

                sb.Append(" |");

                for (int i = 0; i < line.Bytes.Length; i++)
                {
                    byte b = line.Bytes[i].Value;
                    char c = (b >= 0x20 && b <= 0x7e) ? (char)b : '.';
                    sb.Append(c);
                }

                sb.Append('|');

                Paragraph para = new Paragraph();
                para.Margin = new Thickness(0);
                para.Inlines.Add(new Run(sb.ToString())
                {
                    Foreground = normalBrush
                });
                HexDumpDisplay.Document.Blocks.Add(para);
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ComputeConstantBytes
    //
    // Determines which byte positions have identical values across all packets.
    // For each byte offset, checks whether every packet that is long enough to
    // contain that offset has the same value. If any packet is shorter than the
    // offset, that byte is marked as not constant.
    //
    // packets:  List of captured packets to compare.
    // Returns:  Boolean array indexed by byte offset. True if that byte position
    //           has the same value across all packets.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private bool[] ComputeConstantBytes(List<CapturedPacket> packets)
    {
        if (packets.Count == 0)
        {
            return Array.Empty<bool>();
        }
        int maxLength = 0;
        for (int i = 0; i < packets.Count; i++)
        {
            if (packets[i].Payload.Length > maxLength)
            {
                maxLength = packets[i].Payload.Length;
            }
        }
        bool[] isConstant = new bool[maxLength];
        for (int byteIndex = 0; byteIndex < maxLength; byteIndex++)
        {
            bool allSame = true;
            byte firstValue = 0;
            bool hasFirst = false;
            for (int packetIndex = 0; packetIndex < packets.Count; packetIndex++)
            {
                byte[] payload = packets[packetIndex].Payload;
                if (byteIndex >= payload.Length)
                {
                    allSame = false;
                    break;
                }
                if (!hasFirst)
                {
                    firstValue = payload[byteIndex];
                    hasFirst = true;
                }
                else if (payload[byteIndex] != firstValue)
                {
                    allSame = false;
                    break;
                }
            }
            isConstant[byteIndex] = allSame;
        }
        return isConstant;
    }

    private void HandleISXGlassMessage(string msg)
    {
        DebugLog.Write(LogChannel.ISXGlass, $"ISXGlass: message in {msg}");
        var parts = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return;
        }

        string messageId = parts[0];

        switch (messageId)
        {
            // connect ONE session
            case "session_connected":
                {
                    if (parts.Length < 4)
                    {
                        DebugLog.Write(LogChannel.ISXGlass, $"ISXGlass: malformed session_connected: {msg}");
                        return;
                    }

                    string sessionName = parts[1];
                    uint pid = uint.Parse(parts[2]);
                    IntPtr hwnd = (IntPtr)Convert.ToUInt64(parts[3], 16);

                    string characterName = string.Empty;

                    if (GlassContext.ProfileManager.HasActiveProfile)
                    {
                        bool hasId = uint.TryParse(sessionName.Substring(2), out uint accountId);
                        if (!hasId)
                        {
                            DebugLog.Write(LogChannel.ISXGlass, $"ISXGlass: no integer account-id: {sessionName}");
                            return;
                        }

                        characterName = GlassContext.ProfileManager.GetCharacterNameByAccountId(accountId);
                        DebugLog.Write(LogChannel.ISXGlass, $"session connected: {sessionName}, pid={pid}, character={characterName}");
                        GlassContext.SessionRegistry.OnSessionConnected(sessionName, characterName, pid, hwnd);
                        int slot = GlassContext.ProfileManager.GetSlotForCharacter(characterName);
                        if (slot == -1)
                        {
                            return;
                        }

                        string cmd = $"slot_assign {slot} {sessionName} {hwnd:X}";
                        DebugLog.Write(LogChannel.ISXGlass, $"HandleISXGlassMessage: sending {cmd}");
                        GlassContext.GlassVideoPipe.Send(cmd);
                    }
                    else
                    {
                        DebugLog.Write(LogChannel.ISXGlass, $"session connected: {sessionName}, pid={pid}, no active profile.");
                        GlassContext.SessionRegistry.OnSessionConnected(sessionName, characterName, pid, hwnd);
                    }

                    break;
                }

            case "session_disconnected":
                {
                    if (GlassContext.SessionRegistry == null)
                    {
                        // a bit of a hack.  I'm not sure why we sometimes get a stale session disconnect
                        return;
                    }

                    if (parts.Length < 2)
                    {
                        DebugLog.Write(LogChannel.ISXGlass, $"ISXGlass: malformed session_disconnected: {msg}");
                        return;
                    }

                    string sessionName = parts[1];
                    DebugLog.Write(LogChannel.ISXGlass, $"session_disconnected: {sessionName}");
                    GlassContext.GlassVideoPipe.Send($"unassign {sessionName}");
                    GlassContext.SessionRegistry.OnSessionDisconnected(sessionName);
                    break;
                }

            default:
                DebugLog.Write(LogChannel.ISXGlass, $"{msg}");
                break;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HandleAppPacket
    //
    // Handles a decoded application-layer packet. Stores the full packet in the
    // session buffer for later analysis and pcapng save. Updates the opcode grid
    // entry on the UI thread with count, min/max size, and channel information.
    //
    // data:      Raw packet payload bytes.
    // length:    Length of the payload in bytes.
    // direction: Legacy direction byte (retained for compatibility until refactored).
    // opcode:    The decoded opcode value.
    // metadata:  Wire-level metadata including timestamp, IPs, ports, session, stream.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void HandleAppPacket(ReadOnlySpan<byte> data, ushort opcode, PacketMetadata metadata)
    {
        int dataLength = data.Length;
        byte[] copy = data.Slice(0, dataLength).ToArray();
        CapturedPacket packet;
        packet.Metadata = metadata;
        packet.Payload = copy;
        packet.OpcodeValue = opcode;
        packet.OriginalLength = dataLength;
        packet.HighlightColor = 0;

        lock (_payloadLock)
        {
            _capturedPackets.Add(packet);
        }



        Dispatcher.BeginInvoke(() =>
        {
            if (_opcodeLookup.TryGetValue(opcode, out OpcodeEntry? entry))
            {
                entry.Count = entry.Count + 1;
                if (dataLength < entry.MinSize)
                {
                    entry.MinSize = dataLength;
                }
                if (dataLength > entry.MaxSize)
                {
                    entry.MaxSize = dataLength;
                }
            }
            else
            {
                string opcodeHex = "0x" + opcode.ToString("x4");
                entry = new OpcodeEntry(opcodeHex, metadata.Channel, dataLength)
                {
                    RawOpcode = opcode,
                };
                if (_patchOpcodes.TryGetValue(opcode, out string? knownName))
                {
                    entry.Name = knownName;
                }
                _opcodeEntries.Add(entry);
                _opcodeLookup[opcode] = entry;
            }
        });

        OpcodeDispatch.Instance.HandlePacket(data, opcode, metadata);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetFilteredPackets
    //
    // Queries the session packet buffer for packets matching the given filters.
    // All filter parameters are optional — pass null to skip that filter.
    // Returns up to maxResults packets with payloads truncated to maxPayloadBytes
    // for display purposes.
    //
    // opcodeValue:     Optional opcode value to filter on. Null includes all opcodes.
    // channel:         Optional channel filter (StreamId). Null includes all channels.
    // sessionId:       Optional session filter. Null includes all sessions.
    // maxResults:      Maximum number of packets to return.
    // maxPayloadBytes: Maximum payload bytes to include per packet (truncation for display).
    // Returns:         List of CapturedPacket with truncated payloads.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private List<CapturedPacket> GetFilteredPackets(uint? opcodeValue,
        SoeConstants.StreamId? channel, int? sessionId,
        int maxResults, int maxPayloadBytes)
    {
        List<CapturedPacket> results = new List<CapturedPacket>();
        lock (_payloadLock)
        {
            for (int i = 0; i < _capturedPackets.Count; i++)
            {
                CapturedPacket packet = _capturedPackets[i];
                if (opcodeValue != null && packet.OpcodeValue != opcodeValue.Value)
                {
                    continue;
                }
                if (channel != null && packet.Metadata.Channel != channel.Value)
                {
                    continue;
                }
                if (sessionId != null && packet.Metadata.SessionId != sessionId.Value)
                {
                    continue;
                }
                CapturedPacket truncated;
                truncated.Metadata = packet.Metadata;
                truncated.OpcodeValue = packet.OpcodeValue;
                truncated.OriginalLength = packet.OriginalLength;
                truncated.HighlightColor = 0;

                if (packet.Payload.Length > maxPayloadBytes)
                {
                    truncated.Payload = new byte[maxPayloadBytes];
                    Array.Copy(packet.Payload, truncated.Payload, maxPayloadBytes);
                }
                else
                {
                    truncated.Payload = packet.Payload;
                }
                results.Add(truncated);
                if (results.Count >= maxResults)
                {
                    break;
                }
            }
        }
        return results;
    }
}