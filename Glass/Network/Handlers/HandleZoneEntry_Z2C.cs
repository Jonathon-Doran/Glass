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
    private readonly SlotId _guildID_Slot;
    private readonly SlotId _class_Slot;
    private readonly SlotId _unknown_9F_Slot;
    private readonly SlotId _unknown_bA_Slot;
    private readonly SlotId _unknown_9C_Slot;
    private readonly SlotId _unknown_409_Slot;
    private readonly SlotId _lastname_Slot;
    private readonly SlotId _unknown_20AC_Slot;
    private readonly SlotId _unknown_22A_Slot;
    private readonly SlotId _petOwner_Slot;
    private readonly SlotId _unknown_93_Slot;
    private readonly SlotId _unknown_16C_Slot;
    private readonly SlotId _unknown_C2_Slot;
    private readonly SlotId _unknown_4F_Slot;
    private readonly SlotId _unknown_3b_Slot;
    private readonly SlotId _unknown_38_Slot;
    private readonly SlotId _unknown_37_Slot;
    private readonly SlotId _mob12_Slot;
    private readonly SlotId _unknown_FF_Block_1_Slot;
    private readonly SlotId _unknown_FF_Block_2_Slot;
    private readonly SlotId _colorTable_Slot;
    private readonly SlotId _appearance_0_Slot;
    private readonly SlotId _appearance_1_Slot;
    private readonly SlotId _appearance_2_Slot;
    private readonly SlotId _appearance_3_Slot;
    private readonly SlotId _appearance_4_Slot;
    private readonly SlotId _appearance_5_Slot;
    private readonly SlotId _appearance_6_Slot;
    private readonly SlotId _appearance_7_Slot;
    private readonly SlotId _appearance_8_Slot;
    private readonly SlotId _24ByteBlock_Slot;
    private readonly SlotId _titlePrefix_Slot;
    private readonly SlotId _titleSuffix_Slot;
    private readonly SlotId _unknown_To730_Slot;
    private readonly SlotId _unknown_To68_Slot;
    private readonly SlotId _unknown_3b4_Slot;
    private readonly SlotId _unknown_118_Slot;
    private readonly SlotId _unknown_C5_Slot;
    private readonly SlotId _unknown_90_Slot;
    private readonly SlotId _activeMercenary_Slot;
    private readonly SlotId _mercenaryOwner_Slot;
    private readonly SlotId _birthday_Slot;
    private readonly SlotId _unknown_264_Slot;
    private readonly SlotId _unknown_Counter_Slot;
    private readonly SlotId _variableQuadsGate_Slot;

    // slots for Gates
    private readonly SlotId _quad_Unknown_Slot;
    private readonly SlotId _variableQuads_Slot;

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
        _variableQuadsGate_Slot = _registry.IndexOfField(_collectionHandle, "Variable_Quads");
        CollectionHandle quadCollection = _registry.GetCollectionHandle(_patchLevel, "ZoneEntry_Quads");
        CollectionHandle variableQuadsCollect = _registry.GetCollectionHandle(_patchLevel, "ZoneEntry_4Quads");

        // Root collection fields
        _name_Slot = _registry.IndexOfField(_collectionHandle, "name");
        _spawnId_Slot = _registry.IndexOfField(_collectionHandle, "spawn_id");
        _level_Slot = _registry.IndexOfField(_collectionHandle, "level");
        _unknown_23A_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_23A");
        _unknown_11D4_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_11D4");
        _characterId_Slot = _registry.IndexOfField(_collectionHandle, "character_id");
        _type_Slot = _registry.IndexOfField(_collectionHandle, "Type");
        _bitmap_Slot = _registry.IndexOfField(_collectionHandle, "Bitmap");
        _presenceFlags_Slot = _registry.IndexOfField(_collectionHandle, "PresenceFlags");
        _unknown_27A_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_27A");
        _unknown_13D4_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_13D4");
        _numQuads_Slot = _registry.IndexOfField(_collectionHandle, "NumQuads");
        _unknown_4E_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_4E");
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
        _unknown_FC1_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_FC1");
        _unknown_FC2_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_FC2");
        _unknown_FC3_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_FC3");
        _unknown_FC4_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_FC4");
        _spawnSize_Slot = _registry.IndexOfField(_collectionHandle, "Spawn_Size");
        _walkSpeed_Slot = _registry.IndexOfField(_collectionHandle, "WalkSpeed");
        _runSpeed_Slot = _registry.IndexOfField(_collectionHandle, "RunSpeed");
        _model_Slot = _registry.IndexOfField(_collectionHandle, "Model");
        _unknown_255_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_255");
        _unknown_91_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_91");
        _guildID_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_bb");
        _class_Slot = _registry.IndexOfField(_collectionHandle, "Class");
        _unknown_9F_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_9F");
        _unknown_bA_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_bA");
        _unknown_9C_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_9C");
        _unknown_409_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_409");
        _lastname_Slot = _registry.IndexOfField(_collectionHandle, "LastName");
        _unknown_20AC_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_20AC");
        _unknown_22A_Slot = _registry.IndexOfField(_collectionHandle, "Boolean_22A");
        _petOwner_Slot = _registry.IndexOfField(_collectionHandle, "Pet_Owner");
        _unknown_93_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_93");
        _unknown_16C_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_16C");
        _unknown_C2_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_C2");
        _unknown_4F_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_4F");
        _unknown_3b_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_3b");
        _unknown_38_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_38");
        _unknown_37_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_37");
        _mob12_Slot = _registry.IndexOfField(_collectionHandle, "Mob12");
        _unknown_FF_Block_1_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_FF_Block_1");
        _unknown_FF_Block_2_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_FF_Block_2");
        _colorTable_Slot = _registry.IndexOfField(_collectionHandle, "ColorTable");
        _appearance_0_Slot = _registry.IndexOfField(_collectionHandle, "AppearanceSlot_0");
        _appearance_1_Slot = _registry.IndexOfField(_collectionHandle, "AppearanceSlot_1");
        _appearance_2_Slot = _registry.IndexOfField(_collectionHandle, "AppearanceSlot_2");
        _appearance_3_Slot = _registry.IndexOfField(_collectionHandle, "AppearanceSlot_3");
        _appearance_4_Slot = _registry.IndexOfField(_collectionHandle, "AppearanceSlot_4");
        _appearance_5_Slot = _registry.IndexOfField(_collectionHandle, "AppearanceSlot_5");
        _appearance_6_Slot = _registry.IndexOfField(_collectionHandle, "AppearanceSlot_6");
        _appearance_7_Slot = _registry.IndexOfField(_collectionHandle, "AppearanceSlot_7");
        _appearance_8_Slot = _registry.IndexOfField(_collectionHandle, "AppearanceSlot_8");
        _24ByteBlock_Slot = _registry.IndexOfField(_collectionHandle, "24ByteBlock");
        _titlePrefix_Slot = _registry.IndexOfField(_collectionHandle, "Title_Prefix");
        _titleSuffix_Slot = _registry.IndexOfField(_collectionHandle, "Title_Suffix");
        _unknown_To730_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_To730");
        _unknown_To68_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_To68");
        _unknown_3b4_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_3b4");
        _unknown_118_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_118");
        _unknown_C5_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_C5");
        _unknown_90_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_90");
        _activeMercenary_Slot = _registry.IndexOfField(_collectionHandle, "Active_Mercenary");
        _mercenaryOwner_Slot = _registry.IndexOfField(_collectionHandle, "Mercenary_Owner");
        _birthday_Slot = _registry.IndexOfField(_collectionHandle, "Birthday");
        _unknown_264_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_264");
        _unknown_Counter_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_Counter");

        // Quads slots
        _quad_Unknown_Slot = _registry.IndexOfField(quadCollection, "Unknown");

        // 4Quads
        _variableQuads_Slot = _registry.IndexOfField(variableQuadsCollect, "Values");
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

            uint bitmap = _extractor.GetUIntAt(_bitmap_Slot);
            uint presence = _extractor.GetUIntAt(_presenceFlags_Slot);


            DebugLog.Write(LogChannel.DataLogging, "Flags:  message " + metadata.MessageId + "," 
                + metadata.Timestamp.ToString("HH:mm:ss.fff") + "," + name + "," +
                " Bitmap: 0x" + bitmap.ToString("X8") + ", Presence: 0x" +
                presence.ToString("X2") + ", Model: 0x" + model.ToString("X4") +
                ", Type: 0x" + type.ToString("X2"), LogLevel.Info);

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
    // Extracts OP_ZoneEntry against the active patch and builds a display tree: a root node for
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
                    "HandleZoneEntry: quads gate slot present but not gate", LogLevel.Error);
                root.Text = "ZoneEntry";
                return root;
            }


            SpawnId spawnId = (SpawnId) _extractor.GetUIntAt(_spawnId_Slot);
            EQClass classId = (EQClass)_extractor.GetUIntAt(_class_Slot);

            spawnName = ZoneRepository.Instance.LookupSpawnName(zoneId, spawnId);

            string name = FieldNodes.AddStringNode(_extractor, _name_Slot, "Name", root);
            FieldNodes.AddStringNode(_extractor, _lastname_Slot, "Lastname", root);
            FieldNodes.AddUIntNode(_extractor, _level_Slot, "Level", root, "D");

            FieldNodes.AddLabeledNode(_extractor, _class_Slot, "Class: " + classId.ToDisplayString() + " (0x" +
                classId.ToString("X") + ")", root);

            FieldNodes.AddLabeledNode(_extractor, _spawnId_Slot, "Entity: " + spawnName + " (" +
                    spawnId + ", 0x" + spawnId.Value.ToString("X4") + ")", root);
            FieldNodes.AddUInt64Node(_extractor, _characterId_Slot, "Character ID", root, "X");
            FieldNodes.AddUInt64Node(_extractor, _guildID_Slot, "Guild ID", root);
            FieldNodes.AddFloatNode(_extractor, _spawnSize_Slot, "Spawn Size", root, "F2");
            FieldNodes.AddFloatNode(_extractor, _walkSpeed_Slot, "Walk Speed", root, "F2");
            FieldNodes.AddFloatNode(_extractor, _runSpeed_Slot, "Run Speed", root, "F2");

            uint modelId = _extractor.GetUIntAt(_model_Slot);
            string ModelName = DbStringGateway.Instance.LookupString(DbStringType.ModelName, modelId);
            FieldNodes.AddLabeledNode(_extractor, _model_Slot, "Model: " + ModelName + " (" + modelId + ")", root);

            FieldNodes.AddUIntNode(_extractor, _type_Slot, "Type", root, "D");
            FieldNodes.AddBinaryNode(_extractor, _bitmap_Slot, "Bitmap", root);
            FieldNodes.AddBinaryNode(_extractor, _presenceFlags_Slot, "Presence", root);
            FieldNodes.AddUIntNode(_extractor, _petOwner_Slot, "Pet Owner", root);
            FieldNodes.AddUIntNode(_extractor, _mercenaryOwner_Slot, "Mercenary Owner", root);
            FieldNodes.AddUIntNode(_extractor, _activeMercenary_Slot, "Active Mercenary", root);
            FieldNodes.AddUIntNode(_extractor, _birthday_Slot, "Birthday", root);
            if (_extractor.IsPresent(_titlePrefix_Slot) == true)
            {
                FieldNodes.AddStringNode(_extractor, _titlePrefix_Slot, "Title Prefix", root);
            }
            else
            {
                FieldNodes.AddLabeledNode(_extractor, _titlePrefix_Slot, "Title Prefix: None", root);
            }
            if (_extractor.IsPresent(_titleSuffix_Slot) == true)
            {
                FieldNodes.AddStringNode(_extractor, _titleSuffix_Slot, "Title Suffix", root);
            }
            else
            {
                FieldNodes.AddLabeledNode(_extractor, _titleSuffix_Slot, "Title Suffix: None", root);
            }

            // The various Unknown fields
            FieldNodes.AddUIntNode(_extractor, _unknown_23A_Slot, "Unknown 23A", root);
            FieldNodes.AddFloatNode(_extractor, _unknown_11D4_Slot, "Unknown 11D4", root);
            FieldNodes.AddFloatNode(_extractor, _unknown_27A_Slot, "Unknown 27A", root, "F2");
            FieldNodes.AddUIntNode(_extractor, _unknown_13D4_Slot, "Unknown 13D4", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_4E_Slot, "Unknown 4E", root, "?");

            FieldNodes.AddUIntNode(_extractor, _unknown_FC1_Slot, "Unknown FC1", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_FC2_Slot, "Unknown FC2", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_FC3_Slot, "Unknown FC3", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_FC4_Slot, "Unknown FC4", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_FC5_Slot, "Unknown FC5", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_FC6_Slot, "Unknown FC6", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_FC7_Slot, "Unknown FC7", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_FC9_Slot, "Unknown FC9", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_FCA_Slot, "Unknown FCA", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_1F8_Slot, "Unknown 1F8", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_1F9_Slot, "Unknown 1F9", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_1044_Slot, "Unknown 1044", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_209_Slot, "Unknown 209", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_104C_Slot, "Unknown 104C", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_255_Slot, "Unknown 255", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_91_Slot, "Unknown 91", root, "?");

            FieldNodes.AddUIntNode(_extractor, _unknown_9F_Slot, "Unknown 9F", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_bA_Slot, "Unknown bA", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_9C_Slot, "Unknown 9C", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_409_Slot, "Unknown 409", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_20AC_Slot, "Unknown 20AC", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_22A_Slot, "Boolean 22A", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_93_Slot, "Unknown 93", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_16C_Slot, "Unknown 16C", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_C2_Slot, "Unknown C2", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_4F_Slot, "Unknown 4F", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_3b_Slot, "Unknown 3b", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_38_Slot, "Unknown 38", root, "?");
            FieldNodes.AddUInt64Node(_extractor, _unknown_37_Slot, "Unknown 37", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_FF_Block_1_Slot, "Unknown FF Block1", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_FF_Block_2_Slot, "Unknown FF Block2", root, "?");

            FieldNodes.AddUIntNode(_extractor, _unknown_To730_Slot, "Unknown To730", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_To68_Slot, "Unknown To68", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_3b4_Slot, "Unknown 3b4", root, "?");
            FieldNodes.AddStringNode(_extractor, _unknown_118_Slot, "Unknown 118", root);
            FieldNodes.AddIntNode(_extractor, _unknown_C5_Slot, "Unknown C5", root, "?");
            FieldNodes.AddIntNode(_extractor, _unknown_90_Slot, "Unknown 90", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknown_264_Slot, "Unknown 264", root, "?");

            FieldNodes.AddUIntNode(_extractor, _numQuads_Slot, "Num Quads", root, "D");
            uint bagCount = _extractor.BagCount(quadsGate);
            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                _extractor.EnterGate(quadsGate, bagIndex);
                FieldDisplayNode quadNode = new FieldDisplayNode("Quad " + (bagIndex + 1));
                root.AddChild(quadNode);

                FieldNodes.AddUIntNode(_extractor, _quad_Unknown_Slot, "Unknown", quadNode, "?");
                _extractor.EnterGate(rootGate, 0);
            }

            if (_extractor.IsPresent(_mob12_Slot) == true)
            {
                FieldDisplayNode mobDataSubtree = new FieldDisplayNode("Mob Data");
                root.AddChild(mobDataSubtree);

                ReadOnlySpan<uint> unknownMobData = _extractor.GetUIntSpanAt(_mob12_Slot);
                for (int index = 0; index < unknownMobData.Length; index++)
                {
                    FieldNodes.AddLabeledNode(_extractor, _mob12_Slot, "Value " + index + ": 0x" + unknownMobData[index].ToString("X8"),
                        mobDataSubtree);
                }
            }

            if (_extractor.IsPresent(_colorTable_Slot) == true)
            {
                FieldDisplayNode colorSubtree = new FieldDisplayNode("Visible Slot Colors");
                root.AddChild(colorSubtree);

                ReadOnlySpan<uint> colorData = _extractor.GetUIntSpanAt(_colorTable_Slot);
                for (int index = 0; index < colorData.Length; index++)
                {
                    FieldNodes.AddLabeledNode(_extractor, _colorTable_Slot, "Value " + index + ": 0x" + colorData[index].ToString("X8"),
                        colorSubtree);
                }
            }


            FieldDisplayNode appearanceTree = new FieldDisplayNode("Appearance Table");

            uint appearanceSlotsFound = 0;
            if (_extractor.IsPresent(_appearance_0_Slot) == true)
            {
                Describe_AppearanceSlot(_appearance_0_Slot, "Slot 0", appearanceTree);
                appearanceSlotsFound++;
            }
            if (_extractor.IsPresent(_appearance_1_Slot) == true)
            {
                Describe_AppearanceSlot(_appearance_1_Slot, "Slot 1", appearanceTree);
                appearanceSlotsFound++;
            }
            if (_extractor.IsPresent(_appearance_2_Slot) == true)
            {
                Describe_AppearanceSlot(_appearance_2_Slot, "Slot 2", appearanceTree);
                appearanceSlotsFound++;
            }
            if (_extractor.IsPresent(_appearance_3_Slot) == true)
            {
                Describe_AppearanceSlot(_appearance_3_Slot, "Slot 3", appearanceTree);
                appearanceSlotsFound++;
            }
            if (_extractor.IsPresent(_appearance_4_Slot) == true)
            {
                Describe_AppearanceSlot(_appearance_4_Slot, "Slot 4", appearanceTree);
                appearanceSlotsFound++;
            }
            if (_extractor.IsPresent(_appearance_5_Slot) == true)
            {
                Describe_AppearanceSlot(_appearance_5_Slot, "Slot 5", appearanceTree);
                appearanceSlotsFound++;
            }
            if (_extractor.IsPresent(_appearance_6_Slot) == true)
            {
                Describe_AppearanceSlot(_appearance_6_Slot, "Slot 6", appearanceTree);
                appearanceSlotsFound++;
            }
            if (_extractor.IsPresent(_appearance_7_Slot) == true)
            {
                Describe_AppearanceSlot(_appearance_7_Slot, "Slot 7", appearanceTree);
                appearanceSlotsFound++;
            }
            if (_extractor.IsPresent(_appearance_8_Slot) == true)
            {
                Describe_AppearanceSlot(_appearance_8_Slot, "Slot 8", appearanceTree);
                appearanceSlotsFound++;
            }
            if (appearanceSlotsFound > 0)
            {
                root.AddChild(appearanceTree);
            }

            if (_extractor.IsPresent(_24ByteBlock_Slot) == true)
            {
                FieldDisplayNode subtree = new FieldDisplayNode("24 Byte Block");
                root.AddChild(subtree);

                ReadOnlySpan<uint> blockData = _extractor.GetUIntSpanAt(_24ByteBlock_Slot);
                for (int index = 0; index < blockData.Length; index++)
                {
                    FieldNodes.AddLabeledNode(_extractor, _24ByteBlock_Slot, "Value " + index + ": 0x" + blockData[index].ToString("X8"),
                        subtree);
                }
            }

            FieldNodes.AddUIntNode(_extractor, _unknown_Counter_Slot, "Counter", root, "D");

            if (_extractor.IsPresent(_variableQuadsGate_Slot) == true)
            {

                GateHandle varQuadsGate = _extractor.GetGateAt(_variableQuadsGate_Slot);
                uint numRecords = _extractor.BagCount(varQuadsGate);


                // only create subtree if we have bags
                if (bagCount > 0)
                {
                    FieldDisplayNode varQuadsTree = new FieldDisplayNode("Variable Quads");
                    root.AddChild(varQuadsTree);

                    for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
                    {
                        _extractor.EnterGate(varQuadsGate, bagIndex);

                        ReadOnlySpan<uint> blockData = _extractor.GetUIntSpanAt(_variableQuads_Slot);
                        for (int index = 0; index < blockData.Length; index++)
                        {
                            FieldNodes.AddLabeledNode(_extractor, _variableQuads_Slot, "Value " + index + ": 0x" + blockData[index].ToString("X8"),
                                varQuadsTree);
                        }
                    }
                    _extractor.EnterGate(rootGate, 0);
                }

            }
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "ZoneEntry: " + spawnName + " to " + zoneName;
        return root;
    }
    
   void Describe_AppearanceSlot(SlotId slot, string label, FieldDisplayNode parent)
    {
        FieldDisplayNode subtree = new FieldDisplayNode(label);
        parent.AddChild(subtree);

        ReadOnlySpan<uint> slotData = _extractor.GetUIntSpanAt(slot);
        for (int index = 0; index < slotData.Length; index++)
        {
            FieldNodes.AddLabeledNode(_extractor, _colorTable_Slot, "Value " + index + ": 0x" + slotData[index].ToString("X4"),
                subtree);
        }
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