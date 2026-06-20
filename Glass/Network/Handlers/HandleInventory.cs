using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleInventory
//
// Handles OP_Inventory packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleInventory : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_Inventory";
    private readonly CollectionHandle _collectionHandle;
    private readonly PatchOpcode _opcodeHandled;
    private readonly PatchRegistry _registry;
    private readonly PatchLevel _patchLevel;
    private readonly GateDefinitionHandle _top_level_gate;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleInventory (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_Inventory from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleInventory()
    {
        _registry = GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel,  _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "Inventory Top-Level");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Dispose
    //
    // Log any errors in the cold-path, dispose of any local storage. 
    ///////////////////////////////////////////////////////////////////////////////////////////////

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeName
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public string OpcodeName
    {
        get { return _opcodeName; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Dispatches to direction-specific handlers.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        if (metadata.Channel != SoeConstants.StreamId.StreamZoneToClient)
        {
            return;
        }


        FieldExtractor extractor = GlassContext.FieldExtractor;
        uint bagCount = 0;

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);
            bagCount = extractor.BagCount(rootGate);

            DebugLog.Write(LogChannel.Opcodes, "Inventory top level sees " + bagCount + " bags");

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                extractor.EnterGate(rootGate, bagIndex);
            }

        }
        finally
        {
            extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHandled
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode OpcodeHandled
    {
        get { return _opcodeHandled; }
    }
}
