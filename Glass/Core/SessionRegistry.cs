using Glass.Core.Logging;
using Glass.Core.Signals;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Client;
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

    private int _sessionCount = 0;
    private readonly Dictionary<string, SessionEntry> _sessions = new();
    private readonly object _lock = new();

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
            _sessionCount++;
        }


        DebugLog.Write(LogChannel.Sessions, $"SessionRegistry.OnSessionConnected: session={sessionName} count={_sessionCount} character={characterName} pid={pid} hwnd={hwnd}", LogLevel.Info);

        return entry;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnSessionDisconnected
    //
    // Removes a session from the registry when it disconnects.
    //
    // sessionName:  The session that disconnected
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void OnSessionDisconnected(string sessionName)
    {
        DebugLog.Write(LogChannel.Sessions, $"SessionRegistry.OnSessionDisconnected: session={sessionName} count will be={_sessionCount - 1}.", LogLevel.Info);

        SessionEntry? session = GetSession(sessionName);
        if (session == null)
        {
            DebugLog.Write(LogChannel.Database, $"SessionRegistry.OnSessionDisconnected: session '{sessionName}' not found, nothing to save.", LogLevel.Error);
            return;
        }

        if (session.CharacterDatabaseId < 0)
        {
            DebugLog.Write(LogChannel.Database, $"SessionRegistry.OnSessionDisconnected: session '{sessionName}' has no bound character, nothing to save.", LogLevel.Error);
            return;
        }

        CharacterRepository.Instance.Save(session.CharacterDatabaseId);

        bool fireAllDisconnected = false;
        lock (_lock)
        {
            _sessions.Remove(sessionName);
            _sessionCount--;
            if (_sessionCount <= 0)
            {
                _sessionCount = 0;
                fireAllDisconnected = true;
            }
        }

        if (fireAllDisconnected)
        {
            DebugLog.Write(LogChannel.Sessions, "SessionRegistry.OnSessionDisconnected: all sessions disconnected.");
            AllSessionsDisconnected?.Invoke();
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
    // Finds the connection associated with metadata, then extracts the zone from the character
    // associated with the connection.
    //
    // metadata:  Packet metadata from a received packet
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ZoneId ZoneFromMetadata(PacketMetadata metadata)
    {
        // Note:  If the connection does not exist it will be created.  There is no possibility of null.
        Character? character = metadata.Character;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "ZoneFromMetadata: metadata cannot be "
                + "mapped to a character.", LogLevel.Warn);
            return ZoneId.None;
        }
        return character.CurrentZone;
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
        metadata.Session.SetCharacter(character);

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
            }
        }

        DebugLog.Write(LogChannel.LowNetwork, "SessionRegistry.IdentifySession: session " + metadata.Session.SessionId
            + " is character " + character.Name + " but no ISXGlass session entry is registered for that character",
            LogLevel.Warn);
    }
}