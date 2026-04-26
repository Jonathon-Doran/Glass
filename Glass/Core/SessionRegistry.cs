using Glass.Network.Client;
using Glass.Network.Protocol;
using Glass.Network.Capture;
using static Glass.Network.Protocol.SoeConstants;
using System.Runtime.InteropServices;

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
    private readonly Dictionary<int, Connection> _connectionsByPort = new();
    private readonly SoeStream.AppPacketHandler _appPacketHandler;
    private readonly int _arqSeqGiveUp = 512;
    private readonly string? _localIp;
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
        DebugLog.Write(DebugLog.Log_Sessions, $"SessionRegistry.OnSessionConnected: session={sessionName} count={_sessionCount} character={characterName} pid={pid} hwnd={hwnd}");

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
        DebugLog.Write(DebugLog.Log_Sessions, $"SessionRegistry.OnSessionDisconnected: session={sessionName} count will be={_sessionCount - 1}.");

        lock (_lock)
        {
            _sessions.Remove(sessionName);
        }

        _sessionCount--;

        if (_sessionCount <= 0)
        {
            _sessionCount = 0;
            DebugLog.Write(DebugLog.Log_Sessions, "SessionRegistry.OnSessionDisconnected: all sessions disconnected.");
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
        bool isFromClient = (metadata.SourceIp == _localIp);
        int localPort = isFromClient ? metadata.SourcePort : metadata.DestPort;

        lock (_lock)
        {
            if (!_connectionsByPort.TryGetValue(localPort, out Connection? connection))
            {
                connection = new Connection(localPort, _arqSeqGiveUp, _appPacketHandler);
                _connectionsByPort[localPort] = connection;

                DebugLog.Write(DebugLog.Log_Sessions,
                    "SessionRegistry.GetStream: new connection on port " + localPort
                    + ", connections=" + _connectionsByPort.Count);
            }

            return connection.GetStream(metadata.Channel);
        }
    }
}