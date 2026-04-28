using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Client;
using System;
using System.Collections.Generic;
using System.Net;
using static Glass.Network.Protocol.SoeConstants;
using static Glass.Network.Protocol.SoeStream;

namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////
// SessionDemux
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
public class SessionDemux
{
    private readonly string _localIp;
    private readonly uint _localIpInt;
    private readonly int _arqSeqGiveUp;
    private readonly AppPacketHandler _appPacketHandler;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SessionDemux (constructor)
    //
    // localIp:            The IP address of the local machine running EQ clients
    // appPacketHandler:   Delegate called for each decoded application-layer packet
    // arqSeqGiveUp:       Passed through to each stream's ARQ cache threshold
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public SessionDemux(string localIp, AppPacketHandler appPacketHandler, int arqSeqGiveUp = 512)
    {
        _localIp = localIp;
        _localIpInt = IpToUInt32(localIp);
        _arqSeqGiveUp = arqSeqGiveUp;
        _appPacketHandler = appPacketHandler;

        DebugLog.Write("SessionDemux: created, localIp=" + localIp
            + ", appPacketHandler=" + (appPacketHandler != null ? "provided" : "null"));
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
    public void RoutePacket(ReadOnlySpan<byte> rawData, int length, PacketMetadata metadata)
    {
        // Filter chat server traffic
        if (metadata.DestPort == SoeConstants.ChatServerPort ||
            metadata.SourcePort == SoeConstants.ChatServerPort)
        {
            return;
        }

        // Filter world chat traffic
        if (metadata.DestPort == SoeConstants.WorldServerChatPort ||
            metadata.SourcePort == SoeConstants.WorldServerChatPort)
        {
            return;
        }

        if (metadata.DestPort == SoeConstants.WorldServerChat2Port ||
            metadata.SourcePort == SoeConstants.WorldServerChat2Port)
        {
            return;
        }

        metadata.Channel = GlassContext.SessionRegistry.GetChannel(metadata);

        Connection connection = GlassContext.SessionRegistry.GetConnection(metadata);
        metadata.SessionId = connection.SessionId;

        // Route to the stream
        SoeStream stream = connection.GetStream(metadata.Channel);

        stream.HandlePacket(rawData, length, metadata);
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
            DebugLog.Write(LogChannel.LowNetwork, "SessionDemux.IpToUInt32: invalid IP '" + ip + "'");
            return 0;
        }

        return (uint)((byte.Parse(parts[0]) << 24) |
                       (byte.Parse(parts[1]) << 16) |
                       (byte.Parse(parts[2]) << 8) |
                       byte.Parse(parts[3]));
    }
}
