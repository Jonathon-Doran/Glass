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
// Handles OP_MobUpdate packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleMobUpdate : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
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
    public HandleMobUpdate(PatchLevel patchLevel)
        : base(patchLevel, "OP_MobUpdate")
    {
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
    // HandlePacket
    //
    // Dispatches to direction-specific handlers.
    //
    // data:      The application payload
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

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            spawnId = _extractor.GetUIntAt(_spawnIdSlot);
            xPos = _extractor.GetFloatAt(_xPosSlot);
            yPos = _extractor.GetFloatAt(_yPosSlot);
            zPos = _extractor.GetFloatAt(_zPosSlot);
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_MobUpdate against the active patch and builds a display tree: a root node for
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
        string mobName;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleMobUpdate:  No RootGate", LogLevel.Error);
                return root;
            }

            SpawnId spawnId = (SpawnId) _extractor.GetUIntAt(_spawnIdSlot);
            float xPos = _extractor.GetFloatAt(_xPosSlot);
            float yPos = _extractor.GetFloatAt(_yPosSlot);
            float zPos = _extractor.GetFloatAt(_zPosSlot);
            uint headingRaw = _extractor.GetUIntAt(_headingSlot);

            mobName = MobRepository.Instance.LookupSpawnName(zoneId, spawnId);

            FieldNodes.AddLabeledNode(_extractor, _spawnIdSlot, "Spawn: " + mobName + " (" +
                 spawnId + ", 0x" + spawnId.Value.ToString("X4") + ")", root);

            FieldDisplayNode positionNode = new FieldDisplayNode("Position = (" + 
                xPos.ToString("F2") + "," + yPos.ToString("F2") + "," + zPos.ToString("F2") + ")");

            positionNode.AddByteRange(_extractor.GetByteRangeFor(_xPosSlot));
            positionNode.AddByteRange(_extractor.GetByteRangeFor(_yPosSlot));
            positionNode.AddByteRange(_extractor.GetByteRangeFor(_zPosSlot));

            root.AddChild(positionNode);

            FieldNodes.AddLabeledNode(_extractor, _headingSlot, "raw heading: " + headingRaw + " (0x" +
                headingRaw.ToString("X4") + ")", root);
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Mob Update (" + mobName + ")";
        return root;
    }
}