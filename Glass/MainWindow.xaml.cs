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

    private readonly PipeManager _isxGlassPipeManager;
    private readonly PipeManager _glassVideoPipeManager;
    private readonly SessionRegistry _sessionRegistry = new();
    private readonly HashSet<int> _definedSlots = new();
    private ProfileRepository? _activeProfile;
    private readonly HidKeyInput _hidKeyInput = new HidKeyInput();
    private Machine? _currentMachine;

    // Constructor — initializes UI, database, pipe manager, and logging.
    public MainWindow()
    {
        InitializeComponent();
        SetDatabaseMenuState(false);
        AutoLoadDatabase();



        DebugLog.Initialize(msg => Dispatcher.Invoke(() => Log(msg)));
        if (Database.IsInitialized)
        {
            var machineRepo = new MachineRepository();

            _currentMachine = machineRepo.GetOrCreate(Environment.MachineName);
            DebugLog.Write($"MainWindow: current machine id={_currentMachine.Id} name='{_currentMachine.Name}'.");

            if (_currentMachine.Devices.Count == 0)
            {
                DebugLog.Write("MainWindow: no devices configured for this machine.");
            }
        }

        _isxGlassPipeManager = new PipeManager("ISXGlass", "ISXGlass_Commands", "ISXGlass_Notify");
        _isxGlassPipeManager.Connected += () => Dispatcher.Invoke(() => SetISXGlassStatus(true));
        _isxGlassPipeManager.Disconnected += () => Dispatcher.Invoke(() => SetISXGlassStatus(false));
        _isxGlassPipeManager.MessageReceived += msg => Dispatcher.Invoke(() => HandleISXGlassMessage(msg));
        _isxGlassPipeManager.Start();

        _glassVideoPipeManager = new PipeManager("GlassVideo", "GlassVideo_Cmd", "GlassVideo_Notify");
        _glassVideoPipeManager.Connected += () => Dispatcher.Invoke(() => SetGlassVideoStatus(true));
        _glassVideoPipeManager.Disconnected += () => Dispatcher.Invoke(() =>
        {
            SetGlassVideoStatus(false);
            DebugLog.Write(DebugLog.Log_Sessions, "GlassVideo disconnected.");
        });
        _glassVideoPipeManager.MessageReceived += msg => Dispatcher.Invoke(() => Log($"GlassVideo: {msg}"));
        _glassVideoPipeManager.Start();



        Log("Glass started");
    }


    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Window_Loaded
    //
    // Called when the main window has finished loading.
    // Starts G-key input handling.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Log("Window_Loaded");
        _hidKeyInput.KeyStateChanged += OnGKeyPressed; 
        _hidKeyInput.Start();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Window_Closing
    //
    // Stops the pipe manager cleanly before the window closes.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        await _isxGlassPipeManager.StopAsync();
        _isxGlassPipeManager.Dispose();
        await _glassVideoPipeManager.StopAsync();
        _glassVideoPipeManager.Dispose();
        _hidKeyInput.Stop();
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

        Log($"Launching profile: {profileName} ({slots.Count} characters)");
        _activeProfile = repo;
        _definedSlots.Clear();

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
                _glassVideoPipeManager.Send(cmd);
                _definedSlots.Add(placement.SlotNumber);
            }
        }

        // Send slot_assign for any already-connected sessions.
        foreach (var session in _sessionRegistry.GetSessions())
        {
            var assignment = slots.FirstOrDefault(s =>
                charRepo.GetById(s.CharacterId)?.Name == session.CharacterName);

            if ((assignment != null) && (session.Hwnd != IntPtr.Zero))
            {
                string cmd = $"slot_assign {assignment.SlotNumber} {session.SessionName} {session.Hwnd:X}";
                DebugLog.Write(DebugLog.Log_Sessions, $"LaunchProfile: sending {cmd}");
                _glassVideoPipeManager.Send(cmd);
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

            Log($"  Launching: {character.Name} accountId={character.AccountId} server={character.Server}");
            _isxGlassPipeManager.Send($"launch {character.AccountId} {character.Name} {character.Server}");
            int delay = rng.Next(4000, 7000);
            await Task.Delay(delay);
        }
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
            _isxGlassPipeManager.Send(input);
        }

        ConsoleInput.Clear();
        e.Handled = true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // HandleLocalCommand
    //
    // Local command handler.  These are the "/" commands entered on the Glass console.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleLocalCommand(string cmd)
    {
        Log($"Local command: {cmd}");

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
                        var charRepo = new CharacterRepository();

                        if (uint.TryParse(sessionName.Substring(2), out uint sessionId))
                        {
                            var assignment = _activeProfile.GetSlots()
                                .FirstOrDefault(s => charRepo.GetById(s.CharacterId)?.AccountId == (int)sessionId);
                            if (assignment != null)
                            {
                                characterName = charRepo.GetById(assignment.CharacterId)?.Name ?? string.Empty;
                            }
                        }

                        DebugLog.Write(DebugLog.Log_Sessions, $"session connected: {sessionName}, pid={pid}, character={characterName}");
                        _sessionRegistry.OnSessionConnected(sessionName, characterName, pid);

                        if (hwnd != IntPtr.Zero)
                        {
                            var assignment = _activeProfile.GetSlots()
                                .FirstOrDefault(s => charRepo.GetById(s.CharacterId)?.Name == characterName);
                            if (assignment != null)
                            {
                                string cmd = $"slot_assign {assignment.SlotNumber} {sessionName} {hwnd:X}";
                                DebugLog.Write(DebugLog.Log_Sessions, $"HandleISXGlassMessage: sending {cmd}");
                                _glassVideoPipeManager.Send(cmd);
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
                        _sessionRegistry.OnSessionConnected(sessionName, characterName, pid);
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
                    DebugLog.Write($"HandleISXGlassMessage: sending unassign {sessionName}.");
                    DebugLog.Write(DebugLog.Log_Sessions, $"session_disconnected: {sessionName}");
                    _sessionRegistry.OnSessionDisconnected(sessionName);
                    _glassVideoPipeManager.Send($"unassign {sessionName}");
                    break;
                }

            default:
                Log(msg);
                break;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnGKeyPressed
    //
    // Called when a G-key is pressed on any connected Logitech device.
    //
    // sender:  The GKeyInput instance
    // e:       The event args containing device handle and key index
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void OnGKeyPressed(object? sender, HidKeyEventArgs e)
    {
        DebugLog.Write(DebugLog.Log_Input, $"G-key pressed: device={e.Device} key={e.KeyName} isPressed={e.IsPressed}");
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
        _isxGlassPipeManager.Send("status");
        DebugLog.Write(DebugLog.Log_Input, "Status requested.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_ManageCommands_Click
    //
    // Opens the Manage Commands dialog.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_ManageCommands_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("MainWindow.MenuItem_ManageCommands_Click: opening ManageCommandsDialog.");
        var dialog = new ManageCommandsDialog { Owner = this };
        dialog.ShowDialog();
        DebugLog.Write("MainWindow.MenuItem_ManageCommands_Click: dialog closed.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MenuItem_ManageKeyAliases_Click
    //
    // Opens the Manage Key Aliases dialog.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void MenuItem_ManageKeyAliases_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("MainWindow.MenuItem_ManageKeyAliases_Click: opening ManageKeyAliasesDialog.");
        var dialog = new ManageKeyAliasesDialog { Owner = this };
        dialog.ShowDialog();
        DebugLog.Write("MainWindow.MenuItem_ManageKeyAliases_Click: dialog closed.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ManageMachines_Click
    //
    // Opens the Manage Machines dialog.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ManageMachines_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("MainWindow.ManageMachines_Click: opening ManageMachinesDialog.");
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
        DebugLog.Write("MainWindow.KeyTest_Click: opening KeyTestDialog.");

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