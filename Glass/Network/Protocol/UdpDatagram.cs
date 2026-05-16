using System;
using static Glass.Network.Protocol.SoeConstants;

///////////////////////////////////////////////////////////////////////////////////////////
// UdpDatagram
//
// Per-packet context from a captured UDP datagram, threaded from the capture
// source through demux and stream processing.  Fields are populated at the
// stage that knows them: addressing and timing at the pcap source, session
// and channel at SessionDemux.
//
// UdpDatagram is promoted to Message inside SoeStream once an application-
// level payload and its opcode have been decoded.  All UdpDatagram fields
// carry over to Message; payload and opcode are added at promotion.
//
// Payload:      Datagram payload with UDP/IP headers stripped.
// FrameNumber:  Sequential frame counter from the capture source
// Timestamp:    Packet capture timestamp from the pcap header
// SourceIp:     Source IP address as a string
// SourcePort:   Source UDP port
// DestIp:       Destination IP address as a string
// DestPort:     Destination UDP port
// SessionId:    Per-session identifier assigned by SessionRegistry; -1 until set
// Channel:      SOE stream identifier; Unknown until set by SessionDemux
///////////////////////////////////////////////////////////////////////////////////////////

public ref struct UdpDatagram
{
    public ReadOnlySpan<byte> Payload;
    public int FrameNumber;
    public DateTime Timestamp;
    public string SourceIp;
    public int SourcePort;
    public string DestIp;
    public int DestPort;
    public int SessionId;
    public StreamId Channel;
}