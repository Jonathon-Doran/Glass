using Glass.Core;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleFormattedMessage
//
// Handles OP_FormattedMessage packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleFormattedMessage : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _messageIdSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleFormattedMessage(constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_FormattedMessage from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_FormattedMessage, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleFormattedMessage(PatchLevel patchLevel)
        :base(patchLevel, "OP_FormattedMessage")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel,  _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_FormattedMessage");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _messageIdSlot = _registry.IndexOfField(_collectionHandle, "msg_text");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Dispatches to direction-specific handlers.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        switch (metadata.Channel)
        {
            case SoeConstants.StreamId.StreamZoneToClient:
                HandleZoneToClient(data, metadata);
                break;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleZoneToClient
    //
    // Processes zone-to-client traffic
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;
        string message;

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);
            message = extractor.GetStringAt(_messageIdSlot);
        }
        finally
        {
            extractor.Release();
        }
    }
}

