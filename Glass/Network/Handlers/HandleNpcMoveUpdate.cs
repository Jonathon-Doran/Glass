using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
namespace Glass.Network.Handlers;
///////////////////////////////////////////////////////////////////////////////////////////////
// HandleNpcMove
//
// Handles OP_NpcMoveUpdate packets.
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleNpcMoveUpdate : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_NpcMoveUpdate";
    private readonly PatchOpcode _opcodeHandled;
    private readonly CollectionHandle _collectionHandle;
    private readonly PatchRegistry _registry;
    private readonly PatchLevel _patchLevel;

    private readonly SlotId _spawnIdSlot;
    private readonly SlotId _xPosSlot;
    private readonly SlotId _yPosSlot;
    private readonly SlotId _zPosSlot;
    private readonly SlotId _headingSlot;
    private readonly SlotId _pitchSlot;
    private readonly SlotId _headingDeltaSlot;
    private readonly SlotId _velocitySlot;
    private readonly SlotId _dxSlot;
    private readonly SlotId _dySlot;
    private readonly SlotId _dzSlot;

    private readonly SlotId _flagsSlot;

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
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_NpcMoveUpdate");

        _spawnIdSlot = _registry.IndexOfField(_collectionHandle, "spawn_id");
        _xPosSlot = _registry.IndexOfField(_collectionHandle, "x_pos");
        _yPosSlot = _registry.IndexOfField(_collectionHandle, "y_pos");
        _zPosSlot = _registry.IndexOfField(_collectionHandle, "z_pos");
        _headingSlot = _registry.IndexOfField(_collectionHandle, "heading");
        _pitchSlot = _registry.IndexOfField(_collectionHandle, "pitch");
        _headingDeltaSlot = _registry.IndexOfField(_collectionHandle, "heading_delta");
        _velocitySlot = _registry.IndexOfField(_collectionHandle, "velocity");
        _dxSlot = _registry.IndexOfField(_collectionHandle, "dx");
        _dySlot = _registry.IndexOfField(_collectionHandle, "dy");
        _dzSlot = _registry.IndexOfField(_collectionHandle, "dz");

        _flagsSlot = _registry.IndexOfField(_collectionHandle, "flags");
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
      /*  FieldBag bag = _registry.Rent(_collectionHandle);
        try
        {
            GlassContext.FieldExtractor.ExtractCollection(_patchLevel, _collectionHandle, data, bag);

            uint spawnId = bag.GetUIntAt(_spawnIdSlot);
            float x = bag.GetFloatAt(_xPosSlot);
            float y = bag.GetFloatAt(_yPosSlot);
            float z = bag.GetFloatAt(_zPosSlot);
            float heading = bag.GetFloatAt(_headingSlot);
            uint flags = bag.GetUIntAt(_flagsSlot);

            DebugLog.Write(LogChannel.Opcodes, "Flags is 0x" + flags.ToString("x2"));

            double headingDegrees = heading * 360.0 / 2048.0;

            System.Text.StringBuilder optional = new System.Text.StringBuilder();

            if (bag.IsPresent(_pitchSlot))
            {
                optional.Append(" pitch=");
                optional.Append(bag.GetFloatAt(_pitchSlot));
            }
            if (bag.IsPresent(_headingDeltaSlot))
            {
                optional.Append(" headingDelta=");
                optional.Append(bag.GetFloatAt(_headingDeltaSlot));
            }
            if (bag.IsPresent(_velocitySlot))
            {
                optional.Append(" velocity=");
                optional.Append(bag.GetFloatAt(_velocitySlot));
            }
            if (bag.IsPresent(_dxSlot))
            {
                optional.Append(" dx=");
                optional.Append(bag.GetFloatAt(_dxSlot));
            }
            if (bag.IsPresent(_dySlot))
            {
                optional.Append(" dy=");
                optional.Append(bag.GetFloatAt(_dySlot));
            }
            if (bag.IsPresent(_dzSlot))
            {
                optional.Append(" dz=");
                optional.Append(bag.GetFloatAt(_dzSlot));
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
        }*/
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHandled
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode OpcodeHandled
    {
        get { return _opcodeHandled; }
    }
}