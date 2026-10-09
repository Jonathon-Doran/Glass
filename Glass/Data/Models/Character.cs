using Glass.Core.Logging;
using Glass.Data.Repositories;
using Glass.World;

namespace Glass.Data.Models;



public class Character
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CharacterId
    //
    // Our database key for this character, unrelated to the EQ IDs.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public int CharacterId { get; set; }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PersistentId
    //
    // The server's persistent id for this character, unchanged across zones and sessions.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public uint? PersistentId { get; set; }

    public string Name { get; set; } = string.Empty;
    public EQClass Class { get; set; }
    public int AccountId { get; set; }
    public bool Progression { get; set; }
    public string Server { get; set; } = string.Empty;
    public List<RelayGroup> RelayGroups { get; set; } = new();

    public uint? Level { get; set; }
    public uint? PracticePoints { get; set; }
    public uint? CurrentHP { get; set; }
    public uint? CurrentZone { get; set; }
    public uint? MaxHP { get; set; }
    public uint? CurrentMana { get; set; }
    public uint? MaxMana { get; set; }
    public uint? Strength { get; set; }
    public uint? Stamina { get; set; }
    public uint? Charisma { get; set; }
    public uint? Dexterity { get; set; }
    public uint? Intelligence { get; set; }
    public uint? Agility { get; set; }
    public uint? Wisdom { get; set; }

    public ulong? Platinum { get; set; }
    public ulong? Gold { get; set; }
    public ulong? Silver { get; set; }
    public ulong? Copper { get; set; }
    public float? XPos { get; set; }
    public float? YPos { get; set; }
    public float? ZPos { get; set; }
    public float? Heading { get; set; }         // in degrees

    public uint? SpawnId { get; set; }
    public SpellId[] SpellBook { get; set; } = Array.Empty<SpellId>();
    public SpellId[] SpellGems { get; set; } = Array.Empty<SpellId>();

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _activeSpells
    //
    // Every spell currently affecting this character, keyed by spell position.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private readonly Dictionary<uint, ActiveSpell> _activeSpells = new Dictionary<uint, ActiveSpell>();

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ActiveSpells
    //
    // Every spell currently affecting this character, in no particular order.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public IEnumerable<ActiveSpell> ActiveSpells => _activeSpells.Values;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _items
    //
    // Every item instance held by this character, keyed by position.  Contents of
    // containers and augments are entries of their own, and are also reachable
    // through their parent's Children.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private readonly Dictionary<ItemLocation, ItemInstance> _items = new Dictionary<ItemLocation, ItemInstance>();

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Items
    //
    // Every item instance held by this character, in no particular order: worn
    // items, carried items, bag contents, and socketed augments alike.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public IEnumerable<ItemInstance> Items => _items.Values;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ClearItems
    //
    // Removes every item instance held by this character.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void ClearItems()
    {
        int removedCount = _items.Count;
        _items.Clear();
        DebugLog.Write(LogChannel.Fields, "Character.ClearItems: removed " + removedCount +
            " items from '" + Name + "'", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ClearActiveSpells
    //
    // Removes every spell currently affecting this character.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void ClearActiveSpells()
    {
        int removedCount = _activeSpells.Count;
        _activeSpells.Clear();
        DebugLog.Write(LogChannel.Fields, "Character.ClearActiveSpells: removed " + removedCount +
            " active spells from '" + Name + "'", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AddItemBonuses
    //
    // Adds an item instance to this character at the instance's position.  When a
    // parent is given, links the instance into the parent's Children and sets the
    // instance's Parent.  The instance is rejected when its position is invalid,
    // when another instance already occupies the position, when it already has a
    // parent, or when the given parent is not held by this character.
    //
    // item:    The instance to add.  Its Position must be valid.
    // parent:  The container or item holding this instance, or null for a
    //          top-level instance.
    //
    // Returns true if the instance was added, false if it was rejected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public bool AddItem(ItemInstance item, ItemInstance? parent)
    {
        if (item.Location.Exists == false)
        {
            DebugLog.Write(LogChannel.Fields, "Character.AddItemBonuses: item " + item.Id +
                " has no position; not added to '" + Name + "'", LogLevel.Warn);
            return false;
        }

        if (_items.TryGetValue(item.Location, out ItemInstance? occupant))
        {
            DebugLog.Write(LogChannel.Fields, "Character.AddItemBonuses: position " + item.Location +
                " already holds item " + occupant.Id + "; item " + item.Id + " not added to '" +
                Name + "'", LogLevel.Warn);
            return false;
        }

        if (item.Parent != null)
        {
            DebugLog.Write(LogChannel.Fields, "Character.AddItemBonuses: item " + item.Id + " at " +
                item.Location + " already has a parent; not added to '" + Name + "'", LogLevel.Warn);
            return false;
        }

        if (parent != null)
        {
            if (_items.TryGetValue(parent.Location, out ItemInstance? heldParent) == false ||
                ReferenceEquals(heldParent, parent) == false)
            {
                DebugLog.Write(LogChannel.Fields, "Character.AddItemBonuses: parent at " + parent.Location +
                    " is not held by '" + Name + "'; item " + item.Id + " not added", LogLevel.Warn);
                return false;
            }

            item.Parent = parent;
            parent.Children.Add(item);
            DebugLog.Write(LogChannel.Fields, "Character.AddItemBonuses: linked item " + item.Id + " at " +
                item.Location + " under parent " + parent.Id + " at " + parent.Location, LogLevel.Trace);
        }

        _items[item.Location] = item;
        DebugLog.Write(LogChannel.Fields, "Character.AddItemBonuses: added item " + item.Id + " at " +
            item.Location + " to '" + Name + "'", LogLevel.Trace);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetItem
    //
    // Looks up the item instance held by this character at a position.
    //
    // position:  The position to look up.
    // item:      Receives the instance at the position, or null when the position
    //            is invalid or empty.
    //
    // Returns true if an instance is held at the position, false otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public bool TryGetItem(ItemLocation position, out ItemInstance? item)
    {
        if (position.Exists == false)
        {
            item = null;
            DebugLog.Write(LogChannel.Fields, "Character.TryGetItem: invalid position on '" + Name +
                "'", LogLevel.Warn);
            return false;
        }

        if (_items.TryGetValue(position, out item) == false)
        {
            DebugLog.Write(LogChannel.Fields, "Character.TryGetItem: no item at " + position +
                " on '" + Name + "'", LogLevel.Trace);
            return false;
        }

        DebugLog.Write(LogChannel.Fields, "Character.TryGetItem: item " + item.Id + " at " +
            position + " on '" + Name + "'", LogLevel.Trace);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetWornPosition
    //
    // Tests whether an item location denotes worn equipment.  A location is worn
    // when the storage system is Carried and the main position is one of the
    // named worn positions.
    //
    // Note that multiple items may be at a location (armor + augments).  Callers need to be prepared.
    //
    // storageSystem:  Storage system holding the item
    // mainPosition:   Index within the storage system
    // wornPosition:   Receives the worn position when the location is worn,
    //                 WornPosition.None otherwise
    //
    // Returns true when the location is a worn position, false otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static bool TryGetWornPosition(StorageSystem storageSystem, uint mainPosition, out WornPosition wornPosition)
    {
        wornPosition = WornPosition.None;

        if (storageSystem != StorageSystem.Carried)
        {
            return false;
        }

        WornPosition candidate = (WornPosition)mainPosition;

        if (candidate.IsWorn() == false)
        {
            DebugLog.Write(LogChannel.Fields, "TryGetWornPosition: carried position " + mainPosition +
                " is not a worn position", LogLevel.Trace);
            return false;
        }

        wornPosition = candidate;
        DebugLog.Write(LogChannel.Fields, "TryGetWornPosition: worn position " + candidate.DisplayName(), LogLevel.Trace);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetActiveSpell
    //
    // Looks up the active spell held by this character at a buff position.
    //
    // position:  The buff position to look up.
    // spell:     Receives the active spell at the position, or null when the position is empty.
    //
    // Returns true if an active spell is held at the position, false otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public bool TryGetActiveSpell(uint position, out ActiveSpell? spell)
    {
        if (_activeSpells.TryGetValue(position, out spell) == false)
        {
            DebugLog.Write(LogChannel.Fields, "Character.TryGetActiveSpell: no active spell at position " +
                position + " on '" + Name + "'", LogLevel.Trace);
            return false;
        }

        DebugLog.Write(LogChannel.Fields, "Character.TryGetActiveSpell: spell " + spell.SpellId +
            " at position " + position + " on '" + Name + "'", LogLevel.Trace);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // RemoveActiveSpell
    //
    // Removes the active spell held by this character at a buff position.  An empty position
    // is left unchanged.
    //
    // position:  The buff position to clear.
    //
    // Returns true if a spell was removed, false if the position was empty.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public bool RemoveActiveSpell(uint position)
    {
        if (_activeSpells.Remove(position, out ActiveSpell? removed) == false)
        {
            DebugLog.Write(LogChannel.Fields, "Character.RemoveActiveSpell: no active spell at position " +
                position + " on '" + Name + "'; nothing removed", LogLevel.Trace);
            return false;
        }

        DebugLog.Write(LogChannel.Fields, "Character.RemoveActiveSpell: removed spell " + removed.SpellId +
            " at position " + position + " from '" + Name + "'", LogLevel.Trace);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SumWornBonuses
    //
    // Builds new bonus totals from every item worn by this character.  A worn item is a
    // top-level item held in the Carried storage system at a worn position.  Each worn item's
    // bonuses and those of the items socketed in it are added together, then the combined
    // total is added to the character's totals, adjusted by the worn item's recommended level.
    // An item whose definition is not found is logged and skipped.  A character with no level
    // gets empty totals.
    //
    // Returns new totals holding the effective bonuses of the worn items.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public StatBonuses SumWornBonuses()
    {
        StatBonuses bonuses = new StatBonuses();

        if (Level.HasValue == false)
        {
            DebugLog.Write(LogChannel.Fields, "Character.SumWornBonuses: '" + Name +
                "' has no level; bonuses cannot be adjusted, returning empty totals", LogLevel.Warn);
            return bonuses;
        }

        uint characterLevel = Level.Value;
        uint counted = 0;
        uint missing = 0;

        foreach (ItemInstance item in _items.Values)
        {
            if (item.Parent != null)
            {
                continue;
            }

            ItemLocation location = item.Location;
            if (TryGetWornPosition(location.Storage, location.MainPosition, out WornPosition wornPosition) == false)
            {
                continue;
            }

            if (ItemRepository.Instance.TryGet(item.Id, out ItemRecord? record) == false)
            {
                missing++;
                DebugLog.Write(LogChannel.Fields, "Character.SumWornBonuses: no definition for worn item " +
                    item.Id + " at " + location + " on '" + Name + "'; skipped with its children", LogLevel.Warn);
                continue;
            }

            StatBonuses itemTotals = new StatBonuses();
            itemTotals.AddItemBonuses(record);
            counted++;

            foreach (ItemInstance child in item.Children)
            {
                if (ItemRepository.Instance.TryGet(child.Id, out ItemRecord? childRecord) == false)
                {
                    missing++;
                    DebugLog.Write(LogChannel.Fields, "Character.SumWornBonuses: no definition for item " +
                        child.Id + " at " + child.Location + " in '" + record.Name + "' on '" + Name +
                        "'; skipped", LogLevel.Warn);
                    continue;
                }

                itemTotals.AddItemBonuses(childRecord);
                counted++;
                DebugLog.Write(LogChannel.Fields, "Character.SumWornBonuses: added '" + childRecord.Name +
                    "' in '" + record.Name + "' on '" + Name + "'", LogLevel.Trace);
            }

            bonuses.AddScaledBonuses(itemTotals, characterLevel, record.RecommendedLevel);
            DebugLog.Write(LogChannel.Fields, "Character.SumWornBonuses: added '" + record.Name + "' at " +
                wornPosition.DisplayName() + " on '" + Name + "', item HP " + itemTotals.HP +
                ", recommended level " + record.RecommendedLevel, LogLevel.Trace);
        }

        DebugLog.Write(LogChannel.Fields, "Character.SumWornBonuses: '" + Name + "' worn bonuses from " +
            counted + " items, " + missing + " without definitions: HP " + bonuses.HP + ", mana " +
            bonuses.Mana + ", AC " + bonuses.AC, LogLevel.Trace);

        return bonuses;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SumActiveSpellBonuses
    //
    // Builds new bonus totals from every spell currently affecting this character.  Each
    // spell's stat effects are computed at the level of its caster.  A spell missing from the
    // spell catalog is logged and skipped.
    //
    // Returns new totals holding the bonuses of the active spells.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public StatBonuses SumActiveSpellBonuses()
    {
        StatBonuses bonuses = new StatBonuses();
        uint counted = 0;
        uint missing = 0;

        foreach (ActiveSpell spell in _activeSpells.Values)
        {
            if (SpellCatalog.Instance.TryGet(spell.SpellId, out SpellRecord? record) == false)
            {
                missing++;
                DebugLog.Write(LogChannel.Fields, "Character.SumActiveSpellBonuses: spell " + spell.SpellId +
                    " at position " + spell.Position + " on '" + Name + "' not in spell catalog; skipped",
                    LogLevel.Warn);
                continue;
            }

            bonuses.AddSpellBonuses(record, spell.CasterLevel);
            counted++;
        }

        DebugLog.Write(LogChannel.Fields, "Character.SumActiveSpellBonuses: '" + Name + "' bonuses from " +
            counted + " active spells, " + missing + " not in catalog: HP " + bonuses.HP + ", mana " +
            bonuses.Mana + ", AC " + bonuses.AC, LogLevel.Info);

        return bonuses;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetActiveSpell
    //
    // Stores an active spell at its buff position on this character, replacing any spell
    // already held at that position.
    //
    // spell:  The active spell to store.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void SetActiveSpell(ActiveSpell spell)
    {
        if (_activeSpells.TryGetValue(spell.Position, out ActiveSpell? previous))
        {
            DebugLog.Write(LogChannel.Fields, "Character.SetActiveSpell: position " + spell.Position +
                " on '" + Name + "' held spell " + previous.SpellId + "; replaced by spell " +
                spell.SpellId, LogLevel.Trace);
        }
        else
        {
            DebugLog.Write(LogChannel.Fields, "Character.SetActiveSpell: stored spell " + spell.SpellId +
                " at position " + spell.Position + " on '" + Name + "'", LogLevel.Trace);
        }

        _activeSpells[spell.Position] = spell;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // StorageSystemNames
    //
    // Printable names for the storage systems of the item serialization header,
    // keyed by storage system.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private static readonly Dictionary<StorageSystem, string> StorageSystemNames = new Dictionary<StorageSystem, string>()
    {
        { StorageSystem.Carried,                     "Carried" },
        { StorageSystem.Bank,                        "Bank" },
        { StorageSystem.SharedBank,                  "Shared Bank" },
        { StorageSystem.Trade,                       "Trade" },
        { StorageSystem.WorldContainer,              "World Container" },
        { StorageSystem.Limbo,                       "Limbo" },
        { StorageSystem.Tribute,                     "Tribute" },
        { StorageSystem.TrophyTribute,               "Trophy Tribute" },
        { StorageSystem.GuildTribute,                "Guild Tribute" },
        { StorageSystem.Merchant,                    "Merchant" },
        { StorageSystem.Deleted,                     "Deleted" },
        { StorageSystem.Corpse,                      "Corpse" },
        { StorageSystem.Bazaar,                      "Bazaar" },
        { StorageSystem.Inspect,                     "Inspect" },
        { StorageSystem.RealEstate,                  "Real Estate" },
        { StorageSystem.ViewModPC,                   "ViewMod PC" },
        { StorageSystem.ViewModBank,                 "ViewMod Bank" },
        { StorageSystem.ViewModSharedBank,           "ViewMod Shared Bank" },
        { StorageSystem.ViewModLimbo,                "ViewMod Limbo" },
        { StorageSystem.AltStorage,                  "Alt Storage" },
        { StorageSystem.Archived,                    "Archived" },
        { StorageSystem.Mail,                        "Mail" },
        { StorageSystem.GuildTrophyTribute,          "Guild Trophy Tribute" },
        { StorageSystem.Krono,                       "Krono" },
        { StorageSystem.Other,                       "Other" },
        { StorageSystem.MercenaryItems,              "Mercenary Items" },
        { StorageSystem.ViewModMercenaryItems,       "ViewMod Mercenary Items" },
        { StorageSystem.MountKeyRing,                "Mount Key Ring" },
        { StorageSystem.ViewModMountKeyRing,         "ViewMod Mount Key Ring" },
        { StorageSystem.IllusionKeyRing,             "Illusion Key Ring" },
        { StorageSystem.ViewModIllusionKeyRing,      "ViewMod Illusion Key Ring" },
        { StorageSystem.FamiliarKeyRing,             "Familiar Key Ring" },
        { StorageSystem.ViewModFamiliarKeyRing,      "ViewMod Familiar Key Ring" },
        { StorageSystem.HerosForgeKeyRing,           "Hero's Forge Key Ring" },
        { StorageSystem.ViewModHerosForgeKeyRing,    "ViewMod Hero's Forge Key Ring" },
        { StorageSystem.TeleportationKeyRing,        "Teleportation Key Ring" },
        { StorageSystem.ViewModTeleportationKeyRing, "ViewMod Teleportation Key Ring" },
        { StorageSystem.Overflow,                    "Overflow" },
        { StorageSystem.DragonsHoard,                "Dragon's Hoard" },
        { StorageSystem.TradeskillDepot,             "Tradeskill Depot" },
        { StorageSystem.GuildTradeskillDepot,        "Guild Tradeskill Depot" }
    };

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // DescribeStorageLocation
    //
    // Returns a printable string describing a storage system
    //
    // storageSystem:  Storage system holding an item
    //
    // Returns the printable string.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static string DescribeStorageLocation(StorageSystem storageSystem)
    {
        string description;

        if (StorageSystemNames.TryGetValue(storageSystem, out string? storageName))
        {
            description = storageName;
        }
        else
        {
            DebugLog.Write(LogChannel.Fields, "DescribeStorageLocation: unknown storage system " +
                (uint)storageSystem, LogLevel.Warn);
            description = "<unknown>";
        }

        return description;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // DescribePosition
    //
    // Returns a printable string describing a position in a storage system
    //
    // position:  index within the storage system (i.e bag slot)
    //
    // Returns the printable string.
    ///////////////////////////////////////////////////////////////////////////////////////////////

    public static string DescribePosition(uint position)
    {
        const uint NoPosition = 0xFFFF;

        if (position == NoPosition)
        {
            return "None";
        }
        else
        {
            return "Pocket " + position;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // DescribeLocation
    //
    // Builds a printable description of an item location from the four wire
    // fields of the item serialization header.  Positions are reported as raw
    // 0-based wire values.  Worn positions within the carried storage system are
    // reported by name.  Sub position and aug position are appended only when
    // present (not 0xFFFF).
    //
    // storageSystem:  Storage system holding the item
    // mainPosition:   Index within the storage system
    // subPosition:    Index within a bag at mainPosition, 0xFFFF if none
    // augPosition:    Augment socket index within the item at the location,
    //                 0xFFFF if none
    //
    // Returns the printable location string.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static string DescribeLocation(StorageSystem storageSystem, uint mainPosition, uint subPosition, uint augPosition)
    {
        const uint NoPosition = 0xFFFF;
        const uint AmmoPosition = 22;

        string location;

        if (storageSystem == StorageSystem.Carried)
        {
            WornPosition wornPosition = (WornPosition)mainPosition;

            if (wornPosition.IsWorn())
            {
                location = "Worn: " + wornPosition.DisplayName();
            }
            else if (mainPosition == AmmoPosition)
            {
                location = "Ammo ";
            }
            else if (mainPosition >= 23 && mainPosition <= 34)
            {
                location = "Inventory " + (mainPosition - 23);
            }
            else if (mainPosition == 35)
            {
                location = "Cursor";
            }
            else
            {
                DebugLog.Write(LogChannel.Fields, "DescribeLocation: unknown carried position " + mainPosition, LogLevel.Warn);
                location = "Carried " + mainPosition;
            }
        }
        else
        {
            if (StorageSystemNames.TryGetValue(storageSystem, out string? storageName))
            {
                location = storageName + " " + mainPosition;
            }
            else
            {
                DebugLog.Write(LogChannel.Fields, "DescribeLocation: unknown storage system " + (uint)storageSystem, LogLevel.Warn);
                location = "Storage system " + (uint)storageSystem + " " + mainPosition;
            }
        }

        if (subPosition != NoPosition)
        {
            location += ", pocket " + subPosition;
        }

        if (augPosition != NoPosition)
        {
            location += ", augment " + augPosition;
        }

        return location;
    }
}
public enum EQClass
{
    Warrior = 1,
    Cleric,
    Paladin,
    Ranger,
    Shadowknight,
    Druid,
    Monk,
    Bard,
    Rogue,
    Shaman,
    Necromancer,
    Wizard,
    Magician,
    Enchanter,
    Beastlord,
    Berserker
}