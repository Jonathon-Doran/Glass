using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Network.Protocol;
using System;
using static Glass.Network.Protocol.SoeConstants;

namespace Glass.Network.Client;

///////////////////////////////////////////////////////////////////////////////////////////////
// Connection
//
// Represents the low-level connection to one EQ client process.  Owns the four SOE protocol streams
// (client->world, world->client, client->zone, zone->client) and handles
// session key distribution and close propagation between them.
//
// Identified by the local ephemeral port, which is stable for the lifetime
// of the EQ process.
///////////////////////////////////////////////////////////////////////////////////////////////
public class Connection : IDisposable
{
    private readonly int _localPort;
    private readonly Dictionary<StreamId, SoeStream> _streams;
    private bool _disposed;
    private int _sessionId = -1;
    private int _characterId = -1;
    private Character? _character = null;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Connection (constructor)
    //
    // Creates the four streams and wires up session key and close callbacks.
    //
    // me:      The local ephemeral port identifying this client
    // arqSeqGiveUp:   ARQ cache threshold passed through to each stream
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public Connection(int localport, int arqSeqGiveUp, SoeStream.AppPacketHandler _handler)
    {
        _localPort = localport;
        _disposed = false;

        _streams = new Dictionary<StreamId, SoeStream>();

        _streams[StreamId.StreamClientToWorld] = new SoeStream(
            StreamId.StreamClientToWorld,
            SoeConstants.DirectionClientToServer,
            arqSeqGiveUp, StreamNames[StreamId.StreamClientToWorld] + ": " + _localPort);

        _streams[StreamId.StreamWorldToClient] = new SoeStream(
            StreamId.StreamWorldToClient,
            SoeConstants.DirectionServerToClient,
            arqSeqGiveUp, StreamNames[StreamId.StreamWorldToClient] + ": " + _localPort);

        _streams[StreamId.StreamClientToZone] = new SoeStream(
            StreamId.StreamClientToZone,
            SoeConstants.DirectionClientToServer,
            arqSeqGiveUp, StreamNames[StreamId.StreamClientToZone] + ": " + _localPort);

        _streams[StreamId.StreamZoneToClient] = new SoeStream(
            StreamId.StreamZoneToClient,
            SoeConstants.DirectionServerToClient,
            arqSeqGiveUp, StreamNames[StreamId.StreamZoneToClient] + ": " + _localPort);

        // Wire session key distribution
        foreach (StreamId streamId in Enum.GetValues<StreamId>())
        {
            _streams[streamId].OnSessionKey = DistributeSessionKey;
            _streams[streamId].OnClosing = PropagateClose;
        }

        // Enable session tracking on all streams
        foreach (StreamId streamId in Enum.GetValues<StreamId>())
        {
            _streams[streamId].SessionTrackingEnabled = 1;
            _streams[streamId].OnAppPacket = _handler;
        }

        DebugLog.Write(LogChannel.LowNetwork, "Connection: created for local port " + _localPort);
    }

    public int SessionId
    {
        get { return _sessionId; }
        set { _sessionId = value; }
    }


    public int CharacterId
    {
        get { return _characterId; }
        set { _characterId = value; }
    }

    public Character? Character
    {
        get { return _character; }
        set { _character = value; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetStream
    //
    // Returns the stream for the given stream ID.
    //
    // streamId:  One of the SoeConstants.Stream* values
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public SoeStream GetStream(SoeConstants.StreamId streamId)
    {
        return _streams[streamId];
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // DistributeSessionKey
    //
    // Called when any stream receives a session key via SessionResponse.
    // Forwards the key to all other streams with the same session ID.
    //
    // sessionId:   The session ID the key belongs to
    // fromStream:  The stream that received the key
    // sessionKey:  The key value
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void DistributeSessionKey(uint sessionId, StreamId fromStream, uint sessionKey)
    {
        foreach (StreamId streamId in Enum.GetValues<StreamId>())
        {
            _streams[streamId].ReceiveSessionKey(sessionId, fromStream, sessionKey);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PropagateClose
    //
    // Called when any stream receives a SessionDisconnect.
    // Notifies all streams so they can reset if their session ID matches.
    //
    // sessionId:   The session ID that disconnected
    // fromStream:  The stream that received the disconnect
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void PropagateClose(uint sessionId, StreamId fromStream)
    {
        DebugLog.Write(LogChannel.LowNetwork, "Connection.PropagateClose [port " + _localPort
            + "]: session=0x" + sessionId.ToString("X8")
            + " from " + SoeConstants.StreamNames[fromStream]);

        foreach (StreamId streamId in Enum.GetValues<StreamId>())
        {
            _streams[streamId].Close(sessionId, fromStream, 1);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Dispose
    //
    // Disposes all four streams and releases their resources.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Dispose()
    {
        if (!_disposed)
        {
            foreach (StreamId streamId in Enum.GetValues<StreamId>())
            {
                if (_streams[streamId] != null)
                {
                    _streams[streamId].Dispose();
                }
            }

            _disposed = true;

            DebugLog.Write(LogChannel.LowNetwork, "Connection.Dispose: disposed client on port " + _localPort);
        }
    }

    // ---------------------------------------------------------------------------
    // Properties
    // ---------------------------------------------------------------------------

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LocalPort
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public int LocalPort
    {
        get { return _localPort; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // IsDisposed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public bool IsDisposed
    {
        get { return _disposed; }
    }
}