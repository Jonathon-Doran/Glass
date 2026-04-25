///////////////////////////////////////////////////////////////////////////////////////////
// CapturedPacket
//
// Represents a single captured application-layer packet with its wire-level
// metadata and decoded opcode value. Stored in the session packet buffer for
// analysis filtering and pcapng serialization.
///////////////////////////////////////////////////////////////////////////////////////////
public struct CapturedPacket
{
    public PacketMetadata Metadata;
    public byte[] Payload;
    public uint OpcodeValue;
}
