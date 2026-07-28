using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleSessionRequest
//
// Handles OP_SessionRequest packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleSessionRequest : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _sessionIdSlot;
    private readonly SlotId _maxLengthSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleSessionRequest (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleSessionRequest(PatchLevel patchLevel)
        : base(patchLevel, "OP_SessionRequest")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel,  _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_SessionRequest");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _sessionIdSlot = _registry.IndexOfField(_collectionHandle, "session_id");
        _maxLengthSlot = _registry.IndexOfField(_collectionHandle, "max_length");
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
        uint sessionId;
        uint maxLength;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            sessionId = _extractor.GetUIntAt(_sessionIdSlot);
            maxLength = _extractor.GetUIntAt(_maxLengthSlot);
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_SessionRequest against the active patch and builds a display tree: a root node for
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

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            FieldNodes.AddUIntNode(_extractor, _sessionIdSlot, "Session ID", root);
            FieldNodes.AddUIntNode(_extractor, _maxLengthSlot, "Maximum Length", root);
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Session Request";
        return root;
    }
}


