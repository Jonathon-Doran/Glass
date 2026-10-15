using Glass.Core.Logging;
using Glass.Network.Protocol;
using System.Collections.Generic;

namespace Glass.Network.Client;

///////////////////////////////////////////////////////////////////////////////////////////////
// PortPair
//
// The local and remote UDP ports that together identify one session.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct PortPair(ushort LocalPort, ushort RemotePort);

///////////////////////////////////////////////////////////////////////////////////////////////
// SessionStore
//
// Holds every live Session, keyed by its port pair.  A session is present from the time it
// is added until it is removed; the store does not decide when either happens.
//
// Note that this is a singleton.
///////////////////////////////////////////////////////////////////////////////////////////////
public class SessionStore
{
    private readonly Dictionary<PortPair, Session> _sessions = new Dictionary<PortPair, Session>();
    private uint _nextSessionId = 0;
    private static readonly SessionStore _instance = new SessionStore();

    public static SessionStore Instance
    {
        get { return _instance; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SessionStore (constructor)
    //
    // Private so that the single instance is the only one.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private SessionStore()
    {
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Create
    //
    // Creates a session for a port pair, assigns it the next session id, classifies the server
    // by its port, and stores it.
    //
    // localIp:     The client's address on the local machine
    // localPort:   The client's port on the local machine
    // remoteIp:    The server's address
    // remotePort:  The server's port
    //
    // Returns the new Session, or null when a session is already stored for that pair.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public Session? Create(PacketMetadata metadata)
    {
        ServerType serverType;

        ReadOnlySpan<byte> raw = metadata.Payload.AsReadOnlySpan();

        if (raw.Length >= 2 && (ushort)(raw[0] | (raw[1] << 8)) == SoeConstants.OP_SessionDisconnect)
        {
            DebugLog.Write(LogChannel.LowNetwork, "SessionStore.Create: frame " + metadata.FrameNumber
                + " is a disconnect on " + metadata.LocalPort + "->" + metadata.RemotePort
                + " with no session, ignoring", LogLevel.Trace);
            return null;
        }

        if (metadata.RemotePort >= SoeConstants.LoginServerMinPort && 
            metadata.RemotePort <= SoeConstants.LoginServerMaxPort)
        {
            serverType = ServerType.Login;
        }
        else if (metadata.RemotePort >= SoeConstants.WorldServerGeneralMinPort 
            && metadata.RemotePort <= SoeConstants.WorldServerGeneralMaxPort)
        {
            serverType = ServerType.World;
        }
        else
        {
            serverType = ServerType.Zone;
        }

        SessionId sessionId = (SessionId)_nextSessionId;
        _nextSessionId++;

        DebugLog.Write(LogChannel.LowNetwork, "SessionStore.Create: session " + sessionId + " as " + serverType
            + " for " + metadata.LocalIp + ":" + metadata.LocalPort + "->" + 
            metadata.RemoteIp + ":" + metadata.RemotePort, LogLevel.Info);

        Session session = new Session(sessionId, metadata.LocalIp, metadata.LocalPort, 
            metadata.RemoteIp, metadata.RemotePort, serverType);

        if (!Add(session))
        {
            DebugLog.Write(LogChannel.LowNetwork, "SessionStore.Create: session " + sessionId
                + " not stored, disposing it", LogLevel.Error);
            session.Dispose();
            return null;
        }

        return session;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Find
    //
    // Looks up the live session for a port pair.
    //
    // localPort:   The client's port on the local machine
    // remotePort:  The server's port
    //
    // Returns the Session, or null when no session is stored for that pair.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public Session? Find(ushort localPort, ushort remotePort)
    {
        PortPair key = new PortPair(localPort, remotePort);
        Session? session;

        if (_sessions.TryGetValue(key, out session))
        {
            DebugLog.Write(LogChannel.LowNetwork, "SessionStore.Find: " + localPort + "->" + remotePort
                + " is session " + session.SessionId, LogLevel.Trace);
            return session;
        }

        DebugLog.Write(LogChannel.LowNetwork, "SessionStore.Find: no session for " + localPort + "->" + remotePort,
            LogLevel.Trace);
        return null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Add
    //
    // Stores a session under its port pair.
    //
    // session:  The session to store
    //
    // Returns true when stored, false when a session is already stored for that pair.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public bool Add(Session session)
    {
        PortPair key = new PortPair(session.LocalPort, session.RemotePort);

        if (_sessions.TryAdd(key, session))
        {
            DebugLog.Write(LogChannel.LowNetwork, "SessionStore.Add: stored session " + session.SessionId
                + " for " + key.LocalPort + "->" + key.RemotePort + ", count " + _sessions.Count, LogLevel.Info);
            return true;
        }

        DebugLog.Write(LogChannel.LowNetwork, "SessionStore.Add: pair " + key.LocalPort + "->" + key.RemotePort
            + " already holds session " + _sessions[key].SessionId + ", refusing session " + session.SessionId,
            LogLevel.Error);
        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Remove
    //
    // Removes a session from the store by its port pair.
    //
    // session:  The session to remove
    //
    // Returns true when removed, false when no session was stored for that pair.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public bool Remove(Session session)
    {
        PortPair key = new PortPair(session.LocalPort, session.RemotePort);
        Session? stored;

        if (!_sessions.TryGetValue(key, out stored))
        {
            DebugLog.Write(LogChannel.LowNetwork, "SessionStore.Remove: no session stored for "
                + key.LocalPort + "->" + key.RemotePort + ", requested " + session.SessionId, LogLevel.Warn);
            return false;
        }

        if (!ReferenceEquals(stored, session))
        {
            DebugLog.Write(LogChannel.LowNetwork, "SessionStore.Remove: pair " + key.LocalPort + "->" + key.RemotePort
                + " holds session " + stored.SessionId + ", not " + session.SessionId + ", refusing", LogLevel.Error);
            return false;
        }

        _sessions.Remove(key);

        DebugLog.Write(LogChannel.LowNetwork, "SessionStore.Remove: removed session " + session.SessionId
            + " for " + key.LocalPort + "->" + key.RemotePort + ", count " + _sessions.Count, LogLevel.Info);
        return true;
    }
}