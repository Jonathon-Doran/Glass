using Glass.Network.Client;
using Glass.Network.Protocol;
using Glass.Network.Capture;
using Glass.Data.Models;
using static Glass.Network.Protocol.SoeConstants;
using System.Runtime.InteropServices;
using Glass.Core.Logging;
using Glass.Data.Repositories;

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
    private readonly Dictionary<int, int> _characterIdBySession = new();
    private readonly Dictionary<int, Connection> _connectionsByPort = new();
    private readonly SoeStream.AppPacketHandler _appPacketHandler;
    private readonly int _arqSeqGiveUp = 512;
    private readonly string? _localIp;
    private int _nextSessionId = 0;
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

        public int SessionId { get; set; } = -1;

        ///////////////////////////////////////////////////////////////////////////////////////////////
        // CharacterName
        //
        // The character logged in to this session.
        // Set initially from ISX, confirmed later from network traffic.
        ///////////////////////////////////////////////////////////////////////////////////////////////
        public string CharacterName { get; set; } = string.Empty;

        ///////////////////////////////////////////////////////////////////////////////////////////////
        // CharacterId
        //
        // The database primary key for this character.
        // Set when the character is identified, either from profile data
        // or from network traffic.  Zero means not yet identified.
        ///////////////////////////////////////////////////////////////////////////////////////////////
        public int CharacterId { get; set; }

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
    public SessionRegistry(SoeStream.AppPacketHandler _handler)
    {
        _appPacketHandler = _handler;
        _localIp = PacketCapture.GetLocalIP();
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

    public void OnSessionConnected(string sessionName, string characterName, uint pid, IntPtr hwnd)
    {
       lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionName, out var entry))
            {
                entry = new SessionEntry { SessionName = sessionName };
                _sessions[sessionName] = entry;
            }
            entry.CharacterName = characterName;
            entry.Pid = pid;
            entry.Hwnd = hwnd;
        }

        _sessionCount++;
        DebugLog.Write($"SessionRegistry.OnSessionConnected: session={sessionName} count={_sessionCount} character={characterName} pid={pid} hwnd={hwnd}");

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
        DebugLog.Write(LogChannel.Sessions, $"SessionRegistry.OnSessionDisconnected: session={sessionName} count will be={_sessionCount - 1}.");

        SessionEntry? session = GetSession(sessionName);
        if (session == null)
        {
            DebugLog.Write(LogChannel.Database, $"SessionRegistry.OnSessionDisconnected: session '{sessionName}' not found, nothing to save.");
            return;
        }

        if (session.CharacterId < 0)
        {
            DebugLog.Write(LogChannel.Database, $"SessionRegistry.OnSessionDisconnected: session '{sessionName}' has no bound character, nothing to save.");
            return;
        }

        CharacterRepository.Instance.Save(session.CharacterId);

        lock (_lock)
        {
            _sessions.Remove(sessionName);
        }

        _sessionCount--;

        if (_sessionCount <= 0)
        {
            _sessionCount = 0;
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
            return _sessions.TryGetValue(sessionName, out var entry) ? entry : null;
        }
    }

    public Character? CharacterFromSession(int sessionId)
    {
        if (! _characterIdBySession.TryGetValue(sessionId, out var characterId))
        {
            return null;
        }

        return CharacterRepository.Instance.GetById(characterId);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetAllClients
    //
    // Returns a read-only view of all active clients.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyDictionary<int, Connection> GetAllConnections()
    {
        return _connectionsByPort;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetChannel
    //
    // Classifies a packet into a stream type based on port ranges and direction.
    //
    // metadata:  The packet metadata containing source/dest IP and ports.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public SoeConstants.StreamId GetChannel(PacketMetadata metadata)
    {
        bool isFromClient = (metadata.SourceIp == _localIp);
        int remotePort = isFromClient ? metadata.DestPort : metadata.SourcePort;

        if (remotePort >= SoeConstants.LoginServerMinPort &&
            remotePort <= SoeConstants.LoginServerMaxPort)
        {
            return isFromClient
                ? SoeConstants.StreamId.StreamClientToWorld
                : SoeConstants.StreamId.StreamWorldToClient;
        }

        if (remotePort >= SoeConstants.WorldServerGeneralMinPort &&
            remotePort <= SoeConstants.WorldServerGeneralMaxPort)
        {
            return isFromClient
                ? SoeConstants.StreamId.StreamClientToWorld
                : SoeConstants.StreamId.StreamWorldToClient;
        }

        return isFromClient
            ? SoeConstants.StreamId.StreamClientToZone
            : SoeConstants.StreamId.StreamZoneToClient;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetConnection
    //
    // Returns the Connection for the given packet metadata.  Determines the
    // channel, looks up or creates the Connection.  Thread-safe
    //
    // metadata:  The packet metadata containing source/dest IP, ports, etc.
    ///////////////////////////////////////////////////////////////////////////////////////////////

    public Connection GetConnection(PacketMetadata metadata)
    {
        bool isFromClient = (metadata.SourceIp == _localIp);
        int localPort = isFromClient ? metadata.SourcePort : metadata.DestPort;

        lock (_lock)
        {
            if (!_connectionsByPort.TryGetValue(localPort, out Connection? connection))
            {
                connection = new Connection(localPort, _arqSeqGiveUp, _appPacketHandler);
                _connectionsByPort[localPort] = connection;

                DebugLog.Write(LogChannel.Sessions,
                    "SessionRegistry.GetConnection: new connection on port " + localPort
                    + ", connections=" + _connectionsByPort.Count);
            }

            return connection;
        }
    }
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // FindSessionByCharacter
    //
    // Finds the connection associated with metadata, then extracts the name from session associated with the connection.
    //
    // metadata:  Packet metadata from a received packet
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public string CharacterNameFromMetadata(PacketMetadata metadata)
    {
        // Note:  If the connection does not exist it will be created.  There is no possibility of null.
        Connection c = GetConnection(metadata);
        if (c.SessionId != -1)
        {
            return CharacterNameFromSession(c.SessionId);
        }

        return ("unknown");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // FindSessionByCharacter
    //
    // Searches all session entries for one matching the given character name.
    // Returns the SessionEntry if found, null otherwise.  For diagnostic use.
    //
    // characterName:  The character name to search for
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public SessionEntry? FindSessionByCharacter(string characterName)
    {
        lock (_lock)
        {
            foreach (KeyValuePair<string, SessionEntry> pair in _sessions)
            {
                if (pair.Value.CharacterName == characterName)
                {
                    return pair.Value;
                }
            }
        }

        return null;
    }

    public Connection? FindConnectionByCharacter(string characterName)
    {
        SessionEntry? session = FindSessionByCharacter(characterName);
        if (session == null)
        {
            return null;
        }

        lock (_lock)
        {
            foreach (KeyValuePair<int, Connection> pair in _connectionsByPort)
            {
                if (pair.Value.SessionId == session.SessionId)
                {
                    return pair.Value;
                }
            }
        }
        return null;
    }

    public string CharacterNameFromSession(int sessionId)
    {
        lock (_lock)
        {
            foreach (KeyValuePair<string, SessionEntry> pair in _sessions)
            {
                if (pair.Value.SessionId == sessionId)
                {
                    return pair.Value.CharacterName;
                }
            }
        }

        return string.Empty;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetStream
    //
    // Returns the SoeStream for the given packet metadata.  Determines the
    // channel, looks up or creates the Connection, and returns the
    // appropriate stream.  Thread-safe.
    //
    // metadata:  The packet metadata containing source/dest IP, ports, etc.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public SoeStream GetStream(PacketMetadata metadata)
    {
        // Note that the connection will never be null.  It is auto-created if it does not already exist
        Connection connection = GetConnection(metadata);
        return connection.GetStream(metadata.Channel);
    }
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // IdentifyConnection
    //
    // Associates a network connection with a known character.  Called by packet
    // handlers when they decode a character name from network traffic.
    // Finds the connection by local port (from metadata) and searches existing
    // SessionEntries for a matching character name.  If found, links the
    // connection's port to the SessionEntry and assigns the next connection ID.
    //
    // characterName:  The character name decoded from packet data.
    // metadata:       The packet metadata identifying the connection.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void IdentifyConnection(string characterName, PacketMetadata metadata)
    {
        bool isFromClient = (metadata.SourceIp == _localIp);
        int localPort = isFromClient ? metadata.SourcePort : metadata.DestPort;
        DebugLog.Write(LogChannel.LowNetwork, "IdentifyConnection for " + characterName +
            " on port " + metadata.DestPort);
        lock (_lock)
        {
            if (!_connectionsByPort.TryGetValue(localPort, out Connection? connection))
            {
                DebugLog.Write(LogChannel.LowNetwork,
                    "SessionRegistry.IdentifyConnection: no connection for port " + localPort
                    + ", character=" + characterName);
                return;
            }

            DebugLog.Write(LogChannel.LowNetwork, "Connection found on local port "
                + connection.LocalPort);

            if (connection.SessionId >= 0)
            {
                DebugLog.Write(LogChannel.Sessions,
                    "SessionRegistry.IdentifyConnection: port " + localPort
                    + " already identified as connectionId=" + connection.SessionId
                    + ", character=" + characterName);
                return;
            }

            DebugLog.Write(LogChannel.LowNetwork, "Identify:  look for port " + localPort);
            
            foreach (KeyValuePair<string, SessionEntry> pair in _sessions)
            {
                DebugLog.Write(LogChannel.LowNetwork, "check " + pair.Value.CharacterName + " on key " +
                    pair.Key + " port " + pair.Value.LocalPort);

                if (pair.Value.CharacterName == characterName)
                {
                    Character? character = CharacterRepository.Instance.GetByName(characterName);
                    if (character == null)
                    {
                        DebugLog.Write(LogChannel.LowNetwork, "No Character named " + characterName + " found in the CharacterRepository");
                        return;
                    }

                    connection.SessionId = _nextSessionId;
                    _nextSessionId++;
                    pair.Value.LocalPort = localPort;
                    pair.Value.SessionId = connection.SessionId;
                    pair.Value.CharacterId = character.CharacterId;
                    _characterIdBySession[connection.SessionId] = character.CharacterId;
                    connection.CharacterId = character.CharacterId;
                    connection.Character = character;

                    DebugLog.Write(LogChannel.LowNetwork,
                        "SessionRegistry.IdentifyConnection: port " + localPort
                        + " identified as character=" + characterName
                        + " session=" + pair.Value.SessionName
                        + " connectionId=" + connection.SessionId);
                    return;
                }
            }

            DebugLog.Write(LogChannel.LowNetwork,
                "SessionRegistry.IdentifyConnection: no SessionEntry for character="
                + characterName + " on port " + localPort
                + ", connection remains unidentified");
        }
    }
}