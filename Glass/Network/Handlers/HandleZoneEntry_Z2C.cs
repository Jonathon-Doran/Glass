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
// HandleZoneEntry_Z2C
//
// Handles OP_ZoneEntry packets.  Server-to-client packets contain NPC/mob
// spawn data with a null-terminated name at offset 0.  Client-to-server
// packets contain the player's own zone entry with a different layout.
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleZoneEntry_Z2C : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_ZoneEntry_Z2C";
    private readonly PatchOpcode _opcodeHandled;
    private readonly CollectionHandle _collectionHandle;
    private PatchRegistry _registry;
    private PatchLevel _patchLevel;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _nameSlot;
    private readonly SlotId _spawnIdSlot;
    private readonly SlotId _levelSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleZoneEntry_Z2C (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_ZoneEntry from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_ZoneEntry, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleZoneEntry_Z2C()
    {
        _registry = GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_ZoneEntryV1");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _nameSlot = _registry.IndexOfField(_collectionHandle, "name");
        _spawnIdSlot = _registry.IndexOfField(_collectionHandle, "spawn_id");
        _levelSlot = _registry.IndexOfField(_collectionHandle, "level");
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
    // Processes zone-to-client OP_ZoneEntry.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;
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
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);

            name = extractor.GetStringAt(_nameSlot);
            spawnId = extractor.GetUIntAt(_spawnIdSlot);
            level = extractor.GetUIntAt(_levelSlot);
        }
        finally
        {
            extractor.Release();
        }

        if (!MobRepository.Instance.TryGetBySpawnId(zoneId.Value, spawnId, out Spawn? spawn))
        {
            spawn = new Spawn();
            spawn.Name = name;
            spawn.ZoneId = zoneId.Value;
            spawn.SpawnId = spawnId;
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
    public FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;
        FieldDisplayNode root = new FieldDisplayNode();
        string Label;
        uint zoneId = 0;

        // I would think that zone-id would be in the data...

        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "ZoneEntry: metadata cannot be "
                + "mapped to a character.  Zone name unavailable.", LogLevel.Warn);
            root.Text = "ZoneEntry to Unknown Zone";
            return root;
        }
        if (character.CurrentZone == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "ZoneEntry: no current zone "
                + "for character.", LogLevel.Warn);
            root.Text = "ZoneEntry to Unknown Zone";
            return root;
        }

        zoneId = character.CurrentZone.Value;
        string zoneName = ZoneRepository.Instance.GetZoneName(zoneId);
        Label = "ZoneEntry to " + zoneName;

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);

            uint spawnId = extractor.GetUIntAt(_spawnIdSlot);

            FieldNodes.AddLabeledNode(extractor, _spawnIdSlot, "spawnId = 0x" +
                spawnId.ToString("X4"), root);
            string name = FieldNodes.AddStringNode(extractor, _nameSlot, "Name", root);
            FieldNodes.AddUIntNode(extractor, _levelSlot, "Level", root, "D");
            Label += " (" + name + ")";
        }
        finally
        {
            extractor.Release();
        }

        root.Text = Label;
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
    public uint ResolveVersion(ReadOnlySpan<byte> data, PacketMetadata metadata)
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

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHandled
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode OpcodeHandled
    {
        get { return _opcodeHandled; }
    }
}