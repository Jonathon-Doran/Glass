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
// HandleMobUpdate
//
// Handles OP_NpcMove packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleMobUpdate : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_MobUpdate";
    private readonly PatchOpcode _opcodeHandled;
    private readonly OpcodeHandle _opcode;
    private readonly PatchRegistry _registry;
    private readonly PatchLevel _patchLevel;

    private readonly SlotId _spawnIdSlot;
    private readonly SlotId _xPosSlot;
    private readonly SlotId _yPosSlot;
    private readonly SlotId _zPosSlot;
    // private readonly int _headingSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleMobUpdate (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_MobUpdate from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_MobUpdate, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleMobUpdate()
    {
        _registry = GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _opcode = _registry.GetOpcodeHandle(_patchLevel, _opcodeName);

        _spawnIdSlot = _registry.IndexOfField(_patchLevel, _opcode, "spawn_id");
        _xPosSlot = _registry.IndexOfField(_patchLevel, _opcode, "x_pos");
        _yPosSlot = _registry.IndexOfField(_patchLevel, _opcode, "y_pos");
        _zPosSlot = _registry.IndexOfField(_patchLevel, _opcode, "z_pos");

        // Todo:  heading should be 16-bits at byte 12
       // _headingSlot = _fields.IndexOfField("heading");
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
        uint spawnId;
        float xPos;
        float yPos;
        float zPos;

        FieldBag bag = _registry.Rent(_opcodeHandled);
        try
        {
            GlassContext.FieldExtractor.Extract(_patchLevel, _opcode, data, bag);

            spawnId = bag.GetUIntAt(_spawnIdSlot);
            xPos = bag.GetFloatAt(_xPosSlot);
            yPos = bag.GetFloatAt(_yPosSlot);
            zPos = bag.GetFloatAt(_zPosSlot);
        }
        finally
        {
            bag.Release();
        }

        // short headingRaw = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(12));

        DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] "
            + _opcodeName + " spawnid: " + spawnId.ToString("x4") + " at ("
            + xPos.ToString("F2") + "," + yPos.ToString("F2") + "," + zPos.ToString("F2") + ")");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHandled
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode OpcodeHandled
    {
        get { return _opcodeHandled; }
    }
}