using Glass.Network.Protocol;
using Glass.Network.Client;
using Glass.Data.Models;
using static Glass.Network.Protocol.SoeConstants;
using Glass.Core.Memory;

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
    public PatchOpcode Opcode;
    public OpcodeValue WireValue;         // will be removed in favor of Opcode
    public DateTime Timestamp;

    public string SourceIp;
    public int SourcePort;
    public string DestIp;
    public int DestPort;

    // The client's address and port on the local machine
    public string LocalIp;
    public ushort LocalPort;

    // The server's address and port
    public string RemoteIp;
    public ushort RemotePort;

    public SessionId SessionId;      // will be removed in favor of session
    public Character? Character;
    public StreamId Channel;
    public Session Session;
    public bool ToServer;

    // The datagram's payload as it arrived off the wire.
    // Released once the stream has taken the bytes.

    public BufferLease Payload;

    public string CharacterName
    {
        get { return Character == null ? "unknown" : Character.Name; }
    }
    public ushort SourcePortX
    {
        get { return ToServer ? LocalPort : RemotePort; }
    }

    public ushort DestPortX
    {
        get { return ToServer ? RemotePort : LocalPort; }
    }
}
