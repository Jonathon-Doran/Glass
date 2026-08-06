using Glass.Core;
using Glass.Core.Logging;
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
    private readonly SlotId _messageCodeSlot;       // index into eqstr_us.txt

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
        _messageCodeSlot = _registry.IndexOfField(_collectionHandle, "msg_code");
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
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleFormatteedMessage:  No RootGate", LogLevel.Error);
                return;
            }

            string message = _extractor.GetStringAt(_messageIdSlot);
            uint code = _extractor.GetUIntAt(_messageCodeSlot);
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_FormattedMessage against the active patch and builds a display tree: a root node for
    // the collection with one leaf child per field each carrying its payload byte range.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    //
    // Returns:   The root FieldDisplayNode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldDisplayNode root = new FieldDisplayNode();
        string characterName = GlassContext.SessionRegistry.CharacterNameFromMetadata(metadata);

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleFormattedMessage:  No RootGate", LogLevel.Error);
                return root;
            }

            string message = _extractor.GetStringAt(_messageIdSlot);
            uint code = _extractor.GetUIntAt(_messageCodeSlot);

            FieldNodes.AddLabeledNode(_extractor, _messageIdSlot, "message = " + message, root);
            FieldNodes.AddLabeledNode(_extractor, _messageIdSlot, "code = " + code, root);
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Formatted Message (to " + characterName + ")";
        return root;
    }
}