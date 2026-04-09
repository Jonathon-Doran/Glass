using Glass.Core;
using Glass.Network.Client;
using System;
using System.Collections.Generic;
using System.Net;

namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////
// PacketRouter
//
// Receives raw UDP packets from the capture layer and routes them to the
// correct EqClient and stream.  Clients are identified by their local
// ephemeral port, which is stable for the lifetime of the EQ process.
//
// New clients are created automatically when traffic is seen from an
// unrecognized local port during session setup.  Clients are removed
// when all their streams disconnect.
//
// The router also filters out chat and login traffic that is not
// relevant to game state.
///////////////////////////////////////////////////////////////////////////////////////////////
public class PacketRouter
{
    private readonly Dictionary<int, EqClient> _clientsByLocalPort;
    private readonly string _localIp;
    private readonly uint _localIpInt;
    private readonly int _arqSeqGiveUp;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PacketRouter (constructor)
    //
    // localIp:        The IP address of the local machine running EQ clients
    // arqSeqGiveUp:   Passed through to each stream's ARQ cache threshold
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PacketRouter(string localIp, int arqSeqGiveUp = 512)
    {
        _clientsByLocalPort = new Dictionary<int, EqClient>();
        _localIp = localIp;
        _localIpInt = IpToUInt32(localIp);
        _arqSeqGiveUp = arqSeqGiveUp;

        DebugLog.Write("PacketRouter: created, localIp=" + localIp);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // RoutePacket
    //
    // Entry point from the capture layer.  Determines direction, filters
    // unwanted traffic, identifies the client by local port, and routes
    // to the appropriate stream.
    //
    // rawData:      The complete UDP payload
    // length:       Length of rawData
    // sourceIp:     Source IP as a dotted-decimal string
    // sourcePort:   Source UDP port
    // destIp:       Destination IP as a dotted-decimal string
    // destPort:     Destination UDP port
    // frameNumber:  Frame number from the pcap file, or 0 for live capture
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void RoutePacket(ReadOnlySpan<byte> rawData, int length,
                            string sourceIp, int sourcePort,
                            string destIp, int destPort,
                            int frameNumber)
    {
        // Filter chat server traffic
        if (destPort == SoeConstants.ChatServerPort ||
            sourcePort == SoeConstants.ChatServerPort)
        {
            return;
        }

        // Filter world chat traffic
        if (destPort == SoeConstants.WorldServerChatPort ||
            sourcePort == SoeConstants.WorldServerChatPort)
        {
            return;
        }

        if (destPort == SoeConstants.WorldServerChat2Port ||
            sourcePort == SoeConstants.WorldServerChat2Port)
        {
            return;
        }

        // Determine direction and local port
        bool isFromClient = IsLocalIp(sourceIp);
        int localPort = isFromClient ? sourcePort : destPort;

        // Determine which stream type based on port ranges
        int streamId = ClassifyStream(sourcePort, destPort, isFromClient);

        if (streamId < 0)
        {
            DebugLog.Write(DebugLog.Log_Network,
                "PacketRouter.RoutePacket: unclassifiable packet "
                + sourceIp + ":" + sourcePort + " -> "
                + destIp + ":" + destPort + ", dropping");
            return;
        }

        // Find or create the client
        if (!_clientsByLocalPort.TryGetValue(localPort, out EqClient? client))
        {
            client = new EqClient(localPort, _arqSeqGiveUp);
            _clientsByLocalPort[localPort] = client;

            DebugLog.Write("PacketRouter.RoutePacket: new client on local port "
                + localPort + ", total clients=" + _clientsByLocalPort.Count);
        }

        // Route to the stream
        SoeStream stream = client.GetStream(streamId);

        if (stream == null)
        {
            DebugLog.Write("PacketRouter.RoutePacket: no stream for id="
                + streamId + " on client port " + localPort);
            return;
        }

        stream.HandlePacket(rawData, length,
                                    sourceIp, sourcePort, destIp, destPort,
                                    frameNumber);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ClassifyStream
    //
    // Determines the stream type based on port ranges and direction.
    // Returns -1 if the traffic cannot be classified.
    //
    // sourcePort:   Source UDP port
    // destPort:     Destination UDP port
    // isFromClient: True if the source is the local machine
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private int ClassifyStream(int sourcePort, int destPort, bool isFromClient)
    {
        // Login server traffic
        if ((destPort >= SoeConstants.LoginServerMinPort &&
             destPort <= SoeConstants.LoginServerMaxPort) ||
            (sourcePort >= SoeConstants.LoginServerMinPort &&
             sourcePort <= SoeConstants.LoginServerMaxPort))
        {
            if (isFromClient)
            {
                return SoeConstants.StreamClient2World;
            }
            return SoeConstants.StreamWorld2Client;
        }

        // World server traffic
        if ((destPort >= SoeConstants.WorldServerGeneralMinPort &&
             destPort <= SoeConstants.WorldServerGeneralMaxPort) ||
            (sourcePort >= SoeConstants.WorldServerGeneralMinPort &&
             sourcePort <= SoeConstants.WorldServerGeneralMaxPort))
        {
            if (isFromClient)
            {
                return SoeConstants.StreamClient2World;
            }
            return SoeConstants.StreamWorld2Client;
        }

        // Everything else is zone traffic
        if (isFromClient)
        {
            return SoeConstants.StreamClient2Zone;
        }
        return SoeConstants.StreamZone2Client;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // IsLocalIp
    //
    // Returns true if the given IP matches the local machine's IP.
    //
    // ip:  The IP address to test as a dotted-decimal string
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private bool IsLocalIp(string ip)
    {
        return ip == _localIp;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // RemoveClient
    //
    // Removes a client by local port and disposes its resources.
    // Called when all streams on a client have disconnected.
    //
    // localPort:  The local ephemeral port identifying the client
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void RemoveClient(int localPort)
    {
        if (_clientsByLocalPort.TryGetValue(localPort, out EqClient? client))
        {
            client.Dispose();
            _clientsByLocalPort.Remove(localPort);

            DebugLog.Write("PacketRouter.RemoveClient: removed client on port "
                + localPort + ", remaining clients=" + _clientsByLocalPort.Count);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ClientCount
    //
    // Returns the number of active clients.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public int ClientCount
    {
        get { return _clientsByLocalPort.Count; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetAllClients
    //
    // Returns a read-only view of all active clients.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyDictionary<int, EqClient> GetAllClients()
    {
        return _clientsByLocalPort;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Shutdown
    //
    // Disposes all clients and clears the client map.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Shutdown()
    {
        DebugLog.Write("PacketRouter.Shutdown: disposing " + _clientsByLocalPort.Count
            + " clients");

        foreach (KeyValuePair<int, EqClient> kvp in _clientsByLocalPort)
        {
            kvp.Value.Dispose();
        }

        _clientsByLocalPort.Clear();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // IpToUInt32
    //
    // Converts a dotted-decimal IP string to a 32-bit unsigned integer.
    //
    // ip:  The IP address string
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private static uint IpToUInt32(string ip)
    {
        if (ip == null)
        {
            return 0;
        }

        string[] parts = ip.Split('.');

        if (parts.Length != 4)
        {
            DebugLog.Write("PacketRouter.IpToUInt32: invalid IP '" + ip + "'");
            return 0;
        }

        return (uint)((byte.Parse(parts[0]) << 24) |
                       (byte.Parse(parts[1]) << 16) |
                       (byte.Parse(parts[2]) << 8) |
                       byte.Parse(parts[3]));
    }
}
