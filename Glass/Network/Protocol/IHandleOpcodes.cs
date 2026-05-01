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
    // Opcode
    //
    // The application-level opcode this handler is registered for.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    ushort Opcode { get; }

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
    // Extract
    //
    // Fills the supplied bag with field values decoded from data, using this handler's cached
    // field definitions.  Called by OpcodeDispatch.Extract on the cold path (e.g. the
    // Inference opcode log tab during refresh).  Handlers not yet refactored to use the
    // FieldExtractor may leave this empty; callers will see an empty bag.
    //
    // The caller owns the bag's lifetime — must Rent it before this call and Release it after.
    //
    // data:  The application payload
    // bag:   A bag rented by the caller; will be filled by this method
    ///////////////////////////////////////////////////////////////////////////////////////////////
    void Extract(ReadOnlySpan<byte> data, FieldBag bag);
}