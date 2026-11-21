using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.World;
using System;
using System.Buffers.Binary;
using System.Printing;
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
    private readonly SlotId _unknown_23A_Slot;
    private readonly SlotId _unknown_11D4_Slot;
    private readonly SlotId _characterId_Slot;
    private readonly SlotId _type_Slot;
    private readonly SlotId _bitmap_Slot;
    private readonly SlotId _presenceFlags_Slot;
    private readonly SlotId _unknown_27A_Slot;
    private readonly SlotId _unknown_13D4_Slot;
    private readonly SlotId _numQuads_Slot;
    private readonly SlotId _quadsGate_Slot;
    private readonly SlotId _unknown_4E_Slot;
    private readonly SlotId _unknown_FC5_Slot;
    private readonly SlotId _unknown_FC6_Slot;
    private readonly SlotId _unknown_FC7_Slot;
    private readonly SlotId _unknown_1F9_Slot;
    private readonly SlotId _unknown_FC9_Slot;
    private readonly SlotId _unknown_FCA_Slot;
    private readonly SlotId _unknown_1044_Slot;
    private readonly SlotId _unknown_209_Slot;
    private readonly SlotId _unknown_104C_Slot;
    private readonly SlotId _unknown_1F8_Slot;
    private readonly SlotId _unknown_FC1_Slot;
    private readonly SlotId _unknown_FC2_Slot;
    private readonly SlotId _unknown_FC3_Slot;
    private readonly SlotId _spawnSize_Slot;
    private readonly SlotId _unknown_FC4_Slot;
    private readonly SlotId _walkSpeed_Slot;
    private readonly SlotId _runSpeed_Slot;
    private readonly SlotId _model_Slot;
    private readonly SlotId _unknown_255_Slot;
    private readonly SlotId _unknown_91_Slot;
    private readonly SlotId _unknown_bb_Slot;
    private readonly SlotId _class_Slot;
    private readonly SlotId _unknown_9F_Slot;
    private readonly SlotId _unknown_bA_Slot;
    private readonly SlotId _unknown_9C_Slot;
    private readonly SlotId _unknown_409_Slot;
    private readonly SlotId _lastname_Slot;

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
        _unknown_23A_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_23A");
        _unknown_11D4_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_11D4");
        _characterId_Slot = _registry.IndexOfField(_collectionHandle, "character_id");
        _type_Slot = _registry.IndexOfField(_collectionHandle, "Type");
        _bitmap_Slot = _registry.IndexOfField(_collectionHandle, "Bitmap");
        _unknown_27A_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_27A");
        _unknown_13D4_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_13D4");
        _numQuads_Slot = _registry.IndexOfField(_collectionHandle, "NumQuads");
        _unknown_FC5_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_FC5");
        _unknown_FC6_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_FC6");
        _unknown_1F9_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_1F9");
        _unknown_FC7_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_FC7");
        _unknown_FC9_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_FC9");
        _unknown_FCA_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_FCA");
        _unknown_1044_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_1044");
        _unknown_209_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_209");
        _unknown_104C_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_104C");
        _unknown_1F8_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_1F8");
        _unknown_FC2_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_FC2");
        _unknown_FC3_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_FC3");
        _spawnSize_Slot = _registry.IndexOfField(_collectionHandle, "Spawn_Size");
        _walkSpeed_Slot = _registry.IndexOfField(_collectionHandle, "WalkSpeed");
        _runSpeed_Slot = _registry.IndexOfField(_collectionHandle, "RunSpeed");
        _model_Slot = _registry.IndexOfField(_collectionHandle, "Model");
        _unknown_255_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_255");
        _unknown_bb_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_bb");
        _class_Slot = _registry.IndexOfField(_collectionHandle, "Class");
        _unknown_9F_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_9F");
        _unknown_bA_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_bA");
        _unknown_9C_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_9C");
        _unknown_409_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_409");
        _lastname_Slot = _registry.IndexOfField(_collectionHandle, "LastName");


        // Quads slots
        _quad_Unknown_Slot = _registry.IndexOfField(quadCollection, "Unknown");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Processes zone-to-client OP_ZoneEntry.  A spawn whose Type is above 2 (the known values
    // are 0 player, 1 NPC, 2 corpse) is written to the Specimens channel with its whole payload
    // in hex, then processed as usual.  Owned spawns are recorded as companions.  The receiving
    // character's own zone entries are counted on its session, and the character is placed in
    // the zone under the spawn id of the second one; zone entries for any other player are not
    // used.  Only spawns of Type 1 are recorded as mobs, under the receiving character's zone:
    // the mob type is looked up or created by zone and base name, and the spawn record is
    // created on first sight and refreshed with the decoded field values on every sight.
    // A spawn first seen after the receiving session is established is marked as a spawn point.
    // Note that spawns in dynamic zones are not recorded.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        const uint SpawnTypeNpc = 1;   // 0 is a player, 2 is a corpse
        const uint SpawnTypeHighestKnown = 2;

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
        ulong characterId;
        uint model;
        uint mobClass;
        uint type;
        float unknown27A;
        uint unknown13D4;
        uint unknown23A;
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
            characterId = _extractor.GetUInt64At(_characterId_Slot);

            model = _extractor.GetUIntAt(_model_Slot);
            mobClass = _extractor.GetUIntAt(_class_Slot);

            type = _extractor.GetUIntAt(_type_Slot);
            unknown27A = _extractor.GetFloatAt(_unknown_27A_Slot);
            unknown13D4 = _extractor.GetUIntAt(_unknown_13D4_Slot);
            unknown23A = _extractor.GetUIntAt(_unknown_23A_Slot);
            C = _extractor.GetUIntAt(_numQuads_Slot);
            lastName = _extractor.GetStringAt(_lastname_Slot);
            ownerId = 0;
        }
        finally
        {
            _extractor.Release();
        }

        // Keep any spawn of an unknown type for later inspection
        if (type > SpawnTypeHighestKnown)
        {
            DebugLog.Write(LogChannel.Specimens, "HandleZoneEntry_Z2C: unknown Type " + type + " at "
                + metadata.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff") + "Z, message " + metadata.MessageId
                + ", receiver '" + character.Name + "', zoneId=" + zoneId + ", spawnId=0x"
                + spawnId.ToString("X4") + ", name '" + name + "', " + data.Length + " bytes: "
                + Convert.ToHexString(data), LogLevel.Warn);
        }

        // Owned spawns are companions, not mobs
        if (ownerId != 0)
        {
            SpawnId companionSpawnId = (SpawnId)spawnId;
            if (CompanionRepository.Instance.TryGetCompanion(zoneId, companionSpawnId, out Companion? companion) == false)
            {
                companion = new Companion();
                companion.ZoneId = zoneId;
                companion.SpawnId = companionSpawnId;
                if (CompanionRepository.Instance.AddCompanion(companion) == false)
                {
                    DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry_Z2C: companion '" + name + "' spawnId=0x"
                        + spawnId.ToString("X4") + " not stored.", LogLevel.Error);
                    return;
                }
            }

            companion.Name = name;
            companion.Level = (ushort)level;
            companion.Class = mobClass;
            companion.Model = model;
            companion.OwnerSpawnId = (SpawnId)ownerId;

            Character? owner = CharacterRepository.Instance.GetBySpawnId(zoneId, companion.OwnerSpawnId);
            if (owner != null)
            {
                companion.Owner = owner;
                companion.OwnerId = owner.CharacterId;
            }

            DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry_Z2C: companion '" + name + "' spawnId=0x"
                + spawnId.ToString("X4") + " owner spawnId=0x" + ownerId.ToString("X4") + " owner "
                + (owner != null ? owner.Name : "not one of ours") + ".", LogLevel.Trace);
            return;
        }

        // A nonzero persistent id is a player.  Only the receiving character's own zone entries
        // are used: the second one on its session carries its spawn id in this zone.
        if (characterId != 0)
        {
            if (character.CharacterId != (CharacterId)characterId)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry_Z2C: '" + name + "' spawnId=0x"
                    + spawnId.ToString("X4") + " is not the receiving character '" + character.Name
                    + "'; not placed.", LogLevel.Trace);
                return;
            }

            uint zoneEntryCount = metadata.Session.CountOwnZoneEntry();
            if (zoneEntryCount == 2)
            {
                CharacterRepository.Instance.SetZoneSpawn((SpawnId)spawnId, metadata);
                DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry_Z2C: placed '" + character.Name + "' in zoneId="
                    + zoneId + " spawnId=0x" + spawnId.ToString("X4"), LogLevel.Trace);

                uint linked = CompanionRepository.Instance.LinkOwner(zoneId, character);
                DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry_Z2C: linked " + linked + " companions to '"
                    + character.Name + "'", LogLevel.Trace);
            }
            else if (zoneEntryCount > 2)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry_Z2C: own zone entry " + zoneEntryCount + " for '"
                    + character.Name + "' spawnId=0x" + spawnId.ToString("X4") + "; not placed.", LogLevel.Warn);
            }
            else
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry_Z2C: own zone entry " + zoneEntryCount + " for '"
                    + character.Name + "' spawnId=0x" + spawnId.ToString("X4") + " is provisional; not placed.",
                    LogLevel.Trace);
            }
            return;
        }

        // Only NPCs are recorded as mobs; players and corpses are not
        if (type != SpawnTypeNpc)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry_Z2C: '" + name + "' spawnId=0x"
                + spawnId.ToString("X4") + " has Type " + type + ", not an NPC; not recorded.", LogLevel.Trace);
            return;
        }

        // Dynamic zones are not recorded
        if (ZoneRepository.IsDynamicZone(zoneId))
        {
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
            spawn.IsSpawnPoint = metadata.Session.Established;
            MobRepository.Instance.AddSpawn(spawn);
            DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry_Z2C: created new record for " + name + ", zoneId=" + zoneId
                + " spawnId=0x" + spawnId.ToString("X4") + " mobId=" + mobType.Id + ".", LogLevel.Trace);

            if (spawn.IsSpawnPoint)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry_Z2C: '" + name + "' spawnId=0x"
                    + spawnId.ToString("X4") + " appeared after session " + metadata.Session.SessionId
                    + " was established; marked as a spawn point.", LogLevel.Trace);
            }
        }

        // We have the spawn by reference from above, so fill in the data
        spawn.Name = name;
        spawn.Level = (ushort)level;
        spawn.CharacterId = characterId;
        spawn.Type = type;
        spawn.Unknown1 = unknown27A;
        spawn.Unknown2 = unknown13D4;
        spawn.Unknown3 = unknown23A;
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
        string spawnName = "Unknown";

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
            spawnName = ZoneRepository.Instance.LookupSpawnName(zoneId, spawnId);

            FieldNodes.AddLabeledNode(_extractor, _spawnId_Slot, "Spawn: " + spawnName + " (" +
                    spawnId + ", 0x" + spawnId.Value.ToString("X4") + ")", root);
            FieldNodes.AddUInt64Node(_extractor, _characterId_Slot, "Character ID", root, "X");

            string name = FieldNodes.AddStringNode(_extractor, _name_Slot, "Name", root);
            FieldNodes.AddUIntNode(_extractor, _level_Slot, "Level", root, "D");

            uint modelId = _extractor.GetUIntAt(_model_Slot);
            string ModelName = DbStringGateway.Instance.LookupString(DbStringType.ModelName, modelId);
            FieldNodes.AddLabeledNode(_extractor, _model_Slot, "Model: " + ModelName + " (" + modelId + ")", root);

            FieldNodes.AddUIntNode(_extractor, _class_Slot, "Class", root, "D");

            FieldNodes.AddUIntNode(_extractor, _type_Slot, "Type", root, "D");
            FieldNodes.AddUIntNode(_extractor, _bitmap_Slot, "Bitmap", root, "X8");
            FieldNodes.AddFloatNode(_extractor, _unknown_27A_Slot, "Unknown 27A", root, "F2");
            FieldNodes.AddUIntNode(_extractor, _unknown_13D4_Slot, "Unknown 13D4", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_23A_Slot, "Unknown 23A", root);
            FieldNodes.AddUIntNode(_extractor, _numQuads_Slot, "Num Quads", root, "D");
            FieldNodes.AddFloatNode(_extractor, _walkSpeed_Slot, "Walk Speed", root, "F2");
            FieldNodes.AddFloatNode(_extractor, _runSpeed_Slot, "Run Speed", root, "F2");

            uint bagCount = _extractor.BagCount(quadsGate);
            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                _extractor.EnterGate(quadsGate, bagIndex);
                FieldDisplayNode quadNode = new FieldDisplayNode("Quad " + (bagIndex + 1));
                root.AddChild(quadNode);

                FieldNodes.AddUIntNode(_extractor, _quad_Unknown_Slot, "Unknown", quadNode, "?");
            }
       //    FieldNodes.AddStringNode(_extractor, _lastname_Slot, "Lastname", root);

            _extractor.EnterGate(rootGate, 0);
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "ZoneEntry: " + spawnName + " to " + zoneName;
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