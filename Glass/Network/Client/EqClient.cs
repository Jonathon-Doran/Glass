using System;
using Glass.Core;
using Glass.Network.Protocol;

namespace Glass.Network.Client;

///////////////////////////////////////////////////////////////////////////////////////////////
// EqClient
//
// Represents one EQ client process.  Owns the four SOE protocol streams
// (client->world, world->client, client->zone, zone->client) and handles
// session key distribution and close propagation between them.
//
// Identified by the local ephemeral port, which is stable for the lifetime
// of the EQ process.
///////////////////////////////////////////////////////////////////////////////////////////////
public class EqClient : IDisposable
{
    private readonly int _localPort;
    private readonly SoeStream[] _streams;
    private bool _disposed;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // EqClient (constructor)
    //
    // Creates the four streams and wires up session key and close callbacks.
    //
    // localPort:      The local ephemeral port identifying this client
    // arqSeqGiveUp:   ARQ cache threshold passed through to each stream
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public EqClient(int localPort, int arqSeqGiveUp)
    {
        _localPort = localPort;
        _disposed = false;

        _streams = new SoeStream[SoeConstants.MaxStreams];

        _streams[SoeConstants.StreamClient2World] = new SoeStream(
            SoeConstants.StreamClient2World,
            SoeConstants.DirectionClientToServer,
            arqSeqGiveUp,
            "client-world:" + localPort);

        _streams[SoeConstants.StreamWorld2Client] = new SoeStream(
            SoeConstants.StreamWorld2Client,
            SoeConstants.DirectionServerToClient,
            arqSeqGiveUp,
            "world-client:" + localPort);

        _streams[SoeConstants.StreamClient2Zone] = new SoeStream(
            SoeConstants.StreamClient2Zone,
            SoeConstants.DirectionClientToServer,
            arqSeqGiveUp,
            "client-zone:" + localPort);

        _streams[SoeConstants.StreamZone2Client] = new SoeStream(
            SoeConstants.StreamZone2Client,
            SoeConstants.DirectionServerToClient,
            arqSeqGiveUp,
            "zone-client:" + localPort);

        // Wire session key distribution
        for (int i = 0; i < SoeConstants.MaxStreams; i++)
        {
            _streams[i].OnSessionKey = DistributeSessionKey;
            _streams[i].OnClosing = PropagateClose;
        }

        // Enable session tracking on all streams
        for (int i = 0; i < SoeConstants.MaxStreams; i++)
        {
            _streams[i].SessionTrackingEnabled = 1;
        }

        DebugLog.Write("EqClient: created for local port " + localPort);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetStream
    //
    // Returns the stream for the given stream ID.
    //
    // streamId:  One of the SoeConstants.Stream* values
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public SoeStream GetStream(int streamId)
    {
        if (streamId < 0 || streamId >= SoeConstants.MaxStreams)
        {
            DebugLog.Write("EqClient.GetStream: invalid streamId=" + streamId
                + " on port " + _localPort);
            throw new ArgumentOutOfRangeException("streamId",
                "Stream ID " + streamId + " is out of range (0-"
                + (SoeConstants.MaxStreams - 1) + ")");
        }

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
    private void DistributeSessionKey(uint sessionId, int fromStream, uint sessionKey)
    {
        DebugLog.Write(DebugLog.Log_Network,
            "EqClient.DistributeSessionKey [port " + _localPort
            + "]: key=0x" + sessionKey.ToString("X8")
            + " from " + SoeConstants.StreamNames[fromStream]);

        for (int i = 0; i < SoeConstants.MaxStreams; i++)
        {
            _streams[i].ReceiveSessionKey(sessionId, fromStream, sessionKey);
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
    private void PropagateClose(uint sessionId, int fromStream)
    {
        DebugLog.Write("EqClient.PropagateClose [port " + _localPort
            + "]: session=0x" + sessionId.ToString("X8")
            + " from " + SoeConstants.StreamNames[fromStream]);

        for (int i = 0; i < SoeConstants.MaxStreams; i++)
        {
            _streams[i].Close(sessionId, fromStream, 1);
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
            for (int i = 0; i < SoeConstants.MaxStreams; i++)
            {
                if (_streams[i] != null)
                {
                    _streams[i].Dispose();
                }
            }

            _disposed = true;

            DebugLog.Write("EqClient.Dispose: disposed client on port " + _localPort);
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