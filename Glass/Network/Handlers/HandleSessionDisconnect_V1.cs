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
// HandleSessionDisconnect
//
// Handles OP_SessionDisconnect packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleSessionDisconnect_V1 : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _sessionIdSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleSessionDisconnect (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_SessionDisconnect from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_SessionDisconnect, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleSessionDisconnect_V1(PatchLevel patchLevel)
        : base(patchLevel, "OP_SessionDisconnect_V1")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel,  _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_SessionDisconnect_V1");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _sessionIdSlot = _registry.IndexOfField(_collectionHandle, "session_id");
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

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            sessionId = _extractor.GetUIntAt(_sessionIdSlot);
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_SessionDisconnect against the active patch and builds a display tree: a root node for
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
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Session Disconnect";
        return root;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ResolveVersion
    //
    // Returns the opcode version for a packet.
    //
    // data:      The application payload.
    // metadata:  Packet metadata
    //
    // Returns:   The resolved version number.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override uint ResolveVersion(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        // We have seen packets with a flags byte.  Since these are not application packets, the header info is not
        // completely removed.

        switch (data.Length)
        {
            case 8:
                return 1;

            case 9:
                return 2;
        }

        return 0;
    }
}


