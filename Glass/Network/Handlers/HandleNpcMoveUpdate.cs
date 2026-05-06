using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;
using System.Diagnostics;
namespace Glass.Network.Handlers;
///////////////////////////////////////////////////////////////////////////////////////////////
// HandleNpcMove
//
// Handles OP_NpcMoveUpdate packets.
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleNpcMoveUpdate : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_NpcMoveUpdate";
    private OpcodeHandle _handle;
    private PatchRegistry _registry;
    private PatchLevel _patchLevel;

    private readonly int _spawnId;
    private readonly int _xPosId;
    private readonly int _yPosId;
    private readonly int _zPosId;
    private readonly int _headingId;
    private readonly int _pitchId;
    private readonly int _headingDeltaId;
    private readonly int _velocityId;
    private readonly int _dxId;
    private readonly int _dyId;
    private readonly int _dzId; 

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleNpcMoveUpdate (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_NpcMoveUpdate from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_NpcMoveUpdate, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleNpcMoveUpdate()
    {
        _registry = GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
        _handle = GlassContext.PatchRegistry.GetOpcodeHandle(_patchLevel, _opcodeName);

        _spawnId = _registry.IndexOfField(_patchLevel, _handle, "spawn_id");
        _xPosId = _registry.IndexOfField(_patchLevel, _handle, "x_pos");
        _yPosId = _registry.IndexOfField(_patchLevel, _handle, "y_pos");
        _zPosId = _registry.IndexOfField(_patchLevel, _handle, "z_pos");
        _headingId = _registry.IndexOfField(_patchLevel, _handle, "heading");
        _pitchId = _registry.IndexOfField(_patchLevel, _handle, "pitch");
        _headingDeltaId = _registry.IndexOfField(_patchLevel, _handle, "heading_delta");
        _velocityId = _registry.IndexOfField(_patchLevel, _handle, "velocity");
        _dxId = _registry.IndexOfField(_patchLevel, _handle, "dx");
        _dyId = _registry.IndexOfField(_patchLevel, _handle, "dy");
        _dzId = _registry.IndexOfField(_patchLevel, _handle, "dz");
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
        get
        {
            return _opcodeName;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Dispatches to direction-specific handlers.
    //
    // data:       The application payload
    // length:     Length of the application payload
    // direction:  Direction byte
    // opcode:     The application-level opcode
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
    // Processes zone-to-client traffic.  Decodes bit-packed position, heading, and
    // optional motion fields from the packet.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldBag bag = _registry.Rent(_patchLevel, _handle);
        try
        {
            GlassContext.FieldExtractor.Extract(_patchLevel, _handle, data, bag);

            uint spawnId = bag.GetUIntAt(_spawnId);
            float x = bag.GetFloatAt(_xPosId);
            float y = bag.GetFloatAt(_yPosId);
            float z = bag.GetFloatAt(_zPosId);
            uint heading = bag.GetUIntAt(_headingId);

            double headingDegrees = heading * 360.0 / 2048.0;

            System.Text.StringBuilder optional = new System.Text.StringBuilder();

            if (bag.IsPresent(_pitchId))
            {
                optional.Append(" pitch=");
                optional.Append(bag.GetIntAt(_pitchId));
            }
            if (bag.IsPresent(_headingDeltaId))
            {
                optional.Append(" headingDelta=");
                optional.Append(bag.GetIntAt(_headingDeltaId));
            }
            if (bag.IsPresent(_velocityId))
            {
                optional.Append(" velocity=");
                optional.Append(bag.GetIntAt(_velocityId));
            }
            if (bag.IsPresent(_dxId))
            {
                optional.Append(" dx=");
                optional.Append(bag.GetIntAt(_dxId));
            }
            if (bag.IsPresent(_dyId))
            {
                optional.Append(" dy=");
                optional.Append(bag.GetIntAt(_dyId));
            }
            if (bag.IsPresent(_dzId))
            {
                optional.Append(" dz=");
                optional.Append(bag.GetIntAt(_dzId));
            }

            string timestamp = metadata.Timestamp.ToString("HH:mm:ss.fff");
            DebugLog.Write(LogChannel.Opcodes, "[" + timestamp + "] " + _opcodeName
                + " SpawnId=0x" + spawnId.ToString("x4")
                + " pos=(" + x.ToString("F2") + "," + y.ToString("F2") + "," + z.ToString("F2") + ")"
                + " heading=" + headingDegrees.ToString("0.00")
                + optional.ToString());
        }
        finally
        {
            bag.Release();
        }
    }
}