using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;
using System.Text;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandlePlayerProfile
//
// Handles OP_PlayerProfile packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandlePlayerProfile : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_PlayerProfile";
    private readonly PatchOpcode _opcodeHandled;
    private readonly CollectionHandle _collectionHandle;
    private readonly PatchRegistry _registry;
    private readonly PatchLevel _patchLevel;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _nameSlot;
    private readonly SlotId _levelSlot;
    private readonly SlotId _playerClassSlot;
    private readonly SlotId _practicePointsSlot;
    private readonly SlotId _manaSlot;
    private readonly SlotId _hitpointsSlot;
    private readonly SlotId _strengthSlot;
    private readonly SlotId _staminaSlot;
    private readonly SlotId _charismaSlot;
    private readonly SlotId _dexteritySlot;
    private readonly SlotId _intelligenceSlot;
    private readonly SlotId _agilitySlot;
    private readonly SlotId _wisdomSlot;
    private readonly SlotId _platinumCarriedSlot;
    private readonly SlotId _goldCarriedSlot;
    private readonly SlotId _silverCarriedSlot;
    private readonly SlotId _copperCarriedSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePlayerProfile (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_PlayerProfile from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_PlayerProfile, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandlePlayerProfile()
    {
        _registry = GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_PlayerProfile");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _nameSlot = _registry.IndexOfField(_collectionHandle, "name");
        _levelSlot = _registry.IndexOfField(_collectionHandle, "level");
        _playerClassSlot = _registry.IndexOfField(_collectionHandle, "player_class");
        _practicePointsSlot = _registry.IndexOfField(_collectionHandle, "practice_points");
        _manaSlot = _registry.IndexOfField(_collectionHandle, "mana");
        _hitpointsSlot = _registry.IndexOfField(_collectionHandle, "max_hitpoints");
        _strengthSlot = _registry.IndexOfField(_collectionHandle, "strength");
        _staminaSlot = _registry.IndexOfField(_collectionHandle, "stamina");
        _charismaSlot = _registry.IndexOfField(_collectionHandle, "charisma");
        _dexteritySlot =  _registry.IndexOfField(_collectionHandle, "dexterity");
        _intelligenceSlot = _registry.IndexOfField(_collectionHandle, "intelligence");
        _agilitySlot = _registry.IndexOfField(_collectionHandle, "agility");
        _wisdomSlot = _registry.IndexOfField(_collectionHandle, "wisdom");
        _platinumCarriedSlot = _registry.IndexOfField(_collectionHandle, "platinum_carried");
        _goldCarriedSlot = _registry.IndexOfField(_collectionHandle, "gold_carried");
        _silverCarriedSlot = _registry.IndexOfField(_collectionHandle, "silver_carried");
        _copperCarriedSlot = _registry.IndexOfField(_collectionHandle, "copper_carried");
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
    // Dispatches to channel-specific handlers.
    //
    // data:       The application payload
    // length:     Length of the application payload
    // direction:  Direction byte (ignored)
    // opcode:     The application-level opcode
    // metadata:   Packet metadata; the Channel field selects the per-channel handler
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
    // Decodes a player profile packet from the zone stream and logs the player's identity
    // and stats.
    //
    // data:      The application payload
    // metadata:  Packet metadata
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);
            uint bagCount = extractor.BagCount(rootGate);

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                ReadOnlySpan<byte> nameBytes = extractor.GetBytesAt(_nameSlot);
                string name = Encoding.ASCII.GetString(nameBytes);

                Character? character = CharacterRepository.Instance.GetByName(name);
                if (character == null)
                {
                    DebugLog.Write(LogChannel.Opcodes, _opcodeName + ": no Character named '" + name + "' in repository; fields not stored.");
                    return;
                }
                character.Level = extractor.GetUIntAt(_levelSlot);
                character.PracticePoints = extractor.GetUIntAt(_practicePointsSlot);
                character.MaxHP = extractor.GetUIntAt(_hitpointsSlot);
                character.MaxMana = extractor.GetUIntAt(_manaSlot);

                character.Strength = extractor.GetUIntAt(_strengthSlot);
                character.Stamina = extractor.GetUIntAt(_staminaSlot);
                character.Charisma = extractor.GetUIntAt(_charismaSlot);
                character.Dexterity = extractor.GetUIntAt(_dexteritySlot);
                character.Intelligence = extractor.GetUIntAt(_intelligenceSlot);
                character.Agility = extractor.GetUIntAt(_agilitySlot);
                character.Wisdom = extractor.GetUIntAt(_wisdomSlot);

                character.Platinum = extractor.GetUIntAt(_platinumCarriedSlot);
                character.Gold = extractor.GetUIntAt(_goldCarriedSlot);
                character.Silver = extractor.GetUIntAt(_silverCarriedSlot);
                character.Copper = extractor.GetUIntAt(_copperCarriedSlot);

                // PlayerProfile is the first time we see the character name on the network.
                if (metadata.SessionId == -1)
                {
                    GlassContext.SessionRegistry.IdentifyConnection(name, metadata);
                    DebugLog.Write(LogChannel.Inference, "identifying port " + metadata.DestPort + " as " + name);
                    GlassContext.SessionRegistry.FindConnectionByCharacter(name);
                }

                DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] "
                    + _opcodeName + " length=" + data.Length
                    + " name=" + name + " characterId=" + character.CharacterId
                    + " level=" + character.Level + " hp=" + character.MaxHP + " mana=" + character.MaxMana);

            }
        }
        finally
        {
            extractor.Release();
        }
    }

    private static readonly Dictionary<uint, string> ClassNames = new Dictionary<uint, string>()
    {
        { 0, "None" },
        { 1, "Warrior" },
        { 2, "Cleric" },
        { 3, "Paladin" },
        { 4, "Ranger" },
        { 5, "ShadowKnight" },
        { 6, "Druid" },
        { 7, "Monk" },
        { 8, "Bard" },
        { 9, "Rogue" },
        {10, "Shaman" },
        {11, "Necromancer" },
        {12, "Wizard" },
        {13, "Magician" },
        {14, "Enchanter" },
        {15, "Beastlord" },
        {16, "Berserker" }
    };

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetClassName
    //
    // Looks up a class name by its byte value. Returns a descriptive string for unknown values
    // rather than throwing — an unknown class id should log and continue, not crash.
    //
    // classId:    The class byte from OP_PlayerProfile at offset 0x1a
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static string GetClassName(uint classId)
    {
        if (ClassNames.TryGetValue(classId, out string? name))
        {
            return name;
        }

        DebugLog.Write(LogChannel.Opcodes, $"[GetClassName] classId=0x{classId:X2} not in map, returning 'Unknown'");
        return $"Unknown(0x{classId:X2})";
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHandled
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode OpcodeHandled
    {
        get { return _opcodeHandled; }
    }
}

