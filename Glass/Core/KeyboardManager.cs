using Glass.Controls;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Input;
using Glass.Core.Logging;

namespace Glass.Core;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyboardManager
//
// Owns all keyboard activity for the active profile.
// Creates and manages HidKeyInput, routes key events to commands based on
// the active page per device instance, and manages OSD windows.
// HidKeyInput is started on LoadProfile and stopped on UnloadProfile.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class KeyboardManager
{
    private HidKeyInput? _hidKeyInput;

    // Active page per device instance
    private readonly Dictionary<HidDeviceInstance, KeyPage> _activePages = new();

    // All pages for the active profile, keyed by (device instance, page name)
    private readonly Dictionary<(HidDeviceInstance Instance, string PageName), KeyPage> _pageCache = new();

    // Bindings per page ID
    private readonly Dictionary<int, List<KeyBinding>> _bindingCache = new();

    // Commands keyed by command ID
    private readonly Dictionary<int, Command> _commandCache = new();

    // OSD windows keyed by device instance — created on LoadProfile, shown on trigger
    private readonly Dictionary<HidDeviceInstance, KeyboardOsdWindow> _osdWindows = new();

    // Raised for every key state change from any device, regardless of profile state.
    // Allows test/diagnostic UI to observe raw key activity.
    public event EventHandler<HidKeyEventArgs>? KeyEvent;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // KeyboardManager
    //
    // pipeSend:  Delegate used to send messages to ISXGlass over the pipe
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public KeyboardManager()
    {
        DebugLog.Write(LogChannel.Input, "KeyboardManager: initialized.", LogLevel.Trace);
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
            var page = pageRepo.GetPage(profilePage.KeyPageId);
            if (page == null)
            {
                DebugLog.Write(LogChannel.Profiles, $"KeyboardManager.LoadProfile: page id={profilePage.KeyPageId} not found, skipping.", LogLevel.Warn);
                continue;
            }

            var bindings = bindingRepo.GetBindingsForPage(page.Id);
            _bindingCache[page.Id] = bindings;

            // For now use instance 1 for all device types
            var instance = new HidDeviceInstance(page.Device, 1, string.Empty);
            _pageCache[(instance, page.Name)] = page;

            if (profilePage.IsStartPage)
            {
                _activePages[instance] = page;
                DebugLog.Write(LogChannel.Profiles, $"KeyboardManager.LoadProfile: start page for {instance} is '{page.Name}'.", LogLevel.Trace);

                CreateOsdWindow(instance, page);
            }
        }

        DebugLog.Write(LogChannel.Profiles, $"KeyboardManager.LoadProfile: loaded {profilePages.Count} pages {_commandCache.Count} commands.", LogLevel.Trace);

        _hidKeyInput = new HidKeyInput();
        _hidKeyInput.KeyStateChanged += OnKeyStateChanged;
        _hidKeyInput.Start();

        DebugLog.Write(LogChannel.Profiles, "KeyboardManager.LoadProfile: HidKeyInput started.", LogLevel.Info);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UnloadProfile
    //
    // Stops HidKeyInput, closes OSD windows, and clears all cached data.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void UnloadProfile()
    {
        DebugLog.Write(LogChannel.Profiles, "KeyboardManager.UnloadProfile: unloading.", LogLevel.Trace);

        if (_hidKeyInput != null)
        {
            _hidKeyInput.KeyStateChanged -= OnKeyStateChanged;
            _hidKeyInput.Stop();
            _hidKeyInput = null;
            DebugLog.Write(LogChannel.Profiles, "KeyboardManager.UnloadProfile: HidKeyInput stopped.", LogLevel.Trace);
        }

        foreach (var osd in _osdWindows.Values)
        {
            osd.Close();
        }
        _osdWindows.Clear();

        _activePages.Clear();
        _pageCache.Clear();
        _bindingCache.Clear();
        _commandCache.Clear();

        DebugLog.Write(LogChannel.Profiles, "KeyboardManager.UnloadProfile: complete.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ToggleOsd
    //
    // Shows or hides the OSD window for the given device instance.
    //
    // instance:  The device instance whose OSD to toggle
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void ToggleOsd(HidDeviceInstance instance)
    {
        DebugLog.Write(LogChannel.Input, $"KeyboardManager.ToggleOsd: {instance}.", LogLevel.Trace);

        if (!_osdWindows.TryGetValue(instance, out var osd))
        {
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.ToggleOsd: no OSD for {instance}.", LogLevel.Trace);
            return;
        }

        if (osd.IsVisible)
        {
            osd.Hide();
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.ToggleOsd: hidden.", LogLevel.Trace);
        }
        else
        {
            osd.Show();

            if (_activePages.TryGetValue(instance, out var page))
            {
                PushOsdData(instance, page);
            }
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.ToggleOsd: shown.", LogLevel.Trace);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CreateOsdWindow
    //
    // Creates an OSD window for the given device instance and page.
    // The window is created hidden — shown only when triggered.
    //
    // instance:  The device instance
    // page:      The start page for this instance
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void CreateOsdWindow(HidDeviceInstance instance, KeyPage page)
    {
        DebugLog.Write(LogChannel.Input, $"KeyboardManager.CreateOsdWindow: {instance} page='{page.Name}'.", LogLevel.Trace);

        var osd = new KeyboardOsdWindow(page.Device);
        _osdWindows[instance] = osd;

        PushOsdData(instance, page);

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
    // Fires when a key is pressed or released.
    // Routes press events to command execution based on the active page.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void OnKeyStateChanged(object? sender, HidKeyEventArgs e)
    {
        KeyEvent?.Invoke(this, e);

        if (!e.Device.HasValue)
        {
            return;
        }

        HidDeviceInstance instance = e.Device.Value;

        if (!_activePages.TryGetValue(instance, out KeyPage? activePage))
        {
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.OnKeyStateChanged: no active page for {instance}.", LogLevel.Warn);
            return;
        }

        if (!_bindingCache.TryGetValue(activePage.Id, out List<KeyBinding>? bindings))
        {
            DebugLog.Write(LogChannel.Input, $"KeyboardManager.OnKeyStateChanged: no bindings for page='{activePage.Name}'.", LogLevel.Trace);
            return;
        }

        KeyBinding? binding = bindings.FirstOrDefault(b =>
            b.Key == e.KeyName &&
            (b.TriggerOn == TriggerOn.Both ||
            (e.IsPressed && b.TriggerOn == TriggerOn.Press) ||
            (!e.IsPressed && b.TriggerOn == TriggerOn.Release)));



        DebugLog.Write(LogChannel.Input, $"KeyboardManager.OnKeyStateChanged: key='{e.KeyName}'.", LogLevel.Trace);

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
    // Creates HidKeyInput, subscribes to key state events, and starts the
    // device readers.  Called once at application startup so that keyboard
    // hardware is active independent of profile state.  If already started,
    // logs and returns without creating a second HidKeyInput.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Start()
    {
        if (_hidKeyInput != null)
        {
            DebugLog.Write(LogChannel.Input, "KeyboardManager.Start: already started, ignoring.", LogLevel.Warn);
            return;
        }

        DebugLog.Write(LogChannel.Input, "KeyboardManager.Start: creating HidKeyInput.", LogLevel.Trace);

        _hidKeyInput = new HidKeyInput();
        _hidKeyInput.KeyStateChanged += OnKeyStateChanged;
        _hidKeyInput.Start();

        DebugLog.Write(LogChannel.Input, "KeyboardManager.Start: HidKeyInput started.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Stop
    //
    // Unsubscribes from key state events, stops HidKeyInput, and releases it.
    // Called once at application shutdown.  If not started, logs and returns.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Stop()
    {
        if (_hidKeyInput == null)
        {
            DebugLog.Write(LogChannel.Input, "KeyboardManager.Stop: not started, ignoring.", LogLevel.Warn);
            return;
        }

        DebugLog.Write(LogChannel.Input, "KeyboardManager.Stop: stopping HidKeyInput.", LogLevel.Trace);

        _hidKeyInput.KeyStateChanged -= OnKeyStateChanged;
        _hidKeyInput.Stop();
        _hidKeyInput = null;

        DebugLog.Write(LogChannel.Input, "KeyboardManager.Stop: stopped.", LogLevel.Trace);
    }
}
