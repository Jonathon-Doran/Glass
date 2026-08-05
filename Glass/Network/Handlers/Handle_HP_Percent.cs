using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.World;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleHP_Percent
//
// Handles OP_HP_Percent  packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleHP_Percent: OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _spawnIDSlot;
    private readonly SlotId _percentSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleSpellAction  (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleHP_Percent(PatchLevel patchLevel)
        : base(patchLevel, "OP_HP_Percent")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "HP_Percent");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _spawnIDSlot = _registry.IndexOfField(_collectionHandle, "Spawn-ID");
        _percentSlot = _registry.IndexOfField(_collectionHandle, "Percent");
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
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "HP_Percent: metadata cannot be "
                + "mapped to a character.", LogLevel.Warn);
            return;
        }

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            uint spawnID = _extractor.GetUIntAt(_spawnIDSlot);
            uint percent = _extractor.GetUIntAt(_percentSlot);
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_HP_Percent  against the active patch and builds a display tree: a root node for
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
        uint? zoneId = GlassContext.SessionRegistry.ZoneFromMetadata(metadata);
        string spawnName;

        if (! zoneId.HasValue)
        {
            return root;
        }

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            uint spawnID = _extractor.GetUIntAt(_spawnIDSlot);

            if (!MobRepository.Instance.TryGetBySpawnId((ZoneId) zoneId, (SpawnId) spawnID, out Spawn? spawn))
            {
                DebugLog.Write(LogChannel.Opcodes, "HP_Percent: spawnID=" + spawnID
                    + " unknown.", LogLevel.Trace);
                spawnName = "<unknown>";
            }
            else
            {
                spawnName = spawn.Name!;
            }
            FieldNodes.AddLabeledNode(_extractor, _spawnIDSlot, "Spawn: " + spawnName + " (" +
                spawnID + ", " + spawnID.ToString("X4") + ")", root);

            FieldNodes.AddUIntNode(_extractor, _percentSlot, "Percent", root, "D");
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "HP Percent (" + spawnName + ")";
        return root;
    }
}



