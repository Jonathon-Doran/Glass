using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;
using System.Security.Policy;
using System.Text;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleZoneEntry_Z2C (Zone to Client)
//
// Handles OP_ZoneEntry packets.  Server-to-client packets contain NPC/mob
// spawn data with a null-terminated name at offset 0.  Client-to-server
// packets contain the player's own zone entry with a different layout.
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleZoneEntry_Z2C : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _nameSlot;
    private readonly SlotId _spawnIdSlot;
    private readonly SlotId _levelSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleZoneEntry_Z2C (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleZoneEntry_Z2C(PatchLevel patchLevel)
        : base(patchLevel, "OP_ZoneEntry_Z2C")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_ZoneEntryV1");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _nameSlot = _registry.IndexOfField(_collectionHandle, "name");
        _spawnIdSlot = _registry.IndexOfField(_collectionHandle, "spawn_id");
        _levelSlot = _registry.IndexOfField(_collectionHandle, "level");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Processes zone-to-client OP_ZoneEntry.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "ZoneEntry: metadata cannot be "
                + "mapped to a character.  Dropping mob data.", LogLevel.Warn);
            return;
        }
        string name;
        uint spawnId;
        uint level;
        uint? zoneId = character.CurrentZone;

        // Cannot store data if we do not know what zone we are in yet
        if (! zoneId.HasValue)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleSpawn: character '" + character.Name
                + "' has no zone id; spawn data discarded.", LogLevel.Warn);
            return;
        }

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            name = _extractor.GetStringAt(_nameSlot);
            spawnId = _extractor.GetUIntAt(_spawnIdSlot);
            level = _extractor.GetUIntAt(_levelSlot);
        }
        finally
        {
            _extractor.Release();
        }

        if (!MobRepository.Instance.TryGetBySpawnId((ZoneId)zoneId.Value, (SpawnId) spawnId, out Spawn? spawn))
        {
            spawn = new Spawn();
            spawn.Name = name;
            spawn.ZoneId = (ZoneId)zoneId.Value;
            spawn.SpawnId = (SpawnId)spawnId;
            MobRepository.Instance.Add(spawn);
            DebugLog.Write(LogChannel.Opcodes, "SpawnHandler: created new record for " + name + ", zoneId=" + zoneId
                + " spawnId=" + spawnId.ToString("X4") + ".", LogLevel.Info);
        }

        spawn.Name = name;
        spawn.Level = (ushort) level;
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
    public override FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldDisplayNode root = new FieldDisplayNode();

        // I would think that zone-id would be in the data...
        ZoneId zoneId = GlassContext.SessionRegistry.ZoneFromMetadata(metadata);
        string zoneName = ZoneRepository.Instance.GetZoneName(zoneId);

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry:  No RootGate", LogLevel.Error);
                return root;
            }

            SpawnId spawnId = (SpawnId) _extractor.GetUIntAt(_spawnIdSlot);
            String spawnName = MobRepository.Instance.LookupSpawnName(zoneId, spawnId);

            FieldNodes.AddLabeledNode(_extractor, _spawnIdSlot, "Spawn: " + spawnName + " (" +
                    spawnId + ", 0x" + spawnId.Value.ToString("X4") + ")", root);

            string name = FieldNodes.AddStringNode(_extractor, _nameSlot, "Name", root);
            FieldNodes.AddUIntNode(_extractor, _levelSlot, "Level", root, "D");
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "ZoneEntry to " + zoneName;
        return root;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ResolveVersion
    //
    // Returns the opcode version for a packet.
    //
    // data:      The application payload.
    // metadata:  Packet metadata
    //
    // Returns:   The resolved version number.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override uint ResolveVersion(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        switch (metadata.Channel)
        {
            case SoeConstants.StreamId.StreamZoneToClient:
                return 1;

            case SoeConstants.StreamId.StreamClientToZone:
                return 2;
        }

        return 0;
    }
}