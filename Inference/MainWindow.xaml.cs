using Glass.Core;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Capture;
using Inference.Core;
using Inference.Dialogs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Inference;

///////////////////////////////////////////////////////////////////////////////////////////////
// MainWindow
//
// Main window for the Inference tool.
///////////////////////////////////////////////////////////////////////////////////////////////
public partial class MainWindow : Window
{
    private bool _hasPatchLevel = false;
    private bool _hasUnsavedChanges = false;
    private readonly Stack<object> _undoStack = new Stack<object>();
    private SessionDemux? _sessionDemux;
    private PacketCapture? _packetCapture;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // MainWindow
    //
    // Constructs the main window and initializes the XAML-defined components.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public MainWindow()
    {
        InitializeComponent();

        GlassContext.ProfileManager = new ProfileManager();

        InferenceDebugLog.Initialize(WriteToDebugLog);
        InferenceLog.Initialize(WriteToInferenceLog);

        InferenceDebugLog.Write("Inference application started");
        InferenceLog.Write("Inference log initialized");
        InitializePipes();
        OpenDatabase();
        RestoreLastPatchLevel();
        UpdateControlStates();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RestoreLastPatchLevel
    //
    // Reads the LastOpenedPatchDate and LastOpenedPatchServerType settings and
    // restores the working patch level if both are present.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void RestoreLastPatchLevel()
    {
        string patchDate = Properties.Settings.Default.LastOpenedPatchDate;
        string serverType = Properties.Settings.Default.LastOpenedPatchServerType;

        if (string.IsNullOrEmpty(patchDate) || string.IsNullOrEmpty(serverType))
        {
            InferenceDebugLog.Write("RestoreLastPatchLevel: no previous patch level found");
            return;
        }

        _hasPatchLevel = true;
        StatusPatchLevel.Text = serverType + " " + patchDate;
        InferenceDebugLog.Write("RestoreLastPatchLevel: restored " + serverType + " " + patchDate);
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
        InferenceDebugLog.Write("Inference application closing");
        InferenceLog.Shutdown();
        InferenceDebugLog.Shutdown();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // InitializePipes
    //
    // Creates and starts the named pipe connections to ISXGlass and GlassVideo.
    // Wires up status update handlers for the status bar.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void InitializePipes()
    {
      //  DebugLog.Initialize(msg => InferenceDebugLog.Write(msg));

        GlassContext.ISXGlassPipe = new PipeManager("ISXGlass", "ISXGlass_Commands", "ISXGlass_Notify");
        GlassContext.ISXGlassPipe.Connected += () => Dispatcher.Invoke(() =>
        {
            StatusIsx.Text = "ISX: Connected";
            InferenceDebugLog.Write("ISXGlass pipe connected");
        });
        GlassContext.ISXGlassPipe.Disconnected += () => Dispatcher.Invoke(() =>
        {
            StatusIsx.Text = "ISX: Disconnected";
            InferenceDebugLog.Write("ISXGlass pipe disconnected");
        });
        GlassContext.ISXGlassPipe.MessageReceived += msg => Dispatcher.Invoke(() => HandleISXGlassMessage(msg));
        GlassContext.ISXGlassPipe.Start();

        GlassContext.GlassVideoPipe = new PipeManager("GlassVideo", "GlassVideo_Cmd", "GlassVideo_Notify");
        GlassContext.GlassVideoPipe.Connected += () => Dispatcher.Invoke(() =>
        {
            StatusGlassVideo.Text = "GlassVideo: Connected";
            InferenceDebugLog.Write("GlassVideo pipe connected");
        });
        GlassContext.GlassVideoPipe.Disconnected += () => Dispatcher.Invoke(() =>
        {
            StatusGlassVideo.Text = "GlassVideo: Not Running";
            InferenceDebugLog.Write("GlassVideo pipe disconnected");
        });
        GlassContext.GlassVideoPipe.MessageReceived += msg => Dispatcher.Invoke(() =>
        {
            InferenceDebugLog.Write("GlassVideo message: " + msg);
        });
        GlassContext.GlassVideoPipe.Start();

        InferenceDebugLog.Write("InitializePipes: pipes started");
        
        GlassContext.FocusTracker = new FocusTracker();
        GlassContext.SessionRegistry = new SessionRegistry();


        InferenceDebugLog.Write("InitializePipes: session registry and focus tracker initialized");
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
        bool hasPatchLevel = _hasPatchLevel;
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
            InferenceDebugLog.Write("OpenDatabase: database not found at " + dbPath);
            return;
        }

        Glass.Data.Database.Open(dbPath);
        InferenceDebugLog.Write("OpenDatabase: opened " + dbPath);
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
        InferenceDebugLog.Write("OpcodeGrid_SelectionChanged");
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
        InferenceDebugLog.Write("CandidateGrid_SelectionChanged");
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
        InferenceDebugLog.Write("MenuItem_NewPatchLevel_Click");

        NewPatchLevelDialog dialog = new NewPatchLevelDialog();
        dialog.Owner = this;

        if (dialog.ShowDialog() == true)
        {
            string patchDate = dialog.PatchDate.ToString("yyyy-MM-dd");
            string serverType = dialog.ServerType;
            string entry = serverType + "|" + patchDate;

            InferenceDebugLog.Write("New patch level created: " + entry);

            _hasPatchLevel = true;
            StatusPatchLevel.Text = serverType + " " + patchDate;

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
        InferenceDebugLog.Write("MenuItem_OpenPatchLevel_Click");

        OpenDialog dialog = new OpenDialog();
        dialog.Owner = this;

        if (dialog.ShowDialog() == true)
        {
            InferenceDebugLog.Write("Opened patch level: ServerType="
                + dialog.ServerType + " PatchDate=" + dialog.PatchDate);

            _hasPatchLevel = true;
            StatusPatchLevel.Text = dialog.ServerType + " " + dialog.PatchDate;

            Properties.Settings.Default.LastOpenedPatchDate = dialog.PatchDate;
            Properties.Settings.Default.LastOpenedPatchServerType = dialog.ServerType;
            Properties.Settings.Default.Save();

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
        InferenceDebugLog.Write("MenuItem_LaunchProfile_Click");

        string serverType = Properties.Settings.Default.LastOpenedPatchServerType;
        LaunchProfileDialog dialog = new LaunchProfileDialog(serverType);
        dialog.Owner = this;

        if (dialog.ShowDialog() == true)
        {
            InferenceDebugLog.Write("Profile selected: " + dialog.SelectedProfileName);

            (int deviceIndex, string? localIp) = PacketCapture.GetDefaultCaptureDevice();
            if (deviceIndex == -1 || localIp == null)
            {
                InferenceDebugLog.Write("MenuItem_LaunchProfile_Click: no capture device found");
                MessageBox.Show("No suitable capture device found. Is Npcap installed?",
                    "Capture Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            DebugLog.Log_Network = false;
            InferenceDebugLog.Write("MenuItem_LaunchProfile_Click: capture device index="
                + deviceIndex + " localIp=" + localIp);

            _sessionDemux = new SessionDemux(localIp);
            _packetCapture = new PacketCapture(_sessionDemux);

            string bpfFilter = "udp and (net 69.0.0.0/8 or net 64.0.0.0/8)";
            if (!_packetCapture.Start(bpfFilter, deviceIndex))
            {
                InferenceDebugLog.Write("MenuItem_LaunchProfile_Click: capture failed to start");
                MessageBox.Show("Failed to start packet capture.",
                    "Capture Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            InferenceDebugLog.Write("MenuItem_LaunchProfile_Click: capture started");
            StatusCapture.Text = "Capture: Active";

            await GlassContext.ProfileManager.LaunchProfile(dialog.SelectedProfileName);
        }
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
        InferenceDebugLog.Write("MenuItem_Save_Click");
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
        InferenceDebugLog.Write("MenuItem_Exit_Click");
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
        InferenceDebugLog.Write("MenuItem_Undo_Click");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_Analyze_Click
    //
    // Handles the Analyze button click on the Opcodes tab.
    // Triggers analysis on the currently selected opcode row.
    //
    // sender:  The button that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (OpcodeGrid.SelectedItem == null)
        {
            InferenceDebugLog.Write("Button_Analyze_Click: no opcode selected");
            return;
        }
        InferenceDebugLog.Write("Button_Analyze_Click: analyzing selected opcode");
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
            InferenceDebugLog.Write("ToggleButton_AcceptCandidate_Click: no candidate selected");
            return;
        }
        System.Windows.Controls.Primitives.ToggleButton toggle = (System.Windows.Controls.Primitives.ToggleButton)sender;
        bool isAccepted = toggle.IsChecked == true;
        InferenceDebugLog.Write("ToggleButton_AcceptCandidate_Click: accepted=" + isAccepted);
    }

    private void HandleISXGlassMessage(string msg)
    {
        DebugLog.Write($"ISXGlass: message in {msg}");
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
                        InferenceDebugLog.Write($"ISXGlass: malformed session_connected: {msg}");
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
                            InferenceDebugLog.Write($"ISXGlass: no integer account-id: {sessionName}");
                            return;
                        }

                        characterName = GlassContext.ProfileManager.GetCharacterNameByAccountId(accountId);
                        InferenceDebugLog.Write($"session connected: {sessionName}, pid={pid}, character={characterName}");
                        GlassContext.SessionRegistry.OnSessionConnected(sessionName, characterName, pid, hwnd);
                        int slot = GlassContext.ProfileManager.GetSlotForCharacter(characterName);
                        if (slot == -1)
                        {
                            return;
                        }

                        string cmd = $"slot_assign {slot} {sessionName} {hwnd:X}";
                        InferenceDebugLog.Write($"HandleISXGlassMessage: sending {cmd}");
                        GlassContext.GlassVideoPipe.Send(cmd);
                    }
                    else
                    {
                        InferenceDebugLog.Write($"session connected: {sessionName}, pid={pid}, no active profile.");
                        GlassContext.SessionRegistry.OnSessionConnected(sessionName, characterName, pid, hwnd);
                    }

                    break;
                }

            case "session_disconnected":
                {
                    if (parts.Length < 2)
                    {
                        InferenceDebugLog.Write($"ISXGlass: malformed session_disconnected: {msg}");
                        return;
                    }

                    string sessionName = parts[1];
                    InferenceDebugLog.Write($"session_disconnected: {sessionName}");
                    GlassContext.GlassVideoPipe.Send($"unassign {sessionName}");
                    GlassContext.SessionRegistry.OnSessionDisconnected(sessionName);
                    break;
                }

            default:
                InferenceDebugLog.Write($"{msg}");
                break;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // WriteToDebugLog
    //
    // Callback for DebugLog. Appends a message to the Debug Log list box.
    // Dispatches to the UI thread if called from a background thread.
    //
    // message:  The message to display.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void WriteToDebugLog(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => WriteToDebugLog(message));
            return;
        }
        DebugLogOutput.AppendText(message + Environment.NewLine);
        DebugLogScroller.ScrollToEnd();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // WriteToInferenceLog
    //
    // Callback for InferenceLog. Appends a message to the Inference Log list box.
    // Dispatches to the UI thread if called from a background thread.
    //
    // message:  The message to display.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void WriteToInferenceLog(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => WriteToInferenceLog(message));
            return;
        }
        InferenceLogList.Items.Add(message);
        InferenceLogList.ScrollIntoView(InferenceLogList.Items[InferenceLogList.Items.Count - 1]);
    }
}