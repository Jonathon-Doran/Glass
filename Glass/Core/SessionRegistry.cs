using Glass.Core.Logging;
using Glass.Core.Signals;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Client;
using Glass.World;
using System.Runtime.InteropServices;
using static Glass.Network.Protocol.SoeConstants;

namespace Glass.Core;

// Tracks active EverQuest sessions and their associated process and window information.
public class SessionRegistry
{
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public event Action? AllSessionsDisconnected;


    private readonly Dictionary<string, SessionEntry> _sessions = new();
    private readonly object _lock = new();

    // This is a count of ISX sessions, not wire sessions
    private int _isxSessionCount = 0;

    // Identified zone sessions that have not yet closed
    private uint _liveZoneSessions = 0;

    // Set when every ISX session has disconnected and teardown is waiting on the zone sessions
    private bool _teardownPending = false;

    // Represents a single active EverQuest session.
    public class SessionEntry
    {
        ///////////////////////////////////////////////////////////////////////////////////////////////
        // SessionName
        //
        // The Inner Space session name (e.g., "is1", "is7").
        // Empty until ISX reports the session.
        ///////////////////////////////////////////////////////////////////////////////////////////////
        public string SessionName { get; set; } = string.Empty;

        public SessionId Id { get; set; } = SessionId.None;

        ///////////////////////////////////////////////////////////////////////////////////////////////
        // CharacterName
        //
        // The character logged in to this session.
        // Set initially from ISX, confirmed later from network traffic.
        ///////////////////////////////////////////////////////////////////////////////////////////////
        public string CharacterName { get; set; } = string.Empty;

        ///////////////////////////////////////////////////////////////////////////////////////////////
        // DatabaseId
        //
        // The database primary key for this character.
        // Set when the character is identified, either from profile data
        // or from network traffic.  Zero means not yet identified.
        ///////////////////////////////////////////////////////////////////////////////////////////////
        public DatabaseId CharacterDatabaseId { get; set; }

        ///////////////////////////////////////////////////////////////////////////////////////////////
        // Pid
        //
        // The process ID of the EQ client.  Set by ISX on connection.
        ///////////////////////////////////////////////////////////////////////////////////////////////
        public uint Pid { get; set; }

        ///////////////////////////////////////////////////////////////////////////////////////////////
        // Hwnd
        //
        // The main window handle of the EQ client.  Set by ISX on connection.
        ///////////////////////////////////////////////////////////////////////////////////////////////
        public IntPtr Hwnd { get; set; }

        ///////////////////////////////////////////////////////////////////////////////////////////////
        // LocalPort
        //
        // The local UDP port used by this EQ client.  Set by SessionDemux
        // when network traffic is first seen.  Zero means no network
        // traffic observed yet.
        ///////////////////////////////////////////////////////////////////////////////////////////////
        public int LocalPort { get; set; }

        ///////////////////////////////////////////////////////////////////////////////////////////////
        // SlotNumber
        //
        // The slot position in the active profile.  Set at launch time.
        // Zero means not assigned.
        ///////////////////////////////////////////////////////////////////////////////////////////////
        public int SlotNumber { get; set; }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SessionRegistry Constructor
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public SessionRegistry()
    {
        GlassContext.FocusTracker.SessionActivated += OnSessionActivated;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnSessionActivated
    //
    // Called when FocusTracker detects a new EQ session has gained focus.
    // Notifies ISXGlass of the active session.
    //
    // sessionName:  The session that gained focus e.g. "is7"
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void OnSessionActivated(string sessionName)
    {
        GlassContext.ISXGlassPipe.Send($"activate {sessionName}");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnSessionConnected
    //
    // Adds or updates a session when ISXGlass reports it has connected.
    //
    // sessionName:    The session name e.g. "is7"
    // characterName:  The character logged in to this session
    // pid:            The process ID of the EQ client
    // hwnd:           The main window handle of the EQ client
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public SessionEntry OnSessionConnected(string sessionName, string characterName, uint pid, IntPtr hwnd)
    {
        SessionEntry? entry;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionName, out entry))
            {
                entry = new SessionEntry { SessionName = sessionName };
                _sessions[sessionName] = entry;
            }
            entry.CharacterName = characterName;
            entry.Pid = pid;
            entry.Hwnd = hwnd;
            _isxSessionCount++;
        }


        DebugLog.Write(LogChannel.Sessions, $"SessionRegistry.OnSessionConnected: session={sessionName} count={_isxSessionCount} character={characterName} pid={pid} hwnd={hwnd}", LogLevel.Info);

        return entry;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnSessionDisconnected
    //
    // Removes a session from the registry when it disconnects.  The session's character is saved when one is bound;
    // a session with no bound character is still removed and counted as disconnected.
    //
    // sessionName:  The session that disconnected
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void OnSessionDisconnected(string sessionName)
    {
        bool allIsxDisconnected = false;

        DebugLog.Write(LogChannel.Sessions, $"SessionRegistry.OnSessionDisconnected: session={sessionName} count will be={_isxSessionCount - 1}.", LogLevel.Info);

        SessionEntry? session = GetSession(sessionName);
        if (session == null)
        {
            DebugLog.Write(LogChannel.Database, $"SessionRegistry.OnSessionDisconnected: session '{sessionName}' not found, nothing to save.", LogLevel.Error);
            return;
        }

        // Check for a valid character associated with the ISX session
        if (session.CharacterDatabaseId < 0)
        {
            DebugLog.Write(LogChannel.Database, $"SessionRegistry.OnSessionDisconnected: session '{sessionName}' has no bound character, nothing to save, removing session.", LogLevel.Error);
        }
        else
        {
            CharacterRepository.Instance.Save(session.CharacterDatabaseId);
        }

        lock (_lock)
        {
            _sessions.Remove(sessionName);
            _isxSessionCount--;
            if (_isxSessionCount <= 0)
            {
                _isxSessionCount = 0;
                _teardownPending = true;
                allIsxDisconnected = true;
            }
        }

        if (allIsxDisconnected)
        {
            DebugLog.Write(LogChannel.Sessions, "SessionRegistry.OnSessionDisconnected: all ISX sessions disconnected, "
                + "teardown pending.", LogLevel.Trace);
            RaiseAllSessionsDisconnectedIfComplete();
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // FindSessionByHwnd
    //
    // Returns the session name for the given window handle, or null if not found.
    //
    // hwnd:  The window handle to look up
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public string? FindSessionByHwnd(IntPtr hwnd)
    {
        lock (_lock)
        {
            foreach (KeyValuePair<string, SessionEntry> pair in _sessions)
            {
                if (pair.Value.Hwnd == hwnd)
                {
                    return pair.Key;
                }
            }
        }
        return null;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetSession
    //
    // Returns the entry for a specific session, or null if not found.
    //
    // sessionName:  The session to query
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public SessionEntry? GetSession(string sessionName)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionName, out SessionEntry? entry) ? entry : null;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ZoneFromMetadata
    //
    // Returns the zone of the session a packet arrived on.  A zone session serves one zone, set
    // when the player profile arrives on it, so packets that preceded the profile on the same
    // session resolve to that zone as well.  A session whose zone is not known yields None.
    //
    // metadata:  Packet metadata from a received packet
    //
    // Returns the session's zone, or ZoneId.None when it is not known.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ZoneId ZoneFromMetadata(PacketMetadata metadata)
    {
        ZoneId zoneId = metadata.Session.ZoneId;
        if (!zoneId.Exists)
        {
            DebugLog.Write(LogChannel.Opcodes, "SessionRegistry.ZoneFromMetadata: message " + metadata.MessageId
                + " opcode " + metadata.WireValue + " on stream " + StreamNames[metadata.Channel] + ", session "
                + metadata.Session.SessionId + " local port " + metadata.LocalPort + " has no zone.",
                LogLevel.Trace);
        }
        return zoneId;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // IdentifySession
    //
    // Attaches the Character named by a player profile to the network session the profile
    // arrived on, and fills in the ISXGlass session entry registered under that character's
    // name so the two sides are linked.
    //
    // character:    The character named in the player profile
    // metadata:     The profile packet's context, carrying the network session
    //
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void IdentifySession(Character character, PacketMetadata metadata)
    {
        _liveZoneSessions++;

        metadata.Session.SetCharacter(character);

        GroupId groupId = GroupId.None;
        if (GroupRepository.Instance.TryGetGroupOf(character.CharacterId, out Group? group))
        {
            groupId = group.Id;
        }

        RaidId raidId = RaidId.None;
        if (RaidRepository.Instance.TryGetRaidOf(character.CharacterId, out Raid? raid))
        {
            raidId = raid.Id;
        }

        GlassContext.SignalBus.Publish(new SignalMembershipChanged(groupId, raidId));

        lock (_lock)
        {
            foreach (KeyValuePair<string, SessionEntry> pair in _sessions)
            {
                if (pair.Value.CharacterName != character.Name)
                {
                    continue;
                }

                pair.Value.LocalPort = metadata.LocalPort;
                pair.Value.Id = metadata.Session.SessionId;
                pair.Value.CharacterDatabaseId = character.DatabaseId;

                DebugLog.Write(LogChannel.LowNetwork, "SessionRegistry.IdentifySession: session "
                    + metadata.Session.SessionId + " on port " + metadata.LocalPort + " is character "
                    + character.Name + ", ISXGlass session " + pair.Value.SessionName, LogLevel.Trace);

                GlassContext.SignalBus.Publish(new SignalSessionAdded(metadata.Session.SessionId, character.Name));
                DebugLog.Write(LogChannel.SignalBus, "SessionRegistry.IdentifySession: published SignalSessionAdded "
                    + metadata.Session.SessionId + " for " + character.Name, LogLevel.Trace);

                return;
            }
        }

        DebugLog.Write(LogChannel.LowNetwork, "SessionRegistry.IdentifySession: session " + metadata.Session.SessionId
            + " is character " + character.Name + " but no ISXGlass session entry is registered for that character",
            LogLevel.Warn);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // IsCharacterIdentified
    //
    // Reports whether the named character's session has been identified from network traffic,
    // meaning the character has entered a zone.
    //
    // characterName:  The character to look up.
    //
    // Returns:  True when a session for the character has a known session id; false when the
    //           character has no session or has not yet been identified.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public bool IsCharacterIdentified(string characterName)
    {
        lock (_lock)
        {
            foreach (KeyValuePair<string, SessionEntry> pair in _sessions)
            {
                if (pair.Value.CharacterName != characterName)
                {
                    continue;
                }

                bool identified = pair.Value.Id.Exists;
                DebugLog.Write(LogChannel.Sessions, "SessionRegistry.IsCharacterIdentified: " + characterName
                    + " on " + pair.Key + " identified=" + identified, LogLevel.Trace);
                return identified;
            }
        }

        DebugLog.Write(LogChannel.Sessions, "SessionRegistry.IsCharacterIdentified: no session for "
            + characterName, LogLevel.Trace);
        return false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnPcapReplayFinished
    //
    // Marks teardown as pending when a pcap replay has delivered its last packet, then raises AllSessionsDisconnected
    // if no identified zone session remains open.  A replay has no ISX sessions, so the end of the replay takes the
    // place of the last ISX disconnect.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void OnPcapReplayFinished()
    {
        lock (_lock)
        {
            _teardownPending = true;
        }

        DebugLog.Write(LogChannel.Sessions, "SessionRegistry.OnPcapReplayFinished: replay finished, "
            + _liveZoneSessions + " zone sessions still open, teardown pending.", LogLevel.Trace);

        RaiseAllSessionsDisconnectedIfComplete();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RaiseAllSessionsDisconnectedIfComplete
    //
    // Raises AllSessionsDisconnected when every ISX session has disconnected and no identified zone session
    // remains open.  The pending flag is cleared when the event is raised, so it is raised once per profile.
    // When either side is still open, nothing is raised.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void RaiseAllSessionsDisconnectedIfComplete()
    {
        lock (_lock)
        {
            if (!_teardownPending)
            {
                DebugLog.Write(LogChannel.Sessions, "SessionRegistry.RaiseAllSessionsDisconnectedIfComplete: "
                    + "ISX sessions still connected, not raising.", LogLevel.Trace);
                return;
            }

            if (_liveZoneSessions > 0)
            {
                DebugLog.Write(LogChannel.Sessions, "SessionRegistry.RaiseAllSessionsDisconnectedIfComplete: "
                    + _liveZoneSessions + " zone sessions still open, not raising.", LogLevel.Trace);
                return;
            }

            _teardownPending = false;
        }

        DebugLog.Write(LogChannel.Sessions, "SessionRegistry.RaiseAllSessionsDisconnectedIfComplete: ISX and zone "
            + "sessions all closed, raising AllSessionsDisconnected.", LogLevel.Trace);
        AllSessionsDisconnected?.Invoke();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ZoneSessionClosing
    //
    // Records that a zone session is disconnecting.  A zone session with a Character was counted
    // as live when it was identified, so it is removed from the live count here.  For a session
    // with a known zone, its hold on that zone is released, and when it was the last live session
    // in the zone, the zone's mob data is saved.  AllSessionsDisconnected is then raised if
    // teardown is complete.  A session that is not a zone session, or was never identified, is
    // logged and nothing is released.
    //
    // session:  The session whose disconnect is being processed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void ZoneSessionClosing(Session session)
    {
        // We only handle Zone sessions here
        if (session.ServerType != ServerType.Zone)
        {
            DebugLog.Write(LogChannel.Sessions, "SessionRegistry.ZoneSessionClosing: session " + session.SessionId
                + " is a " + session.ServerType + " session, nothing to release.", LogLevel.Info);
            return;
        }

        // A zone session is counted as live only once a player profile identifies it
        if (session.Character == null)
        {
            DebugLog.Write(LogChannel.Sessions, "SessionRegistry.ZoneSessionClosing: zone session " + session.SessionId
                + " was never identified, not counted as live, nothing to release.", LogLevel.Trace);
            return;
        }

        lock (_lock)
        {
            _liveZoneSessions--;

            DebugLog.Write(LogChannel.Sessions, "SessionRegistry.ZoneSessionClosing: session " + session.SessionId
                + " removed from live zone sessions, " + _liveZoneSessions + " live.", LogLevel.Trace);
        }
        CharacterRepository.Instance.Save(session.Character.DatabaseId);

        GlassContext.SignalBus.Publish(new SignalSessionRemoved(session.SessionId, session.Character.CharacterId));

        DebugLog.Write(LogChannel.SignalBus, "SessionRegistry.ZoneSessionClosing: published SignalSessionRemoved "
            + session.SessionId + " for " + session.Character.Name + " characterId=" + session.Character.CharacterId
            + ".", LogLevel.Trace);

        if (!session.ZoneId.Exists)
        {
            DebugLog.Write(LogChannel.Sessions, "SessionRegistry.ZoneSessionClosing: zone session " + session.SessionId
                + " never recorded a zone, no zone to release.", LogLevel.Warn);
        }
        else
        {
            uint remaining = ZoneRepository.Instance.SessionsLeftInZone(session.ZoneId);
            if (remaining > 0)
            {
                DebugLog.Write(LogChannel.Sessions, "SessionRegistry.ZoneSessionClosing: session " + session.SessionId
                    + " left zoneId=" + session.ZoneId + ", " + remaining + " sessions remain.", LogLevel.Info);
            }
            else
            {
                DebugLog.Write(LogChannel.Sessions, "SessionRegistry.ZoneSessionClosing: session " + session.SessionId
                    + " was the last in zoneId=" + session.ZoneId + ", saving zone.", LogLevel.Info);
                List<EntityId> mobIds = ZoneRepository.Instance.GetZoneEntityIds(session.ZoneId, EntityType.Mob);
                MobRepository.Instance.SaveMobs(mobIds);
            }
        }

        RaiseAllSessionsDisconnectedIfComplete();
    }
}