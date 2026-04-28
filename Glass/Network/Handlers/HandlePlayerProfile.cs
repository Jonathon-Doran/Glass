using Glass.Core;
using Glass.Core.Logging;
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

    private ushort _opcode;
    private readonly FieldDefinition[]? _fields;

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
    // Resolves the wire opcode and loads the field definitions for OP_PlayerProfile from the
    // active patch via GlassContext.FieldExtractor.  Caches the index of each field the
    // handler reads so the hot path can access the bag by integer index without name lookup.
    //
    // If the active patch does not define OP_PlayerProfile, GetOpcodeValue returns 0 and the
    // handler is effectively disabled — OpcodeDispatch refuses to register handlers with a
    // zero opcode, so this handler simply will not receive packets.  All field index lookups
    // resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandlePlayerProfile()
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;
        _opcode = extractor.GetOpcodeValue(_opcodeName);
        _fields = extractor.GetFieldDefinitions(_opcodeName);

        _nameId = _fields.IndexOfField("name");
        _levelId = _fields.IndexOfField("level");
        _playerClassId = _fields.IndexOfField("player_class");
        _practicePointsId = _fields.IndexOfField("practice_points");
        _manaId = _fields.IndexOfField("mana");
        _hitpointsId = _fields.IndexOfField("hitpoints");
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
            + _opcode.ToString("x4") + ", " + (_fields == null ? 0 : _fields.Length)
            + " field definition(s) loaded");
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
    public void HandlePacket(ReadOnlySpan<byte> data, int length,
                              byte direction, ushort opcode, PacketMetadata metadata)
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
    // metadata:  Packet metadata; Timestamp is used for the log line
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        if (_fields == null)
        {
            return;
        }

        FieldExtractor extractor = GlassContext.FieldExtractor;
        FieldBag bag = extractor.Rent();
        try
        {
            extractor.Extract(_fields, data, bag);

            ReadOnlySpan<byte> nameBytes = bag.GetBytesAt(_nameId);
            string name = Encoding.ASCII.GetString(nameBytes);
            int level = bag.GetIntAt(_levelId);
            uint playerClass = bag.GetUIntAt(_playerClassId);
            string className = GetClassName(playerClass);
            uint practicePoints = bag.GetUIntAt(_practicePointsId);
            uint mana = bag.GetUIntAt(_manaId);
            int hitpoints = bag.GetIntAt(_hitpointsId);
            int strength = bag.GetIntAt(_strengthId);
            int stamina = bag.GetIntAt(_staminaId);
            int charisma = bag.GetIntAt(_charismaId);
            int dexterity = bag.GetIntAt(_dexterityId);
            int intelligence = bag.GetIntAt(_intelligenceId);
            int agility = bag.GetIntAt(_agilityId);
            int wisdom = bag.GetIntAt(_wisdomId);
            int platinumCarried = bag.GetIntAt(_platinumCarriedId);
            int goldCarried = bag.GetIntAt(_goldCarriedId);
            int silverCarried = bag.GetIntAt(_silverCarriedId);
            int copperCarried = bag.GetIntAt(_copperCarriedId);

            DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] "
                + _opcodeName + " length=" + data.Length);
            DebugLog.Write(LogChannel.Opcodes, "Name: " + name + ", " + level + " " + className);
            DebugLog.Write(LogChannel.Opcodes, "HP: " + hitpoints + ", Mana: " + mana + ", "
                + platinumCarried + "pp/" + goldCarried + "gp/" + silverCarried + "sp/"
                + copperCarried + "cp");
            DebugLog.Write(LogChannel.Opcodes, "Str: " + strength + ", Sta: " + stamina
                + ", Cha: " + charisma + ", Dex: " + dexterity + ", Int: " + intelligence
                + ", Agi: " + agility + ", Wis: " + wisdom);

            // PlayerProfile is the first time when we see the character name on the network
            if (metadata.SessionId == -1)
            {
                GlassContext.SessionRegistry.IdentifyConnection(name, metadata);
                DebugLog.Write(LogChannel.Inference, "identifying port " + metadata.DestPort + " as " +
                    name);
                GlassContext.SessionRegistry.FindConnectionByCharacter(name);
            }
        }
        finally
        {
            bag.Release();
        }


    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleServerToClient
    //
    // Processes zone-to-client traffic
    //
    // data:    The application payload
    // length:  Length of the application payload
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleServerToClient(ReadOnlySpan<byte> data, int length, PacketMetadata metadata)
    {
        if (length < 4)
        {
            DebugLog.Write(LogChannel.Opcodes, _opcodeName + " too short, length=" + length);
            return;
        }

        string name = "unknown";

        int nullPosition = FindNullTerminator(data.Slice(0x4fcb), length - 0x4fcb);
        if (nullPosition != -1)
        {
            name = System.Text.Encoding.ASCII.GetString(data.Slice(0x4fcb, nullPosition));
        }

        int level = BinaryPrimitives.ReadInt32BigEndian(data.Slice(0x1a));
        uint playerClass = data[0x19];
        string className = GetClassName(playerClass);
        uint practicePoints = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0x3b2));
        uint mana = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0x3b6));
        int hitpoints = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(0x3ba));
        int strength = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(0x3be));
        int stamina = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(0x3c2));
        int charisma = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(0x3c6));
        int dexterity = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(0x3ca));
        int intelligence = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(0x3ce));
        int agility = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(0x3d2));
        int wisdom = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(0x3d6));

        int platinumCarried = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(0x4beb));
        int goldCarried = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(0x4bef));
        int silverCarried = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(0x4bf3));
        int copperCarried = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(0x4bf7));


        DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] "
            + _opcodeName + " length=" + length);
        DebugLog.Write(LogChannel.Opcodes, "Name: " + name + ", " + level + " " + className);
        DebugLog.Write(LogChannel.Opcodes, "HP: " + hitpoints + ", Mana: " + mana + ", " + platinumCarried + "pp/" + goldCarried + "gp/" + silverCarried + "sp/" + copperCarried + "cp");
        DebugLog.Write(LogChannel.Opcodes, "Str: " + strength + ", Cha: " + charisma + ", Dex: " + dexterity + ", Int: " + intelligence + ", Agi: " + agility + ", Wis: " + wisdom);
    }
    private int FindNullTerminator(ReadOnlySpan<byte> data, int length)
    {
        // Find the null terminator for the name string at offset 0
        int nullPos = -1;
        for (int i = 0; i < length; i++)
        {
            if (data[i] == 0)
            {
                nullPos = i;
                break;
            }
        }

        if (nullPos < 0)
        {
            DebugLog.Write(LogChannel.Opcodes, _opcodeName + ": no null terminator found");
            return -1;
        }

        return nullPos;
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

