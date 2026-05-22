using Glass.Core.Logging;
using Glass.Core.Signals;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Capture;
using Glass.Network.Client;
using Glass.Network.Protocol;
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
    private readonly Dictionary<int, int> _characterIdBySession = new();
    private readonly Dictionary<int, Connection> _connectionsByPort = new();
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
    public SessionRegistry()
    {
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
            if (!_sessions.TryGetValue(sessionName, out SessionEntry? entry))
            {
                entry = new SessionEntry { SessionName = sessionName };
                _sessions[sessionName] = entry;
            }
            entry.CharacterName = characterName;
            entry.Pid = pid;
            entry.Hwnd = hwnd;
            _sessionCount++;
        }


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

    public Character? CharacterFromSession(int sessionId)
    {
        int characterId;

        lock (_lock)
        {
            if (!_characterIdBySession.TryGetValue(sessionId, out characterId))
            {
                return null;
            }
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
        Dictionary<int, Connection> snapshot;
        lock (_lock)
        {
            snapshot = new Dictionary<int, Connection>(_connectionsByPort);
        }
        return snapshot;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetChannel
    //
    // Classifies a packet into a stream type based on port ranges and direction.
    //
    // dgram:  The UDP datagram with headers removed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public SoeConstants.StreamId GetChannel(UdpDatagram dgram)
    {
        bool isFromClient = (dgram.SourceIp == _localIp);

        int remotePort = isFromClient ? dgram.DestPort : dgram.SourcePort;

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
                connection = new Connection(localPort, _arqSeqGiveUp);
                _connectionsByPort[localPort] = connection;

                DebugLog.Write(LogChannel.Sessions,
                    "SessionRegistry.GetConnection: new connection on port " + localPort
                    + ", connections=" + _connectionsByPort.Count);
            }

            return connection;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetConnection
    //
    // Returns the Connection for the given packet metadata.  Determines the
    // channel, looks up or creates the Connection.  Thread-safe
    //
    // dgram:  The UDP datagram with headers removed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public Connection GetConnection(UdpDatagram dgram)
    {
        bool isFromClient = (dgram.SourceIp == _localIp);
        int localPort = isFromClient ? dgram.SourcePort : dgram.DestPort;

        lock (_lock)
        {
            if (!_connectionsByPort.TryGetValue(localPort, out Connection? connection))
            {
                connection = new Connection(localPort, _arqSeqGiveUp);
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
    //
    // Returns:  True if the session is identified, False otherwise
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public bool IdentifyConnection(string characterName, PacketMetadata metadata)
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
                return false;
            }

            DebugLog.Write(LogChannel.LowNetwork, "Connection found on local port "
                + connection.LocalPort);

            if (connection.SessionId >= 0)
            {
                DebugLog.Write(LogChannel.Sessions,
                    "SessionRegistry.IdentifyConnection: port " + localPort
                    + " already identified as connectionId=" + connection.SessionId
                    + ", character=" + characterName);
                return false;
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
                        return false;
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

                    GlassContext.SignalBus.Publish(
                        new SignalSessionAdded(connection.SessionId, characterName));

                    DebugLog.Write(LogChannel.SignalBus,
                        "SessionRegistry.IdentifyConnection: published SignalSessionAdded sessionId="
                        + connection.SessionId + " character=" + characterName);
                    return true;
                }
            }

            DebugLog.Write(LogChannel.LowNetwork,
                "SessionRegistry.IdentifyConnection: no SessionEntry for character="
                + characterName + " on port " + localPort
                + ", bootstrapping synthetic session");

            Character? bootstrapCharacter = CharacterRepository.Instance.GetByName(characterName);
            if (bootstrapCharacter == null)
            {
                DebugLog.Write(LogChannel.LowNetwork,
                    "SessionRegistry.IdentifyConnection: no Character named " + characterName
                    + " in CharacterRepository, cannot bootstrap");
                return false;
            }

            string synthName = "pcap-" + _sessionCount;
            SessionEntry synthEntry = EnsureSessionEntry(synthName, characterName, 0, IntPtr.Zero);
            _sessionCount++;

            connection.SessionId = _nextSessionId;
            _nextSessionId++;
            synthEntry.LocalPort = localPort;
            synthEntry.SessionId = connection.SessionId;
            synthEntry.CharacterId = bootstrapCharacter.CharacterId;
            _characterIdBySession[connection.SessionId] = bootstrapCharacter.CharacterId;
            connection.CharacterId = bootstrapCharacter.CharacterId;
            connection.Character = bootstrapCharacter;

            DebugLog.Write(LogChannel.LowNetwork,
                "SessionRegistry.IdentifyConnection: port " + localPort
                + " identified as character=" + characterName
                + " synth session=" + synthName
                + " connectionId=" + connection.SessionId);

            GlassContext.SignalBus.Publish(
                new SignalSessionAdded(connection.SessionId, characterName));

            DebugLog.Write(LogChannel.SignalBus,
                "SessionRegistry.IdentifyConnection: published SignalSessionAdded sessionId="
                + connection.SessionId + " character=" + characterName);

            return true;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // EnsureSessionEntry
    //
    // Looks up or creates a SessionEntry by sessionName.  Assigns the
    // character name, pid, and hwnd to the entry.  Caller must hold _lock.
    //
    // sessionName:    The session name key.
    // characterName:  The character logged in to this session.
    // pid:            The process ID of the EQ client, or 0 if not known.
    // hwnd:           The main window handle of the EQ client, or IntPtr.Zero
    //                 if not known.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private SessionEntry EnsureSessionEntry(string sessionName, string characterName, uint pid, IntPtr hwnd)
    {
        if (!_sessions.TryGetValue(sessionName, out SessionEntry? entry))
        {
            entry = new SessionEntry { SessionName = sessionName };
            _sessions[sessionName] = entry;
        }
        entry.CharacterName = characterName;
        entry.Pid = pid;
        entry.Hwnd = hwnd;
        return entry;
    }
}