using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.World;
using System;
using System.Reflection;

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
    private readonly SlotId _persistentID_Slot;
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
    private readonly SlotId _numAASlot;
    private readonly SlotId _numSkillsSlot;
    private readonly SlotId _genderSlot;
    private readonly SlotId _raceSlot;
    private readonly SlotId _numLanguagesSlot;

    // spell info
    private readonly SlotId _spellbookCountSlot;
    private readonly SlotId _spellbookSlot;
    private readonly SlotId _spellgemCountSlot;
    private readonly SlotId _spellgemSlot;

    private readonly SlotId _buff_unknown_1_Slot;
    private readonly SlotId _buff_casterID_Slot;
    private readonly SlotId _buff_unknown_2_Slot;
    private readonly SlotId _buff_remainingTicks_Slot;
    private readonly SlotId _buff_totalTicks_Slot;
    private readonly SlotId _buff_casterLevel_Slot;
    private readonly SlotId _buff_spellID_Slot;
    private readonly SlotId _buff_unknown_3_Slot;


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
        CollectionHandle characterBuffs = _registry.GetCollectionHandle(_patchLevel, "Character_Buffs");

        _nameSlot = _registry.IndexOfField(_collectionHandle, "name");
        _persistentID_Slot = _registry.IndexOfField(_collectionHandle, "persistent_id");
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

        _numAASlot = _registry.IndexOfField(_collectionHandle, "num_aa");
        _numSkillsSlot = _registry.IndexOfField(_collectionHandle, "num_skills");
        _genderSlot = _registry.IndexOfField(_collectionHandle, "gender");
        _raceSlot = _registry.IndexOfField(_collectionHandle, "race");
        _numLanguagesSlot = _registry.IndexOfField(_collectionHandle, "num_languages");

        // spell info
        _spellbookCountSlot = _registry.IndexOfField(_collectionHandle, "spellbook_count");
        _spellbookSlot = _registry.IndexOfField(_collectionHandle, "spellbook");
        _spellgemCountSlot = _registry.IndexOfField(_collectionHandle, "spellgem_count");
        _spellgemSlot = _registry.IndexOfField(_collectionHandle, "mem_spells");

        // character buffs
        _buff_unknown_1_Slot = _registry.IndexOfField(characterBuffs, "Unknown_1");
        _buff_casterID_Slot = _registry.IndexOfField(characterBuffs, "Caster_ID");
        _buff_unknown_2_Slot = _registry.IndexOfField(characterBuffs, "Unknown_2");
        _buff_remainingTicks_Slot = _registry.IndexOfField(characterBuffs, "Remaining_Ticks");
        _buff_totalTicks_Slot = _registry.IndexOfField(characterBuffs, "Total_Ticks");
        _buff_casterLevel_Slot = _registry.IndexOfField(characterBuffs, "Caster_Level");
        _buff_spellID_Slot = _registry.IndexOfField(characterBuffs, "Spell_ID");
        _buff_unknown_3_Slot = _registry.IndexOfField(characterBuffs, "Unknown_3");
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
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            uint bagCount = _extractor.BagCount(rootGate);

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                string name = _extractor.GetStringAt(_nameSlot);

                Character? character = CharacterRepository.Instance.GetByName(name);
                if (character == null)
                {
                    DebugLog.Write(LogChannel.Opcodes, _opcodeName + ": no Character named '" + name + 
                        "' in repository; fields not stored.", LogLevel.Trace);
                    return;
                }
                character.PersistentId = _extractor.GetUIntAt(_persistentID_Slot);
                character.Level = _extractor.GetUIntAt(_levelSlot);
                character.PracticePoints = _extractor.GetUIntAt(_practicePointsSlot);
                character.MaxHP = _extractor.GetUIntAt(_hitpointsSlot);
                character.MaxMana = _extractor.GetUIntAt(_manaSlot);

                character.Strength = _extractor.GetUIntAt(_strengthSlot);
                character.Stamina = _extractor.GetUIntAt(_staminaSlot);
                character.Charisma = _extractor.GetUIntAt(_charismaSlot);
                character.Dexterity = _extractor.GetUIntAt(_dexteritySlot);
                character.Intelligence = _extractor.GetUIntAt(_intelligenceSlot);
                character.Agility = _extractor.GetUIntAt(_agilitySlot);
                character.Wisdom = _extractor.GetUIntAt(_wisdomSlot);

                character.Platinum = _extractor.GetUInt64At(_platinumCarriedSlot);
                character.Gold = _extractor.GetUInt64At(_goldCarriedSlot);
                character.Silver = _extractor.GetUInt64At(_silverCarriedSlot);
                character.Copper = _extractor.GetUInt64At(_copperCarriedSlot);

                character.CurrentZone = _extractor.GetUIntAt(_zoneIdSlot);


                ReadOnlySpan<uint> spellbook = _extractor.GetUIntSpanAt(_spellbookSlot);
                ReadOnlySpan<uint> spellgems = _extractor.GetUIntSpanAt(_spellgemSlot);

                character.SpellBook = new SpellId[spellbook.Length];
                for (int index = 0; index < spellbook.Length; index++)
                {
                    character.SpellBook[index] = (SpellId)spellbook[index];
                }
                character.SpellGems = new SpellId[spellgems.Length];
                for (int index = 0; index < spellgems.Length; index++)
                {
                    character.SpellGems[index] = (SpellId)spellgems[index];
                }

                CaptureActiveSpells(character, rootGate, bagIndex);
                StatBonuses spellBonuses = character.SumActiveSpellBonuses();
                DebugLog.Write(LogChannel.Fields, "HandlePlayerProfile.HandleZoneToClient: '" + name +
                    "' spell bonuses STR " + spellBonuses.Strength + ", STA " + spellBonuses.Stamina +
                    ", AGI " + spellBonuses.Agility + ", DEX " + spellBonuses.Dexterity + ", CHA " +
                    spellBonuses.Charisma + ", HP " + spellBonuses.HP + ", AC " + spellBonuses.AC +
                    ", magic " + spellBonuses.SaveMagic, LogLevel.Info);

                // PlayerProfile is the first time we see the character name on the network.
                if (metadata.SessionId == -1)
                {
                    GlassContext.SessionRegistry.IdentifyConnection(name, metadata);
                    DebugLog.Write(LogChannel.Inference, "identifying port " + metadata.DestPort + " as " + name,
                        LogLevel.Trace);
                    GlassContext.SessionRegistry.FindConnectionByCharacter(name);
                }
            }
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CaptureActiveSpells
    //
    // Replaces the character's active spells with those in the profile's buff table.  Each
    // entry of the Buffs_Gate is one buff position; an entry whose caster id is 0 is empty and
    // is skipped.  Re-enters rootGate at bagIndex before returning, so the active bag on exit
    // matches the active bag on entry.
    //
    // character:  The character receiving the active spells.
    // rootGate:   The gate whose instance holds the profile.
    // bagIndex:   The instance index of the profile within rootGate.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void CaptureActiveSpells(Character character, GateHandle rootGate, uint bagIndex)
    {
        character.ClearActiveSpells();

        SlotId buffsGateSlot = _registry.IndexOfField(_extractor.CollectionOf(), "Buffs_Gate");
        if (_extractor.IsPresent(buffsGateSlot) == false)
        {
            DebugLog.Write(LogChannel.Fields, "HandlePlayerProfile.CaptureActiveSpells: no Buffs_Gate for '" +
                character.Name + "'; no active spells stored", LogLevel.Warn);
            return;
        }

        GateHandle buffsGate = _extractor.GetGateAt(buffsGateSlot);
        if (buffsGate.Exists == false)
        {
            DebugLog.Write(LogChannel.Fields, "HandlePlayerProfile.CaptureActiveSpells: Buffs_Gate present but no gate for '" +
                character.Name + "'; no active spells stored", LogLevel.Warn);
            return;
        }

        uint positionCount = _extractor.BagCount(buffsGate);
        uint storedCount = 0;

        for (uint position = 0; position < positionCount; position++)
        {
            _extractor.EnterGate(buffsGate, position);

            uint casterId = _extractor.GetUIntAt(_buff_casterID_Slot);
            if (casterId == 0)
            {
                continue;
            }

            SpellId spellId = (SpellId)_extractor.GetUIntAt(_buff_spellID_Slot);
            uint casterLevel = _extractor.GetUIntAt(_buff_casterLevel_Slot);
            uint totalTicks = _extractor.GetUIntAt(_buff_totalTicks_Slot);
            uint remainingTicks = _extractor.GetUIntAt(_buff_remainingTicks_Slot);

            character.SetActiveSpell(new ActiveSpell(position, spellId, casterId, casterLevel, totalTicks,
                remainingTicks));
            storedCount++;
        }

        _extractor.EnterGate(rootGate, bagIndex);

        DebugLog.Write(LogChannel.Fields, "HandlePlayerProfile.CaptureActiveSpells: stored " + storedCount +
            " active spells of " + positionCount + " positions for '" + character.Name + "'", LogLevel.Trace);
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
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandlePlayerProfile:  No RootGate", LogLevel.Error);
                return root;
            }

            name = _extractor.GetStringAt(_nameSlot);
            uint playerClass = _extractor.GetUIntAt(_playerClassSlot);
            ZoneId zoneId = (ZoneId) _extractor.GetUIntAt(_zoneIdSlot);
            String zoneName = ZoneRepository.Instance.GetZoneName(zoneId);

            FieldNodes.AddStringNode(_extractor, _nameSlot, "Name", root);
            FieldNodes.AddUIntNode(_extractor, _persistentID_Slot, "Persistent ID", root);
            FieldNodes.AddUIntNode(_extractor, _levelSlot, "Level", root, "D");
            FieldNodes.AddLabeledNode(_extractor, _playerClassSlot, "Class: " + GetClassName(playerClass), root);
            FieldNodes.AddLabeledNode(_extractor, _zoneIdSlot, "Zone: " + zoneName + 
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
            FieldNodes.AddUInt64Node(_extractor, _platinumCarriedSlot, "Platinum", moneySubtree, "D");
            FieldNodes.AddUInt64Node(_extractor, _goldCarriedSlot, "Gold", moneySubtree, "D");
            FieldNodes.AddUInt64Node(_extractor, _silverCarriedSlot, "Silver", moneySubtree, "D");
            FieldNodes.AddUInt64Node(_extractor, _copperCarriedSlot, "Copper", moneySubtree, "D");

            FieldNodes.AddUIntNode(_extractor, _numAASlot, "Num AAs", root, "D");
            FieldNodes.AddUIntNode(_extractor, _numSkillsSlot, "Num Skills", root, "D");
            FieldNodes.AddUIntNode(_extractor, _numLanguagesSlot, "Num Languages", root, "D");
            FieldNodes.AddUIntNode(_extractor, _genderSlot, "Gender", root, "D");
            FieldNodes.AddUIntNode(_extractor, _raceSlot, "Race", root, "D");
            AddSpellNode(root);
            AddBuffsNode(root);
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
    // Builds the spellbook display subtree under the given parent: a "Spells" node containing a
    // "SpellBook" node with one leaf child per known spell.  The spellbook array is read from
    // the active bag; entries holding the empty sentinel (0xFFFFFFFF) are skipped.  Each leaf
    // is labeled with a running count and the raw spell ID.  The SpellBook node's text carries
    // the total number of known spells.  A stored spellbook size that disagrees with the
    // array's element count is logged at Warn.
    //
    // root:  The display node that receives the "Spells" subtree.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void AddSpellNode (FieldDisplayNode parent)
    {
        uint spellBookSize = _extractor.GetUIntAt(_spellbookCountSlot);
        uint spellGemCount = _extractor.GetUIntAt(_spellgemCountSlot);
        uint knownSpellCount = 0;

        ReadOnlySpan<uint> spellbook = _extractor.GetUIntSpanAt(_spellbookSlot);
        ReadOnlySpan<uint> spellgems = _extractor.GetUIntSpanAt(_spellgemSlot);

        FieldDisplayNode spellSubtree = new FieldDisplayNode("Spells");
        parent.AddChild(spellSubtree);

        FieldDisplayNode spellBookTree = new FieldDisplayNode();
        spellSubtree.AddChild(spellBookTree);

        for (int index = 0; index < spellbook.Length; index++)
        {
            if (spellbook[index] != SpellId.None)
            {
                knownSpellCount++;
                String spellName = SpellCatalog.Instance.LookupSpell((SpellId) spellbook[index]);

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
            if (spellgems[index] != SpellId.None)
            {
                knownSpellCount++;
                String spellName = SpellCatalog.Instance.LookupSpell((SpellId) spellgems[index]);

                string spellEntry = knownSpellCount.ToString() + ":  " + spellName + " (" + spellgems[index].ToString() + ")";

               FieldNodes.AddLabeledNode(_extractor, _spellgemSlot, spellEntry, spellGemTree);
            }
        }
        spellGemTree.Text = "Memorized Spells (" + knownSpellCount + " entries)";
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AddBuffsNode
    //
    // Builds the buffs display subtree under the given root: a "Buffs" node containing a
    // "Buff" node with one leaf child per buff active on the character.
    //
    // parent:  The parent display node
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void AddBuffsNode(FieldDisplayNode parent)
    {
        uint buffs_seen = 0;

        SlotId buffsGate_Slot = _registry.IndexOfField(_extractor.CollectionOf(), "Buffs_Gate");
        if (_extractor.IsPresent(buffsGate_Slot) == false)
        {
            DebugLog.Write(LogChannel.Fields, "PlayerProfile: No Buffs_Gate found", LogLevel.Error);
            return;
        }

        GateHandle buffsGate = _extractor.GetGateAt(buffsGate_Slot);
        if (buffsGate.Exists == false)
        {
            DebugLog.Write(LogChannel.Fields, "PlayerProfile: Buffs slot found but no gate", LogLevel.Error);
            return;
        }

        uint bagCount = _extractor.BagCount(buffsGate);
        if (bagCount == 0)
        {
            return;         // no bags extracted
        }

        FieldDisplayNode buffsRoot = new FieldDisplayNode("Active Buffs");

        for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
        {
            _extractor.EnterGate(buffsGate, bagIndex);

            uint CasterID = _extractor.GetUIntAt(_buff_casterID_Slot);

            if (CasterID == 0)
            {
                continue;
            }
            buffs_seen++;
            FieldDisplayNode buffNode = new FieldDisplayNode();
            buffsRoot.AddChild(buffNode);

            SpellId spellID = (SpellId)_extractor.GetUIntAt(_buff_spellID_Slot);

            String spellName = SpellCatalog.Instance.LookupSpell(spellID);
            string spellEntry = spellName + " (" + spellID.ToString() + ", 0x" + spellID.Value.ToString("X4") + ")";

            buffNode.Text = bagIndex.ToString() + ": " + spellEntry;

            FieldNodes.AddLabeledNode(_extractor, _buff_spellID_Slot, spellEntry, buffNode);
            FieldNodes.AddUIntNode(_extractor, _buff_casterID_Slot, "Caster ID", buffNode, "X");
            FieldNodes.AddUIntNode(_extractor, _buff_casterLevel_Slot, "Caster Level", buffNode, "D");
            FieldNodes.AddUIntNode(_extractor, _buff_totalTicks_Slot, "Total Ticks", buffNode, "D");
            FieldNodes.AddUIntNode(_extractor, _buff_remainingTicks_Slot, "Remaining Ticks", buffNode, "D");
            FieldNodes.AddFloatNode(_extractor, _buff_unknown_1_Slot, "Unknown 1", buffNode, "F2");
            FieldNodes.AddUIntNode(_extractor, _buff_unknown_2_Slot, "Unknown 2", buffNode, "?");
            FieldNodes.AddUIntNode(_extractor, _buff_unknown_3_Slot, "Unknown 3", buffNode, "?");
        }

        if (buffs_seen > 0)
        {
            parent.AddChild(buffsRoot);
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
    // classId:    The class ID to query
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static string GetClassName(uint classId)
    {
        if (ClassNames.TryGetValue(classId, out string? name))
        {
            return name;
        }

        DebugLog.Write(LogChannel.Opcodes, $"[GetClassName] classId=0x{classId:X2} not in map, returning 'Unknown'",
            LogLevel.Trace);
        return $"Unknown(0x{classId:X2})";
    }
}

