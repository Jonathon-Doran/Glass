using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Network.Protocol;
using static Glass.Network.Protocol.SoeConstants;

namespace Glass.Network.Client;

///////////////////////////////////////////////////////////////////////////////////////////////
// ServerType
//
// The kind of server at the remote end of a session, decided once from the remote port.
///////////////////////////////////////////////////////////////////////////////////////////////
public enum ServerType
{
    Login = 1,
    World = 2,
    Zone = 3
}

///////////////////////////////////////////////////////////////////////////////////////////////
// Session
//
// One SOE session between an EQ client and one server, identified by its local and remote
// port pair.  Owns the streams carrying that session's traffic, one per direction,
// both created with the session.  Holds the session key once a session response supplies it,
// the Character once a player profile identifies it, and the zone once that profile supplies it.
// Lives from the first packet seen on the port pair until the session disconnect for that pair
// has been processed.
///////////////////////////////////////////////////////////////////////////////////////////////
public class Session : IDisposable
{
    private readonly SessionId _sessionId;
    private readonly string _localIp;
    private readonly string _remoteIp;
    private readonly ushort _localPort;
    private readonly ushort _remotePort;
    private readonly ServerType _serverType;

    private uint _sessionKey = 0;
    private Character? _character = null;
    private ZoneId _zoneId = ZoneId.None;
    private bool _closeRequested = false;
    private readonly SoeStream _toServer;
    private readonly SoeStream _fromServer;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Session (constructor)
    //
    // Records the identity of the session and creates its two streams, one per direction,
    // with their stream identifiers chosen by the server type.
    //
    // sessionId:     The Glass identifier assigned to this session
    // localIp:       The client's address on the local machine
    // localPort:     The client's port on the local machine
    // remoteIp:      The server's address
    // remotePort:    The server's port
    // serverType:    The kind of server at the remote end
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public Session(SessionId sessionId, string localIp, ushort localPort, string remoteIp, ushort remotePort,
                   ServerType serverType)
    {
        _sessionId = sessionId;
        _localIp = localIp;
        _remoteIp = remoteIp;
        _localPort = localPort;
        _remotePort = remotePort;
        _serverType = serverType;

        StreamId toServerId;
        StreamId fromServerId;

        switch (_serverType)
        {
            case ServerType.Login:
                {
                    toServerId = StreamId.StreamClientToLogin;
                    fromServerId = StreamId.StreamLoginToClient;
                    break;
                }
            case ServerType.World:
                {
                    toServerId = StreamId.StreamClientToWorld;
                    fromServerId = StreamId.StreamWorldToClient;
                    break;
                }
            case ServerType.Zone:
                {
                    toServerId = StreamId.StreamClientToZone;
                    fromServerId = StreamId.StreamZoneToClient;
                    break;
                }
            default:
                {
                    DebugLog.Write(LogChannel.LowNetwork, "Session: server type " + _serverType
                        + " has no stream identifiers for " + Describe(), LogLevel.Error);
                    throw new ArgumentOutOfRangeException(nameof(serverType), _serverType,
                        "Session: server type has no stream identifiers");
                }
        }

        _toServer = new SoeStream(toServerId, StreamNames[toServerId] + ": " + Describe());
        _toServer.OnSessionKey = StoreSessionKey;
        _toServer.OnClosing = RequestClose;

        _fromServer = new SoeStream(fromServerId, StreamNames[fromServerId] + ": " + Describe());
        _fromServer.OnSessionKey = StoreSessionKey;
        _fromServer.OnClosing = RequestClose;

        DebugLog.Write(LogChannel.LowNetwork, "Session: created " + Describe() + " with streams "
            + toServerId + " and " + fromServerId, LogLevel.Trace);
    }

    public SessionId SessionId
    {
        get { return _sessionId; }
    }
    public ZoneId ZoneId
    {
        get { return _zoneId; }
    }

    public ushort LocalPort
    {
        get { return _localPort; }
    }

    public ushort RemotePort
    {
        get { return _remotePort; }
    }

    public string LocalIp
    {
        get { return _localIp; }
    }

    public string RemoteIp
    {
        get { return _remoteIp; }
    }

    public ServerType ServerType
    {
        get { return _serverType; }
    }

    public bool HasSessionKey
    {
        get { return _sessionKey != 0; }
    }

    public uint SessionKey
    {
        get { return _sessionKey; }
    }

    public Character? Character
    {
        get { return _character; }
    }

    public bool CloseRequested
    {
        get { return _closeRequested; }
    }
    public SoeStream ToServerStream
    {
        get { return _toServer; }
    }

    public SoeStream FromServerStream
    {
        get { return _fromServer; }
    }


    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetCharacter
    //
    // Records the Character identified for this session.  Logs at Warn when a different
    // Character was already recorded.
    //
    // character:  The Character this session belongs to
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void SetCharacter(Character character)
    {
        if (_character != null && !ReferenceEquals(_character, character))
        {
            DebugLog.Write(LogChannel.LowNetwork, "Session.SetCharacter: " + Describe()
                + " already had a different Character, replacing it", LogLevel.Warn);
        }

        _character = character;

        DebugLog.Write(LogChannel.LowNetwork, "Session.SetCharacter: " + Describe()
            + " identified as " + character.Name, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetZone
    //
    // Records the zone this session is connected to.  Only a Zone session carries a zone;
    // any other server type is rejected and logged at Error.  Logs at Warn when a different
    // zone was already recorded.
    //
    // zoneId:  The zone the session is connected to
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void SetZone(ZoneId zoneId)
    {
        if (_serverType != ServerType.Zone)
        {
            DebugLog.Write(LogChannel.LowNetwork, "Session.SetZone: " + Describe()
                + " is not a zone session, ignoring zoneId=" + zoneId, LogLevel.Error);
            return;
        }

        if (_zoneId.Exists && _zoneId != zoneId)
        {
            DebugLog.Write(LogChannel.LowNetwork, "Session.SetZone: " + Describe()
                + " already had zoneId=" + _zoneId + ", replacing with " + zoneId, LogLevel.Warn);
        }

        _zoneId = zoneId;

        DebugLog.Write(LogChannel.LowNetwork, "Session.SetZone: " + Describe()
            + " zoneId=" + zoneId, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // StoreSessionKey
    //
    // Records the session key delivered by a session response.  Logs at Warn when a key
    // was already stored and the new one differs.
    //
    // sessionId:   The SOE session id carried in the response
    // fromStream:  The stream that received the response
    // sessionKey:  The key value
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void StoreSessionKey(uint sessionId, StreamId fromStream, uint sessionKey)
    {
        if (_sessionKey != 0 && _sessionKey != sessionKey)
        {
            DebugLog.Write(LogChannel.LowNetwork, "Session.StoreSessionKey: " + Describe()
                + " key changed from 0x" + _sessionKey.ToString("X8")
                + " to 0x" + sessionKey.ToString("X8"), LogLevel.Warn);
        }

        _sessionKey = sessionKey;

        DebugLog.Write(LogChannel.LowNetwork, "Session.StoreSessionKey: " + Describe()
            + " SOE session 0x" + sessionId.ToString("X8")
            + " key 0x" + sessionKey.ToString("X8")
            + " from " + StreamNames[fromStream], LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // RequestClose
    //
    // Marks the session as closing.  Streams are not touched here; they are released by
    // Dispose once the packet that carried the disconnect has finished processing.
    //
    // sessionId:   The SOE session id carried in the disconnect
    // fromStream:  The stream that received the disconnect
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void RequestClose(uint sessionId, StreamId fromStream)
    {
        if (_closeRequested)
        {
            DebugLog.Write(LogChannel.LowNetwork, "Session.RequestClose: " + Describe()
                + " close already requested", LogLevel.Warn);
            return;
        }

        _closeRequested = true;

        DebugLog.Write(LogChannel.LowNetwork, "Session.RequestClose: " + Describe()
            + " SOE session 0x" + sessionId.ToString("X8")
            + " from " + StreamNames[fromStream], LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Dispose
    //
    // Disposes every stream this session created and releases their resources.  Safe to
    // call more than once.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Dispose()
    {
        _toServer.Dispose();
        _fromServer.Dispose();

        DebugLog.Write(LogChannel.LowNetwork, "Session.Dispose: " + Describe() + " disposed both streams",
            LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Formats the session identity for log messages.
    //
    // Returns the session id, server type, and port pair as one string.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private string Describe()
    {
        return "session " + _sessionId + " (" + _serverType + " " + _localIp + ":" + _localPort
            + "->" + _remoteIp + ":" + _remotePort + ")";
    }
}