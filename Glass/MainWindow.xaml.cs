using Glass.ClientUI;
using Glass.Core;
using Glass.Data;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Input;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Glass;

public partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    private readonly HashSet<int> _definedSlots = new();
    private ProfileRepository? _activeProfile;


    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MainWindow
    //
    // Initializes UI, database, pipe managers, keyboard manager, and logging.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public MainWindow()
    {
        InitializeComponent();
        SetDatabaseMenuState(false);
        AutoLoadDatabase();

        DebugLog.Initialize(msg => Dispatcher.Invoke(() => Log(msg)));

        if (Database.IsInitialized)
        {
            MachineRepository machineRepo = new MachineRepository();
            GlassContext.CurrentMachine = machineRepo.GetOrCreate(Environment.MachineName);
            DebugLog.Write($"MainWindow: current machine id={GlassContext.CurrentMachine.Id} name='{GlassContext.CurrentMachine.Name}'.");
            if (GlassContext.CurrentMachine.Devices.Count == 0)
            {
                DebugLog.Write("MainWindow: no devices configured for this machine.");
            }
        }



        GlassContext.ISXGlassPipe = new PipeManager("ISXGlass", "ISXGlass_Commands", "ISXGlass_Notify");
        GlassContext.ISXGlassPipe.Connected += () => Dispatcher.Invoke(() => SetISXGlassStatus(true));
        GlassContext.ISXGlassPipe.Disconnected += () => Dispatcher.Invoke(() => SetISXGlassStatus(false));
        GlassContext.ISXGlassPipe.MessageReceived += msg => Dispatcher.Invoke(() => HandleISXGlassMessage(msg));
        GlassContext.ISXGlassPipe.Start();

        GlassContext.KeyboardManager = new KeyboardManager();

        GlassContext.GlassVideoPipe = new PipeManager("GlassVideo", "GlassVideo_Cmd", "GlassVideo_Notify");
        GlassContext.GlassVideoPipe.Connected += () => Dispatcher.Invoke(() => SetGlassVideoStatus(true));
        GlassContext.GlassVideoPipe.Disconnected += () => Dispatcher.Invoke(() =>
        {
            SetGlassVideoStatus(false);
            DebugLog.Write(DebugLog.Log_Sessions, "GlassVideo disconnected.");
        });
        GlassContext.GlassVideoPipe.MessageReceived += msg => Dispatcher.Invoke(() => Log($"GlassVideo: {msg}"));
        GlassContext.GlassVideoPipe.Start();
        GlassContext.FocusTracker = new FocusTracker();
        GlassContext.SessionRegistry = new SessionRegistry();
        GlassContext.SessionRegistry.AllSessionsDisconnected += OnAllSessionsDisconnected;
    }


    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Window_Loaded
    //
    // Called when the main window has finished loading.
    // Starts G-key input handling.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Window_Closing
    //
    // Stops the pipe manager and keyboard manager cleanly before the window closes.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        DebugLog.Write("MainWindow.Window_Closing: shutting down.");
        DebugLog.Shutdown();
        GlassContext.KeyboardManager.UnloadProfile();
        await GlassContext.ISXGlassPipe.StopAsync();
        GlassContext.ISXGlassPipe.Dispose();
        await GlassContext.GlassVideoPipe.StopAsync();
        GlassContext.GlassVideoPipe.Dispose();
        GlassContext.FocusTracker.Stop();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Log
    //
    // Appends a timestamped message to the console output.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Log(string message)
    {
        ConsoleOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        ConsoleScroller.ScrollToBottom();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetISXGlassStatus
    //
    // Updates the ISXGlass connection status indicator.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void SetISXGlassStatus(bool connected)
    {
        ISXGlassStatusIndicator.Fill = connected
            ? new SolidColorBrush(Colors.LimeGreen)
            : new SolidColorBrush(Colors.Red);
        ISXGlassStatusText.Text = connected ? "ISXGlass: Connected" : "ISXGlass: Not Connected";
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetGlassVideoStatus
    //
    // Updates the GlassVideo connection status indicator.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void SetGlassVideoStatus(bool connected)
    {
        GlassVideoStatusIndicator.Fill = connected
            ? new SolidColorBrush(Colors.LimeGreen)
            : new SolidColorBrush(Colors.Red);
        GlassVideoStatusText.Text = connected ? "GlassVideo: Connected" : "GlassVideo: Not Connected";
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetDatabaseMenuState
    //
    // Enables or disables menu items that require an open database.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void SetDatabaseMenuState(bool isOpen)
    {
        MenuEdit.IsEnabled = isOpen;
        MenuSessions.IsEnabled = isOpen;
        MenuTools.IsEnabled = isOpen;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SaveLastDatabasePath
    //
    // Persists the last opened database path to user settings.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void SaveLastDatabasePath(string path)
    {
        Properties.Settings.Default.LastDatabasePath = path;
        Properties.Settings.Default.Save();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AutoLoadDatabase
    //
    // Reopens the last database on startup if it still exists.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void AutoLoadDatabase()
    {
        var path = Properties.Settings.Default.LastDatabasePath;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            Database.Open(path);
            SetDatabaseMenuState(true);
            BuildRecentProfilesMenu();
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // BuildRecentProfilesMenu
    //
    // Rebuilds the recent profiles list.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void BuildRecentProfilesMenu()
    {
        var recent = RecentProfiles.Get();

        var editItemsToRemove = MenuEdit.Items.OfType<MenuItem>()
            .Where(i => i.Tag as string == "recent")
            .ToList();
        foreach (var item in editItemsToRemove)
        {
            MenuEdit.Items.Remove(item);
        }

        var sessionItemsToRemove = MenuSessions.Items.OfType<MenuItem>()
            .Where(i => i.Tag as string == "recent")
            .ToList();
        foreach (var item in sessionItemsToRemove)
        {
            MenuSessions.Items.Remove(item);
        }

        var firstSeparatorIndex = MenuEdit.Items.OfType<Separator>()
            .Select(s => MenuEdit.Items.IndexOf(s))
            .FirstOrDefault();

        foreach (var name in recent)
        {
            var editItem = new MenuItem { Header = name, Tag = "recent" };
            editItem.Click += (s, e) =>
            {
                var dialog = new ProfileDialog(name) { Owner = this };
                dialog.ShowDialog();
            };
            MenuEdit.Items.Insert(firstSeparatorIndex + 1, editItem);

            var sessionItem = new MenuItem { Header = name, Tag = "recent" };
            sessionItem.Click += async (s, e) =>
            {
                await LaunchProfile(name);
            };
            MenuSessions.Items.Add(sessionItem);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LaunchProfile
    //
    // Launches a specified profile.  Sends a launch command to ISXGlass for the given profile, and sets up
    // GlassVideo for all characters in the profile.
    //
    // profile:  The profile to launch
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private async Task LaunchProfile(string profileName)
    {
        var repo = new ProfileRepository(profileName);
        var slots = repo.GetSlots();
        var charRepo = new CharacterRepository();

        if (_activeProfile != null)
        {
            DebugLog.Write("MainWindow.LaunchProfile: a profile is already active, refusing launch.");
            MessageBox.Show("A profile is already active. Please wait for all sessions to disconnect before launching a new profile.", "Profile Active", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Log($"Launching profile: {profileName} ({slots.Count} characters)");
        _activeProfile = repo;
        _definedSlots.Clear();
        GlassContext.FocusTracker.Start();

        GlassContext.ISXGlassPipe.Send("new_profile");
        PushRelayGroupState(repo.GetId());
        PushCommandState();

        // Load slot placements from the first layout for this profile.
        var layoutRepo = new WindowLayoutRepository();
        var placements = layoutRepo.GetLayout(profileName);
        DebugLog.Write(DebugLog.Log_Database, $"LaunchProfile: {placements.Count} slot placements loaded for profile '{profileName}'.");

        // Send any new slot definitions to GlassVideo.
        foreach (var placement in placements)
        {
            if (!_definedSlots.Contains(placement.SlotNumber))
            {
                string cmd = $"slot_define {placement.SlotNumber} {placement.X} {placement.Y} {placement.Width} {placement.Height}";
                DebugLog.Write(DebugLog.Log_Sessions, $"LaunchProfile: sending {cmd}");
                GlassContext.GlassVideoPipe.Send(cmd);
                _definedSlots.Add(placement.SlotNumber);
            }
        }

        // Send slot_assign for any already-connected sessions.
        foreach (var session in GlassContext.SessionRegistry.GetSessions())
        {
            var assignment = slots.FirstOrDefault(s =>
                charRepo.GetById(s.CharacterId)?.Name == session.CharacterName);

            if ((assignment != null) && (session.Hwnd != IntPtr.Zero))
            {
                string cmd = $"slot_assign {assignment.SlotNumber} {session.SessionName} {session.Hwnd:X}";
                DebugLog.Write(DebugLog.Log_Sessions, $"LaunchProfile: sending {cmd}");
                GlassContext.GlassVideoPipe.Send(cmd);
            }
        }

        // Launch characters with small random delays.
        var rng = new Random();
        foreach (var slot in slots)
        {
            Character? character = charRepo.GetById(slot.CharacterId);
            if (character == null)
            {
                DebugLog.Write(DebugLog.Log_Sessions, $"LaunchProfile: no character found for id={slot.CharacterId}, skipping.");
                continue;
            }
            GlassContext.KeyboardManager.LoadProfile(profileName);
            Log($"  Launching: {character.Name} accountId={character.AccountId} server={character.Server} id={character.Id}");
            GlassContext.ISXGlassPipe.Send($"launch {character.AccountId} {character.Name} {character.Server} {character.Id}");
            int delay = rng.Next(4000, 7000);
            await Task.Delay(delay);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ToggleG15Osd_Click
    //
    // Temporarily toggles the G15 OSD for testing.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ToggleG15Osd_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("MainWindow.ToggleG15Osd_Click: toggling G15 OSD.");
        GlassContext.KeyboardManager.ToggleOsd(new HidDeviceInstance(KeyboardType.G15, 1, string.Empty));
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ConsoleInput_KeyDown
    //
    // Sends console input to ISXGlass when Enter is pressed.
    // Lines prefixed with '/' are handled locally by Glass.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private void ConsoleInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter)
            return;

        var input = ConsoleInput.Text.Trim();
        if (string.IsNullOrEmpty(input))
            return;

        Log($"> {input}");

        if (input.StartsWith("/"))
        {
            HandleLocalCommand(input.Substring(1));
        }
        else
        {
            GlassContext.ISXGlassPipe.Send(input);
        }

        ConsoleInput.Clear();
        e.Handled = true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PushRelayGroupState
    //
    // Sends the full relay group membership state for the given profile to ISXGlass.
    // Sends one message per group: "relaygroup <groupId> <characterId1> <characterId2>..."
    // Groups with no profile members are skipped.
    // Called on ISXGlass connect if a profile is active, and on profile load if already connected.
    //
    // profileId:  The profile whose relay group state to push
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void PushRelayGroupState(int profileId)
    {
        List<RelayGroup> groups = new RelayGroupRepository().GetAllGroupsForProfile(profileId);

        foreach (RelayGroup group in groups)
        {
            if (group.Characters.Count == 0)
            {
                continue;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append($"relay_group {group.Id}");
            foreach (Character character in group.Characters)
            {
                sb.Append($" {character.Id}");
            }

            string message = sb.ToString();
            GlassContext.ISXGlassPipe.Send(message);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PushCommandState
    //
    // Sends all command definitions to ISXGlass over the pipe.
    // Sends cmd_define for each command, followed by cmd_step for each step.
    // Key alias values are resolved to their actual keystroke strings before sending.
    // Called as part of profile launch after new_profile is sent.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void PushCommandState()
    {
        List<Command> commands = new CommandRepository().GetAllCommands();
        KeyAliasRepository aliasRepo = new KeyAliasRepository();

        foreach (Command command in commands)
        {
            Command? full = new CommandRepository().GetCommand(command.Id);
            if (full == null)
            {
                DebugLog.Write($"MainWindow.PushCommandState: commandId={command.Id} not found, skipping.");
                continue;
            }

            if (full.Steps.Count == 0)
            {
                DebugLog.Write($"MainWindow.PushCommandState: commandId={command.Id} name='{command.Name}' has no steps, skipping.");
                continue;
            }

            GlassContext.ISXGlassPipe.Send($"cmd_define {full.Id}");

            foreach (CommandStep step in full.Steps.OrderBy(s => s.Sequence))
            {
                string value = step.Value;

                if (step.Type == "pageload")
                {
                    continue;
                }

                if (step.Type == "key")
                {
                    string? resolved = aliasRepo.Resolve(value);
                    if (resolved != null)
                    {
                        value = resolved;
                    }
                }

                string message = $"cmd_step {full.Id} {step.Sequence} {step.Type} {step.DelayMs} {value}";
                GlassContext.ISXGlassPipe.Send(message);
            }
        }

        DebugLog.Write("MainWindow.PushCommandState: done.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // HandleLocalCommand
    //
    // Local command handler.  These are the "/" commands entered on the Glass console.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleLocalCommand(string cmd)
    {
        var parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return;
        }

        switch (parts[0].ToLower())
        {
            case "log":
                {
                    if (parts.Length == 1)
                    {
                        Log($"pipes:    {DebugLog.Log_Pipes}");
                        Log($"video:    {DebugLog.Log_Video}");
                        Log($"sessions: {DebugLog.Log_Sessions}");
                        Log($"input:    {DebugLog.Log_Input}");
                        break;
                    }
                    if (parts.Length < 3)
                    {
                        Log("Usage: log <feature> on|off");
                        break;
                    }
                    bool enabled = parts[2].ToLower() == "on";
                    if (!DebugLog.SetFlag(parts[1], enabled))
                    {
                        Log($"Unknown log feature: {parts[1]}");
                    }
                    else
                    {
                        Log($"Log_{parts[1]} set to {enabled}");
                    }
                    break;
                }

            default:
                Log($"Unknown command: {parts[0]}");
                break;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // HandleISXGlassMessage
    //
    // Parses and dispatches messages received from ISXGlass over the notify pipe.
    //
    // msg:  The message received
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private void HandleISXGlassMessage(string msg)
    {
        DebugLog.Write($"ISXGlass: message in {msg}");
        var parts = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return;
        }

        switch (parts[0])
        {
            case "session_connected":
                {
                    if (parts.Length < 4)
                    {
                        DebugLog.Write(DebugLog.Log_Input, $"ISXGlass: malformed session_connected: {msg}");
                        return;
                    }

                    string sessionName = parts[1];
                    uint pid = uint.Parse(parts[2]);
                    IntPtr hwnd = (IntPtr)Convert.ToUInt64(parts[3], 16);
                    string characterName = string.Empty;

                    if (_activeProfile != null)
                    {
                        CharacterRepository charRepo = new CharacterRepository();

                        if (uint.TryParse(sessionName.Substring(2), out uint sessionId))
                        {
                            SlotAssignment? assignment = _activeProfile.GetSlots()
                                .FirstOrDefault(s => charRepo.GetById(s.CharacterId)?.AccountId == (int)sessionId);
                            if (assignment != null)
                            {
                                characterName = charRepo.GetById(assignment.CharacterId)?.Name ?? string.Empty;
                            }
                        }

                        DebugLog.Write(DebugLog.Log_Sessions, $"session connected: {sessionName}, pid={pid}, character={characterName}");
                        GlassContext.SessionRegistry.OnSessionConnected(sessionName, characterName, pid, hwnd);

                        if (hwnd != IntPtr.Zero)
                        {
                            SlotAssignment? assignment = _activeProfile.GetSlots()
                                .FirstOrDefault(s => charRepo.GetById(s.CharacterId)?.Name == characterName);
                            if (assignment != null)
                            {
                                string cmd = $"slot_assign {assignment.SlotNumber} {sessionName} {hwnd:X}";
                                DebugLog.Write(DebugLog.Log_Sessions, $"HandleISXGlassMessage: sending {cmd}");
                                GlassContext.GlassVideoPipe.Send(cmd);
                            }
                            else
                            {
                                DebugLog.Write(DebugLog.Log_Sessions, $"HandleISXGlassMessage: no slot assignment found for character '{characterName}'.");
                            }
                        }
                    }
                    else
                    {
                        DebugLog.Write(DebugLog.Log_Sessions, $"session connected: {sessionName}, pid={pid}, no active profile.");
                        GlassContext.SessionRegistry.OnSessionConnected(sessionName, characterName, pid, hwnd);
                    }

                    break;
                }

            case "session_disconnected":
                {
                    if (parts.Length < 2)
                    {
                        DebugLog.Write(DebugLog.Log_Sessions, $"ISXGlass: malformed session_disconnected: {msg}");
                        return;
                    }

                    string sessionName = parts[1];
                    DebugLog.Write(DebugLog.Log_Sessions, $"session_disconnected: {sessionName}");
                    GlassContext.SessionRegistry.OnSessionDisconnected(sessionName);
                    GlassContext.GlassVideoPipe.Send($"unassign {sessionName}");

                    break;
                }

            default:
                Log(msg);
                break;
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
        DebugLog.Write(DebugLog.Log_Sessions, "MainWindow.OnAllSessionsDisconnected: all sessions disconnected, clearing active profile.");
        _activeProfile = null;
        GlassContext.FocusTracker.Stop();
        GlassContext.FocusTracker.ClearActiveSession();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_NewDatabase_Click
    //
    // Prompts the user to create a new database file.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_NewDatabase_Click(object sender, RoutedEventArgs e)
    {
        var defaultDir = System.IO.Path.GetDirectoryName(Database.DefaultPath)!;
        Directory.CreateDirectory(defaultDir);

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Create New Database",
            FileName = "glass.db",
            DefaultExt = ".db",
            Filter = "SQLite Database|*.db",
            InitialDirectory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Glass")
        };

        if (dialog.ShowDialog() == true)
        {
            Database.Create(dialog.FileName);
            Log($"Database created: {dialog.FileName}");
        }

        SaveLastDatabasePath(dialog.FileName);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_OpenDatabase_Click
    //
    // Prompts the user to open an existing database file.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_OpenDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open Database",
            DefaultExt = ".db",
            Filter = "SQLite Database|*.db",
            InitialDirectory = System.IO.Path.GetDirectoryName(Database.DefaultPath)!
        };

        if (dialog.ShowDialog() == true)
        {
            Database.Open(dialog.FileName);
            SetDatabaseMenuState(true);
            Log($"Database opened: {dialog.FileName}");
        }

        SaveLastDatabasePath(dialog.FileName);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_Exit_Click
    //
    // Shuts down the application.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();


    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_EditProfile_Click
    //
    // Opens the select profile dialog and launches the chosen profile for editing.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_EditProfile_Click(object sender, RoutedEventArgs e)
    {
        var select = new SelectProfileDialog { Owner = this };
        if ((select.ShowDialog() == true) && (select.SelectedProfileName != null))
        {
            var dialog = new ProfileDialog(select.SelectedProfileName) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                BuildRecentProfilesMenu();
            }
        }
    }

    // Placeholder for settings dialog.
    private void MenuItem_Settings_Click(object sender, RoutedEventArgs e) { }

    // Opens the select profile dialog and launches the chosen profile.
    private async void MenuItem_Launch_Click(object sender, RoutedEventArgs e)
    {
        var select = new SelectProfileDialog { Owner = this };
        if ((select.ShowDialog() == true) && (select.SelectedProfileName != null))
        {
            await LaunchProfile(select.SelectedProfileName);
        }
    }

    // Opens the new profile dialog and rebuilds the recent profiles menu on save.
    private void MenuItem_NewProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProfileDialog { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            BuildRecentProfilesMenu();
        }
    }

    // Sends a status request to ISXGlass.
    private void MenuItem_Status_Click(object sender, RoutedEventArgs e)
    {
        GlassContext.ISXGlassPipe.Send("status");
        DebugLog.Write(DebugLog.Log_Input, "Status requested.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_ManageCommands_Click
    //
    // Opens the Manage Commands dialog.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_ManageCommands_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ManageCommandsDialog { Owner = this };
        dialog.ShowDialog();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_ManageKeyAliases_Click
    //
    // Opens the Manage Key Aliases dialog.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_ManageKeyAliases_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ManageKeyAliasesDialog { Owner = this };
        dialog.ShowDialog();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ManageMachines_Click
    //
    // Opens the Manage Machines dialog.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ManageMachines_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ManageMachinesDialog { Owner = this };
        dialog.ShowDialog();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // KeyTest_Click
    //
    // Opens the Key Test dialog for verifying KeyDisplayControl rendering.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void KeyTest_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new KeyTestDialog { Owner = this };
        dialog.Show();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_GenerateEQUI_Click
    //
    // Generates EQ client files for all characters in the database.
    // Writes to the "UI Files" folder next to the Glass executable.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_GenerateEQUI_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("MainWindow.MenuItem_GenerateEQUI_Click: generating EQ UI files.");

        string outputDirectory = Glass.Properties.Settings.Default.ClientFilesPath;

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            DebugLog.Write("MainWindow.MenuItem_GenerateEQUI_Click: ClientFilesDirectory not configured.");
            MessageBox.Show("Please configure the Client Files Directory in settings before generating.", "Not Configured", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
            DebugLog.Write($"MainWindow.MenuItem_GenerateEQUI_Click: created output directory '{outputDirectory}'.");
        }

        var characterRepo = new CharacterRepository();
        var characters = characterRepo.GetAll();

        DebugLog.Write($"MainWindow.MenuItem_GenerateEQUI_Click: generating files for {characters.Count} characters.");

        var eqClientGenerator = new EqClientFileGenerator(outputDirectory);
        var hotbuttonGenerator = new HotbuttonFileGenerator(outputDirectory);
        var uiGenerator = new UiFileGenerator(outputDirectory);

        foreach (var character in characters)
        {
            DebugLog.Write($"MainWindow.MenuItem_GenerateEQUI_Click: generating for '{character.Name}'.");
            eqClientGenerator.Generate(character);
            hotbuttonGenerator.Generate(character);
            uiGenerator.Generate(character);
        }

        DebugLog.Write($"MainWindow.MenuItem_GenerateEQUI_Click: done. {characters.Count} characters processed.");
    }
}