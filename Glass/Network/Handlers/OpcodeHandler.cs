using Glass.Core;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// OpcodeHandler
//
// Abstract base for opcode handlers.  Holds the patch level, the opcode name, and the
// resolved opcode.
///////////////////////////////////////////////////////////////////////////////////////////////
public abstract class OpcodeHandler
{
    protected readonly PatchLevel _patchLevel;
    protected readonly string _opcodeName;
    protected PatchOpcode _opcodeHandled;
    protected readonly PatchRegistry _registry;
    protected FieldExtractor _extractor;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHandler (constructor)
    //
    // Stores the patch level and opcode name.
    //
    // patchLevel:  The patch level this handler decodes against.
    // opcodeName:  The concrete handler's opcode name.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    protected OpcodeHandler(PatchLevel patchLevel, string opcodeName)
    {
        _patchLevel = patchLevel;
        _opcodeName = opcodeName;
        _registry = GlassContext.PatchRegistry;
        _extractor = GlassContext.FieldExtractor;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeName
    //
    // The concrete handler's opcode name.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public string OpcodeName
    {
        get { return _opcodeName; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHandled
    //
    // The resolved opcode the concrete handler sets.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode OpcodeHandled
    {
        get { return _opcodeHandled; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Processes a packet of this handler's opcode.
    //
    // data:      The application payload (opcode bytes already stripped).
    // metadata:  Packet metadata (timestamp, source/dest, channel).
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public abstract void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata);

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Builds a display model for the packet's decoded fields.  Returns a single childless node
    // unless overridden.
    //
    // data:      The application payload (opcode bytes already stripped).
    // metadata:  Packet metadata (timestamp, source/dest, channel).
    //
    // Returns:   The root FieldDisplayNode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public virtual FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        return new FieldDisplayNode();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ResolveVersion
    //
    // Returns the schema version number for decoding this packet.  Returns 1 unless overridden.
    //
    // data:      The application payload (opcode bytes already stripped).
    // metadata:  Packet metadata (timestamp, source/dest, channel).
    //
    // Returns:   The version number for the PatchOpcode key.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public virtual uint ResolveVersion(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        return 1;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Dispose
    //
    // Suppresses finalization.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}