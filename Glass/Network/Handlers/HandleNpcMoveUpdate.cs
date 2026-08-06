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
public class HandleNpcMoveUpdate : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
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
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleNpcMoveUpdate(PatchLevel patchLevel)
        : base(patchLevel, "OP_NpcMoveUpdate")
    {
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
    // Processes zone-to-client traffic.  Decodes bit-packed position, heading, and
    // optional motion fields from the packet.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        uint spawnId;
        float x, y, z;
        float heading, headingDegrees;
        uint flags;

        // optional fields
        float pitch, heading_delta, velocity, dx, dy, dz;

        System.Text.StringBuilder optional = new System.Text.StringBuilder();

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            spawnId = _extractor.GetUIntAt(_spawnIdSlot);
            x = _extractor.GetFloatAt(_xPosSlot);
            y = _extractor.GetFloatAt(_yPosSlot);
            z = _extractor.GetFloatAt(_zPosSlot);

            heading = _extractor.GetFloatAt(_headingSlot);
            headingDegrees = heading * 360.0f / 2048.0f;
            flags = _extractor.GetUIntAt(_flagsSlot);

            if (_extractor.IsPresent(_pitchSlot))
            {
                pitch = _extractor.GetFloatAt(_pitchSlot);
                optional.Append(", pitch=" + pitch);
            }
            if (_extractor.IsPresent(_headingDeltaSlot))
            {
                heading_delta = _extractor.GetFloatAt(_headingDeltaSlot);
                optional.Append(", heading delta=" + heading_delta);
            }
            if (_extractor.IsPresent(_velocitySlot))
            {
                velocity = _extractor.GetFloatAt(_velocitySlot);
                optional.Append(", velocity=" + velocity);
            }
            if (_extractor.IsPresent(_dxSlot))
            {
                dx = _extractor.GetFloatAt(_dxSlot);
                optional.Append(", dx=" + dx);
            }
            if (_extractor.IsPresent(_dySlot))
            {
                dy = _extractor.GetFloatAt(_dySlot);
                optional.Append(", dy=" + dy);
            }
            if (_extractor.IsPresent(_dzSlot))
            {
                dz = _extractor.GetFloatAt(_dzSlot);
                optional.Append(", dz=" + dz);
            }
        }
        finally
        {
            _extractor.Release();
        }
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
    public override FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldDisplayNode root = new FieldDisplayNode();
        ZoneId zoneId = GlassContext.SessionRegistry.ZoneFromMetadata(metadata);
        string npcName;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            SpawnId spawnId = (SpawnId) _extractor.GetUIntAt(_spawnIdSlot);
            float xPos = _extractor.GetFloatAt(_xPosSlot);
            float yPos = _extractor.GetFloatAt(_yPosSlot);
            float zPos = _extractor.GetFloatAt(_zPosSlot);
            float headingRaw = _extractor.GetFloatAt(_headingSlot);
            float headingDegrees = headingRaw * 360.0f / 2048.0f;
            uint  flags = _extractor.GetUIntAt(_flagsSlot);

            npcName = MobRepository.Instance.LookupSpawnName(zoneId, spawnId);

            FieldNodes.AddLabeledNode(_extractor, _spawnIdSlot, "Spawn: " + npcName + " (" +
                    spawnId + ", 0x" + spawnId.Value.ToString("X4") + ")", root);

            FieldDisplayNode positionNode = new FieldDisplayNode("Position = (" +
                xPos.ToString("F2") + "," + yPos.ToString("F2") + "," + zPos.ToString("F2") + ")");
            positionNode.AddByteRange(_extractor.GetByteRangeFor(_xPosSlot));
            positionNode.AddByteRange(_extractor.GetByteRangeFor(_yPosSlot));
            positionNode.AddByteRange(_extractor.GetByteRangeFor(_zPosSlot));
            root.AddChild(positionNode);

            FieldNodes.AddLabeledNode(_extractor, _headingSlot, "Heading (degrees) = " + headingDegrees.ToString("F2"), root);
            FieldNodes.AddUIntNode(_extractor, _flagsSlot, "Flags", root, "B");
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "NPCMoveUpdate (" + npcName + ")";
        return root;
    }
}