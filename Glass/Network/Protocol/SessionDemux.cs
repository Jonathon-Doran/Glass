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
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // RoutePacket
    //
    // Entry point from the capture layer.  Discards chat traffic, finds the session the
    // datagram belongs to, and hands the datagram to that session's stream for its direction.
    //
    // metadata:  The datagram with its wire payload, addresses, and direction filled in
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void RoutePacket(PacketMetadata metadata)
    {
        if (metadata.RemotePort == SoeConstants.ChatServerPort ||
            metadata.RemotePort == SoeConstants.WorldServerChatPort ||
            metadata.RemotePort == SoeConstants.WorldServerChat2Port)
        {
            metadata.Payload.Dispose();
            return;
        }

        Session? session = SessionStore.Instance.Find(metadata.LocalPort, metadata.RemotePort);

        if (session == null)
        {
            session = SessionStore.Instance.Create(metadata);

            if (session == null)
            {
                DebugLog.Write(LogChannel.LowNetwork, "SessionDemux.RoutePacket: frame " + metadata.FrameNumber
                    + " has no session for " + metadata.LocalPort + "->" + metadata.RemotePort + ", dropping",
                    LogLevel.Trace);
                metadata.Payload.Dispose();
                return;
            }
        }

        metadata.Session = session;
        metadata.SessionId = session.SessionId;

        SoeStream stream = metadata.ToServer ? session.ToServerStream : session.FromServerStream;
        stream.HandlePacket(metadata);

        if (session.CloseRequested)
        {
            SessionStore.Instance.Remove(session);
            session.Dispose();
        }
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
