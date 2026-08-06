using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;
using System.Security.Policy;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleTargetResponse
//
// Handles OP_TargetResponse packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleTargetResponse : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _spawnIdSlot;
    private readonly SlotId _hpPercentSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleTargetResponse  (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleTargetResponse(PatchLevel patchLevel)
        : base(patchLevel, "OP_TargetResponse")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "Target_Response");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _spawnIdSlot = _registry.IndexOfField(_collectionHandle, "Spawn-id");
        _hpPercentSlot = _registry.IndexOfField(_collectionHandle, "HP-Percent");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Dispatches to direction-specific handlers.
    //
    // data:       The application payload
    // metadata:   Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        switch (metadata.Channel)
        {
            case SoeConstants.StreamId.StreamClientToZone:
                HandleClientToZone(data, metadata);
                break;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleClientToZone
    //
    // Processes client-to-zone traffic
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleClientToZone(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "TargetResponse: metadata cannot be "
                + "mapped to a character.  Dropping mob data.", LogLevel.Warn);
            return;
        }

        uint spawnId;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleTargetResponse:  No RootGate", LogLevel.Error);
                return;
            }

            spawnId = _extractor.GetUIntAt(_spawnIdSlot);
            uint hpPercent = _extractor.GetUIntAt(_hpPercentSlot);
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_TargetResponse against the active patch and builds a display tree: a root node for
    // the collection with one leaf child per field each carrying its payload byte range.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    //
    // Returns:   The root FieldDisplayNode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        ZoneId zoneId = GlassContext.SessionRegistry.ZoneFromMetadata(metadata);

        FieldDisplayNode root = new FieldDisplayNode();
        string targetName;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            SpawnId spawnId = (SpawnId) _extractor.GetUIntAt(_spawnIdSlot);
            targetName = MobRepository.Instance.LookupSpawnName(zoneId, spawnId);

            FieldNodes.AddLabeledNode(_extractor, _spawnIdSlot, "Spawn: " + targetName + " (" +
                    spawnId + ", 0x" + spawnId.Value.ToString("X4") + ")", root);

            FieldNodes.AddUIntNode(_extractor, _hpPercentSlot, "HP Percent", root, "D");
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Target (" + targetName + ")";
        return root;
    }
}