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
    private OpcodeHandle _opcode;
    private PatchRegistry _registry;
    private PatchLevel _patchLevel;

    private readonly uint _spawnId;
    private readonly uint _xPosId;
    private readonly uint _yPosId;
    private readonly uint _zPosId;
    // private readonly int _headingId;

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
        _opcode = GlassContext.PatchRegistry.GetOpcodeHandle(_patchLevel, _opcodeName);

        _spawnId = _registry.IndexOfField(_patchLevel, _opcode, "spawn_id");
        _xPosId = _registry.IndexOfField(_patchLevel, _opcode, "x_pos");
        _yPosId = _registry.IndexOfField(_patchLevel, _opcode, "y_pos");
        _zPosId = _registry.IndexOfField(_patchLevel, _opcode, "z_pos");

        // Todo:  heading should be 16-bits at byte 12
       // _headingId = _fields.IndexOfField("heading");
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

        FieldBag bag = _registry.Rent(_patchLevel, _opcode);
        try
        {
            GlassContext.FieldExtractor.Extract(_patchLevel, _opcode, data, bag);

            spawnId = bag.GetUIntAt(_spawnId);
            xPos = bag.GetFloatAt(_xPosId);
            yPos = bag.GetFloatAt(_yPosId);
            zPos = bag.GetFloatAt(_zPosId);
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
}