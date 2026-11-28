using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Reference;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.World;
using System;
using System.Reflection;
using System.Security.Policy;

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

    private readonly SlotId _name_Slot;
    private readonly SlotId _characterId_Slot;
    private readonly SlotId _level_Slot;
    private readonly SlotId _zoneId_Slot;
    private readonly SlotId _zoneInstance_Slot;
    private readonly SlotId _playerClass_Slot;
    private readonly SlotId _practicePoints_Slot;
    private readonly SlotId _mana_Slot;
    private readonly SlotId _hitpoints_Slot;
    private readonly SlotId _strength_Slot;
    private readonly SlotId _stamina_Slot;
    private readonly SlotId _charisma_Slot;
    private readonly SlotId _dexterity_Slot;
    private readonly SlotId _intelligence_Slot;
    private readonly SlotId _agility_Slot;
    private readonly SlotId _wisdom_Slot;
    private readonly SlotId _platinumCarried_Slot;
    private readonly SlotId _goldCarried_Slot;
    private readonly SlotId _silverCarried_Slot;
    private readonly SlotId _copperCarried_Slot;
    private readonly SlotId _numAA_Slot;
    private readonly SlotId _numSkills_Slot;
    private readonly SlotId _gender_Slot;
    private readonly SlotId _race_Slot;
    private readonly SlotId _numLanguages_Slot;

    // spell info
    private readonly SlotId _spellbookCount_Slot;
    private readonly SlotId _spellbook_Slot;
    private readonly SlotId _spellgemCount_Slot;
    private readonly SlotId _spellgem_Slot;

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
		if (_opcodeHandled == PatchOpcode.None)
		{
			// happens when replaying captures before the opcode was added
			return;
		}

		_collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_PlayerProfile");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);
        CollectionHandle characterBuffs = _registry.GetCollectionHandle(_patchLevel, "Character_Buffs");

        if (!characterBuffs.Exists)
        {
            return;
        }

        _name_Slot = _registry.IndexOfField(_collectionHandle, "name");
        _characterId_Slot = _registry.IndexOfField(_collectionHandle, "persistent_id");
        _level_Slot = _registry.IndexOfField(_collectionHandle, "level");
        _zoneId_Slot = _registry.IndexOfField(_collectionHandle, "zone_id");
        _zoneInstance_Slot = _registry.IndexOfField(_collectionHandle, "instance_id");
        _playerClass_Slot = _registry.IndexOfField(_collectionHandle, "player_class");
        _practicePoints_Slot = _registry.IndexOfField(_collectionHandle, "practice_points");
        _mana_Slot = _registry.IndexOfField(_collectionHandle, "mana");
        _hitpoints_Slot = _registry.IndexOfField(_collectionHandle, "max_hitpoints");
        _strength_Slot = _registry.IndexOfField(_collectionHandle, "strength");
        _stamina_Slot = _registry.IndexOfField(_collectionHandle, "stamina");
        _charisma_Slot = _registry.IndexOfField(_collectionHandle, "charisma");
        _dexterity_Slot =  _registry.IndexOfField(_collectionHandle, "dexterity");
        _intelligence_Slot = _registry.IndexOfField(_collectionHandle, "intelligence");
        _agility_Slot = _registry.IndexOfField(_collectionHandle, "agility");
        _wisdom_Slot = _registry.IndexOfField(_collectionHandle, "wisdom");
        _platinumCarried_Slot = _registry.IndexOfField(_collectionHandle, "platinum_carried");
        _goldCarried_Slot = _registry.IndexOfField(_collectionHandle, "gold_carried");
        _silverCarried_Slot = _registry.IndexOfField(_collectionHandle, "silver_carried");
        _copperCarried_Slot = _registry.IndexOfField(_collectionHandle, "copper_carried");

        _numAA_Slot = _registry.IndexOfField(_collectionHandle, "num_aa");
        _numSkills_Slot = _registry.IndexOfField(_collectionHandle, "num_skills");
        _gender_Slot = _registry.IndexOfField(_collectionHandle, "gender");
        _race_Slot = _registry.IndexOfField(_collectionHandle, "race");
        _numLanguages_Slot = _registry.IndexOfField(_collectionHandle, "num_languages");

        // spell info
        _spellbookCount_Slot = _registry.IndexOfField(_collectionHandle, "spellbook_count");
        _spellbook_Slot = _registry.IndexOfField(_collectionHandle, "spellbook");
        _spellgemCount_Slot = _registry.IndexOfField(_collectionHandle, "spellgem_count");
        _spellgem_Slot = _registry.IndexOfField(_collectionHandle, "mem_spells");

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
                string name = _extractor.GetStringAt(_name_Slot);

                Character? character = CharacterRepository.Instance.GetByName(name);
                if (character == null)
                {
                    DebugLog.Write(LogChannel.Opcodes, _opcodeName + ": no Character named '" + name + 
                        "' in repository; fields not stored.", LogLevel.Error);
                    return;
                }
                character.CharacterId = (CharacterId) _extractor.GetUIntAt(_characterId_Slot);
                character.Level = _extractor.GetUIntAt(_level_Slot);
                character.PracticePoints = _extractor.GetUIntAt(_practicePoints_Slot);

                character.Strength = _extractor.GetUIntAt(_strength_Slot);
                character.Stamina = _extractor.GetUIntAt(_stamina_Slot);
                character.Charisma = _extractor.GetUIntAt(_charisma_Slot);
                character.Dexterity = _extractor.GetUIntAt(_dexterity_Slot);
                character.Intelligence = _extractor.GetUIntAt(_intelligence_Slot);
                character.Agility = _extractor.GetUIntAt(_agility_Slot);
                character.Wisdom = _extractor.GetUIntAt(_wisdom_Slot);

                character.Platinum = _extractor.GetUInt64At(_platinumCarried_Slot);
                character.Gold = _extractor.GetUInt64At(_goldCarried_Slot);
                character.Silver = _extractor.GetUInt64At(_silverCarried_Slot);
                character.Copper = _extractor.GetUInt64At(_copperCarried_Slot);

                ZoneId zoneId = (ZoneId)_extractor.GetUIntAt(_zoneId_Slot);
                metadata.Session.SetZone(zoneId);
                uint zoneSessions = ZoneRepository.Instance.SessionEntered(zoneId);

                // If this is the first session in the zone, then clear out ephemeral data
                if (zoneSessions == 1)
                {
                    DebugLog.Write(LogChannel.Sessions, "HandlePlayerProfile: first session in zoneId=" + zoneId +
                        ", clearing cached mobs.", LogLevel.Trace);

                    List<EntityId> mobIds = ZoneRepository.Instance.GetZoneEntityIds(zoneId, EntityType.Mob);
                    List<EntityId> entityIds = ZoneRepository.Instance.GetZoneEntityIds(zoneId);
                    uint removed = 0;

                    for (int idIndex = 0; idIndex < entityIds.Count; idIndex++)
                    {
                        if (entityIds[idIndex].Type == EntityType.Character)
                        {
                            continue;
                        }

                        if (EntityRepository.Instance.Remove(entityIds[idIndex]))
                        {
                            removed++;
                        }
                    }

                    uint cleared = ZoneRepository.Instance.ClearZonePlacements(zoneId);
                    DebugLog.Write(LogChannel.Sessions, "HandlePlayerProfile.HandleZoneToClient: zoneId=" + zoneId
                        + " removed " + removed + " of " + entityIds.Count + " entities, cleared " + cleared
                        + " placements.", LogLevel.Trace);

                    DebugLog.Write(LogChannel.Sessions, "HandlePlayerProfile: zoneId=" + zoneId + " removed "
                        + mobIds.Count + " mobs, cleared " + cleared + " placements.", LogLevel.Trace);
                }

                ReadOnlySpan<uint> spellbook = _extractor.GetUIntSpanAt(_spellbook_Slot);
                ReadOnlySpan<uint> spellgems = _extractor.GetUIntSpanAt(_spellgem_Slot);

                uint knownSpellCount = 0;
                for (int index = 0; index < spellbook.Length; index++)
                {
                    if (spellbook[index] != SpellId.None)
                    {
                        knownSpellCount++;
                    }
                }

                character.SpellBook = new SpellId[knownSpellCount];
                uint knownIndex = 0;
                for (int index = 0; index < spellbook.Length; index++)
                {
                    if (spellbook[index] != SpellId.None)
                    {
                        character.SpellBook[knownIndex] = (SpellId)spellbook[index];
                        knownIndex++;
                    }
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
                if (metadata.Character == null)
                {
                    GlassContext.SessionRegistry.IdentifySession(character, metadata);
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

            name = _extractor.GetStringAt(_name_Slot);
            EQClass playerClass = (EQClass) _extractor.GetUIntAt(_playerClass_Slot);
            ZoneId zoneId = (ZoneId) _extractor.GetUIntAt(_zoneId_Slot);
            String zoneName = ZoneRepository.Instance.GetZoneName(zoneId);
            uint instanceId = _extractor.GetUIntAt(_zoneInstance_Slot);
            string instanceDesc = string.Empty;
            if (instanceId != 0)
            {
                instanceDesc = ", Instance: " + instanceId.ToString("X4");
            }
            FieldNodes.AddStringNode(_extractor, _name_Slot, "Name", root);
            FieldNodes.AddUIntNode(_extractor, _characterId_Slot, "Character ID", root);
            FieldNodes.AddUIntNode(_extractor, _level_Slot, "Level", root, "D");

            FieldNodes.AddLabeledNode(_extractor, _playerClass_Slot, "Class: " + playerClass.ToDisplayString() + "(0x" +
                    playerClass.ToString("X") + ")", root);
            FieldNodes.AddLabeledNode(_extractor, _zoneId_Slot, "Zone: " + zoneName + 
                    " (" + zoneId + ") " + instanceDesc, root);

            FieldNodes.AddUIntNode(_extractor, _practicePoints_Slot, "Practice Points", root, "D");
            FieldNodes.AddUIntNode(_extractor, _mana_Slot, "Mana", root, "D");
            FieldNodes.AddUIntNode(_extractor, _hitpoints_Slot, "HP", root, "D");

            FieldDisplayNode statsSubtree = new FieldDisplayNode("Stats");
            root.AddChild(statsSubtree);
            FieldNodes.AddUIntNode(_extractor, _strength_Slot, "Strength", statsSubtree, "D");
            FieldNodes.AddUIntNode(_extractor, _stamina_Slot, "Stamina", statsSubtree, "D");
            FieldNodes.AddUIntNode(_extractor, _charisma_Slot, "Charisma", statsSubtree, "D");
            FieldNodes.AddUIntNode(_extractor, _dexterity_Slot, "Dexterity", statsSubtree, "D");
            FieldNodes.AddUIntNode(_extractor, _intelligence_Slot, "Intelligence", statsSubtree, "D");
            FieldNodes.AddUIntNode(_extractor, _agility_Slot, "Agility", statsSubtree, "D");
            FieldNodes.AddUIntNode(_extractor, _wisdom_Slot, "Wisdom", statsSubtree, "D");

            FieldDisplayNode moneySubtree = new FieldDisplayNode("Money");
            root.AddChild(moneySubtree);
            FieldNodes.AddUInt64Node(_extractor, _platinumCarried_Slot, "Platinum", moneySubtree, "D");
            FieldNodes.AddUInt64Node(_extractor, _goldCarried_Slot, "Gold", moneySubtree, "D");
            FieldNodes.AddUInt64Node(_extractor, _silverCarried_Slot, "Silver", moneySubtree, "D");
            FieldNodes.AddUInt64Node(_extractor, _copperCarried_Slot, "Copper", moneySubtree, "D");

            FieldNodes.AddUIntNode(_extractor, _numAA_Slot, "Num AAs", root, "D");
            FieldNodes.AddUIntNode(_extractor, _numSkills_Slot, "Num Skills", root, "D");
            FieldNodes.AddUIntNode(_extractor, _numLanguages_Slot, "Num Languages", root, "D");
            FieldNodes.AddUIntNode(_extractor, _gender_Slot, "Gender", root, "D");
            FieldNodes.AddUIntNode(_extractor, _race_Slot, "Race", root, "D");
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
        uint spellBookSize = _extractor.GetUIntAt(_spellbookCount_Slot);
        uint spellGemCount = _extractor.GetUIntAt(_spellgemCount_Slot);
        uint knownSpellCount = 0;

        ReadOnlySpan<uint> spellbook = _extractor.GetUIntSpanAt(_spellbook_Slot);
        ReadOnlySpan<uint> spellgems = _extractor.GetUIntSpanAt(_spellgem_Slot);

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

                FieldNodes.AddLabeledNode(_extractor, _spellbook_Slot, spellEntry, spellBookTree);
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

               FieldNodes.AddLabeledNode(_extractor, _spellgem_Slot, spellEntry, spellGemTree);
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
}

