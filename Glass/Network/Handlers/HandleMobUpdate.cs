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
    private readonly CollectionHandle _collectionHandle;
    private readonly PatchRegistry _registry;
    private readonly PatchLevel _patchLevel;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _spawnIdSlot;
    private readonly SlotId _xPosSlot;
    private readonly SlotId _yPosSlot;
    private readonly SlotId _zPosSlot;
    private readonly SlotId _headingSlot;

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
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_MobUpdate");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _spawnIdSlot = _registry.IndexOfField(_collectionHandle, "spawn_id");
        _xPosSlot = _registry.IndexOfField(_collectionHandle, "x_pos");
        _yPosSlot = _registry.IndexOfField(_collectionHandle, "y_pos");
        _zPosSlot = _registry.IndexOfField(_collectionHandle, "z_pos");
        _headingSlot = _registry.IndexOfField(_collectionHandle, "headingRaw");
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
        FieldExtractor extractor = GlassContext.FieldExtractor;
        uint spawnId;
        float xPos;
        float yPos;
        float zPos;

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);

            spawnId = extractor.GetUIntAt(_spawnIdSlot);
            xPos = extractor.GetFloatAt(_xPosSlot);
            yPos = extractor.GetFloatAt(_yPosSlot);
            zPos = extractor.GetFloatAt(_zPosSlot);
        }
        finally
        {
            extractor.Release();
        }


        // short headingRaw = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(12));

        /*
        DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] "
            + _opcodeName + " spawnid: " + spawnId.ToString("x4") + " at ("
            + xPos.ToString("F2") + "," + yPos.ToString("F2") + "," + zPos.ToString("F2") + ")");
        */
    } 
    
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_Death against the active patch and builds a display tree: a root node for
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
        uint spawnId;
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;
        uint? zoneId;
        string mobName;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "MobUpdate: metadata cannot be "
                + "mapped to a character.", LogLevel.Warn);
            root.Text = "Mob <Unknown>";
            return root;
        }
        if (character.CurrentZone == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "MobUpdate: no current zone "
                + "for character.", LogLevel.Warn);
            root.Text = "Mob <Unknown>";
            return root;
        }

        zoneId = character.CurrentZone.Value;

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);

            spawnId = extractor.GetUIntAt(_spawnIdSlot);
            float xPos = extractor.GetFloatAt(_xPosSlot);
            float yPos = extractor.GetFloatAt(_yPosSlot);
            float zPos = extractor.GetFloatAt(_zPosSlot);
            uint headingRaw = extractor.GetUIntAt(_headingSlot);

            if (!MobRepository.Instance.TryGetBySpawnId(zoneId, spawnId, out Spawn? spawn))
            {
                DebugLog.Write(LogChannel.Opcodes, "TargetResolver: spawnId=" + spawnId
                    + " unknown.", LogLevel.Trace);
                mobName = "<unknown>";
            }
            else
            {
                mobName = spawn.Name!;
            }

            FieldNodes.AddLabeledNode(extractor, _spawnIdSlot, "NPC: " + mobName +
                " (0x" + spawnId.ToString("X4") + ")", root);

            FieldDisplayNode positionNode = new FieldDisplayNode("Position = (" + 
                xPos.ToString("F2") + "," + yPos.ToString("F2") + "," + zPos.ToString("F2") + ")");

            positionNode.AddByteRange(extractor.GetByteRangeFor(_xPosSlot));
            positionNode.AddByteRange(extractor.GetByteRangeFor(_yPosSlot));
            positionNode.AddByteRange(extractor.GetByteRangeFor(_zPosSlot));

            root.AddChild(positionNode);

            FieldDisplayNode headingNode = new FieldDisplayNode("raw heading = 0x" + headingRaw.ToString("X4"));
            headingNode.AddByteRange(extractor.GetByteRangeFor(_headingSlot));
            root.AddChild(headingNode);
        }
        finally
        {
            extractor.Release();
        }

        root.Text = "Mob Update (" + mobName + ")";
        return root;
    }

    /*
  
            _spawnIdSlot = _registry.IndexOfField(_collectionHandle, "spawn_id");
        _xPosSlot = _registry.IndexOfField(_collectionHandle, "x_pos");
        _yPosSlot = _registry.IndexOfField(_collectionHandle, "y_pos");
        _zPosSlot = _registry.IndexOfField(_collectionHandle, "z_pos");
        _headingSlot = _registry.IndexOfField(_collectionHandle, "headingRaw");
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