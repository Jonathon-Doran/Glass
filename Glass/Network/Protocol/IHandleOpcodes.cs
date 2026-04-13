using System;

namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////
// IHandleOpcodes
//
// Interface for classes that handle a specific application-level opcode.
// Implementations are discovered by OpcodeDispatch via reflection at startup.
// Each implementation handles exactly one opcode.
///////////////////////////////////////////////////////////////////////////////////////////////
public interface IHandleOpcodes
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Opcode
    //
    // The application-level opcode this handler is registered for.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    ushort Opcode { get; }

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
    void HandlePacket(ReadOnlySpan<byte> data, int length, byte direction, ushort opcode, PacketMetadata metadata);
}