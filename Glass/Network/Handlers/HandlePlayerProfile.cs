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

    private OpcodeHandle _handle;
    private ushort _opcode;
    private readonly IReadOnlyList<FieldDefinition>? _fields;
    private bool _nullFieldsObserved = false;

    private readonly int _nameId;
    private readonly int _levelId;
    private readonly int _playerClassId;
    private readonly int _practicePointsId;
    private readonly int _manaId;
    private readonly int _hitpointsId;
    private readonly int _strengthId;
    private readonly int _staminaId;
    private readonly int _charismaId;
    private readonly int _dexterityId;
    private readonly int _intelligenceId;
    private readonly int _agilityId;
    private readonly int _wisdomId;
    private readonly int _platinumCarriedId;
    private readonly int _goldCarriedId;
    private readonly int _silverCarriedId;
    private readonly int _copperCarriedId;

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
        FieldExtractor extractor = GlassContext.FieldExtractor;
        PatchLevel patchLevel = GlassContext.CurrentPatchLevel;

        _handle = GlassContext.PatchRegistry.GetOpcodeHandle(patchLevel, _opcodeName);

        _opcode = extractor.GetOpcodeValue(patchLevel, _opcodeName);
        PatchOpcode opcodeId = new PatchOpcode(patchLevel, _opcode);
        _fields = extractor.GetFields(patchLevel, opcodeId);

        _nameId = _fields.IndexOfField("name");
        _levelId = _fields.IndexOfField("level");
        _playerClassId = _fields.IndexOfField("player_class");
        _practicePointsId = _fields.IndexOfField("practice_points");
        _manaId = _fields.IndexOfField("mana");
        _hitpointsId = _fields.IndexOfField("max_hitpoints");
        _strengthId = _fields.IndexOfField("strength");
        _staminaId = _fields.IndexOfField("stamina");
        _charismaId = _fields.IndexOfField("charisma");
        _dexterityId = _fields.IndexOfField("dexterity");
        _intelligenceId = _fields.IndexOfField("intelligence");
        _agilityId = _fields.IndexOfField("agility");
        _wisdomId = _fields.IndexOfField("wisdom");
        _platinumCarriedId = _fields.IndexOfField("platinum_carried");
        _goldCarriedId = _fields.IndexOfField("gold_carried");
        _silverCarriedId = _fields.IndexOfField("silver_carried");
        _copperCarriedId = _fields.IndexOfField("copper_carried");

        DebugLog.Write(LogChannel.Opcodes, _opcodeName + " ctor: opcode=0x"
            + _opcode.ToString("x4") + ", " + (_fields == null ? 0 : _fields.Count)
            + " field definition(s) loaded");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Dispose
    //
    // Log any errors in the cold-path, dispose of any local storage. 
    ///////////////////////////////////////////////////////////////////////////////////////////////

    public void Dispose()
    {
        if (_nullFieldsObserved)
        {
            DebugLog.Write(LogChannel.Opcodes, _opcodeName + " had null field descriptions");
        }
        GC.SuppressFinalize(this);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Opcode
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ushort Opcode
    {
        get { return _opcode; }
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
        // Ensure that _fields exist if we process this packet
        if (_fields == null)
        {
            _nullFieldsObserved = true;         // log this on exit
            return;
        }

        FieldBag bag = GlassContext.FieldExtractor.Rent(_opcodeName);
        try
        {
            GlassContext.FieldExtractor.Extract(_fields!, data, bag);

            ReadOnlySpan<byte> nameBytes = bag.GetBytesAt(_nameId);
            string name = Encoding.ASCII.GetString(nameBytes);

            Character? character = CharacterRepository.Instance.GetByName(name);
            if (character == null)
            {
                DebugLog.Write(LogChannel.Opcodes, _opcodeName + ": no Character named '" + name + "' in repository; fields not stored.");
                return;
            }

            character.Level = bag.GetUIntAt(_levelId);
            character.PracticePoints = bag.GetUIntAt(_practicePointsId);
            character.MaxHP = bag.GetUIntAt(_hitpointsId);
            character.MaxMana = bag.GetUIntAt(_manaId);

            character.Strength = bag.GetUIntAt(_strengthId);
            character.Stamina = bag.GetUIntAt(_staminaId);
            character.Charisma = bag.GetUIntAt(_charismaId);
            character.Dexterity = bag.GetUIntAt(_dexterityId);
            character.Intelligence = bag.GetUIntAt(_intelligenceId);
            character.Agility = bag.GetUIntAt(_agilityId);
            character.Wisdom = bag.GetUIntAt(_wisdomId);

            character.Platinum = bag.GetUIntAt(_platinumCarriedId);
            character.Gold = bag.GetUIntAt(_goldCarriedId);
            character.Silver = bag.GetUIntAt(_silverCarriedId);
            character.Copper = bag.GetUIntAt(_copperCarriedId);

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
        finally
        {
            bag.Release();
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
}

