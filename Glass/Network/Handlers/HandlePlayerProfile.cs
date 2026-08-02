using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.World;
using System;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandlePlayerProfile
//
// Handles OP_PlayerProfile packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandlePlayerProfile : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _nameSlot;
    private readonly SlotId _levelSlot;
    private readonly SlotId _zoneIdSlot;
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

    // spell info
    private const uint SpellNone = 0xFFFFFFFFu;
    private readonly SlotId _spellbookCountSlot;
    private readonly SlotId _spellbookSlot;
    private readonly SlotId _spellgemCountSlot;
    private readonly SlotId _spellgemSlot;


    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePlayerProfile (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandlePlayerProfile(PatchLevel patchLevel)
        : base(patchLevel, "OP_PlayerProfile")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_PlayerProfile");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _nameSlot = _registry.IndexOfField(_collectionHandle, "name");
        _levelSlot = _registry.IndexOfField(_collectionHandle, "level");
        _zoneIdSlot = _registry.IndexOfField(_collectionHandle, "zone_id");
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

        // spell info
        _spellbookCountSlot = _registry.IndexOfField(_collectionHandle, "spellbook_count");
        _spellbookSlot = _registry.IndexOfField(_collectionHandle, "spellbook");
        _spellgemCountSlot = _registry.IndexOfField(_collectionHandle, "spellgem_count");
        _spellgemSlot = _registry.IndexOfField(_collectionHandle, "mem_spells");
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
                string name = extractor.GetStringAt(_nameSlot);

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

                character.CurrentZone = extractor.GetUIntAt(_zoneIdSlot);
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
        string name;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            name = _extractor.GetStringAt(_nameSlot);
            uint playerClass = _extractor.GetUIntAt(_playerClassSlot);
            uint zoneId = _extractor.GetUIntAt(_zoneIdSlot);

            FieldNodes.AddStringNode(_extractor, _nameSlot, "Name", root);
            FieldNodes.AddUIntNode(_extractor, _levelSlot, "Level", root, "D");
            FieldNodes.AddLabeledNode(_extractor, _playerClassSlot, "Class: " + GetClassName(playerClass), root);
            FieldNodes.AddLabeledNode(_extractor, _zoneIdSlot, "Zone: " + GetZoneName(zoneId) + 
                    " (" + zoneId + ")", root);

            FieldNodes.AddUIntNode(_extractor, _practicePointsSlot, "Practice Points", root, "D");
            FieldNodes.AddUIntNode(_extractor, _manaSlot, "Mana", root, "D");
            FieldNodes.AddUIntNode(_extractor, _hitpointsSlot, "HP", root, "D");

            FieldDisplayNode statsSubtree = new FieldDisplayNode("Stats");
            root.AddChild(statsSubtree);
            FieldNodes.AddUIntNode(_extractor, _strengthSlot, "Strength", statsSubtree, "D");
            FieldNodes.AddUIntNode(_extractor, _staminaSlot, "Stamina", statsSubtree, "D");
            FieldNodes.AddUIntNode(_extractor, _charismaSlot, "Charisma", statsSubtree, "D");
            FieldNodes.AddUIntNode(_extractor, _dexteritySlot, "Dexterity", statsSubtree, "D");
            FieldNodes.AddUIntNode(_extractor, _intelligenceSlot, "Intelligence", statsSubtree, "D");
            FieldNodes.AddUIntNode(_extractor, _agilitySlot, "Agility", statsSubtree, "D");
            FieldNodes.AddUIntNode(_extractor, _wisdomSlot, "Wisdom", statsSubtree, "D");

            FieldDisplayNode moneySubtree = new FieldDisplayNode("Money");
            root.AddChild(moneySubtree);
            FieldNodes.AddUIntNode(_extractor, _platinumCarriedSlot, "Platinum", moneySubtree, "D");
            FieldNodes.AddUIntNode(_extractor, _goldCarriedSlot, "Gold", moneySubtree, "D");
            FieldNodes.AddUIntNode(_extractor, _silverCarriedSlot, "Silver", moneySubtree, "D");
            FieldNodes.AddUIntNode(_extractor, _copperCarriedSlot, "Copper", moneySubtree, "D");

            AddSpellNode(root);
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Player Profile (" + name + ")";
        return root;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AddSpellNode
    //
    // Builds the spellbook display subtree under the given root: a "Spells" node containing a
    // "SpellBook" node with one leaf child per known spell.  The spellbook array is read from
    // the active bag; entries holding the empty sentinel (0xFFFFFFFF) are skipped.  Each leaf
    // is labeled with a running count and the raw spell ID.  The SpellBook node's text carries
    // the total number of known spells.  A stored spellbook size that disagrees with the
    // array's element count is logged at Warn.
    //
    // root:  The display node that receives the "Spells" subtree.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void AddSpellNode (FieldDisplayNode root)
    {
        uint spellBookSize = _extractor.GetUIntAt(_spellbookCountSlot);
        uint spellGemCount = _extractor.GetUIntAt(_spellgemCountSlot);
        uint knownSpellCount = 0;

        ReadOnlySpan<uint> spellbook = _extractor.GetUIntSpanAt(_spellbookSlot);
        ReadOnlySpan<uint> spellgems = _extractor.GetUIntSpanAt(_spellgemSlot);

        FieldDisplayNode spellSubtree = new FieldDisplayNode("Spells");
        root.AddChild(spellSubtree);

        FieldDisplayNode spellBookTree = new FieldDisplayNode();
        spellSubtree.AddChild(spellBookTree);

        for (int index = 0; index < spellbook.Length; index++)
        {
            if (spellbook[index] != SpellNone)
            {
                knownSpellCount++;
                string spellName = LookupSpell(spellbook[index]);

                string spellEntry = knownSpellCount.ToString() + ":  " + spellName + " (" + spellbook[index].ToString() + ")";

                FieldNodes.AddLabeledNode(_extractor, _spellbookSlot, spellEntry, spellBookTree);
            }
        }
        spellBookTree.Text = "SpellBook (" + knownSpellCount + " entries)";

        FieldDisplayNode spellGemTree = new FieldDisplayNode();
        spellSubtree.AddChild(spellGemTree);

        knownSpellCount = 0;

        for (int index = 0; index < spellgems.Length; index++)
        {
            if (spellgems[index] != SpellNone)
            {
                knownSpellCount++;
                string spellName = LookupSpell(spellgems[index]);

                string spellEntry = knownSpellCount.ToString() + ":  " + spellName + " (" + spellgems[index].ToString() + ")";

               FieldNodes.AddLabeledNode(_extractor, _spellgemSlot, spellEntry, spellGemTree);
            }
        }
        spellGemTree.Text = "Memorized Spells (" + knownSpellCount + " entries)";
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LookupSpell
    //
    // A helper to lookup the spell name from ID
    //
    // spellId:  The ID to query
    //
    // Returns:   The name of the spell, or "unknown"
    ///////////////////////////////////////////////////////////////////////////////////////////////

    private string LookupSpell(uint spellId)
    {
        string spellName;
        SpellRecord? record;
        if (SpellCatalog.Instance.TryGet(spellId, out record) == true)
        {
            spellName = record.Name;
        }
        else
        {
            spellName = "unknown";
        }

        return spellName;
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
    // classId:    The class ID to query
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

    private static readonly Dictionary<uint, string> ZoneNames = new Dictionary<uint, string>()
    {
        { 13, "North Karana" },
        { 14, "South Karana" },
        { 29, "Halas" },
        {118, "Great Divide" },
        {202, "Plane of Knowledge" },
        {394, "Crescent Reach" }
    };

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetZoneName
    //
    // Looks up a zone name by its byte value. Returns a descriptive string for unknown values
    // rather than throwing.
    //
    // zoneId:    The zoneId to query
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static string GetZoneName(uint zoneId)
    {
        if (ZoneNames.TryGetValue(zoneId, out string? name))
        {
            return name;
        }

        DebugLog.Write(LogChannel.Opcodes, $"[GetZoneName] zoneId=0x{zoneId:X2} not in map, returning 'Unknown'", LogLevel.Warn);
        return $"Unknown(0x{zoneId:X2})";
    }
}

