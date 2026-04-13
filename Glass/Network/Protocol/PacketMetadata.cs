using System;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// PacketMetadata
//
// Carries per-packet context from the capture source down to opcode handlers.
// Fields originate from the pcap packet header and IP/UDP headers.
//
// FrameNumber:  Sequential frame counter from the capture source
// Timestamp:    Packet capture timestamp from the pcap header
// SourceIp:     Source IP address as a string
// SourcePort:   Source UDP port
// DestIp:       Destination IP address as a string
// DestPort:     Destination UDP port
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public struct PacketMetadata
{
    public int FrameNumber;
    public DateTime Timestamp;
    public string SourceIp;
    public int SourcePort;
    public string DestIp;
    public int DestPort;
}
