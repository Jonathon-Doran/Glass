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

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SessionDemux (constructor)
    //
    // localIp:            The IP address of the local machine running EQ clients
    // arqSeqGiveUp:       Passed through to each stream's ARQ cache threshold
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public SessionDemux(string localIp, int arqSeqGiveUp = 512)
    {
        _localIp = localIp;
        _localIpInt = IpToUInt32(localIp);
        _arqSeqGiveUp = arqSeqGiveUp;

        DebugLog.Write(LogChannel.Input, "SessionDemux: created, localIp=" + localIp);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // RoutePacket
    //
    // Entry point from the capture layer.  Determines direction, filters
    // unwanted traffic, identifies the client by local port, and routes
    // to the appropriate stream.
    //
    // dgram:  The UDP datagram with headers removed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void RoutePacket(UdpDatagram dgram)
    {
        // Filter chat server traffic
        if (dgram.DestPort == SoeConstants.ChatServerPort ||
            dgram.SourcePort == SoeConstants.ChatServerPort)
        {
            dgram.Payload.Dispose();
            return;
        }

        // Filter world chat traffic
        if (dgram.DestPort == SoeConstants.WorldServerChatPort ||
            dgram.SourcePort == SoeConstants.WorldServerChatPort)
        {
            dgram.Payload.Dispose();
            return;
        }

        if (dgram.DestPort == SoeConstants.WorldServerChat2Port ||
            dgram.SourcePort == SoeConstants.WorldServerChat2Port)
        {
            dgram.Payload.Dispose();
            return;
        }

        dgram.Channel = GlassContext.SessionRegistry.GetChannel(dgram);

        Connection connection = GlassContext.SessionRegistry.GetConnection(dgram);
        dgram.SessionId = connection.SessionId;

        // Route to the stream
        SoeStream stream = connection.GetStream(dgram.Channel);

        stream.HandlePacket(dgram);
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
