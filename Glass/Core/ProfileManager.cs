using Glass.Core.Logging;
using Glass.Core.Signals;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Capture;
using Glass.Network.Protocol;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Glass.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// ProfileManager
//
// Manages the active profile lifecycle: launching characters through ISXGlass,
// sending layout and region definitions to GlassVideo, and tracking which
// slots have been defined. Used by both Glass and Inference.
///////////////////////////////////////////////////////////////////////////////////////////////
public class ProfileManager
{
    private ProfileRepository? _activeProfile;
    private readonly HashSet<int> _definedSlots = new();

    private const string UcsKeepaliveCommandName = "UCS Keepalive";
    private const uint UcsKeepaliveIntervalMs = 15000;
    private const int AllGroupTarget = 2;
    ///////////////////////////////////////////////////////////////////////////////////////////
    // ActiveProfile
    //
    // The currently active profile repository, or null if no profile is active.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public ProfileRepository? ActiveProfile => _activeProfile;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HasActiveProfile
    //
    // Returns true if a profile is currently active.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool HasActiveProfile => _activeProfile != null;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ClearActiveProfile
    //
    // Clears the active profile and resets the defined slots. Called when all
    // sessions have disconnected.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void ClearActiveProfile()
    {
        DebugLog.Write(LogChannel.Profiles, "ProfileManager.ClearActiveProfile: clearing active profile", LogLevel.Trace);
        GlassContext.SignalBus.Unsubscribe<SignalSessionAdded>(OnSessionAdded);
        GlassContext.KeyboardManager.UnloadProfile();
        _activeProfile = null;
        _definedSlots.Clear();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LaunchProfile
    //
    // Launches all characters in the specified profile through ISXGlass.  A different profile
    // is refused while one is active.  Live packet capture is started first when none is
    // running; if it cannot start, nothing is launched.  When no profile is active, loads the
    // profile, its characters and its keyboard pages and bindings, and sends the layout to
    // GlassVideo before launching.  When the specified profile is already the active one,
    // only the launch commands are sent again; ISXGlass skips sessions that are already
    // running, so only missing sessions start.
    //
    // profileName:  The name of the profile to launch.
    //
    // Returns:  True when launch commands were sent; false when the launch was refused or
    //           capture could not be started.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public async Task<bool> LaunchProfile(string profileName)
    {
        if (_activeProfile != null && _activeProfile.GetLoadedProfileName() != profileName)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileManager.LaunchProfile: profile '"
                + _activeProfile.GetLoadedProfileName() + "' is active, refusing launch of '" + profileName + "'.",
                LogLevel.Warn);
            return false;
        }

        if (GlassContext.PacketCapture != null && GlassContext.PacketCapture.IsCapturing)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileManager.LaunchProfile: capture already running.",
                LogLevel.Trace);
        }
        else
        {
            SessionDemux demux = new SessionDemux();
            PacketCapture capture = new PacketCapture(demux);
            if (!capture.Start(SoeConstants.CaptureFilter()))
            {
                DebugLog.Write(LogChannel.Profiles, "ProfileManager.LaunchProfile: capture failed to start, '"
                    + profileName + "' not launched.", LogLevel.Error);
                return false;
            }

            GlassContext.PacketCapture = capture;
            DebugLog.Write(LogChannel.Profiles, "ProfileManager.LaunchProfile: capture started.", LogLevel.Trace);
        }

        if (_activeProfile != null)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileManager.LaunchProfile: '" + profileName
                + "' is already active, relaunching missing sessions.", LogLevel.Trace);
            await LaunchCharacters(_activeProfile.GetSlots());
            return true;
        }

        ProfileRepository profile = ProfileRepository.Instance;
        profile.LoadProfile(profileName);
        CharacterRepository.Instance.Load(profile.GetDatabaseIds());
        IReadOnlyList<SlotAssignment> slots = profile.GetSlots();

        _activeProfile = profile;
        _definedSlots.Clear();
        GlassContext.KeyboardManager.LoadProfile(profile.GetId());

        DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.LaunchProfile: launching profile '" + profileName
            + "' with " + slots.Count + " characters", LogLevel.Trace);

        GlassContext.FocusTracker.Start();
        GlassContext.ISXGlassPipe.Send("new_profile");
        PushCommandState();
        PushRelayGroupState(profile.GetId());
        GlassContext.SignalBus.Subscribe<SignalSessionAdded>(OnSessionAdded);

        int layoutId = profile.GetLayoutId() ?? 0;
        if (layoutId != 0)
        {
            SendGlassVideoLayout(layoutId);
        }
        else
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileManager.LaunchProfile: no layout assigned to profile, skipping GlassVideo layout.", LogLevel.Warn);
        }

        await LaunchCharacters(slots);

        DebugLog.Write(LogChannel.Profiles, "ProfileManager.LaunchProfile: all characters launched", LogLevel.Trace);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LaunchCharacters
    //
    // Sends a launch command to ISXGlass for each character in the given slot assignments,
    // with a small random delay between them to avoid overwhelming the login server.  A slot
    // whose character is not cached is skipped.
    //
    // slots:  The slot assignments whose characters are launched.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private async Task LaunchCharacters(IReadOnlyList<SlotAssignment> slots)
    {
        Random rng = new Random();
        foreach (SlotAssignment slot in slots)
        {
            Character? character = CharacterRepository.Instance.GetByDatabaseId(slot.DatabaseId);
            if (character == null)
            {
                DebugLog.Write(LogChannel.Profiles, "ProfileManager.LaunchCharacters: no character found for id="
                    + slot.DatabaseId + ", skipping", LogLevel.Warn);
                continue;
            }

            DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.LaunchCharacters: launching " + character.Name
                + " accountId=" + character.AccountId
                + " server=" + character.Server
                + " id=" + character.DatabaseId, LogLevel.Trace);

            GlassContext.ISXGlassPipe.Send("launch " + character.AccountId
                + " " + character.Name
                + " " + character.Server
                + " " + character.DatabaseId);

            int delay = rng.Next(1000, 3000);
            await Task.Delay(delay);
        }

        DebugLog.Write(LogChannel.Profiles, "ProfileManager.LaunchCharacters: sent launch for "
            + slots.Count + " slots", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // StartUcsKeepalive
    //
    // Starts the UCS keepalive in ISXGlass: the command named by UcsKeepaliveCommandName is
    // repeated on the All group at UcsKeepaliveIntervalMs, round robin, so one client at a
    // time sends it.  When no command has that name, nothing is started.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void StartUcsKeepalive()
    {
        Command? keepalive = FindUcsKeepaliveCommand();
        if (keepalive == null)
        {
            DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.StartUcsKeepalive: no keepalive command, not started.",
                LogLevel.Warn);
            return;
        }

        string message = "cmd_repeat_start " + keepalive.Id + " " + AllGroupTarget + " "
            + UcsKeepaliveIntervalMs + " 1";
        GlassContext.ISXGlassPipe.Send(message);
        DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.StartUcsKeepalive: sent " + message, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // StopUcsKeepalive
    //
    // Stops the UCS keepalive repeat on the All group in ISXGlass.  When no keepalive command
    // is defined, nothing is sent.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void StopUcsKeepalive()
    {
        Command? keepalive = FindUcsKeepaliveCommand();
        if (keepalive == null)
        {
            DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.StopUcsKeepalive: no keepalive command, nothing stopped.",
                LogLevel.Warn);
            return;
        }

        string message = "cmd_repeat_stop " + keepalive.Id + " " + AllGroupTarget;
        GlassContext.ISXGlassPipe.Send(message);
        DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.StopUcsKeepalive: sent " + message, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindUcsKeepaliveCommand
    //
    // Looks up the command named by UcsKeepaliveCommandName in the database.
    //
    // Returns:  The command, or null when no command has that name.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private Command? FindUcsKeepaliveCommand()
    {
        CommandGateway commandGateway = new CommandGateway();
        List<Command> commands = commandGateway.GetAllCommands();
        Command? keepalive = commands.FirstOrDefault(c => c.Name == UcsKeepaliveCommandName);

        if (keepalive == null)
        {
            DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.FindUcsKeepaliveCommand: no command named '"
                + UcsKeepaliveCommandName + "'.", LogLevel.Warn);
            return null;
        }

        DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.FindUcsKeepaliveCommand: '" + UcsKeepaliveCommandName
            + "' is commandId=" + keepalive.Id, LogLevel.Trace);
        return keepalive;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SendGlassVideoLayout
    //
    // Sends slot definitions, slot assignments, video source regions, and video
    // destination regions to GlassVideo for the active profile's layout.
    //
    // layoutId:  The layout to send.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void SendGlassVideoLayout(int layoutId)
    {
        if (_activeProfile == null)
        {
            DebugLog.Write(LogChannel.Video, "ProfileManager.SendGlassVideoLayout: no active profile.", LogLevel.Error);
            return;
        }

        WindowLayoutRepository layoutRepo = new WindowLayoutRepository();
        IReadOnlyList<SlotPlacement> placements = layoutRepo.GetSlotPlacements(layoutId);
        IReadOnlyList<SlotAssignment> slots = _activeProfile.GetSlots();

        foreach (SlotPlacement placement in placements)
        {
            if (!_definedSlots.Contains(placement.SlotNumber))
            {
                string cmd = "slot_define " + placement.SlotNumber
                    + " " + placement.X
                    + " " + placement.Y
                    + " " + placement.Width
                    + " " + placement.Height;
                DebugLog.Write(LogChannel.Video, "ProfileManager.SendGlassVideoLayout: sending " + cmd, LogLevel.Trace);
                GlassContext.GlassVideoPipe.Send(cmd);
                _definedSlots.Add(placement.SlotNumber);
            }
        }

        WindowLayout? layout = layoutRepo.GetLayoutById(layoutId);
        if (layout?.UISkinId.HasValue == true)
        {
            VideoSourceGateway sourceRepo = new VideoSourceGateway();
            IReadOnlyList<VideoSource> sources = sourceRepo.GetByUISkin(layout.UISkinId.Value);
            foreach (VideoSource source in sources)
            {
                string cmd = "region_source " + source.Name
                    + " " + source.X
                    + " " + source.Y
                    + " " + source.Width
                    + " " + source.Height;
                DebugLog.Write(LogChannel.Video, "ProfileManager.SendGlassVideoLayout: sending " + cmd, LogLevel.Trace);
                GlassContext.GlassVideoPipe.Send(cmd);
            }
            DebugLog.Write(LogChannel.Video, "ProfileManager.SendGlassVideoLayout: sent " + sources.Count + " video sources.", LogLevel.Trace);
        }
        else
        {
            DebugLog.Write(LogChannel.Video, "ProfileManager.SendGlassVideoLayout: no UI skin assigned to layout, skipping video sources.", LogLevel.Trace);
        }

        VideoDestinationGateway destRepo = new VideoDestinationGateway();
        IReadOnlyList<VideoDestination> destinations = destRepo.GetAll();
        foreach (VideoDestination dest in destinations)
        {
            string cmd = "region_dest " + dest.Name
                + " " + dest.X
                + " " + dest.Y
                + " " + dest.Width
                + " " + dest.Height;
            DebugLog.Write(LogChannel.Video, "ProfileManager.SendGlassVideoLayout: sending " + cmd, LogLevel.Trace);
            GlassContext.GlassVideoPipe.Send(cmd);
        }
        DebugLog.Write(LogChannel.Video, "ProfileManager.SendGlassVideoLayout: sent " + destinations.Count + " video destinations.", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PushCommandState
    //
    // Sends every command definition to ISXGlass over the pipe: cmd_define for each command
    // that has steps, followed by cmd_step for each of its steps in sequence order.  Key step
    // values that name a key alias are resolved to their keystroke strings before sending.
    // Page load steps are handled by Glass and are not sent.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void PushCommandState()
    {
        CommandGateway commandGateway = new CommandGateway();
        KeyAliasGateway aliasGateway = new KeyAliasGateway();
        List<Command> commands = commandGateway.GetAllCommands();
        int defined = 0;

        foreach (Command command in commands)
        {
            Command? full = commandGateway.GetCommand(command.Id);
            if (full == null)
            {
                DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.PushCommandState: commandId=" + command.Id
                    + " not found, skipping.", LogLevel.Warn);
                continue;
            }

            if (full.Steps.Count == 0)
            {
                DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.PushCommandState: commandId=" + command.Id
                    + " name='" + command.Name + "' has no steps, skipping.", LogLevel.Trace);
                continue;
            }

            GlassContext.ISXGlassPipe.Send("cmd_define " + full.Id);
            defined++;

            foreach (CommandStep step in full.Steps.OrderBy(s => s.Sequence))
            {
                if (step.Type == "pageload")
                {
                    DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.PushCommandState: commandId=" + full.Id
                        + " step=" + step.Sequence + " is a page load, not sent.", LogLevel.Trace);
                    continue;
                }

                string value = step.Value;
                string message;

                if (step.Type == "key")
                {
                    string? resolved = aliasGateway.Resolve(value);
                    if (resolved != null)
                    {
                        value = resolved;
                    }
                    message = "cmd_step " + full.Id + " " + step.Sequence + " " + step.Type + " "
                        + step.PressType + " " + step.DelayMs + " " + value;
                }
                else
                {
                    message = "cmd_step " + full.Id + " " + step.Sequence + " " + step.Type + " "
                        + step.DelayMs + " " + value;
                }

                GlassContext.ISXGlassPipe.Send(message);
                DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.PushCommandState: sent " + message,
                    LogLevel.Trace);
            }
        }

        DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.PushCommandState: defined " + defined + " of "
            + commands.Count + " commands.", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PushRelayGroupState
    //
    // Sends the relay group membership of the given profile to ISXGlass, one message per
    // group: "relay_group <groupId> <databaseId> <databaseId> ...".  Groups with no member in
    // the profile are not sent.  A member without a database id is left out of its group's
    // message.
    //
    // profileId:  The profile whose relay group membership is sent.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void PushRelayGroupState(ProfileId profileId)
    {
        if (!profileId.Exists)
        {
            DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.PushRelayGroupState: no profile id, nothing sent.",
                LogLevel.Warn);
            return;
        }

        List<RelayGroup> groups = new RelayGroupGateway().GetAllGroupsForProfile((int)profileId.Value);
        int sent = 0;

        foreach (RelayGroup group in groups)
        {
            if (group.Characters.Count == 0)
            {
                DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.PushRelayGroupState: group " + group.Id
                    + " has no members in profile " + profileId + ", skipping.", LogLevel.Trace);
                continue;
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.Append("relay_group " + group.Id);
            foreach (Character character in group.Characters)
            {
                if (!character.DatabaseId.Exists)
                {
                    DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.PushRelayGroupState: '" + character.Name
                        + "' in group " + group.Id + " has no database id, left out.", LogLevel.Warn);
                    continue;
                }
                builder.Append(" " + character.DatabaseId.Value);
            }

            string message = builder.ToString();
            GlassContext.ISXGlassPipe.Send(message);
            sent++;
            DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.PushRelayGroupState: sent " + message,
                LogLevel.Trace);
        }

        DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.PushRelayGroupState: sent " + sent + " of "
            + groups.Count + " groups for profile " + profileId + ".", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HandleISXGlassMessage
    //
    // Parses and dispatches a message received from ISXGlass over the notify pipe.
    // session_connected registers the session with the session registry and, when a profile
    // is active, resolves the session's character and assigns its GlassVideo slot.
    // session_disconnected unassigns the session in GlassVideo and removes it from the
    // session registry.  Any other message is logged.
    //
    // msg:  The message received.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void HandleISXGlassMessage(string msg)
    {
        DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.HandleISXGlassMessage: message in " + msg, LogLevel.Trace);
        string[] parts = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.HandleISXGlassMessage: empty message ignored", LogLevel.Warn);
            return;
        }

        string messageId = parts[0];

        switch (messageId)
        {
            case "session_connected":
                {
                    if (parts.Length < 4)
                    {
                        DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.HandleISXGlassMessage: malformed session_connected: "
                            + msg, LogLevel.Warn);
                        return;
                    }

                    string sessionName = parts[1];
                    uint pid = uint.Parse(parts[2]);
                    IntPtr hwnd = (IntPtr)Convert.ToUInt64(parts[3], 16);

                    string characterName = string.Empty;

                    if (HasActiveProfile)
                    {
                        bool hasId = uint.TryParse(sessionName.Substring(2), out uint accountId);
                        if (!hasId)
                        {
                            DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.HandleISXGlassMessage: no integer account-id: "
                                + sessionName, LogLevel.Warn);
                            return;
                        }

                        characterName = GetCharacterNameByAccountId(accountId);
                        DebugLog.Write(LogChannel.Sessions, "ProfileManager.HandleISXGlassMessage: session connected: "
                            + sessionName + ", pid=" + pid + ", character=" + characterName, LogLevel.Trace);
                        GlassContext.SessionRegistry.OnSessionConnected(sessionName, characterName, pid, hwnd);
                        int slot = GetSlotForCharacter(characterName);
                        if (slot == -1)
                        {
                            DebugLog.Write(LogChannel.Sessions, "ProfileManager.HandleISXGlassMessage: no slot for "
                                + characterName + ", slot_assign not sent", LogLevel.Warn);
                            return;
                        }

                        string cmd = "slot_assign " + slot + " " + sessionName + " " + hwnd.ToString("X");
                        DebugLog.Write(LogChannel.Sessions, "ProfileManager.HandleISXGlassMessage: sending " + cmd, LogLevel.Trace);
                        GlassContext.GlassVideoPipe.Send(cmd);
                    }
                    else
                    {
                        DebugLog.Write(LogChannel.Sessions, "ProfileManager.HandleISXGlassMessage: session connected: "
                            + sessionName + ", pid=" + pid + ", no active profile", LogLevel.Trace);
                        GlassContext.SessionRegistry.OnSessionConnected(sessionName, characterName, pid, hwnd);
                    }

                    break;
                }

            case "session_disconnected":
                {
                    if (parts.Length < 2)
                    {
                        DebugLog.Write(LogChannel.Sessions, "ProfileManager.HandleISXGlassMessage: malformed session_disconnected: "
                            + msg, LogLevel.Warn);
                        return;
                    }

                    string sessionName = parts[1];
                    DebugLog.Write(LogChannel.Sessions, "ProfileManager.HandleISXGlassMessage: session_disconnected: "
                        + sessionName, LogLevel.Trace);
                    GlassContext.GlassVideoPipe.Send("unassign " + sessionName);
                    if (GlassContext.SessionRegistry != null)
                    {
                        GlassContext.SessionRegistry.OnSessionDisconnected(sessionName);
                    }
                    else
                    {
                        DebugLog.Write(LogChannel.Sessions, "ProfileManager.HandleISXGlassMessage: no session registry, "
                            + sessionName + " not removed", LogLevel.Warn);
                    }

                    break;
                }

            default:
                DebugLog.Write(LogChannel.ISXGlass, "ProfileManager.HandleISXGlassMessage: unhandled message: " + msg, LogLevel.Trace);
                break;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OnSessionAdded
    //
    // Handles SignalSessionAdded.  When every character in the active profile has entered a
    // zone, starts the UCS keepalive.  ISXGlass ignores a start for a repeat that is already
    // running, so the start is sent on each signal once all characters are in.
    //
    // signal:  The signal naming the character whose session was identified.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void OnSessionAdded(SignalSessionAdded signal)
    {
        DebugLog.Write(LogChannel.Profiles, "ProfileManager.OnSessionAdded: " + signal.CharacterName
            + " entered a zone.", LogLevel.Trace);

        if (_activeProfile == null)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileManager.OnSessionAdded: no active profile, keepalive not started.",
                LogLevel.Trace);
            return;
        }

        if (GlassContext.SessionRegistry == null)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileManager.OnSessionAdded: no session registry, keepalive not started.",
                LogLevel.Warn);
            return;
        }

        foreach (SlotAssignment slot in _activeProfile.GetSlots())
        {
            Character? character = CharacterRepository.Instance.GetByDatabaseId(slot.DatabaseId);
            if (character == null)
            {
                DebugLog.Write(LogChannel.Profiles, "ProfileManager.OnSessionAdded: no character for id="
                    + slot.DatabaseId + ", keepalive not started.", LogLevel.Warn);
                return;
            }

            if (!GlassContext.SessionRegistry.IsCharacterIdentified(character.Name))
            {
                DebugLog.Write(LogChannel.Profiles, "ProfileManager.OnSessionAdded: " + character.Name
                    + " not yet in a zone, keepalive not started.", LogLevel.Trace);
                return;
            }
        }

        DebugLog.Write(LogChannel.Profiles, "ProfileManager.OnSessionAdded: all profile characters are in a zone.",
            LogLevel.Trace);
        StartUcsKeepalive();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetCharacterNameByAccountId
    //
    // Searches the active profile's slot assignments for a character whose
    // account ID matches the given value. Returns the character name, or
    // empty string if no match is found or no profile is active.
    // Multiple characters may share an account, but only one can be in a profile.
    //
    // accountId:  The account ID to search for.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string GetCharacterNameByAccountId(uint accountId)
    {
        if (_activeProfile == null)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileManager.GetCharacterNameByAccountId: no active profile", LogLevel.Trace);
            return string.Empty;
        }

        foreach (SlotAssignment slot in _activeProfile.GetSlots())
        {
            Character? character = CharacterRepository.Instance.GetByDatabaseId(slot.DatabaseId);
            if (character != null && character.AccountId == accountId)
            {
                DebugLog.Write(LogChannel.Profiles, "ProfileManager.GetCharacterNameByAccountId: accountId=" + accountId
                    + " character=" + character.Name, LogLevel.Trace);
                return character.Name;
            }
        }

        DebugLog.Write(LogChannel.Profiles, "ProfileManager.GetCharacterNameByAccountId: no match for accountId=" + accountId, LogLevel.Trace);
        return string.Empty;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetSlotForCharacter
    //
    // Returns the slot number assigned to the named character in the active profile,
    // or -1 if no assignment exists or no profile is active.
    //
    // characterName:  The character name to look up.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public int GetSlotForCharacter(string characterName)
    {
        if (_activeProfile == null)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileManager.GetSlotForCharacter: no active profile", LogLevel.Error);
            return -1;
        }

        SlotAssignment? assignment = _activeProfile.GetSlots()
            .FirstOrDefault(s => CharacterRepository.Instance.GetByDatabaseId(s.DatabaseId)?.Name == characterName);

        if (assignment == null)
        {
            DebugLog.Write(LogChannel.Profiles, "ProfileManager.GetSlotForCharacter: no slot for character '" + 
                    characterName + "'", LogLevel.Error);
            return -1;
        }

        DebugLog.Write(LogChannel.Profiles, "ProfileManager.GetSlotForCharacter: character='" + 
                characterName + "' slot=" + assignment.SlotNumber, LogLevel.Trace);
        return assignment.SlotNumber;
    }
}
