using Glass.Controls;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Input;
using System.Windows.Threading;

namespace Glass.Core;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyboardManager
//
// Owns all keyboard activity for the active profile.
// Creates and manages HidKeyInput, routes key events to commands based on
// the active page per device instance, and manages OSD windows.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class KeyboardManager
{
    private readonly HidManager _hidManager = new HidManager();

    // Active page per device instance
    private readonly Dictionary<HidDeviceInstance, KeyPage> _activePages = new();

    // All pages for the active profile, keyed by (device instance, page name)
    private readonly Dictionary<(HidDeviceInstance Instance, string PageName), KeyPage> _pageCache = new();

    // Bindings per page ID
    private readonly Dictionary<int, List<KeyBinding>> _bindingCache = new();

    // Commands keyed by command ID
    private readonly Dictionary<int, Command> _commandCache = new();

    // OSD windows keyed by device instance
    private readonly Dictionary<HidDeviceInstance, KeyboardOsdWindow> _osdWindows = new();

    // Raised for every key state change from any device, regardless of profile state.
    // Allows test/diagnostic UI to observe raw key activity.
    public event EventHandler<HidKeyEventArgs>? KeyEvent;

    // Chord detectors keyed by device instance — created lazily on first key event
    private readonly Dictionary<HidDeviceInstance, ChordDetector> _chordDetectors = new();

    // Deferral window for chord detection
    private const int ChordWindowMs = 75;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // KeyboardManager
    //
    // Creates the HidManager, which discovers connected devices, subscribes
    // to its key events, and creates a hidden OSD window for every
    // discovered device instance.  Nothing is started here — Start handles
    // lifecycle.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public KeyboardManager()
    {
        _hidManager.KeyStateChanged += OnKeyStateChanged;

        // Note that HidManager performed the initial enuemration of devices.
        foreach (HidDeviceInstance instance in _hidManager.GetConnectedInstances())
        {
            CreateOsdWindow(instance);
        }

        DebugLog.Write(LogChannel.Input, $"KeyboardManager: initialized, {_osdWindows.Count} OSD window(s).", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Shutdown
    //
    // Closes and releases all OSD windows.  This is the end of device
    // lifetime — the windows cannot be shown again after this.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Shutdown()
    {
        DebugLog.Write(LogChannel.Input, $"KeyboardManager.Shutdown: closing {_osdWindows.Count} OSD window(s).", LogLevel.Trace);

        Stop();

        foreach (KeyboardOsdWindow osd in _osdWindows.Values)
        {
            osd.Close();
        }

        _osdWindows.Clear();

        DebugLog.Write(LogChannel.Input, "KeyboardManager.Shutdown: complete.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadProfile
    //
    // Loads pages and bindings for the given profile.
    // Creates HidKeyInput, starts device readers, creates OSD windows.
    // Sets the start page as active for each device instance.
    //
    // profileName:  The profile to load
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void LoadProfile(string profileName)
    {
        DebugLog.Write(LogChannel.Input, $"KeyboardManager.LoadProfile: profileName='{profileName}'.", LogLevel.Info);

        UnloadProfile();

        var profileRepo = new ProfileRepository(profileName);
        int profileId = profileRepo.GetId();

        if (profileId == 0)
        {
            DebugLog.Write(LogChannel.Profiles, $"KeyboardManager.LoadProfile: profile '{profileName}' not found.", LogLevel.Warn);
            return;
        }

        var profilePageRepo = new ProfilePageRepository();
        var profilePages = profilePageRepo.GetPagesForProfile(profileId);

        if (profilePages.Count == 0)
        {
            DebugLog.Write(LogChannel.Profiles, $"KeyboardManager.LoadProfile: no pages assigned to profile '{profileName}'.", LogLevel.Warn);
            return;
        }

        var pageRepo = new KeyPageRepository();
        var bindingRepo = new KeyBindingRepository();
        var commandRepo = new CommandRepository();

        foreach (var command in commandRepo.GetAllCommands())
        {
            _commandCache[command.Id] = command;
        }

        foreach (var profilePage in profilePages)
        {
            KeyPage? page = pageRepo.GetPage(profilePage.KeyPageId);
            if (page == null)
            {
                DebugLog.Write(LogChannel.Profiles, $"KeyboardManager.LoadProfile: page id={profilePage.KeyPageId} not found, skipping.", LogLevel.Warn);
                continue;
            }

            List<KeyBinding> bindings = bindingRepo.GetBindingsForPage(page.Id);
            _bindingCache[page.Id] = bindings;

            foreach (HidDeviceInstance instance in _hidManager.GetConnectedInstances())
            {
                if (instance.Type != page.Device)
                {
                    continue;
                }

                _pageCache[(instance, page.Name)] = page;

                if (profilePage.IsStartPage)
                {
                    _activePages[instance] = page;
                    DebugLog.Write(LogChannel.Profiles, $"KeyboardManager.LoadProfile: start page for {instance} is '{page.Name}'.", LogLevel.Trace);

                    PushOsdData(instance, page);
                }
            }
        }

        DebugLog.Write(LogChannel.Profiles, $"KeyboardManager.LoadProfile: loaded {profilePages.Count} pages {_commandCache.Count} commands.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UnloadProfile
    //
    // Clears all cached profile data and empties and hides the OSD windows.
    // The windows survive — they belong to the devices, not the profile.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void UnloadProfile()
    {
        DebugLog.Write(LogChannel.Profiles, "KeyboardManager.UnloadProfile: unloading.", LogLevel.Trace);

        foreach (KeyboardOsdWindow osd in _osdWindows.Values)
        {
            osd.SetPage(string.Empty, new Dictionary<string, KeyDisplay>());
            osd.Hide();
        }

        _activePages.Clear();
        _pageCache.Clear();
        _bindingCache.Clear();
        _commandCache.Clear();

        DebugLog.Write(LogChannel.Profiles, "KeyboardManager.UnloadProfile: complete.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ToggleOsd
    //
    // Shows or hides the OSD window for the given device instance.  On show,
    // pushes the instance's active page data if a profile is loaded.
    //
    // instance:  The device instance whose OSD to toggle
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void ToggleOsd(HidDeviceInstance instance)
    {
        DebugLog.Write(LogChannel.Input, $"KeyboardManager.ToggleOsd: {instance}.", LogLevel.Trace);

        if (!_osdWindows.TryGetValue(instance, out KeyboardOsdWindow? osd))
        {
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.ToggleOsd: no OSD for {instance}.", LogLevel.Warn);
            return;
        }

        bool visible = osd.ToggleVisibility();

        if (visible && _activePages.TryGetValue(instance, out KeyPage? page))
        {
            PushOsdData(instance, page);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CreateOsdWindow
    //
    // Creates a hidden, empty OSD window for the given device instance.
    // Content arrives later via PushOsdData when a profile is loaded.
    //
    // instance:  The device instance the window belongs to
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void CreateOsdWindow(HidDeviceInstance instance)
    {
        if (_osdWindows.ContainsKey(instance))
        {
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.CreateOsdWindow: window already exists for {instance}, ignoring.", LogLevel.Warn);
            return;
        }

        KeyboardOsdWindow osd = new KeyboardOsdWindow(instance.Type);
        _osdWindows[instance] = osd;

        DebugLog.Write(LogChannel.Input, $"KeyboardManager.CreateOsdWindow: created for {instance}.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PushOsdData
    //
    // Builds a KeyDisplay dictionary for the given page and pushes it to the OSD window.
    //
    // instance:  The device instance
    // page:      The page to display
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void PushOsdData(HidDeviceInstance instance, KeyPage page)
    {
        if (!_osdWindows.TryGetValue(instance, out var osd))
        {
            return;
        }

        if (!_bindingCache.TryGetValue(page.Id, out var bindings))
        {
            return;
        }

        var keys = new Dictionary<string, KeyDisplay>();

        foreach (var binding in bindings)
        {
            string label = "-";

            if (binding.CommandId.HasValue && _commandCache.TryGetValue(binding.CommandId.Value, out var command))
            {
                label = !string.IsNullOrWhiteSpace(binding.Label)
                    ? binding.Label
                    : string.IsNullOrWhiteSpace(command.Label) ? command.Name : command.Label;
            }

            keys[binding.Key] = new KeyDisplay
            {
                KeyName = binding.Key,
                Label = label,
                KeyType = binding.KeyType
            };
        }

        osd.SetPage(page.Name, keys);

        DebugLog.Write(LogChannel.Input, $"KeyboardManager.PushOsdData: pushed {keys.Count} keys for page='{page.Name}'.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnKeyStateChanged
    //
    // Fires for every key state change from any device.  Raises the raw
    // KeyEvent for diagnostic observers, forwards the raw press state to the
    // instance's OSD window for the key-down visual, then routes the event
    // through the instance's chord detector, which either fires a chord or
    // passes the event on to DispatchKey for normal bind execution.
    //
    // sender:  The HidManager that raised the event
    // e:       The key event, carrying key name, press state, and device instance
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void OnKeyStateChanged(object? sender, HidKeyEventArgs e)
    {
        KeyEvent?.Invoke(this, e);

        if (!e.Device.HasValue)
        {
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.OnKeyStateChanged: key='{e.KeyName}' has no device instance, ignoring.", LogLevel.Warn);
            return;
        }

        HidDeviceInstance instance = e.Device.Value;

        if (_osdWindows.TryGetValue(instance, out KeyboardOsdWindow? osd))
        {
            osd.SetKeyDown(e.KeyName, e.IsPressed);
        }
        else
        {
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.OnKeyStateChanged: no OSD window for {instance}, skipping key visual.", LogLevel.Trace);
        }

        ChordDetector detector = GetOrCreateChordDetector(instance);
        detector.HandleKey(e);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetOrCreateChordDetector
    //
    // Returns the chord detector for the given device instance, creating and
    // registering it on first use.  Chord registration is per device type:
    // the Dominator gets the OSD chord and a swallow-only entry for the
    // firmware test-mode chord, the Logitech boards get an OSD chord on
    // their outer G-keys.  Device types with no chords still get a detector,
    // which passes all traffic through.
    //
    // instance:  The device instance whose detector to fetch
    // Returns:   The detector bound to that instance
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private ChordDetector GetOrCreateChordDetector(HidDeviceInstance instance)
    {
        if (_chordDetectors.TryGetValue(instance, out ChordDetector? existing))
        {
            return existing;
        }

        DebugLog.Write(LogChannel.Input, $"KeyboardManager.GetOrCreateChordDetector: creating detector for {instance}.", LogLevel.Trace);

        ChordDetector detector = new ChordDetector(ChordWindowMs, DispatchKey);

        switch (instance.Type)
        {
            case KeyboardType.DominatorX36:
                detector.AddChord("X-1", "X-2", () => ToggleOsd(instance), "OSD");
                detector.AddChord("X-1", "X-6", null, "FirmwareTestMode");
                break;

            case KeyboardType.G15:
                detector.AddChord("G1", "G3", () => ToggleOsd(instance), "OSD");
                break;

            case KeyboardType.G13:
                detector.AddChord("G1", "G2", () => ToggleOsd(instance), "OSD");
                break;

            default:
                DebugLog.Write(LogChannel.Input, $"KeyboardManager.GetOrCreateChordDetector: no chords defined for {instance.Type}.", LogLevel.Trace);
                break;
        }

        _chordDetectors[instance] = detector;
        return detector;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DispatchKey
    //
    // Executes normal bind dispatch for one key event that has passed through
    // chord detection.  Looks up the active page for the event's device
    // instance, finds a binding matching the key and trigger condition, and
    // executes its command.  Events with no device instance, no active page,
    // or no matching binding are logged and dropped.
    //
    // e:  The key event to dispatch, carrying key name, press state, and device instance
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void DispatchKey(HidKeyEventArgs e)
    {
        if (!e.Device.HasValue)
        {
            DebugLog.Write(LogChannel.Input, $"DispatchKey: key='{e.KeyName}' has no device instance, ignoring.", LogLevel.Warn);
            return;
        }

        HidDeviceInstance instance = e.Device.Value;

        if (!_activePages.TryGetValue(instance, out KeyPage? activePage))
        {
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.DispatchKey: no active page for {instance}.", LogLevel.Warn);
            return;
        }

        if (!_bindingCache.TryGetValue(activePage.Id, out List<KeyBinding>? bindings))
        {
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.DispatchKey: no bindings for page='{activePage.Name}'.", LogLevel.Trace);
            return;
        }

        KeyBinding? binding = bindings.FirstOrDefault(b =>
            b.Key == e.KeyName &&
            (b.TriggerOn == TriggerOn.Both ||
            (e.IsPressed && b.TriggerOn == TriggerOn.Press) ||
            (!e.IsPressed && b.TriggerOn == TriggerOn.Release)));

        DebugLog.Write(LogChannel.Input, $"KeyboardManager.DispatchKey: key='{e.KeyName}' isPressed={e.IsPressed}.", LogLevel.Trace);

        ExecuteCommand(binding, instance);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ExecuteCommand
    //
    // Executes all steps of a command for the triggering device instance.
    // Relay steps (key/text) are sent to ISXGlass via cmd_execute.
    // Page load steps are handled locally.
    //
    // command:       The command to execute
    // instance:      The device instance that triggered the command
    // target:        The relay group ID to execute on
    // roundrobin     Whether to round-robin within the target
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ExecuteCommand(KeyBinding? binding, HidDeviceInstance instance)
    {
        if (binding == null)
        {
            DebugLog.Write(LogChannel.Input, "KeyboardManager.ExecuteCommand: binding is null.", LogLevel.Warn);
            return;
        }
        if (!binding.CommandId.HasValue)
        {
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.ExecuteCommand: binding key='{binding.Key}' has no command.", LogLevel.Warn);
            return;
        }

        if (!_commandCache.TryGetValue(binding.CommandId.Value, out Command? command))
        {
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.ExecuteCommand: commandId={binding.CommandId.Value} not found in cache.", LogLevel.Trace);
            return;
        }

        int target = binding.Target;
        bool roundrobin = binding.RoundRobin;

        DebugLog.Write(LogChannel.Input, $"KeyboardManager.ExecuteCommand: command='{command.Name}' instance={instance} target={target} roundrobin={roundrobin}.", LogLevel.Trace);


        if ((command.Steps == null) || (command.Steps.Count == 0))
        {
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.ExecuteCommand: command='{command.Name}' has no steps.", LogLevel.Trace);
            return;
        }

        if (binding.KeyType == KeyType.Toggle)
        {
            binding.IsToggled = !binding.IsToggled;
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.ExecuteCommand: toggle key='{binding.Key}' isToggled={binding.IsToggled}.", LogLevel.Trace);

            if (binding.RepeatIntervalMs > 0)
            {
                if (binding.IsToggled)
                {
                    string repeatMessage = $"cmd_repeat_start {command.Id} {target} {binding.RepeatIntervalMs} {(roundrobin ? 1 : 0)}";
                    DebugLog.Write(LogChannel.Input, $"KeyboardManager.ExecuteCommand: sending: {repeatMessage}", LogLevel.Trace);
                    GlassContext.ISXGlassPipe.Send(repeatMessage);
                }
                else
                {
                    string stopMessage = $"cmd_repeat_stop {command.Id} {target}";
                    DebugLog.Write(LogChannel.Input, $"KeyboardManager.ExecuteCommand: sending: {stopMessage}", LogLevel.Trace);
                    GlassContext.ISXGlassPipe.Send(stopMessage);
                }

                UpdateKeyToggleState(instance, binding);
                return;
            }

            if (!binding.IsToggled)
            {
                DebugLog.Write(LogChannel.Input, $"KeyboardManager.ExecuteCommand: toggle off, skipping execution.", LogLevel.Trace);
                return;
            }
        }

        if (target > 0)
        {
            string message = $"cmd_execute {command.Id} {target} {(roundrobin ? 1 : 0)}";
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.ExecuteCommand: sending: {message}", LogLevel.Trace);
            GlassContext.ISXGlassPipe.Send(message);
        }
        else
        {
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.ExecuteCommand: target={target} is not a valid group, skipping pipe send.", LogLevel.Trace);
        }

        foreach (CommandStep step in command.Steps.OrderBy(s => s.Sequence))
        {
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.ExecuteCommand: step={step.Sequence} type='{step.Type}' value='{step.Value}'.", LogLevel.Trace);

            if (step.Type == "pageload")
            {
                ExecutePageLoad(instance, step.Value);
            }
            else
            {
                DebugLog.Write(LogChannel.Input, $"KeyboardManager.ExecuteCommand: step type='{step.Type}' handled by ISXGlass.", LogLevel.Trace);
            }
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UpdateKeyToggleState
    //
    // Updates the OSD key display to reflect the current toggle state of a binding.
    //
    // instance:  The device instance
    // binding:   The binding whose toggle state has changed
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void UpdateKeyToggleState(HidDeviceInstance instance, KeyBinding binding)
    {
        DebugLog.Write(LogChannel.Input, $"KeyboardManager.UpdateKeyToggleState: key='{binding.Key}' isToggled={binding.IsToggled}.", LogLevel.Trace);

        if (!_osdWindows.TryGetValue(instance, out KeyboardOsdWindow? osd))
        {
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.UpdateKeyToggleState: no OSD for {instance}.", LogLevel.Trace);
            return;
        }

        string label = string.Empty;
        if (binding.CommandId.HasValue && _commandCache.TryGetValue(binding.CommandId.Value, out Command? command))
        {
            label = !string.IsNullOrWhiteSpace(binding.Label) ? binding.Label : command.Label;
        }

        KeyDisplay keyDisplay = new KeyDisplay
        {
            KeyName = binding.Key,
            Label = label,
            KeyType = binding.KeyType,
            IsPressed = binding.IsToggled
        };

        osd.UpdateKey(keyDisplay);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ExecutePageLoad
    //
    // Switches the active page for the given device instance to the named page.
    // If the page is not found in the cache, logs and returns without changing state.
    //
    // instance:  The device instance to switch
    // pageName:  The name of the page to load
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ExecutePageLoad(HidDeviceInstance instance, string pageName)
    {
        DebugLog.Write(LogChannel.Input, $"KeyboardManager.ExecutePageLoad: instance={instance} pageName='{pageName}'.", LogLevel.Trace);

        if (!_pageCache.TryGetValue((instance, pageName), out KeyPage? page))
        {
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.ExecutePageLoad: page='{pageName}' not found in cache for {instance}, ignoring.", LogLevel.Trace);
            return;
        }

        _activePages[instance] = page;
        DebugLog.Write(LogChannel.Input, $"KeyboardManager.ExecutePageLoad: active page for {instance} set to '{page.Name}'.", LogLevel.Trace);

        PushOsdData(instance, page);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Start
    //
    // Starts the HidManager's device lifecycle.  Note that the manager may be started/stopped without shutting down Glass.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Start()
    {
        DebugLog.Write(LogChannel.Input, "KeyboardManager.Start: starting HidManager.", LogLevel.Trace);

        _hidManager.Start();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Stop
    //
    // Stops the HidManager's device lifecycle.  The manager, its event
    // subscription, and the OSD windows all survive so Start can run again.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Stop()
    {
        DebugLog.Write(LogChannel.Input, "KeyboardManager.Stop: stopping HidManager.", LogLevel.Trace);

        _hidManager.Stop();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // IsDeviceConnected
    //
    // Returns true if a reader is currently running for the given device type.
    //
    // type:  The keyboard type to check
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool IsDeviceConnected(KeyboardType type)
    {
        return _hidManager.IsDeviceConnected(type);
    }
}
