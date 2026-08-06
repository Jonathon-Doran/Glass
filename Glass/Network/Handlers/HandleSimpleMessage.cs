using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;
using System.Security.Policy;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleSimpleMessage
//
// Handles OP_SimpleMessage packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleSimpleMessage : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _messageNumberSlot;
    private readonly SlotId _messageColorSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleSimpleMessage  (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleSimpleMessage(PatchLevel patchLevel)
        : base(patchLevel, "OP_Simple_Message")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "Simple Message");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _messageNumberSlot = _registry.IndexOfField(_collectionHandle, "Message_Number");
        _messageColorSlot = _registry.IndexOfField(_collectionHandle, "Message_Color");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Dispatches to direction-specific handlers.
    //
    // data:       The application payload
    // metadata:   Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        switch (metadata.Channel)
        {
            case SoeConstants.StreamId.StreamClientToZone:
                HandleClientToZone(data, metadata);
                break;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleClientToZone
    //
    // Processes client-to-zone traffic
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleClientToZone(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "SimpleMessage: metadata cannot be "
                + "mapped to a character.", LogLevel.Warn);
            return;
        }

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            uint Message_Number = _extractor.GetUIntAt(_messageNumberSlot);
            uint Message_Color = _extractor.GetUIntAt(_messageColorSlot);
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_Simple_Message against the active patch and builds a display tree: a root node for
    // the collection with one leaf child per field each carrying its payload byte range.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    //
    // Returns:   The root FieldDisplayNode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;
        FieldDisplayNode root = new FieldDisplayNode();

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "SimpleMessage: metadata cannot be "
                + "mapped to a character", LogLevel.Warn);
            root.Text = "Simple Message <Unknown>";
            return root;
        }
  
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            FieldNodes.AddUIntNode(_extractor, _messageNumberSlot, "Message Number", root, "D");
            FieldNodes.AddUIntNode(_extractor, _messageColorSlot, "Color", root, "X8");
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Simple Message to " + character.Name;
        return root;
    }
}