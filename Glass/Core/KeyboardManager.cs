using Glass.Controls;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Input;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Data;

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

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // KeyboardManager
    //
    // pipeSend:  Delegate used to send messages to ISXGlass over the pipe
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public KeyboardManager()
    {
        DebugLog.Write("KeyboardManager: initialized.");
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
        DebugLog.Write($"KeyboardManager.LoadProfile: profileName='{profileName}'.");

        UnloadProfile();

        var profileRepo = new ProfileRepository(profileName);
        int profileId = profileRepo.GetId();

        if (profileId == 0)
        {
            DebugLog.Write($"KeyboardManager.LoadProfile: profile '{profileName}' not found.");
            return;
        }

        var profilePageRepo = new ProfilePageRepository();
        var profilePages = profilePageRepo.GetPagesForProfile(profileId);

        if (profilePages.Count == 0)
        {
            DebugLog.Write($"KeyboardManager.LoadProfile: no pages assigned to profile '{profileName}'.");
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
                DebugLog.Write($"KeyboardManager.LoadProfile: page id={profilePage.KeyPageId} not found, skipping.");
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
                DebugLog.Write($"KeyboardManager.LoadProfile: start page for {instance} is '{page.Name}'.");

                CreateOsdWindow(instance, page);
            }
        }

        DebugLog.Write($"KeyboardManager.LoadProfile: loaded {profilePages.Count} pages {_commandCache.Count} commands.");

        _hidKeyInput = new HidKeyInput();
        _hidKeyInput.KeyStateChanged += OnKeyStateChanged;
        _hidKeyInput.Start();

        DebugLog.Write("KeyboardManager.LoadProfile: HidKeyInput started.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UnloadProfile
    //
    // Stops HidKeyInput, closes OSD windows, and clears all cached data.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void UnloadProfile()
    {
        DebugLog.Write("KeyboardManager.UnloadProfile: unloading.");

        if (_hidKeyInput != null)
        {
            _hidKeyInput.KeyStateChanged -= OnKeyStateChanged;
            _hidKeyInput.Stop();
            _hidKeyInput = null;
            DebugLog.Write("KeyboardManager.UnloadProfile: HidKeyInput stopped.");
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

        DebugLog.Write("KeyboardManager.UnloadProfile: complete.");
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
        DebugLog.Write($"KeyboardManager.ToggleOsd: {instance}.");

        if (!_osdWindows.TryGetValue(instance, out var osd))
        {
            DebugLog.Write($"KeyboardManager.ToggleOsd: no OSD for {instance}.");
            return;
        }

        if (osd.IsVisible)
        {
            osd.Hide();
            DebugLog.Write($"KeyboardManager.ToggleOsd: hidden.");
        }
        else
        {
            osd.Show();

            if (_activePages.TryGetValue(instance, out var page))
            {
                PushOsdData(instance, page);
            }
            DebugLog.Write($"KeyboardManager.ToggleOsd: shown.");
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
        DebugLog.Write($"KeyboardManager.CreateOsdWindow: {instance} page='{page.Name}'.");

        var osd = new KeyboardOsdWindow(page.Device);
        _osdWindows[instance] = osd;

        PushOsdData(instance, page);

        DebugLog.Write($"KeyboardManager.CreateOsdWindow: created for {instance}.");
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

        DebugLog.Write($"KeyboardManager.PushOsdData: pushed {keys.Count} keys for page='{page.Name}'.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnKeyStateChanged
    //
    // Fires when a key is pressed or released.
    // Routes press events to command execution based on the active page.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void OnKeyStateChanged(object? sender, HidKeyEventArgs e)
    {
        if (!e.Device.HasValue)
        {
            return;
        }

        HidDeviceInstance instance = e.Device.Value;

        if (!_activePages.TryGetValue(instance, out KeyPage? activePage))
        {
            DebugLog.Write($"KeyboardManager.OnKeyStateChanged: no active page for {instance}.");
            return;
        }

        if (!_bindingCache.TryGetValue(activePage.Id, out List<KeyBinding>? bindings))
        {
            DebugLog.Write($"KeyboardManager.OnKeyStateChanged: no bindings for page='{activePage.Name}'.");
            return;
        }

        KeyBinding? binding = bindings.FirstOrDefault(b =>
            b.Key == e.KeyName &&
            (b.TriggerOn == TriggerOn.Both ||
            (e.IsPressed && b.TriggerOn == TriggerOn.Press) ||
            (!e.IsPressed && b.TriggerOn == TriggerOn.Release)));



        DebugLog.Write($"KeyboardManager.OnKeyStateChanged: key='{e.KeyName}'.");

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
            DebugLog.Write("KeyboardManager.ExecuteCommand: binding is null.");
            return;
        }
        if (!binding.CommandId.HasValue)
        {
            DebugLog.Write($"KeyboardManager.ExecuteCommand: binding key='{binding.Key}' has no command.");
            return;
        }

        if (!_commandCache.TryGetValue(binding.CommandId.Value, out Command? command))
        {
            DebugLog.Write($"KeyboardManager.ExecuteCommand: commandId={binding.CommandId.Value} not found in cache.");
            return;
        }

        int target = binding.Target;
        bool roundrobin = binding.RoundRobin;

        DebugLog.Write($"KeyboardManager.ExecuteCommand: command='{command.Name}' instance={instance} target={target} roundrobin={roundrobin}.");


        if ((command.Steps == null) || (command.Steps.Count == 0))
        {
            DebugLog.Write($"KeyboardManager.ExecuteCommand: command='{command.Name}' has no steps.");
            return;
        }

        if (binding.KeyType == KeyType.Toggle)
        {
            binding.IsToggled = !binding.IsToggled;
            DebugLog.Write($"KeyboardManager.ExecuteCommand: toggle key='{binding.Key}' isToggled={binding.IsToggled}.");

            if (binding.RepeatIntervalMs > 0)
            {
                if (binding.IsToggled)
                {
                    string repeatMessage = $"cmd_repeat_start {command.Id} {target} {binding.RepeatIntervalMs} {(roundrobin ? 1 : 0)}";
                    DebugLog.Write($"KeyboardManager.ExecuteCommand: sending: {repeatMessage}");
                    GlassContext.ISXGlassPipe.Send(repeatMessage);
                }
                else
                {
                    string stopMessage = $"cmd_repeat_stop {command.Id} {target}";
                    DebugLog.Write($"KeyboardManager.ExecuteCommand: sending: {stopMessage}");
                    GlassContext.ISXGlassPipe.Send(stopMessage);
                }

                UpdateKeyToggleState(instance, binding);
                return;
            }

            if (!binding.IsToggled)
            {
                DebugLog.Write($"KeyboardManager.ExecuteCommand: toggle off, skipping execution.");
                return;
            }
        }

        if (target > 0)
        {
            string message = $"cmd_execute {command.Id} {target} {(roundrobin ? 1 : 0)}";
            DebugLog.Write($"KeyboardManager.ExecuteCommand: sending: {message}");
            GlassContext.ISXGlassPipe.Send(message);
        }
        else
        {
            DebugLog.Write($"KeyboardManager.ExecuteCommand: target={target} is not a valid group, skipping pipe send.");
        }

        foreach (CommandStep step in command.Steps.OrderBy(s => s.Sequence))
        {
            DebugLog.Write($"KeyboardManager.ExecuteCommand: step={step.Sequence} type='{step.Type}' value='{step.Value}'.");

            if (step.Type == "pageload")
            {
                ExecutePageLoad(instance, step.Value);
            }
            else
            {
                DebugLog.Write($"KeyboardManager.ExecuteCommand: step type='{step.Type}' handled by ISXGlass.");
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
        DebugLog.Write($"KeyboardManager.UpdateKeyToggleState: key='{binding.Key}' isToggled={binding.IsToggled}.");

        if (!_osdWindows.TryGetValue(instance, out KeyboardOsdWindow? osd))
        {
            DebugLog.Write($"KeyboardManager.UpdateKeyToggleState: no OSD for {instance}.");
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
        DebugLog.Write($"KeyboardManager.ExecutePageLoad: instance={instance} pageName='{pageName}'.");

        if (!_pageCache.TryGetValue((instance, pageName), out KeyPage? page))
        {
            DebugLog.Write($"KeyboardManager.ExecutePageLoad: page='{pageName}' not found in cache for {instance}, ignoring.");
            return;
        }

        _activePages[instance] = page;
        DebugLog.Write($"KeyboardManager.ExecutePageLoad: active page for {instance} set to '{page.Name}'.");

        PushOsdData(instance, page);
    }
}
