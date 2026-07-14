using Glass.Core;
using Glass.Core.Logging;
using Glass.Core.Memory;
using Glass.Core.Signals;
using Glass.Data.Repositories;
using Glass.Network.Capture;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.UI;
using Inference.Core;
using Inference.Dialogs;
using Inference.Models;
using Inference.UI;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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

    private readonly RetainedBufferPool _retainedBufferPool;
    private readonly PacketCatalog _packetCatalog;
    private readonly OpcodeRowPresenter _opcodeRowPresenter;
    private readonly OpcodeTracePresenter _opcodeTracePresenter;
    private Border? _armedColorPatch;
    private OpcodeTraceRow? _contextMenuRow;

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
        DebugLog.Write(LogChannel.InferenceDebug, "Inference application started", LogLevel.Trace);
        DebugLog.Write(LogChannel.Inference, "Inference log initialized", LogLevel.Trace);

        InitializeAnalysisFilters();
        AddDummyCandidates();
        InitializePipes();

        GlassContext.SignalBus = new SignalBus();
        GlassContext.PacketBus = new PacketBus();

        OpenDatabase();
        ProtocolStackBootstrap.Initialize();

        BuildRecentPatchesMenu();
        RestoreLastPatchLevel();
        GlassContext.PatchRegistry.LoadPatchLevel(_currentPatchLevel!.Value);

        GlassContext.SessionRegistry.AllSessionsDisconnected += OnAllSessionsDisconnected;

        UpdateControlStates();


        _retainedBufferPool = new RetainedBufferPool();
        _packetCatalog = new PacketCatalog(_retainedBufferPool);
        _opcodeRowPresenter = new OpcodeRowPresenter(_packetCatalog);
        OpcodeGrid.ItemsSource = _opcodeRowPresenter.Rows;
        GlassContext.PacketBus.Subscribe(HandleAppPacket);

        _opcodeTracePresenter = new OpcodeTracePresenter(_packetCatalog, OpcodeTraceList);
        OpcodeTraceList.ItemsSource = _opcodeTracePresenter.Rows;
        ArmColorPatch(ColorPatchYellow);

        GlassContext.BufferPool = new BufferPool(
            new uint[] { 16, 64, 256, 512, 1024, 2048, 16384, 65536, 262144, 524288 },
            new uint[] { 1000, 10000, 4000, 2000, 4000, 1000, 1000, 20, 20, 20 });
        GcMonitor.Start(5);
    }
    private void InitializeLogging()
    {
        DebugLog.SetMinimumLevel(LogLevel.Info);

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
        DebugLog.Route(LogChannel.Memory, LogSink.GlassDebugLogfile);

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

        GlassDebugLogHandler opcodesLogHandler = new GlassDebugLogHandler("opcodes.log");
        DebugLog.AddHandler(LogSink.Aux1LogFile, opcodesLogHandler);
        DebugLog.Route(LogChannel.Opcodes, LogSink.Aux1LogFile);

        GlassDebugLogHandler memoryLogHandler = new GlassDebugLogHandler("memory.log");
        DebugLog.AddHandler(LogSink.Aux2LogFile, memoryLogHandler);
        DebugLog.Route(LogChannel.Memory, LogSink.Aux2LogFile);

        GlassDebugLogHandler fieldsLogHandler = new GlassDebugLogHandler("fields.log");
        DebugLog.AddHandler(LogSink.Aux3LogFile, fieldsLogHandler);
        DebugLog.Route(LogChannel.Fields, LogSink.Aux3LogFile);

        // The inference tab, just inference messages
        GlassConsoleLogHandler inferenceTabHandler = new GlassConsoleLogHandler(InferenceLogOutput, InferenceLogScroller);
        DebugLog.AddHandler(LogSink.InferenceTab, inferenceTabHandler);
        DebugLog.Route(LogChannel.Inference, LogSink.InferenceTab);

     //   DebugLog.DisableAllChannels();
     //   DebugLog.Enable(LogChannel.Memory);
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
            DebugLog.Write(LogChannel.InferenceDebug, "RestoreLastPatchLevel: no previous patch level found", LogLevel.Error);
            return;
        }

        GlassContext.CurrentPatchLevel = new PatchLevel(savedPatchDate, savedServerType);

        // TODO:  Why is _currentPatchLevel even used when we have GlassContext?
        _currentPatchLevel = GlassContext.CurrentPatchLevel;

        string displayServerType = savedServerType.Substring(0, 1).ToUpper() + savedServerType.Substring(1);
        StatusPatchLevel.Text = savedPatchDate + " (" + displayServerType + ")";
        UpdateRecentPatches(savedPatchDate, savedServerType);
        BuildRecentPatchesMenu();
        DebugLog.Write(LogChannel.InferenceDebug, "RestoreLastPatchLevel: restored "
            + savedServerType + " " + savedPatchDate, LogLevel.Trace);
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
        GlassContext.BufferPool.LogStatistics();

        DebugLog.Write(LogChannel.InferenceDebug, "Inference application closing", LogLevel.Trace);
        GlassContext.PatchRegistry.LogPoolStatistics();
        DebugLog.Shutdown();
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
            DebugLog.Write(LogChannel.InferenceDebug, "ISXGlass pipe connected", LogLevel.Trace);
        });
        GlassContext.ISXGlassPipe.Disconnected += () => Dispatcher.Invoke(() =>
        {
            StatusIsx.Text = "ISX: Disconnected";
            DebugLog.Write(LogChannel.InferenceDebug, "ISXGlass pipe disconnected", LogLevel.Trace);
        });
        GlassContext.ISXGlassPipe.MessageReceived += msg => Dispatcher.Invoke(() => HandleISXGlassMessage(msg));
        GlassContext.ISXGlassPipe.Start();

        GlassContext.GlassVideoPipe = new PipeManager("GlassVideo", "GlassVideo_Cmd", "GlassVideo_Notify");
        GlassContext.GlassVideoPipe.Connected += () => Dispatcher.Invoke(() =>
        {
            StatusGlassVideo.Text = "GlassVideo: Connected";
            DebugLog.Write(LogChannel.InferenceDebug, "GlassVideo pipe connected", LogLevel.Trace);
        });
        GlassContext.GlassVideoPipe.Disconnected += () => Dispatcher.Invoke(() =>
        {
            StatusGlassVideo.Text = "GlassVideo: Not Running";
            DebugLog.Write(LogChannel.InferenceDebug, "GlassVideo pipe disconnected", LogLevel.Trace);
        });
        GlassContext.GlassVideoPipe.MessageReceived += msg => Dispatcher.Invoke(() =>
        {
            DebugLog.Write(LogChannel.InferenceDebug, "GlassVideo message: " + msg);
        });
        GlassContext.GlassVideoPipe.Start();

        DebugLog.Write(LogChannel.InferenceDebug, "InitializePipes: pipes started", LogLevel.Trace);
        
        DebugLog.Write(LogChannel.InferenceDebug, "InitializePipes: session registry and focus tracker initialized", LogLevel.Trace);
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
            DebugLog.Write(LogChannel.InferenceDebug, "OpenDatabase: database not found at " + dbPath, LogLevel.Error);
            return;
        }

        Glass.Data.Database.Open(dbPath);
        DebugLog.Write(LogChannel.InferenceDebug, "OpenDatabase: opened " + dbPath, LogLevel.Info);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    /// UpdateRecentPatches - Store the patch level in the settings.
    /// 
    /// Parameters:
    ///     patchLevel:  The current patch level
    ///////////////////////////////////////////////////////////////////////////////////////////
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
        MenuItem fileMenu = (MenuItem)MenuDuplicatePatchLevel.Parent;

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
        int insertIndex = fileMenu.Items.IndexOf(MenuDuplicatePatchLevel) + 1;

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
            DebugLog.Write(LogChannel.InferenceDebug, "RefreshAnalysis: no opcode selected, skipping", LogLevel.Warn);
            return;
        }

        OpcodeEntry selected = (OpcodeEntry)OpcodeGrid.SelectedItem;

        List<CatalogedPacket> packets = _packetCatalog.PacketsFor(
            selected.RawOpcode, _analysisFilterChannel, _analysisFilterSessionId,
            _analysisMaxPackets);

        if (packets.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "RefreshAnalysis: no packets for "
                + selected.Opcode + " with current filters", LogLevel.Warn);
            RenderHexDump(new List<HexDumpSample>());
            return;
        }

        DebugLog.Write(LogChannel.InferenceDebug, "RefreshAnalysis: analyzing " + selected.Opcode
            + " packets=" + packets.Count
            + " channel=" + (_analysisFilterChannel?.ToString() ?? "All")
            + " session=" + (_analysisFilterSessionId?.ToString() ?? "All")
            + " maxHex=" + _analysisMaxHexBytes, LogLevel.Trace);

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
        DebugLog.Write(LogChannel.InferenceDebug, "MenuItem_NewPatchLevel_Click", LogLevel.Trace);

        NewPatchLevelDialog dialog = new NewPatchLevelDialog();
        dialog.Owner = this;

        if (dialog.ShowDialog() == true)
        {
            string patchDate = dialog.PatchDate.ToString("yyyy-MM-dd");
            string serverType = dialog.ServerType;
            string entry = patchDate + " (" + serverType + ")";

            DebugLog.Write(LogChannel.InferenceDebug, "New patch level created: " + entry, LogLevel.Trace);

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
        DebugLog.Write(LogChannel.InferenceDebug, "MenuItem_OpenPatchLevel_Click", LogLevel.Trace);

        OpenDialog dialog = new OpenDialog();
        dialog.Owner = this;

        if (dialog.ShowDialog() == true)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "Opened patch level: ServerType="
                + dialog.ServerType + " PatchDate=" + dialog.PatchDate, LogLevel.Trace);

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
    // MenuItem_DuplicatePatchLevel_Click
    //
    // Handles the File > Duplicate Patch Level menu item click.  Opens the Duplicate
    // Patch Level dialog, and on confirmation runs PatchLevelDuplicator against the
    // database.  The duplicated patch level is then added to the recent patches list,
    // the recent patches menu is rebuilt, and the new patch level is left for the
    // user to open from the menu.
    //
    // sender:  The menu item that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_DuplicatePatchLevel_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "MenuItem_DuplicatePatchLevel_Click", LogLevel.Trace);

        if (!Glass.Data.Database.IsInitialized)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "MenuItem_DuplicatePatchLevel_Click: database not initialized, aborting", LogLevel.Error);
            MessageBox.Show("The database is not open.",
                "Duplicate Patch Level", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        using SqliteConnection connection = Glass.Data.Database.Instance.Connect();
        connection.Open();

        DuplicatePatchLevelDialog dialog = new DuplicatePatchLevelDialog(connection);
        dialog.Owner = this;

        bool? dialogResult = dialog.ShowDialog();
        if (dialogResult != true)
        {
            return;
        }

        string sourcePatchDate = dialog.SourcePatchDate;
        string sourceServerType = dialog.SourceServerType;
        string targetPatchDate = dialog.TargetPatchDate;

        DebugLog.Write(LogChannel.InferenceDebug,
            "MenuItem_DuplicatePatchLevel_Click: duplicating (" + sourcePatchDate + "," + sourceServerType
            + ") -> " + targetPatchDate, LogLevel.Trace);

        int duplicatedCount;
        try
        {
            PatchLevelDuplicator duplicator = new PatchLevelDuplicator(connection);
            duplicatedCount = duplicator.Duplicate(sourcePatchDate, sourceServerType, targetPatchDate);
        }
        catch (Exception ex)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "MenuItem_DuplicatePatchLevel_Click: duplication failed: " + ex.Message, LogLevel.Error);
            MessageBox.Show("Duplication failed: " + ex.Message,
                "Duplicate Patch Level", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (duplicatedCount == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "MenuItem_DuplicatePatchLevel_Click: source had no rows, nothing duplicated", LogLevel.Warn);
            MessageBox.Show("The source patch level contained no opcodes.  Nothing was duplicated.",
                "Duplicate Patch Level", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "MenuItem_DuplicatePatchLevel_Click: duplicated " + duplicatedCount
            + " opcodes into " + targetPatchDate, LogLevel.Info);

        UpdateRecentPatches(targetPatchDate, sourceServerType);
        BuildRecentPatchesMenu();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_OpenPcap_Click
    //
    // Handles the File > Open Pcap menu item.  Opens a file dialog to select
    // a pcap file, sets the current patch level from the user's restored
    // patch level, constructs the demux and file reader, and processes the
    // file.  Packets flow through the PacketBus into the catalog and
    // presenters in the normal way.
    //
    // sender:  The menu item that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private async void MenuItem_OpenPcap_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "MenuItem_OpenPcap_Click", LogLevel.Trace);

        if (_currentPatchLevel == null)
        {
            DebugLog.Write(LogChannel.General, "MenuItem_OpenPcap_Click: no patch level, aborting", LogLevel.Error);
            MessageBox.Show("Select a patch level before opening a pcap.",
                "No Patch Level", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
        dialog.Filter = "Pcap files (*.pcap;*.pcapng)|*.pcap;*.pcapng|All files (*.*)|*.*";
        dialog.Title = "Select a packet capture file";

        bool? result = dialog.ShowDialog();
        if (result != true)
        {
            return;
        }

        UIReset();

        GlassContext.PatchRegistry.LoadPatchLevel(_currentPatchLevel.Value);
        GlassContext.CurrentPatchLevel = _currentPatchLevel.Value;
        OpcodeDispatch.RebuildForCurrentPatchLevel();
        DebugLog.Write(LogChannel.InferenceDebug,
            "MenuItem_OpenPcap_Click: patch level set, OpcodeDispatch rebuilt", LogLevel.Trace);

        string localIp = PacketCapture.GetLocalIP()!;
        if (localIp == null)
        {
            DebugLog.Write(LogChannel.Network,
                "MenuItem_OpenPcap_Click: no local IP, aborting", LogLevel.Error);
            return;
        }

        SessionDemux router = new SessionDemux(localIp);
        PcapFileReader reader = new PcapFileReader(router);

        // disable the menu item while the capture file is processed
        MenuItem? menuItem = sender as MenuItem;
        if (menuItem != null)
        {
            menuItem.IsEnabled = false;
        }

        StatusCapture.Text = "Capture: Reading pcap";
        StatusBarProgressPanel.Visibility = Visibility.Visible;
        StatusBarProgressText.Text = "Scanning...";
        StatusBarProgressBar.Value = 0;
        StatusBarRowText.Visibility = Visibility.Collapsed;
        StatusBarSecondaryText.Visibility = Visibility.Collapsed;

        // initialize the progress bar on the status bar
        Progress<int> progress = new Progress<int>(percent =>
        {
            StatusBarProgressBar.Value = percent;
            StatusBarProgressText.Text = "Loading " + percent + "%";
        });

        int routed = await Task.Run(() => reader.ProcessFile(dialog.FileName, null, progress));

        StatusCapture.Text = "Capture: Pcap complete (" + routed + " packets)";

        // restore the menu item
        StatusBarProgressPanel.Visibility = Visibility.Collapsed;
        StatusBarRowText.Visibility = Visibility.Visible;
        StatusBarSecondaryText.Visibility = Visibility.Visible;

        if (menuItem != null)
        {
            menuItem.IsEnabled = true;
        }

        DebugLog.Write(LogChannel.Network,
            "MenuItem_OpenPcap_Click: " + routed + " packets routed", LogLevel.Info);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_LaunchProfile_Click
    //
    // Handles the Profile > Launch Profile menu item click.  Opens the Launch
    // Profile dialog filtered by the current patch level's server type.  If the
    // user selects a profile, starts packet capture and then launches the
    // profile.
    //
    // sender:  The menu item that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private async void MenuItem_LaunchProfile_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "MenuItem_LaunchProfile_Click", LogLevel.Trace);

        if (_currentPatchLevel == null)
        {
            DebugLog.Write(LogChannel.General, "No patch level before launch.", LogLevel.Error);
            return;
        }

        GlassContext.CurrentPatchLevel = _currentPatchLevel.Value;
        string serverType = _currentPatchLevel.Value.ServerType;
        GlassContext.PatchRegistry.LoadPatchLevel(_currentPatchLevel.Value);
        OpcodeDispatch.RebuildForCurrentPatchLevel();

        DebugLog.Write(LogChannel.Inference,
            "MenuItem_LaunchProfile_Click: patch level set for serverType=" + serverType, LogLevel.Trace);

        LaunchProfileDialog dialog = new LaunchProfileDialog(serverType);
        dialog.Owner = this;

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "MenuItem_LaunchProfile_Click: profile selected: " + dialog.SelectedProfileName, LogLevel.Info);

        string? localIp = PacketCapture.GetLocalIP();
        if (localIp == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "MenuItem_LaunchProfile_Click: no capture device found", LogLevel.Error);
            MessageBox.Show("No suitable capture device found. Is Npcap installed?",
                "Capture Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _sessionDemux = new SessionDemux(localIp);
        _packetCapture = new PacketCapture(_sessionDemux);

        string bpfFilter = "udp and (net 69.174.0.0/16 or net 64.37.0.0/16 or net 209.0.0.0/16)";
        if (!_packetCapture.Start(bpfFilter))
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "MenuItem_LaunchProfile_Click: capture failed to start", LogLevel.Error);
            MessageBox.Show("Failed to start packet capture.",
                "Capture Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "MenuItem_LaunchProfile_Click: capture started", LogLevel.Info);
        StatusCapture.Text = "Capture: Active";

        await GlassContext.ProfileManager.LaunchProfile(dialog.SelectedProfileName);
    }

    ///////////////////////////////////////////////////////////////////////////////////////
    // Window_PreviewKeyDown
    //
    // Window-scoped key handler.  Catches Ctrl+F to show the opcode trace find bar
    // and Escape to hide it, but only when the Opcode Trace tab is active.  Lives at
    // window scope rather than tab scope so Escape works regardless of where focus
    // has drifted while the bar was open.
    //
    // sender:  The window.
    // e:       Key event from WPF's preview tunnel.
    ///////////////////////////////////////////////////////////////////////////////////////
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (MainTabs.SelectedItem != OpcodeTraceTabItem)
        {
            return;
        }

        if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            OpcodeTraceFindBar.Visibility = Visibility.Visible;
            TextBoxOpcodeTraceFind.Focus();
            Keyboard.Focus(TextBoxOpcodeTraceFind);
            e.Handled = true;
            DebugLog.Write(LogChannel.InferenceDebug,
                "Window_PreviewKeyDown: Ctrl+F — find bar shown, focus moved", LogLevel.Trace);
            return;
        }

        if (e.Key == Key.Escape && OpcodeTraceFindBar.Visibility == Visibility.Visible)
        {
            OpcodeTraceFindBar.Visibility = Visibility.Collapsed;
            e.Handled = true;
            DebugLog.Write(LogChannel.InferenceDebug,
                "Window_PreviewKeyDown: Escape — find bar hidden", LogLevel.Trace);
            return;
        }

        if (e.Key == Key.G && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            MenuItem_GoToMessage_Click(MenuGoToMessage, new RoutedEventArgs());
            DebugLog.Write(LogChannel.Opcodes,
                "Window_PreviewKeyDown: Ctrl+G — opening Go To Message dialog", LogLevel.Trace);
            return;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_Find_Click
    //
    // Shows the trace find bar and moves keyboard focus into the find
    // text box, selecting any existing query so a new search overwrites
    // it.  Same behavior as the Ctrl+F shortcut handled in
    // Window_PreviewKeyDown.
    //
    // sender:  The Find... menu item.
    // e:       Routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_Find_Click(object sender, RoutedEventArgs e)
    {
        OpcodeTraceFindBar.Visibility = Visibility.Visible;
        TextBoxOpcodeTraceFind.Focus();
        TextBoxOpcodeTraceFind.SelectAll();
    }

    ///////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_ShowAllRows_Click
    //
    // Restores full visibility of the trace.  Flips IsHidden to false on
    // every row in the presenter, then clears the per-opcode hide set so
    // a future Refresh does not re-apply it.
    //
    // sender:  The Show all rows menu item.
    // e:       Routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_ShowAllRows_Click(object sender, RoutedEventArgs e)
    {
        _opcodeTracePresenter.SetRowsHidden(_opcodeTracePresenter.Rows, false);
        _opcodeTracePresenter.ClearHiddenOpcodes();
    }

    ///////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_CollapseAllRows_Click
    //
    // Collapses every expanded row in the presenter by flipping
    // IsExpanded to false on each.  Does not affect IsHidden state.
    //
    // sender:  The Collapse all rows menu item.
    // e:       Routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_CollapseAllRows_Click(object sender, RoutedEventArgs e)
    {
        _opcodeTracePresenter.CollapseAllRows();
    }

    ///////////////////////////////////////////////////////////////////////////////////////
    // Button_OpcodeTraceFindClose_Click
    //
    // Hides the find bar.  Does not clear the text box — reopening the bar with
    // Ctrl+F shows the prior query, which is useful when the user wants to resume a
    // recent search.
    //
    // sender:  The close button on the find bar.
    // e:       Routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////
    private void Button_OpcodeTraceFindClose_Click(object sender, RoutedEventArgs e)
    {
        OpcodeTraceFindBar.Visibility = Visibility.Collapsed;
    }

    ///////////////////////////////////////////////////////////////////////////////////////
    // TextBoxOpcodeTraceFind_KeyDown
    //
    // Key handler for the find bar's text box.  Escape hides the bar.  Enter triggers
    // a forward search; Shift+Enter triggers a backward search.  Other keys pass
    // through normally so the user can type into the box.
    //
    // sender:  The find bar's text box.
    // e:       Key event args.
    ///////////////////////////////////////////////////////////////////////////////////////
    private void TextBoxOpcodeTraceFind_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            OpcodeTraceFindBar.Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            e.Handled = true;

            if (shift)
            {
                Button_OpcodeTraceFindPrevious_Click(sender, e);
            }
            else
            {
                Button_OpcodeTraceFindNext_Click(sender, e);
            }
            return;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////
    // CheckBoxOpcodeTraceFindWrap_Changed
    //
    // Fires on both Checked and Unchecked events of the wrap checkbox.  Propagates
    // the new state to the presenter so subsequent FindNext/FindPrevious calls honor
    // the user's preference.
    //
    // Guards against early firing during XAML load when the presenter may not yet
    // have been constructed.
    //
    // sender:  The wrap checkbox.
    // e:       Routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////
    private void CheckBoxOpcodeTraceFindWrap_Changed(object sender, RoutedEventArgs e)
    {
        // check if the presenter has not been constructed
        if (_opcodeTracePresenter == null)
        {
            return;
        }

        bool wrap = CheckBoxOpcodeTraceFindWrap.IsChecked == true;
        _opcodeTracePresenter.SetSearchWrap(wrap);
        DebugLog.Write(LogChannel.InferenceDebug,
            "CheckBoxOpcodeTraceFindWrap_Changed: wrap=" + wrap, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_OpcodeTraceFindNext_Click
    //
    // Find-bar Next handler.  Reads the typed query and the deep/fast checkbox, then calls
    // FindNext on the presenter, which compares the query against its cached query, rebuilds
    // the match list when it changed, advances the cursor forward to the next live match, and
    // scrolls and paints that match.  On a hit the status bar shows the landed row's message
    // number and the cursor ordinal over the match count; on a miss it shows no-match.  Deep
    // mode widens the hex length before searching.
    //
    // sender:  The Next button.
    // e:       The routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
 
    private void Button_OpcodeTraceFindNext_Click(object sender, RoutedEventArgs e)
    {
        // guard against early firing during XAML load
        if (_opcodeTracePresenter == null)
        {
            return;
        }

        string typedText = TextBoxOpcodeTraceFind.Text ?? string.Empty;

        SearchMode mode;
        if (CheckBoxOpcodeTraceFindDeep.IsChecked == true)
        {
            mode = SearchMode.Deep;
            SetHexLengthFull();
        }
        else
        {
            mode = SearchMode.Fast;
        }

        SearchMatch? match = _opcodeTracePresenter.FindNext(typedText, mode);
        if (match == null)
        {
            StatusBarSecondaryText.Text = "No match";
            DebugLog.Write(LogChannel.InferenceDebug,
                "Button_OpcodeTraceFindNext_Click: no match (mode=" + mode + ")", LogLevel.Trace);
            return;
        }

        MessageIndex cursorMessage = _opcodeTracePresenter.CursorMessage;
        if (cursorMessage.Exists)
        {
            StatusBarRowText.Text = "Message " + cursorMessage;
        }

        StatusBarSecondaryText.Text = _opcodeTracePresenter.CursorOrdinal
            + "/" + _opcodeTracePresenter.NumCurrentMatches + " matches";

        DebugLog.Write(LogChannel.InferenceDebug,
            "Button_OpcodeTraceFindNext_Click: match found (mode=" + mode + ")", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_OpcodeTraceFindPrevious_Click
    //
    // Find-bar Previous handler.  Reads the typed query and the deep/fast checkbox, then calls
    // FindPrevious on the presenter, which compares the query against its cached query,
    // rebuilds the match list when it changed, advances the cursor backward to the previous
    // live match, and scrolls and paints that match.  On a hit the status bar shows the landed
    // row's message number and the cursor ordinal over the match count; on a miss it shows
    // no-match.  Deep mode widens the hex length before searching.
    //
    // sender:  The Previous button on the find bar.
    // e:       The routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_OpcodeTraceFindPrevious_Click(object sender, RoutedEventArgs e)
    {
        // guard against early firing during XAML load
        if (_opcodeTracePresenter == null)
        {
            return;
        }

        string typedText = TextBoxOpcodeTraceFind.Text ?? string.Empty;

        SearchMode mode;
        if (CheckBoxOpcodeTraceFindDeep.IsChecked == true)
        {
            mode = SearchMode.Deep;
            SetHexLengthFull();
        }
        else
        {
            mode = SearchMode.Fast;
        }

        SearchMatch? match = _opcodeTracePresenter.FindPrevious(typedText, mode);
        if (match == null)
        {
            StatusBarSecondaryText.Text = "No match";
            DebugLog.Write(LogChannel.InferenceDebug,
                "Button_OpcodeTraceFindPrevious_Click: no match (mode=" + mode + ")", LogLevel.Trace);
            return;
        }

        MessageIndex cursorMessage = _opcodeTracePresenter.CursorMessage;
        if (cursorMessage.Exists)
        {
            StatusBarRowText.Text = "Message " + cursorMessage;
        }

        StatusBarSecondaryText.Text = _opcodeTracePresenter.CursorOrdinal
            + "/" + _opcodeTracePresenter.NumCurrentMatches + " matches";

        DebugLog.Write(LogChannel.InferenceDebug,
            "Button_OpcodeTraceFindPrevious_Click: match found (mode=" + mode + ")", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_OpcodeTraceFindFirst_Click
    //
    // Find-bar First handler.  Reads the typed query and the deep/fast checkbox, then calls
    // FindFirst on the presenter, which parks the cursor at the top of the trace, rebuilds the
    // match list against the query when it changed, advances forward to the first live match,
    // and scrolls and paints that match.  On a hit the status bar shows the landed row's
    // message number and the cursor ordinal over the match count; on a miss it shows no-match.
    // Deep mode widens the hex length before searching.
    //
    // sender:  The First button on the find bar.
    // e:       The routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_OpcodeTraceFindFirst_Click(object sender, RoutedEventArgs e)
    {
        // guard against early firing during XAML load
        if (_opcodeTracePresenter == null)
        {
            return;
        }

        string typedText = TextBoxOpcodeTraceFind.Text ?? string.Empty;

        SearchMode mode;
        if (CheckBoxOpcodeTraceFindDeep.IsChecked == true)
        {
            mode = SearchMode.Deep;
            SetHexLengthFull();
        }
        else
        {
            mode = SearchMode.Fast;
        }

        SearchMatch? match = _opcodeTracePresenter.FindFirst(typedText, mode);
        if (match == null)
        {
            StatusBarSecondaryText.Text = "No matches";
            DebugLog.Write(LogChannel.InferenceDebug,
                "Button_OpcodeTraceFindFirst_Click: no matches (mode=" + mode + ")", LogLevel.Trace);
            return;
        }

        MessageIndex cursorMessage = _opcodeTracePresenter.CursorMessage;
        if (cursorMessage.Exists)
        {
            StatusBarRowText.Text = "Message " + cursorMessage;
        }

        StatusBarSecondaryText.Text = _opcodeTracePresenter.CursorOrdinal
            + "/" + _opcodeTracePresenter.NumCurrentMatches + " matches";
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindElementTextBlock
    //
    // Walks the visual tree under a container and returns the first TextBlock whose
    // FieldHighlightBehavior.FDN attached property is the requested element.  Returns null when no
    // such TextBlock is realized under the container, which happens when the container has not yet
    // been laid out or when the element belongs to a collapsed or unscrolled part of the row.
    //
    // container:  The realized container for the row of interest.
    // element:    The FieldDisplayNode whose displaying TextBlock to locate.
    //
    // returns:    The matching TextBlock, or null when none is found.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static TextBlock? FindElementTextBlock(DependencyObject container, FieldDisplayNode element)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(container);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(container, i);

            TextBlock? asTextBlock = child as TextBlock;
            if (asTextBlock != null)
            {
                FieldDisplayNode? childElement = FieldHighlightBehavior.GetFDN(asTextBlock);
                if (object.ReferenceEquals(childElement, element))
                {
                    return asTextBlock;
                }
            }

            TextBlock? found = FindElementTextBlock(child, element);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CenterRowInList
    //
    // Scrolls the Opcode Trace list so the given row sits vertically centered in the outer
    // viewport, giving the match context above and below rather than landing it at an edge.  The
    // list is item-scrolling (CanContentScroll defaults true for a ListView), so the outer
    // ScrollViewer's offsets and viewport are in item units, not pixels: centering is the row's
    // item index minus half the viewport's item count, clamped to the scrollable range.
    //
    // Returns silently with a log entry when the row is not in the list or the outer ScrollViewer
    // cannot be found.
    //
    // row:  The row to center.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void CenterRowInList(OpcodeTraceRow row)
    {
        int rowIndex = OpcodeTraceList.Items.IndexOf(row);
        if (rowIndex < 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "MainWindow.CenterRowInList: row not in list, ignoring", LogLevel.Warn);
            return;
        }

        ScrollViewer? outerScroller = FindDescendantScrollViewer(OpcodeTraceList);
        if (outerScroller == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "MainWindow.CenterRowInList: no outer ScrollViewer, ignoring", LogLevel.Warn);
            return;
        }

        double viewportItems = outerScroller.ViewportHeight;
        double targetOffset = rowIndex - (viewportItems / 2.0);

        if (targetOffset < 0)
        {
            targetOffset = 0;
        }
        double maxOffset = outerScroller.ScrollableHeight;
        if (targetOffset > maxOffset)
        {
            targetOffset = maxOffset;
        }

        outerScroller.ScrollToVerticalOffset(targetOffset);

        DebugLog.Write(LogChannel.InferenceDebug,
            "MainWindow.CenterRowInList: centered packetIndex=" + row.PacketIndex
            + " rowIndex=" + rowIndex + " viewportItems=" + viewportItems
            + " targetOffset=" + targetOffset, LogLevel.Info);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindDescendantScrollViewer
    //
    // Walks down the visual tree from a starting element and returns the first ScrollViewer
    // found in breadth-first order.  Used to reach a ListView's own template ScrollViewer.
    // Returns null when no ScrollViewer exists beneath the starting element.
    //
    // root:  The element from which to begin the downward walk.
    //
    // returns:  The first descendant ScrollViewer, or null when none exists.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);

            ScrollViewer? asScroller = child as ScrollViewer;
            if (asScroller != null)
            {
                return asScroller;
            }

            ScrollViewer? found = FindDescendantScrollViewer(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScrollOffsetIntoView
    //
    // Scrolls the ScrollViewer ancestor of a TextBlock so the character at
    // the given offset is inside the viewport with a 20-pixel margin.  The
    // character rectangle is obtained by resolving the character offset to
    // a TextPointer through the TextBlock's Inlines, then asking that
    // TextPointer for its forward character rect and transforming the rect
    // into the ScrollViewer's coordinate space.  Both vertical and
    // horizontal offsets are adjusted so the rectangle is visible.
    //
    // Returns silently with a log entry when the TextBlock has no
    // ScrollViewer ancestor, when the offset is outside the TextBlock's
    // rendered text, or when the resulting character rectangle is empty.
    //
    // text:    The TextBlock containing the match.
    // offset:  Zero-based character offset within the TextBlock's rendered
    //          text.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void ScrollOffsetIntoView(TextBlock text, int offset)
    {
        const double margin = 20.0;

        ScrollViewer? scroller = FindAncestorScrollViewer(text);
        if (scroller == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "MainWindow.ScrollOffsetIntoView: no ScrollViewer ancestor, ignoring", LogLevel.Warn);
            return;
        }

        TextPointer? pointer = GetTextPointerAtCharacterOffset(text, offset);
        if (pointer == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "MainWindow.ScrollOffsetIntoView: offset " + offset + " could not be resolved to a TextPointer", LogLevel.Error);
            return;
        }

        Rect charRect = pointer.GetCharacterRect(LogicalDirection.Forward);
        if (charRect.IsEmpty || double.IsInfinity(charRect.Top) || double.IsInfinity(charRect.Left))
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "MainWindow.ScrollOffsetIntoView: empty character rect at offset " + offset, LogLevel.Warn);
            return;
        }

        GeneralTransform toScroller = text.TransformToAncestor(scroller);
        Rect rectInScroller = toScroller.TransformBounds(charRect);

        double currentVertical = scroller.VerticalOffset;
        double viewportHeight = scroller.ViewportHeight;
        double rectTop = rectInScroller.Top + currentVertical;
        double rectCenter = rectTop + (rectInScroller.Height / 2.0);

        double newVertical = rectCenter - (viewportHeight / 2.0);
        if (newVertical < 0)
        {
            newVertical = 0;
        }
        double maxVertical = scroller.ScrollableHeight;
        if (newVertical > maxVertical)
        {
            newVertical = maxVertical;
        }

        double currentHorizontal = scroller.HorizontalOffset;
        double viewportWidth = scroller.ViewportWidth;
        double rectLeft = rectInScroller.Left + currentHorizontal;
        double rectRight = rectInScroller.Right + currentHorizontal;
        double newHorizontal = currentHorizontal;

        if (rectLeft - margin < currentHorizontal)
        {
            newHorizontal = rectLeft - margin;
            if (newHorizontal < 0)
            {
                newHorizontal = 0;
            }
        }
        else if (rectRight + margin > currentHorizontal + viewportWidth)
        {
            newHorizontal = rectRight + margin - viewportWidth;
        }

        if (newVertical != currentVertical)
        {
            scroller.ScrollToVerticalOffset(newVertical);
        }
        if (newHorizontal != currentHorizontal)
        {
            scroller.ScrollToHorizontalOffset(newHorizontal);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindAncestorScrollViewer
    //
    // Walks up the visual tree from a starting element and returns the
    // nearest ScrollViewer ancestor.  Returns null when no ScrollViewer is
    // found above the starting element.
    //
    // start:    The element from which to begin the upward walk.  May be
    //           null, in which case null is returned.
    //
    // returns:  The nearest enclosing ScrollViewer, or null when none
    //           exists above start.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject? start)
    {
        if (start == null)
        {
            return null;
        }

        DependencyObject? current = VisualTreeHelper.GetParent(start);
        while (current != null)
        {
            ScrollViewer? asScroller = current as ScrollViewer;
            if (asScroller != null)
            {
                return asScroller;
            }
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetTextPointerAtCharacterOffset
    //
    // Returns a TextPointer at the given character offset into a TextBlock's
    // rendered text.  The TextBlock's text may be composed of multiple Run
    // inlines (as produced by HighlightTextBehavior); the offset is into
    // the concatenated visible text, not into the TextBlock's element-symbol
    // space.
    //
    // Walks the TextBlock's Inlines, accumulating text length per Run.
    // When the cumulative length reaches the requested offset, returns a
    // TextPointer at the position inside that Run.  Returns null when the
    // offset is past the end of the rendered text or when the TextBlock
    // contains inline types other than Run that this helper does not know
    // how to traverse.
    //
    // text:    The TextBlock whose rendered text is being indexed.
    // offset:  Zero-based character offset into the concatenated rendered
    //          text.
    //
    // returns: A TextPointer at the requested character position, or null
    //          when the offset is out of range.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static TextPointer? GetTextPointerAtCharacterOffset(TextBlock text, int offset)
    {
        if (offset < 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "MainWindow.GetTextPointerAtCharacterOffset: negative offset " + offset, LogLevel.Error);
            return null;
        }

        int totalRunLength = 0;
        foreach (Inline countInline in text.Inlines)
        {
            Run? countRun = countInline as Run;
            if (countRun != null)
            {
                totalRunLength += countRun.Text.Length;
            }
        }

        int remaining = offset;
        foreach (Inline inline in text.Inlines)
        {
            Run? run = inline as Run;
            if (run == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "MainWindow.GetTextPointerAtCharacterOffset: non-Run inline encountered, cannot traverse", LogLevel.Error);
                return null;
            }

            int runLength = run.Text.Length;
            if (remaining <= runLength)
            {
                TextPointer? pointer = run.ContentStart.GetPositionAtOffset(remaining, LogicalDirection.Forward);
                if (pointer == null)
                {
                    DebugLog.Write(LogChannel.InferenceDebug,
                        "MainWindow.GetTextPointerAtCharacterOffset: Run.ContentStart returned null for offset "
                        + remaining + " within run of length " + runLength, LogLevel.Error);
                }
                return pointer;
            }

            remaining = remaining - runLength;
        }

        return null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////
    // Button_OpcodeTraceFindClear_Click
    //
    // Clears the find bar's text box, drops the presenter's stored query, and
    // empties every row's highlights.  Returns focus to the text box so the user
    // can start a new search immediately.
    //
    // Guards against early firing during XAML load.
    //
    // sender:  The clear button on the find bar.
    // e:       Routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////
    private void Button_OpcodeTraceFindClear_Click(object sender, RoutedEventArgs e)
    {
        if (_opcodeTracePresenter == null)
        {
            return;
        }

        TextBoxOpcodeTraceFind.Clear();
        StatusBarSecondaryText.Text = "";
        _opcodeTracePresenter.SetSearchQuery(null);
        TextBoxOpcodeTraceFind.Focus();
    }

    ///////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_OpcodeTraceHideRow_Click
    //
    // Hides the row that was under the cursor when the context menu
    // opened.  Reads the row stashed by OpcodeTraceList_ContextMenuOpening
    // and passes it to the presenter wrapped in a one-element array.
    // No-ops when no row was stashed.
    //
    // sender:  The menu item.
    // e:       Routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_OpcodeTraceHideRow_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuRow == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "MenuItem_OpcodeTraceHideRow_Click: no stashed row, ignoring", LogLevel.Warn);
            return;
        }

        OpcodeTraceRow row = _contextMenuRow;
        _opcodeTracePresenter.SetRowsHidden(new[] { row }, true);

        DebugLog.Write(LogChannel.InferenceDebug,
            "MenuItem_OpcodeTraceHideRow_Click: hid packetIndex=" + row.PacketIndex, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_GoToMessage_Click
    //
    // Opens the Go To Message dialog seeded with the current cursor row's message index and
    // the lowest and highest indices present in the trace.  On confirm, resolves the entered
    // target to the nearest present row and scrolls it into view, updating the status bar to
    // the landed row's message index.  No-ops with a status message when the trace is empty.
    // Does not change the list selection or the presenter's search cursor.
    //
    // sender:  The Go to message menu item.
    // e:       Routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_GoToMessage_Click(object sender, RoutedEventArgs e)
    {
        MessageIndexBounds bounds = _opcodeTracePresenter.GetMessageIndexBounds();
        if (!bounds.HasRows)
        {
            StatusBarSecondaryText.Text = "No messages";
            DebugLog.Write(LogChannel.Opcodes,
                "MainWindow.MenuItem_GoToMessage_Click: no rows, nothing to go to", LogLevel.Warn);
            return;
        }

        uint current;
        MessageIndex cursorMessage = _opcodeTracePresenter.CursorMessage;
        if (cursorMessage.Exists)
        {
            current = cursorMessage;
        }
        else
        {
            current = bounds.Lowest;
        }

        GoToMessageDialog dialog = new GoToMessageDialog(current, bounds.Lowest, bounds.Highest);
        dialog.Owner = this;

        if (dialog.ShowDialog() != true)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "MainWindow.MenuItem_GoToMessage_Click: dialog cancelled", LogLevel.Trace);
            return;
        }

        OpcodeTraceRow? target = _opcodeTracePresenter.RowForNearestPacketIndex(dialog.Target);
        if (target == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "MainWindow.MenuItem_GoToMessage_Click: no row resolved for target "
                + dialog.Target, LogLevel.Error);
            return;
        }

        OpcodeTraceList.SelectedItem = target;
        _opcodeTracePresenter.CenterRowForPacketIndex(target.PacketIndex);
        StatusBarRowText.Text = "Message " + target.PacketIndex;

        DebugLog.Write(LogChannel.Opcodes,
            "MainWindow.MenuItem_GoToMessage_Click: requested " + dialog.Target
            + ", landed on message " + target.PacketIndex, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_OpcodeTraceManage_Click
    //
    // Opens a modeless OpcodeManageWindow showing every opcode known to
    // the catalog at the moment of opening, sorted hex ascending, each
    // with a checkbox bound two-way to its per-opcode hide state.  Flips
    // push immediately through to the presenter so the trace list
    // updates in real time while the dialog is open.
    //
    // sender:  The Manage... button on the trace toolbar.
    // e:       Routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_OpcodeTraceManage_Click(object sender, RoutedEventArgs e)
    {
        OpcodeManageWindow window = new OpcodeManageWindow(_opcodeTracePresenter, _packetCatalog);
        window.Owner = this;
        window.Show();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UpdateToolsMenuState
    //
    // Adjust the tools menu availability based on application state.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private void UpdateToolsMenuState()
    {

    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnAllSessionsDisconnected
    //
    // Called when all EQ sessions have disconnected.
    // Clears the active profile and stops focus tracking.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void OnAllSessionsDisconnected()
    {
        DebugLog.Write(LogChannel.Sessions, "MainWindow.OnAllSessionsDisconnected: all sessions disconnected, clearing active profile.", LogLevel.Info);
        ProtocolStackBootstrap.Teardown();
        UpdateToolsMenuState();
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

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_PatchDataEditor_Click
    //
    // Opens the Patch Data Editor as a modeless window owned by this MainWindow.
    //
    // sender:  The Tools > Patch Data Editor menu item.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_OpcodeEditor_Click(object sender, RoutedEventArgs e)
    {
        OpcodeEditor editor = new OpcodeEditor();
        editor.Owner = this;
        editor.Show();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_CollectionEditor_Click
    //
    // Opens the Collection Editor as a modeless window owned by this MainWindow.
    //
    // sender:  The Tools > Collection Editor menu item.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_CollectionEditor_Click(object sender, RoutedEventArgs e)
    {
        CollectionEditor editor = new CollectionEditor();
        editor.Owner = this;
        editor.Show();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_GateEditor_Click
    //
    // Opens the Gate Editor as a modeless window owned by this MainWindow.
    //
    // sender:  The Tools > Gate Editor menu item.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_GateEditor_Click(object sender, RoutedEventArgs e)
    {
        GateEditor editor = new GateEditor();
        editor.Owner = this;
        editor.Show();
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
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisSessionFilter_SelectionChanged: no selection, filter cleared", LogLevel.Trace);
            RefreshAnalysis();
            return;
        }

        if (selected.Tag is int sessionId)
        {
            _analysisFilterSessionId = sessionId;
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisSessionFilter_SelectionChanged: filter set to session " + sessionId, LogLevel.Trace);
        }
        else
        {
            _analysisFilterSessionId = null;
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisSessionFilter_SelectionChanged: filter cleared (All)", LogLevel.Trace);
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
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisChannelFilter_SelectionChanged: no selection, filter cleared", LogLevel.Trace);
            RefreshAnalysis();
            return;
        }

        string value = selected.Content as string ?? "";

        if (selected.Tag is SoeConstants.StreamId streamId)
        {
            _analysisFilterChannel = streamId;
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisChannelFilter_SelectionChanged: filter set to " + streamId, LogLevel.Trace);
        }
        else
        {
            _analysisFilterChannel = null;
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisChannelFilter_SelectionChanged: filter cleared (All)", LogLevel.Trace);
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
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisPacketCount_SelectionChanged: no selection", LogLevel.Trace);
            return;
        }

        string value = selected.Content as string ?? "";

        if (value == "All")
        {
            _analysisMaxPackets = int.MaxValue;
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisPacketCount_SelectionChanged: set to All", LogLevel.Trace);
        }
        else if (int.TryParse(value, out int count))
        {
            _analysisMaxPackets = count;
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisPacketCount_SelectionChanged: set to " + count, LogLevel.Trace);
        }
        else
        {
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisPacketCount_SelectionChanged: unexpected value '" + value + "'", LogLevel.Warn);
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
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisHexLength_SelectionChanged: no selection", LogLevel.Trace);
            return;
        }

        string value = selected.Content as string ?? "";

        if (value == "Full")
        {
            _analysisMaxHexBytes = int.MaxValue;
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisHexLength_SelectionChanged: set to Full", LogLevel.Trace);
        }
        else if (int.TryParse(value, out int bytes))
        {
            _analysisMaxHexBytes = bytes;
            DebugLog.Write(LogChannel.InferenceDebug, "AnalysisHexLength_SelectionChanged: set to " + bytes, LogLevel.Trace);
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
            DebugLog.Write(LogChannel.InferenceDebug, "ToggleButton_AcceptCandidate_Click: no candidate selected", LogLevel.Trace);
            return;
        }
        System.Windows.Controls.Primitives.ToggleButton toggle = (System.Windows.Controls.Primitives.ToggleButton)sender;
        bool isAccepted = toggle.IsChecked == true;
        DebugLog.Write(LogChannel.InferenceDebug, "ToggleButton_AcceptCandidate_Click: accepted=" + isAccepted, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////
    // OpcodeTraceList_SelectionChanged
    //
    // Reflects the new selection state across the toolbar and status bar, and moves the search
    // cursor to follow a single deliberate row pick.  The Hide and Expand controls are enabled
    // when any row is selected, the Expand toggle is synced to the selected row's expansion
    // state, and the status bar's row text shows the selected row's message number.
    //
    // The cursor follows the selection only when exactly one row is selected: a single row pick
    // moves the cursor to that row.  When no rows or several rows are selected — the latter being
    // a multi-row copy gesture — the cursor is left where it is.
    //
    // sender:  The trace ListView.
    // e:       Selection change event args.
    ///////////////////////////////////////////////////////////////////////////////////////
    private void OpcodeTraceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OpcodeTraceRow? row = OpcodeTraceList.SelectedItem as OpcodeTraceRow;
        bool hasSelection = row != null;
        ButtonOpcodeTraceHide.IsEnabled = hasSelection;
        ToggleOpcodeTraceExpand.IsEnabled = hasSelection;

        if (OpcodeTraceList.SelectedItems.Count == 1 && row != null)
        {
            StatusBarSecondaryText.Text = "";
            _opcodeTracePresenter.MoveCursorToMessage(row.PacketIndex);
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTraceList_SelectionChanged: single selection, cursor moved to packetIndex "
                + row.PacketIndex, LogLevel.Info);
        }
        else
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTraceList_SelectionChanged: selection count "
                + OpcodeTraceList.SelectedItems.Count + ", cursor left unchanged", LogLevel.Info);
        }

        if (row != null)
        {
            ToggleOpcodeTraceExpand.IsChecked = row.IsExpanded;
            StatusBarRowText.Text = "Message " + row.PacketIndex;
        }
        else
        {
            ToggleOpcodeTraceExpand.IsChecked = false;
            StatusBarRowText.Text = "";
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeTraceList_KeyDown
    //
    // Key handler for the Opcode Trace list.  Catches Ctrl-C and copies the
    // selected rows to the clipboard.  Each row contributes one tab-separated
    // summary line; expanded rows additionally contribute their field detail
    // indented on following lines.  Collapsed rows contribute summary only,
    // even if their detail has been populated by a prior expand.
    //
    // sender:  The ListView that raised the event.
    // e:       Key event args; Handled is set when Ctrl-C is consumed.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void OpcodeTraceList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.C)
        {
            return;
        }
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        if (OpcodeTraceList.SelectedItems.Count == 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTraceList_KeyDown: Ctrl-C with no selection, ignoring", LogLevel.Info);
            return;
        }

        StringBuilder sb = new StringBuilder();
        int rowsCopied = 0;
        int rowsWithDetail = 0;

        List<OpcodeTraceRow> sortedRows = new List<OpcodeTraceRow>(OpcodeTraceList.SelectedItems.Count);
      
        for (int i = 0; i < OpcodeTraceList.SelectedItems.Count; i++)
        {
            OpcodeTraceRow? row = OpcodeTraceList.SelectedItems[i] as OpcodeTraceRow;
            if (row != null)
            {
                sortedRows.Add(row);
            }
        }

        sortedRows.Sort((a, b) => a.PacketIndex.CompareTo(b.PacketIndex));

        for (int i = 0; i < sortedRows.Count; i++)
        {
            OpcodeTraceRow row = sortedRows[i];
            sb.Append(row.PacketIndex);
            sb.Append('\t');
            sb.Append(row.TimestampLocal);
            sb.Append('\t');
            sb.Append(row.OpcodeHex);
            sb.Append('\t');
            sb.Append(row.OpcodeName);
            sb.Append('\t');
            sb.Append(row.ChannelAbbrev);
            sb.Append('\t');
            sb.Append(row.PortsText);
            sb.Append('\t');
            sb.Append(row.CharacterName);
            sb.Append('\t');
            sb.Append(row.Length);
            sb.Append(Environment.NewLine);

            if (row.IsExpanded && row.FieldTree != null)
            {
                string fieldText = FieldTreeFormatter.ToIndentedText(row.FieldTree);
                string[] detailLines = fieldText.Split('\n');
                for (int j = 0; j < detailLines.Length; j++)
                {
                    string line = detailLines[j].TrimEnd('\r');
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    sb.Append("    ");
                    sb.Append(line);
                    sb.Append(Environment.NewLine);
                }
                rowsWithDetail++;
            }

            if (row.IsExpanded && !string.IsNullOrEmpty(row.HexDumpText))
            {
                string[] hexLines = row.HexDumpText.Split('\n');
                for (int j = 0; j < hexLines.Length; j++)
                {
                    string line = hexLines[j].TrimEnd('\r');
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    sb.Append("    ");
                    sb.Append(line);
                    sb.Append(Environment.NewLine);
                }
            }

            rowsCopied++;
        }

        Clipboard.SetText(sb.ToString());

        e.Handled = true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Toggle_OpcodeTraceColorPacket_Click
    //
    // Momentary apply-then-disarm toggle.  Applies the armed color to the
    // selected row's per-packet color override.  No-op if no color is
    // armed or no row is selected.
    //
    // sender:  The toggle button that raised the event.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Toggle_OpcodeTraceColorPacket_Click(object sender, RoutedEventArgs e)
    {
        ApplyArmedColor(ToggleOpcodeTraceColorPacket, (uint argb, OpcodeTraceRow row) =>
        {
            _opcodeTracePresenter.SetPacketColor(row.PacketIndex, argb);
        });
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Toggle_OpcodeTraceColorOpcode_Click
    //
    // Momentary apply-then-disarm toggle.  Applies the armed color to every
    // row sharing the selected row's opcode.  No-op if no color is armed or
    // no row is selected.
    //
    // sender:  The toggle button that raised the event.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Toggle_OpcodeTraceColorOpcode_Click(object sender, RoutedEventArgs e)
    {
        ApplyArmedColor(ToggleOpcodeTraceColorOpcode, (uint argb, OpcodeTraceRow row) =>
        {
            _opcodeTracePresenter.SetOpcodeColor(row.OpcodeValue, argb);
        });
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeTraceHexLength_SelectionChanged
    //
    // Hex length cap selector on the Opcode Trace toolbar.  Reads the selected
    // value, converts "Full" to int.MaxValue, and passes the cap to the
    // presenter, which re-formats any already-populated rows in place.
    //
    // sender:  The ComboBox that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void OpcodeTraceHexLength_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ComboBoxItem? selectedItem = OpcodeTraceHexLength.SelectedItem as ComboBoxItem;
        if (selectedItem == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTraceHexLength_SelectionChanged: no selection", LogLevel.Trace);
            return;
        }

        string value = selectedItem.Content as string ?? "";

        uint maxBytes;
        if (value == "Full")
        {
            maxBytes = uint.MaxValue;
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTraceHexLength_SelectionChanged: set to Full", LogLevel.Trace);
        }
        else if (uint.TryParse(value, out uint parsed))
        {
            maxBytes = parsed;
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTraceHexLength_SelectionChanged: set to " + parsed, LogLevel.Trace);
        }
        else
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTraceHexLength_SelectionChanged: unexpected value '" + value + "'", LogLevel.Warn);
            return;
        }

        if (_opcodeTracePresenter  != null)
        {
            _opcodeTracePresenter.SetMaxHexBytes(maxBytes);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////
    // SetHexLengthFull
    //
    // Finds the ComboBoxItem in OpcodeTraceHexLength whose Content is the
    // string "Full" and assigns it as the SelectedItem.  Triggers
    // OpcodeTraceHexLength_SelectionChanged, which pushes int.MaxValue
    // into the presenter's _maxHexBytes and re-formats already-populated
    // rows to the new cap.  No-ops when the dropdown already has Full
    // selected or when no such item exists.
    ///////////////////////////////////////////////////////////////////////////////////////
    private void SetHexLengthFull()
    {
        for (int i = 0; i < OpcodeTraceHexLength.Items.Count; i++)
        {
            ComboBoxItem? item = OpcodeTraceHexLength.Items[i] as ComboBoxItem;
            if (item == null)
            {
                continue;
            }

            string content = item.Content as string ?? string.Empty;
            if (content != "Full")
            {
                continue;
            }

            if (ReferenceEquals(OpcodeTraceHexLength.SelectedItem, item))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "MainWindow.SetHexLengthFull: dropdown already on Full, no change", LogLevel.Trace);
                return;
            }

            OpcodeTraceHexLength.SelectedItem = item;
            DebugLog.Write(LogChannel.InferenceDebug,
                "MainWindow.SetHexLengthFull: dropdown set to Full", LogLevel.Trace);
            return;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "MainWindow.SetHexLengthFull: Full entry not found in dropdown", LogLevel.Warn);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Toggle_OpcodeTraceExpand_Click
    //
    // Flips the selected row's IsExpanded state.
    //
    // sender:  The toggle button that raised the event.
    // e:       Standard event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Toggle_OpcodeTraceExpand_Click(object sender, RoutedEventArgs e)
    {
        OpcodeTraceRow? row = OpcodeTraceList.SelectedItem as OpcodeTraceRow;
        if (row == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "Toggle_OpcodeTraceExpand_Click: no row selected, ignoring", LogLevel.Trace);
            ToggleOpcodeTraceExpand.IsChecked = false;
            return;
        }

        ToggleRowExpand(row);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ToggleRowExpand
    //
    // Flips the given row's IsExpanded state, populates field detail on first
    // expand, and syncs the Expand toggle's checked state to the row.  Shared
    // by the Expand toggle button and the row double-click handler.
    //
    // row:  The row to toggle.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ToggleRowExpand(OpcodeTraceRow row)
    {
        if (row.FieldText == null)
        {
            _opcodeTracePresenter.PopulateRowDetail(row);
        }

        row.IsExpanded = !row.IsExpanded;
        ToggleOpcodeTraceExpand.IsChecked = row.IsExpanded;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Row_DoubleClick
    //
    // Mouse-down handler for an Opcode Trace row's summary grid.  Gated on
    // ClickCount == 2 so it acts only on double-clicks.  Resolves the row from
    // the sender's DataContext and toggles its expand state.
    //
    // sender:  The summary Grid that raised the event.
    // e:       Standard mouse event args; marked Handled on a double-click so
    //          the ListView does not also process it for selection side effects.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Row_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        FrameworkElement? element = sender as FrameworkElement;
        if (element == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "Row_DoubleClick: sender was not a FrameworkElement, ignoring", LogLevel.Warn);
            return;
        }

        OpcodeTraceRow? row = element.DataContext as OpcodeTraceRow;
        if (row == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "Row_DoubleClick: DataContext was not OpcodeTraceRow, ignoring", LogLevel.Error);
            return;
        }

        ToggleRowExpand(row);
        e.Handled = true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ArmColorPatch
    //
    // Arms a color patch: restores any previously armed patch's border, sets this patch's
    // border to the armed brush, records it as the armed patch, and pushes its color to the
    // presenter as the active highlight color that subsequent highlighting operations stamp
    // their spans with.  Exactly one patch is armed at a time.
    //
    // The patch's color is taken from its Tag, a string of the form "0xAARRGGBB".  A Tag that
    // is missing or does not parse is a construction error: the method logs and returns without
    // changing the armed patch, so the armed state and the presenter's active color stay
    // consistent.
    //
    // patch:  The color patch Border to arm.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ArmColorPatch(Border patch)
    {
        string? tagText = patch.Tag as string;
        if (tagText == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "ArmColorPatch: patch has no Tag, ignoring", LogLevel.Error);
            return;
        }

        uint raw;
        if (!uint.TryParse(tagText.Substring(2),
            System.Globalization.NumberStyles.HexNumber, null, out raw))
        {
            DebugLog.Write(LogChannel.Opcodes,
                "ArmColorPatch: could not parse Tag '" + tagText + "' as hex uint, ignoring",
                LogLevel.Error);
            return;
        }

        ArgbColor argb = new ArgbColor(raw);

        if (_armedColorPatch != null)
        {
            _armedColorPatch.BorderBrush = Brushes.Gray;
        }
        patch.BorderBrush = Brushes.White;
        _armedColorPatch = patch;

        _opcodeTracePresenter.SetActiveHighlightColor(argb);

        DebugLog.Write(LogChannel.Opcodes,
            "ArmColorPatch: armed patch tag='" + tagText + "' color=0x" + argb.ToString(),
            LogLevel.Info);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ColorPatch_MouseLeftButtonUp
    //
    // Click handler shared by every color patch on the Opcode Trace toolbar.  Clicking a patch
    // arms it through ArmColorPatch and turns off the previous patch.
    // Only one patch is armed at a time.
    //
    // sender:  The Border that raised the event.
    // e:       Standard mouse event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ColorPatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        Border? patch = sender as Border;
        if (patch == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "ColorPatch_MouseLeftButtonUp: sender was not a Border, ignoring", LogLevel.Error);
            return;
        }

        // skip arming the same color twice
        if (ReferenceEquals(_armedColorPatch, patch))
        {
            return;
        }

        ArmColorPatch(patch);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Button_OpcodeTraceRefresh_Click
    //
    // Refresh button for the Opcode Trace tab.  Asks the presenter to
    // rebuild the row list from the catalog's current state.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Button_OpcodeTraceRefresh_Click(object sender, RoutedEventArgs e)
    {
        _opcodeTracePresenter.Refresh();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_OpcodeTraceHide_Click
    //
    // Gathers the distinct wire opcode values from the rows currently
    // selected in OpcodeTraceList and passes them to the presenter to be
    // hidden as a class.  Multi-select is honored: every distinct opcode
    // represented in the selection is hidden, which collapses every row
    // for those opcodes regardless of whether the row itself was
    // selected.
    //
    // No-ops with a log line when the selection is empty.
    //
    // sender:  The Hide button on the trace toolbar.
    // e:       Routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_OpcodeTraceHide_Click(object sender, RoutedEventArgs e)
    {
        System.Collections.IList selected = OpcodeTraceList.SelectedItems;
        if (selected.Count == 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "Button_OpcodeTraceHide_Click: no rows selected, ignoring", LogLevel.Trace);
            return;
        }

        HashSet<OpcodeValue> opcodes = new HashSet<OpcodeValue>();
        for (int i = 0; i < selected.Count; i++)
        {
            OpcodeTraceRow? row = selected[i] as OpcodeTraceRow;
            if (row == null)
            {
                continue;
            }
            opcodes.Add(row.OpcodeValue);
        }

        _opcodeTracePresenter.SetOpcodesHidden(opcodes, true);

        DebugLog.Write(LogChannel.Opcodes,
            "Button_OpcodeTraceHide_Click: hid " + opcodes.Count
            + " opcode(s) from " + selected.Count + " selected row(s)", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////
    // OpcodeTraceList_ContextMenuOpening
    //
    // Hit-tests the mouse position against the list to find the row under
    // the cursor and stashes it in _contextMenuRow for the menu's Click
    // handlers to read.  When the cursor is not over a row (empty space
    // below the last item, header area, etc.) _contextMenuRow is set to
    // null and the event is marked Handled so the menu does not open at
    // all.
    //
    // sender:  The trace ListView.
    // e:       Context menu opening event args.
    ///////////////////////////////////////////////////////////////////////////////////////
    private void OpcodeTraceList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        Point mousePosition = Mouse.GetPosition(OpcodeTraceList);
        HitTestResult hitResult = VisualTreeHelper.HitTest(OpcodeTraceList, mousePosition);
        if (hitResult == null)
        {
            _contextMenuRow = null;
            e.Handled = true;
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTraceList_ContextMenuOpening: no hit, menu suppressed", LogLevel.Warn);
            return;
        }

        DependencyObject? current = hitResult.VisualHit;
        ListViewItem? container = null;
        while (current != null)
        {
            container = current as ListViewItem;
            if (container != null)
            {
                break;
            }
            current = VisualTreeHelper.GetParent(current);
        }

        if (container == null)
        {
            _contextMenuRow = null;
            e.Handled = true;
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTraceList_ContextMenuOpening: hit was not under a ListViewItem, menu suppressed", LogLevel.Warn);
            return;
        }

        OpcodeTraceRow? row = container.DataContext as OpcodeTraceRow;
        if (row == null)
        {
            _contextMenuRow = null;
            e.Handled = true;
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTraceList_ContextMenuOpening: container DataContext was not OpcodeTraceRow, menu suppressed", LogLevel.Warn);
            return;
        }

        _contextMenuRow = row;
        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTraceList_ContextMenuOpening: menu opening for packetIndex="
            + row.PacketIndex, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_OpcodeTraceOpenDetail_Click
    //
    // Opens a modeless PacketDetailWindow for the row that was under the
    // cursor when the context menu opened.  Looks up the CatalogedPacket
    // by the row's PacketIndex and passes it to the window.  No-ops on
    // any miss (no stashed row, catalog returned null) with a log line.
    //
    // sender:  The menu item.
    // e:       Routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_OpcodeTraceOpenDetail_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuRow == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "MenuItem_OpcodeTraceOpenDetail_Click: no stashed row, ignoring", LogLevel.Warn);
            return;
        }

        OpcodeTraceRow row = _contextMenuRow;
        CatalogedPacket? packet = _packetCatalog.PacketAt(row.PacketIndex);
        if (packet == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "MenuItem_OpcodeTraceOpenDetail_Click: catalog has no packet at index "
                + row.PacketIndex, LogLevel.Error);
            return;
        }

        PacketDetailWindow window = new PacketDetailWindow(packet.Value);
        window.Owner = this;
        window.Show();

        DebugLog.Write(LogChannel.InferenceDebug,
            "MenuItem_OpcodeTraceOpenDetail_Click: opened detail window for packetIndex="
            + row.PacketIndex, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////
    // ShowFindBar
    //
    // Makes the trace find bar visible and moves keyboard focus to the
    // find text box, selecting any existing text so a new query
    // overwrites it.  Idempotent: calling when the bar is already
    // visible just re-focuses and re-selects.
    ///////////////////////////////////////////////////////////////////////////////////////
    private void ShowFindBar()
    {
        OpcodeTraceFindBar.Visibility = Visibility.Visible;
        TextBoxOpcodeTraceFind.Focus();
        TextBoxOpcodeTraceFind.SelectAll();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ApplyArmedColor
    //
    // Common logic for the Color packet and Color opcode momentary toggles.
    // Validates that a color is armed and a row is selected, parses the
    // armed patch's Tag to a uint ARGB value, and asks the presenter to
    // apply it via the supplied delegate.  The toggle is returned to its
    // unchecked state on every path so it behaves as momentary.
    //
    // toggle:       The toggle button that fired.  Always reset to
    //               unchecked before this method returns.
    // applyAction:  Presenter call that writes the color into the right
    //               map (per-packet or per-opcode) and refreshes affected
    //               rows.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ApplyArmedColor(ToggleButton toggle, Action<uint, OpcodeTraceRow> applyAction)
    {
        toggle.IsChecked = false;

        if (_armedColorPatch == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "ApplyArmedColor: no color armed, ignoring", LogLevel.Error);
            return;
        }

        OpcodeTraceRow? row = OpcodeTraceList.SelectedItem as OpcodeTraceRow;
        if (row == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "ApplyArmedColor: no row selected, ignoring", LogLevel.Error);
            return;
        }

        string? tagText = _armedColorPatch.Tag as string;
        if (tagText == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "ApplyArmedColor: armed patch has no Tag, ignoring", LogLevel.Error);
            return;
        }

        uint argb;
        if (!uint.TryParse(tagText.Substring(2),
            System.Globalization.NumberStyles.HexNumber, null, out argb))
        {
            DebugLog.Write(LogChannel.Opcodes,
                "ApplyArmedColor: could not parse Tag '" + tagText + "' as hex uint", LogLevel.Error);
            return;
        }

        applyAction(argb, row);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AnalyzeOpcode
    //
    // Computes constant-byte analysis across the given packets and builds hex dump
    // data for display. Each packet's payload is formatted as a hex dump with
    // constant bytes highlighted. Results are dispatched to the UI thread for
    // rendering.  The hex dump is capped at _analysisMaxHexBytes per packet for
    // display purposes only; the analysis itself runs over the full payload.
    //
    // packets:  List of cataloged packets to analyze.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AnalyzeOpcode(List<CatalogedPacket> packets)
    {
        bool[] isConstant = ComputeConstantBytes(packets);
        List<HexDumpSample> dumpData = new List<HexDumpSample>();
        for (int packetIndex = 0; packetIndex < packets.Count; packetIndex++)
        {
            CatalogedPacket packet = packets[packetIndex];
            ReadOnlySpan<byte> payload = packet.Payload.AsReadOnlySpan();
            int payloadLength = payload.Length;
            string name = "(unknown)";

            if (packet.Metadata.SessionId >= 0)
            {
                name = "(" + GlassContext.SessionRegistry.CharacterNameFromSession(packet.Metadata.SessionId) + ")";
            }
            DebugLog.Write(LogChannel.Fields, "session " + packet.Metadata.SessionId + " is [" +
                name + "] and using metadata we get " + GlassContext.SessionRegistry.CharacterNameFromMetadata(packet.Metadata), LogLevel.Trace);

            int displayLength = Math.Min(payloadLength, _analysisMaxHexBytes);
            bool truncated = payloadLength > _analysisMaxHexBytes;
            HexDumpSample dumpSample = new HexDumpSample();
            dumpSample.Header = "--- Packet " + (packetIndex + 1)
                + "  " + packet.Metadata.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff")
                + "  (" + payloadLength + " bytes)"
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
                    line.Bytes[i].Value = payload[offset + i];
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
    // packets:  List of cataloged packets to compare.
    // Returns:  Boolean array indexed by byte offset. True if that byte position
    //           has the same value across all packets.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private bool[] ComputeConstantBytes(List<CatalogedPacket> packets)
    {
        if (packets.Count == 0)
        {
            return Array.Empty<bool>();
        }
        int maxLength = 0;
        for (int i = 0; i < packets.Count; i++)
        {
            int payloadLength = packets[i].Payload.Length;
            if (payloadLength > maxLength)
            {
                maxLength = payloadLength;
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
                ReadOnlySpan<byte> payload = packets[packetIndex].Payload.AsReadOnlySpan();
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
    ///////////////////////////////////////////////////////////////////////////////////////////
    // HandleISXGlassMessage
    //
    // Opcode a message from the ISXGlass extension.  These are typically session management
    // notifications that must be passed on.
    //
    // msg:  The text of the message to process
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void HandleISXGlassMessage(string msg)
    {
        DebugLog.Write(LogChannel.ISXGlass, $"ISXGlass: message in {msg}", LogLevel.Info);
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
                        DebugLog.Write(LogChannel.InferenceDebug, $"ISXGlass: malformed session_connected: {msg}", LogLevel.Error);
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
                            DebugLog.Write(LogChannel.InferenceDebug, $"ISXGlass: no integer account-id: {sessionName}", LogLevel.Error);
                            return;
                        }

                        characterName = GlassContext.ProfileManager.GetCharacterNameByAccountId(accountId);
                        DebugLog.Write(LogChannel.ISXGlass, $"session connected: {sessionName}, pid={pid}, character={characterName}", LogLevel.Info);
                        GlassContext.SessionRegistry.OnSessionConnected(sessionName, characterName, pid, hwnd);
                        int slot = GlassContext.ProfileManager.GetSlotForCharacter(characterName);
                        if (slot == -1)
                        {
                            return;
                        }

                        string cmd = $"slot_assign {slot} {sessionName} {hwnd:X}";
                        DebugLog.Write(LogChannel.ISXGlass, $"HandleISXGlassMessage: sending {cmd}", LogLevel.Trace);
                        GlassContext.GlassVideoPipe.Send(cmd);
                    }
                    else
                    {
                        DebugLog.Write(LogChannel.ISXGlass, $"session connected: {sessionName}, pid={pid}, no active profile.", LogLevel.Error);
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
                        DebugLog.Write(LogChannel.InferenceDebug, $"ISXGlass: malformed session_disconnected: {msg}", LogLevel.Error);
                        return;
                    }

                    string sessionName = parts[1];
                    DebugLog.Write(LogChannel.ISXGlass, $"session_disconnected: {sessionName}", LogLevel.Trace);
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
    // UIReset
    //
    // Returns the application to its initial state.  Clears the packet catalog
    // and both presenters, closes any open OpcodeManageWindow, clears the
    // candidate grid and hex dump display, resets the Analysis tab filters
    // to their first item, collapses the find bar and clears its text, and
    // clears the trace status text blocks.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void UIReset()
    {
        _packetCatalog.Clear();
        _opcodeRowPresenter.Clear();
        _opcodeTracePresenter.Clear();

        Window[] ownedSnapshot = new Window[OwnedWindows.Count];
        OwnedWindows.CopyTo(ownedSnapshot, 0);
        for (int ownedIndex = 0; ownedIndex < ownedSnapshot.Length; ownedIndex++)
        {
            OpcodeManageWindow? manageWindow = ownedSnapshot[ownedIndex] as OpcodeManageWindow;
            if (manageWindow != null)
            {
                manageWindow.Close();
            }
        }

        CandidateGrid.ItemsSource = null;
        HexDumpDisplay.Document.Blocks.Clear();

        if (AnalysisSessionFilter.Items.Count > 0)
        {
            AnalysisSessionFilter.SelectedIndex = 0;
        }
        if (AnalysisChannelFilter.Items.Count > 0)
        {
            AnalysisChannelFilter.SelectedIndex = 0;
        }

        OpcodeTraceFindBar.Visibility = Visibility.Collapsed;
        TextBoxOpcodeTraceFind.Text = string.Empty;

        StatusBarRowText.Text = string.Empty;
        StatusBarSecondaryText.Text = string.Empty;

        DebugLog.Write(LogChannel.InferenceDebug, "MainWindow.UIReset: complete", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HandleAppPacket
    //
    // PacketBus delivery target for the Opcodes grid.  Storage is handled
    // by PacketCatalog, which is subscribed to the same bus and runs before
    // this handler in subscription order.  This handler dispatches to the UI
    // thread and asks the row presenter to update the grid row for the
    // opcode.
    //
    // data:      Unused by this handler.  PacketCatalog has already retained
    //            its own copy of the bytes.
    // opcode:    Wire opcode value.
    // metadata:  Unused by this handler.  PacketCatalog has already recorded
    //            the metadata alongside the retained payload.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void HandleAppPacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        uint packetLength = (uint)data.Length;

        Dispatcher.BeginInvoke(() =>
        {
            _opcodeRowPresenter.Update(metadata, packetLength);
        });
    }
}