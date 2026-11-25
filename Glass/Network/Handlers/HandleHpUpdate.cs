using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.World;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleHpUpdate
//
// Handles OP_HpUpdate messages.  
//
// These come in for players with HP values that change, and are not sent unless needed.
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleHpUpdate : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _playerIdSlot;
    private readonly SlotId _currentHPSlot;
    private readonly SlotId _maxHPSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleHpUpdate (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleHpUpdate(PatchLevel patchLevel)
        :base(patchLevel, "OP_HpUpdate")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel,  _opcodeName);
        if (_opcodeHandled == PatchOpcode.None)
        {
            // happens when replaying captures before the opcode was added
            return;
        }

        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_HpUpdate");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _playerIdSlot = _registry.IndexOfField(_collectionHandle, "player_id");
        _currentHPSlot = _registry.IndexOfField(_collectionHandle, "current_hp");
        _maxHPSlot = _registry.IndexOfField(_collectionHandle, "max_hp");
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

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HandleZoneToClient
    //
    // Stores the current and maximum HP from an HP update into the entity the update concerns,
    // found by its spawn id in the session's zone.  When that entity is the session's own
    // character, the update omits the worn HP bonus, so the bonus is added to both values
    // before storing; any other entity's values are stored as received.
    //
    // data:      The payload of the update.
    // metadata:  The packet's metadata, carrying its session and receiving character.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            SpawnId spawnId = (SpawnId)_extractor.GetUIntAt(_playerIdSlot);
            ulong wireMaxHP = _extractor.GetUInt64At(_maxHPSlot);
            long wireCurrentHP = _extractor.GetInt64At(_currentHPSlot);

            if (metadata.Session == null)
            {
                DebugLog.Write(LogChannel.Opcodes, _opcodeName + ".HandleZoneToClient: no session for spawnId="
                    + spawnId + "; fields not stored", LogLevel.Warn);
                return;
            }

            Entity? entity = ZoneRepository.Instance.LookupEntity(metadata.Session.ZoneId, spawnId);
            if (entity == null)
            {
                DebugLog.Write(LogChannel.Opcodes, _opcodeName + ".HandleZoneToClient: message " + metadata.MessageId
                    + " to '" + metadata.CharacterName + "' names spawnId=" + spawnId + " (0x"
                    + spawnId.Value.ToString("X4") + ") in zoneId=" + metadata.Session.ZoneId
                    + ", which matches no entity; fields not stored", LogLevel.Warn);
                return;
            }

            if (ReferenceEquals(entity, metadata.Character) == false)
            {
                entity.MaxHP = wireMaxHP;
                entity.CurrentHP = wireCurrentHP;
                DebugLog.Write(LogChannel.Opcodes, _opcodeName + ".HandleZoneToClient: '" + entity.Name
                    + "' entityId=" + entity.EntityId + " reported to '" + metadata.CharacterName
                    + "', stored as received: current " + wireCurrentHP + ", max " + wireMaxHP, LogLevel.Trace);
                return;
            }

            Character character = (Character)entity;

            /*
            DebugLog.Write(LogChannel.DataLogging, "HP," + metadata.MessageId + ","
                + metadata.Timestamp.ToString("HH:mm:ss.fff") + "," + character.Name + ","
                + metadata.CharacterName + ",obs," + wireCurrentHP + "," + character.CurrentHP, LogLevel.Info);
            */
            int wornHP = character.SumWornBonuses().HP;
            if (wornHP < 0)
            {
                DebugLog.Write(LogChannel.Opcodes, _opcodeName + ".HandleZoneToClient: '" + character.Name
                    + "' has negative worn HP bonus " + wornHP + "; fields not stored", LogLevel.Warn);
                return;
            }

            character.MaxHP = wireMaxHP + (uint)wornHP;
            character.CurrentHP = wireCurrentHP + wornHP;
            DebugLog.Write(LogChannel.Opcodes, _opcodeName + ".HandleZoneToClient: '" + character.Name
                + "' reported to itself, worn HP bonus " + wornHP + " added: current " + character.CurrentHP
                + ", max " + character.MaxHP, LogLevel.Trace);
            /*
            DebugLog.Write(LogChannel.DataLogging, "HP," + metadata.MessageId + ","
                + metadata.Timestamp.ToString("HH:mm:ss.fff") + "," + character.Name + ","
                + metadata.CharacterName + ",self," + wireCurrentHP + "," + character.CurrentHP, LogLevel.Info);
            */
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_HpUpdate against the active patch and builds a display tree: a root node for
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
        string characterName = "Unknown";
        long currentHP = 0;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            SpawnId playerId = (SpawnId) _extractor.GetUIntAt(_playerIdSlot);
            characterName = ZoneRepository.Instance.LookupSpawnName(zoneId, playerId);

            currentHP = _extractor.GetInt64At(_currentHPSlot);
            ulong maxHP = _extractor.GetUInt64At(_maxHPSlot);

            FieldNodes.AddLabeledNode(_extractor, _playerIdSlot, "Spawn: " + characterName + " (" +
                    playerId + ", 0x" + playerId.Value.ToString("X4") + ")", root);

            FieldNodes.AddInt64Node(_extractor, _currentHPSlot, "Current HP", root, "D");
            FieldNodes.AddUInt64Node(_extractor, _maxHPSlot, "Max HP", root, "D");
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "HP Update (" + characterName + "=" + currentHP + ")";
        return root;
    }
}