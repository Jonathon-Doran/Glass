using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.World;
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

    private readonly SlotId _name_Slot;
    private readonly SlotId _spawnId_Slot;
    private readonly SlotId _level_Slot;
    private readonly SlotId _unknown_3_Slot;
    private readonly SlotId _id_Slot;

    private readonly SlotId _isPlayer_Slot;
    private readonly SlotId _type_Slot;
    private readonly SlotId _unknown_1_Slot;
    private readonly SlotId _unknown_2_Slot;
    private readonly SlotId _C_Slot;
    private readonly SlotId _quadsGate_Slot;
    private readonly SlotId _A_Slot;
    private readonly SlotId _model_Slot;
    private readonly SlotId _class_Slot;
    private readonly SlotId _lastname_Slot;
    private readonly SlotId _ownerId_Slot;

    private readonly SlotId _quad_Unknown_Slot;

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
        if (_opcodeHandled == PatchOpcode.None)
        {
            // happens when replaying captures before the opcode was added
            return;
        }


        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_ZoneEntryV1");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);
        _quadsGate_Slot = _registry.IndexOfField(_collectionHandle, "Quads_Gate");

        CollectionHandle quadCollection = _registry.GetCollectionHandle(_patchLevel, "ZoneEntry_Quads");

        _name_Slot = _registry.IndexOfField(_collectionHandle, "name");
        _spawnId_Slot = _registry.IndexOfField(_collectionHandle, "spawn_id");
        _level_Slot = _registry.IndexOfField(_collectionHandle, "level");
        _unknown_3_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_3");
        _id_Slot = _registry.IndexOfField(_collectionHandle, "character_id");
        _isPlayer_Slot = _registry.IndexOfField(_collectionHandle, "is_player");
        _type_Slot = _registry.IndexOfField(_collectionHandle, "Type");
        _unknown_1_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_1");
        _unknown_2_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_2");
        _C_Slot = _registry.IndexOfField(_collectionHandle, "C");
        _A_Slot = _registry.IndexOfField(_collectionHandle, "A");
        _model_Slot = _registry.IndexOfField(_collectionHandle, "Model");
        _class_Slot = _registry.IndexOfField(_collectionHandle, "Class");

        _lastname_Slot = _registry.IndexOfField(_collectionHandle, "LastName");
        _ownerId_Slot = _registry.IndexOfField(_collectionHandle, "owner_id");

        // Quads slots
        _quad_Unknown_Slot = _registry.IndexOfField(quadCollection, "Unknown");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Processes zone-to-client OP_ZoneEntry.  Players are not stored.  Every other spawn is
    // recorded under the receiving character's zone: its mob type is looked up or created by
    // zone and base name, and the spawn record is created on first sight and refreshed with the
    // decoded field values on every sight.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        Character? character = metadata.Character;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry_Z2C: metadata cannot be "
                + "mapped to a character.  Dropping mob data.", LogLevel.Warn);
            return;
        }

        string name;
        uint spawnId;
        uint level;
        uint persistentId;
        uint A;
        uint model;
        uint mobClass;
        uint isPlayer;
        uint type;
        float unknown1;
        uint unknown2;
        uint unknown3;
        uint C;
        string lastName;
        uint ownerId;

        ZoneId zoneId = metadata.Session.ZoneId;

        // Cannot store data if we do not know what zone we are in yet
        if (zoneId.Exists == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry_Z2C: character '" + character.Name
                + "' has no zone id; spawn data discarded.", LogLevel.Warn);
            return;
        }

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            name = _extractor.GetStringAt(_name_Slot);
            spawnId = _extractor.GetUIntAt(_spawnId_Slot);
            level = _extractor.GetUIntAt(_level_Slot);
            persistentId = _extractor.GetUIntAt(_id_Slot);
            A = _extractor.GetUIntAt(_A_Slot);
            model = _extractor.GetUIntAt(_model_Slot);
            mobClass = _extractor.GetUIntAt(_class_Slot);
            isPlayer = _extractor.GetUIntAt(_isPlayer_Slot);
            type = _extractor.GetUIntAt(_type_Slot);
            unknown1 = _extractor.GetFloatAt(_unknown_1_Slot);
            unknown2 = _extractor.GetUIntAt(_unknown_2_Slot);
            unknown3 = _extractor.GetUIntAt(_unknown_3_Slot);
            C = _extractor.GetUIntAt(_C_Slot);
            lastName = _extractor.GetStringAt(_lastname_Slot);
            ownerId = _extractor.GetUIntAt(_ownerId_Slot);
        }
        finally
        {
            _extractor.Release();
        }

        // A nonzero persistent id is a player.  One of our own characters is placed in the zone under its
        // spawn id; other players are not stored.
        if (persistentId != 0)
        {
            Character? player = CharacterRepository.Instance.GetByCharacterId((CharacterId)persistentId);
            if (player != null)
            {
                CharacterRepository.Instance.SetZoneSpawn(player, zoneId, (SpawnId)spawnId);
                DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry_Z2C: placed '" + player.Name + "' in zoneId="
                    + zoneId + " spawnId=0x" + spawnId.ToString("X4"), LogLevel.Trace);
            }
            else
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry_Z2C: '" + name + "' spawnId=0x"
                    + spawnId.ToString("X4") + " is another player; not stored.", LogLevel.Trace);
            }
            return;
        }

        string baseName = BaseNameOf(name);
        if (!MobRepository.Instance.TryGetTypeByName((ZoneId)zoneId.Value, baseName, out MobType? mobType))
        {
            mobType = new MobType();
            mobType.BaseName = baseName;
            mobType.ZoneId = (ZoneId)zoneId.Value;
            mobType.LevelMin = (ushort)level;
            mobType.LevelMax = (ushort)level;
            mobType.FirstSeen = metadata.Timestamp;
            mobType.LastSeen = metadata.Timestamp;
            mobType.Model = model;
            mobType.Class = mobClass;
            mobType.Sightings = 1;

            if (MobRepository.Instance.Create(mobType).Exists == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry_Z2C: could not create type for '" + baseName
                    + "'; spawn '" + name + "' not stored.", LogLevel.Error);
                return;
            }
        }
        else
        {
            if (level < mobType.LevelMin)
            {
                mobType.LevelMin = (ushort)level;
            }
            if (level > mobType.LevelMax)
            {
                mobType.LevelMax = (ushort)level;
            }
            mobType.LastSeen = metadata.Timestamp;
            mobType.Sightings++;
        }

        if (!MobRepository.Instance.TryGetBySpawnId((ZoneId)zoneId.Value, (SpawnId)spawnId, out Spawn? spawn))
        {
            spawn = new Spawn();
            spawn.ZoneId = (ZoneId)zoneId.Value;
            spawn.SpawnId = (SpawnId)spawnId;
            spawn.MobId = mobType.Id;
            spawn.Observed = metadata.Timestamp;
            MobRepository.Instance.AddSpawn(spawn);
            DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry_Z2C: created new record for " + name + ", zoneId=" + zoneId
                + " spawnId=0x" + spawnId.ToString("X4") + " mobId=" + mobType.Id + ".", LogLevel.Trace);
        }

        // We have the spawn by reference from above, so fill in the data
        spawn.Name = name;
        spawn.Level = (ushort)level;
        spawn.A = A;
        spawn.PersistentId = persistentId;
        spawn.IsPlayer = isPlayer;
        spawn.Type = type;
        spawn.Unknown1 = unknown1;
        spawn.Unknown2 = unknown2;
        spawn.Unknown3 = unknown3;
        spawn.C = C;
        spawn.Model = model;
        spawn.Class = mobClass;
        spawn.LastName = lastName;
        spawn.OwnerId = ownerId;
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

        // ZoneEntry messages do not contain the zone-id
        ZoneId zoneId = metadata.Session.ZoneId;
        string zoneName = ZoneRepository.Instance.GetZoneName(zoneId);

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry:  No RootGate", LogLevel.Error);
                root.Text = "ZoneEntry";
                return root;
            }

            if (_extractor.IsPresent(_quadsGate_Slot) == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HandleZoneEntry: no quads gate present", LogLevel.Warn);
                root.Text = "ZoneEntry";
                return root;
            }
            GateHandle quadsGate = _extractor.GetGateAt(_quadsGate_Slot);
            if (! quadsGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HandleZoneEntry: quads gate slot present but not gate", LogLevel.Warn);
                root.Text = "ZoneEntry";
                return root;
            }

            SpawnId spawnId = (SpawnId) _extractor.GetUIntAt(_spawnId_Slot);
            String spawnName = ZoneRepository.Instance.LookupSpawnName(zoneId, spawnId);

            FieldNodes.AddLabeledNode(_extractor, _spawnId_Slot, "Spawn: " + spawnName + " (" +
                    spawnId + ", 0x" + spawnId.Value.ToString("X4") + ")", root);
            FieldNodes.AddUIntNode(_extractor, _id_Slot, "Persistent ID", root, "X");

            string name = FieldNodes.AddStringNode(_extractor, _name_Slot, "Name", root);
            FieldNodes.AddUIntNode(_extractor, _level_Slot, "Level", root, "D");

            uint modelId = _extractor.GetUIntAt(_model_Slot);
            string ModelName = DbStringGateway.Instance.LookupString(DbStringType.ModelName, modelId);
            FieldNodes.AddLabeledNode(_extractor, _model_Slot, "Model: " + ModelName + " (" + modelId + ")", root);

            FieldNodes.AddUIntNode(_extractor, _class_Slot, "Class", root, "D");
            FieldNodes.AddUIntNode(_extractor, _isPlayer_Slot, "Player", root, "D");
            FieldNodes.AddFloatNode(_extractor, _unknown_1_Slot, "Unknown 1", root, "F2");
            FieldNodes.AddUIntNode(_extractor, _unknown_2_Slot, "Unknown 2", root, "?");
            FieldNodes.AddUIntNode(_extractor, _C_Slot, "C", root, "D");

            uint bagCount = _extractor.BagCount(quadsGate);
            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                _extractor.EnterGate(quadsGate, bagIndex);
                FieldDisplayNode quadNode = new FieldDisplayNode("Quad " + (bagIndex + 1));
                root.AddChild(quadNode);

                FieldNodes.AddUIntNode(_extractor, _quad_Unknown_Slot, "Unknown", quadNode, "?");
            }
            _extractor.EnterGate(rootGate, 0);
            FieldNodes.AddUIntNode(_extractor, _A_Slot, "A", root, "D");
            FieldNodes.AddStringNode(_extractor, _lastname_Slot, "Lastname", root);
            FieldNodes.AddUIntNode(_extractor, _ownerId_Slot, "Owner Spawn ID", root);
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "ZoneEntry to " + zoneName;
        return root;
    }
    
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // BaseNameOf
    //
    // Strips the trailing instance digits from a spawn name, so every instance of one mob type
    // yields the same string.  A name with no trailing digits is returned unchanged.
    //
    // name:  The spawn name as sent in the packet.
    //
    // Returns:   The name without its trailing digits.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private static string BaseNameOf(string name)
    {
        int end = name.Length;
        while ((end > 0) && char.IsDigit(name[end - 1]))
        {
            end--;
        }

        if (end == name.Length)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry_Z2C.BaseNameOf: '" + name
                + "' has no trailing digits.", LogLevel.Trace);
            return name;
        }

        return name.Substring(0, end);
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