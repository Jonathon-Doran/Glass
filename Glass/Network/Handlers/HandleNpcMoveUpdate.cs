using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System.Security.Policy;
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
    private readonly GateDefinitionHandle _top_level_gate;

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
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

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
        FieldExtractor extractor = GlassContext.FieldExtractor;
        uint spawnId;
        float x, y, z;
        float heading, headingDegrees;
        uint flags;

        // optional fields
        float pitch, heading_delta, velocity, dx, dy, dz;

        System.Text.StringBuilder optional = new System.Text.StringBuilder();

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);

            spawnId = extractor.GetUIntAt(_spawnIdSlot);
            x = extractor.GetFloatAt(_xPosSlot);
            y = extractor.GetFloatAt(_yPosSlot);
            z = extractor.GetFloatAt(_zPosSlot);

            heading = extractor.GetFloatAt(_headingSlot);
            headingDegrees = heading * 360.0f / 2048.0f;
            flags = extractor.GetUIntAt(_flagsSlot);

            if (extractor.IsPresent(_pitchSlot))
            {
                pitch = extractor.GetFloatAt(_pitchSlot);
                optional.Append(", pitch=" + pitch);
            }
            if (extractor.IsPresent(_headingDeltaSlot))
            {
                heading_delta = extractor.GetFloatAt(_headingDeltaSlot);
                optional.Append(", heading delta=" + heading_delta);
            }
            if (extractor.IsPresent(_velocitySlot))
            {
                velocity = extractor.GetFloatAt(_velocitySlot);
                optional.Append(", velocity=" + velocity);
            }
            if (extractor.IsPresent(_dxSlot))
            {
                dx = extractor.GetFloatAt(_dxSlot);
                optional.Append(", dx=" + dx);
            }
            if (extractor.IsPresent(_dySlot))
            {
                dy = extractor.GetFloatAt(_dySlot);
                optional.Append(", dy=" + dy);
            }
            if (extractor.IsPresent(_dzSlot))
            {
                dz = extractor.GetFloatAt(_dzSlot);
                optional.Append(", dz=" + dz);
            }
        }
        finally
        {
            extractor.Release();
        }

        /*
        string timestamp = metadata.Timestamp.ToString("HH:mm:ss.fff");
        DebugLog.Write(LogChannel.Opcodes, "[" + timestamp + "] " + _opcodeName
            + " SpawnId=0x" + spawnId.ToString("x4")
            + " pos=(" + x.ToString("F2") + "," + y.ToString("F2") + "," + z.ToString("F2") + ")"
            + " heading=" + headingDegrees.ToString("0.00")
            + optional.ToString());
        */
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_NPCMoveUpdate against the active patch and builds a display tree: a root node for
    // the collection with one leaf child per field each carrying its payload byte range.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    //
    // Returns:   The root FieldDisplayNode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;
        FieldDisplayNode root = new FieldDisplayNode();
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;
        uint? zoneId;
        string npcName;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "Target: metadata cannot be "
                + "mapped to a character.  Dropping mob data.", LogLevel.Warn);
            root.Text = "Target <Unknown>";
            return root;
        }
        if (character.CurrentZone == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "Target: no current zone "
                + "for character.", LogLevel.Warn);
            root.Text = "Target <Unknown>";
            return root;
        }

        zoneId = character.CurrentZone.Value;

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);

            uint spawnId = extractor.GetUIntAt(_spawnIdSlot);
            float xPos = extractor.GetFloatAt(_xPosSlot);
            float yPos = extractor.GetFloatAt(_yPosSlot);
            float zPos = extractor.GetFloatAt(_zPosSlot);
            float headingRaw = extractor.GetFloatAt(_headingSlot);
            float headingDegrees = headingRaw * 360.0f / 2048.0f;
            uint  flags = extractor.GetUIntAt(_flagsSlot);



            spawnId = extractor.GetUIntAt(_spawnIdSlot);

            if (!MobRepository.Instance.TryGetBySpawnId(zoneId, spawnId, out Spawn? spawn))
            {
                DebugLog.Write(LogChannel.Opcodes, "TargetResolver: spawnId=" + spawnId
                    + " unknown.", LogLevel.Trace);
                npcName = "<unknown>";
            }
            else
            {
                npcName = spawn.Name!;
            }

            FieldNodes.AddLabeledNode(extractor, _spawnIdSlot, "NPC: " + npcName +
                " (0x" + spawnId.ToString("X4") + ")", root);


            FieldDisplayNode positionNode = new FieldDisplayNode("Position = (" +
                xPos.ToString("F2") + "," + yPos.ToString("F2") + "," + zPos.ToString("F2") + ")");
            positionNode.AddByteRange(extractor.GetByteRangeFor(_xPosSlot));
            positionNode.AddByteRange(extractor.GetByteRangeFor(_yPosSlot));
            positionNode.AddByteRange(extractor.GetByteRangeFor(_zPosSlot));
            root.AddChild(positionNode);

            FieldDisplayNode headingNode = new FieldDisplayNode("heading (degrees) = " + headingDegrees.ToString("F2"));
            headingNode.AddByteRange(extractor.GetByteRangeFor(_headingSlot));
            root.AddChild(headingNode);

            FieldDisplayNode flagsNode = new FieldDisplayNode("flags = " + flags.ToString("B"));
            flagsNode.AddByteRange(extractor.GetByteRangeFor(_flagsSlot));
            root.AddChild(flagsNode);
        }
        finally
        {
            extractor.Release();
        }

        root.Text = "NPCMoveUpdate (" + npcName + ")";
        return root;
    }

    /*
     * 
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
     * 
     */



    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHandled
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode OpcodeHandled
    {
        get { return _opcodeHandled; }
    }
}