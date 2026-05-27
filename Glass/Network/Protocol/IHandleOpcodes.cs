using Glass.Network.Protocol.Fields;
using System;

namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////
// IHandleOpcodes
//
// Interface for classes that handle a specific application-level opcode.
// Implementations are discovered by OpcodeDispatch via reflection at startup.
// Each implementation handles exactly one opcode.
///////////////////////////////////////////////////////////////////////////////////////////////
public interface IHandleOpcodes : IDisposable
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeName
    //
    // The name of the opcode handled
    //////////////////////////////////////////////////////////////////////////////////////////////

    string OpcodeName { get; }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Called by OpcodeDispatch when a packet with the matching opcode arrives.
    //
    // data:       The application payload (opcode bytes already stripped)
    // length:     Length of the application payload
    // direction:  Direction byte (DirectionClient or DirectionServer)
    // opcode:     The application-level opcode
    ///////////////////////////////////////////////////////////////////////////////////////////////
    void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata);

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ResolveVersion
    //
    // Inspects the packet's metadata and payload and returns the schema version number that
    // applies for decoding this packet.  Used by callers that need to select between multiple
    // PatchOpcode rows defined for the same wire opcode value.
    //
    // The default implementation returns 1, suitable for any opcode that has only one schema
    // in PatchOpcode.  Handlers whose opcode has multiple schemas override this.
    //
    // data:      The application payload (opcode bytes already stripped)
    // metadata:  Packet metadata (timestamp, source/dest, channel)
    //
    // Returns:   The version number to use when constructing a PatchOpcode key for handle
    //           lookup.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    uint ResolveVersion(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        return 1;
    }
}