using System;
using static Glass.Network.Protocol.SoeConstants;

///////////////////////////////////////////////////////////////////////////////////////////
// Message
//
// Application-level message decoded from the SOE protocol stack.  Carries the
// payload bytes, the wire opcode, and the per-packet context from the capture
// source down to subscribers of AppPacketBus.
//
// Named to match EQ's own internal vocabulary (per Ghidra exploration) rather
// than the "packet" / "opcode" terminology that grew up around this code
// before the binary was studied.
//
// Payload:      Application payload, opcode bytes already stripped
// Opcode:       Wire opcode value, durable across patch levels
// FrameNumber:  Sequential frame counter from the capture source
// Timestamp:    Packet capture timestamp from the pcap header
// SourceIp:     Source IP address as a string
// SourcePort:   Source UDP port
// DestIp:       Destination IP address as a string
// DestPort:     Destination UDP port
// SessionId:    Per-session identifier assigned by SessionRegistry
// Channel:      SOE stream identifier (client/world/zone direction)
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public struct Message
{
    public byte[] payload;
    public ushort Opcode;
    public int FrameNumber;
    public DateTime Timestamp;
    public string SourceIp;
    public int SourcePort;
    public string DestIp;
    public int DestPort;
    public int SessionId;
    public StreamId Channel;
}
