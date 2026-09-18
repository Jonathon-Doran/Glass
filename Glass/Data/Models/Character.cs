using Glass.Core.Logging;

namespace Glass.Data.Models;



public class Character
{
    public int CharacterId { get; set; }
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

    public uint? Platinum { get; set; }
    public uint? Gold { get; set; }
    public uint? Silver { get; set; }
    public uint? Copper { get; set; }
    public float? XPos { get; set; }
    public float? YPos { get; set; }
    public float? ZPos { get; set; }
    public float? Heading { get; set; }         // in degrees

    public uint? SpawnId { get; set; }
    public SpellId[] SpellBook { get; set; } = Array.Empty<SpellId>();
    public SpellId[] SpellGems { get; set; } = Array.Empty<SpellId>();

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _items
    //
    // Every item instance held by this character, keyed by position.  Contents of
    // containers and augments are entries of their own, and are also reachable
    // through their parent's Children.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private readonly Dictionary<ItemPosition, ItemInstance> _items = new Dictionary<ItemPosition, ItemInstance>();

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
    // AddItem
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
        if (item.Position.Exists == false)
        {
            DebugLog.Write(LogChannel.Fields, "Character.AddItem: item " + item.Id +
                " has no position; not added to '" + Name + "'", LogLevel.Warn);
            return false;
        }

        if (_items.TryGetValue(item.Position, out ItemInstance? occupant))
        {
            DebugLog.Write(LogChannel.Fields, "Character.AddItem: position " + item.Position +
                " already holds item " + occupant.Id + "; item " + item.Id + " not added to '" +
                Name + "'", LogLevel.Warn);
            return false;
        }

        if (item.Parent != null)
        {
            DebugLog.Write(LogChannel.Fields, "Character.AddItem: item " + item.Id + " at " +
                item.Position + " already has a parent; not added to '" + Name + "'", LogLevel.Warn);
            return false;
        }

        if (parent != null)
        {
            if (_items.TryGetValue(parent.Position, out ItemInstance? heldParent) == false ||
                ReferenceEquals(heldParent, parent) == false)
            {
                DebugLog.Write(LogChannel.Fields, "Character.AddItem: parent at " + parent.Position +
                    " is not held by '" + Name + "'; item " + item.Id + " not added", LogLevel.Warn);
                return false;
            }

            item.Parent = parent;
            parent.Children.Add(item);
            DebugLog.Write(LogChannel.Fields, "Character.AddItem: linked item " + item.Id + " at " +
                item.Position + " under parent " + parent.Id + " at " + parent.Position, LogLevel.Trace);
        }

        _items[item.Position] = item;
        DebugLog.Write(LogChannel.Fields, "Character.AddItem: added item " + item.Id + " at " +
            item.Position + " to '" + Name + "'", LogLevel.Trace);
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
    public bool TryGetItem(ItemPosition position, out ItemInstance? item)
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
            DebugLog.Write(LogChannel.Fields, "TryGetWornPosition: storage system " + (uint)storageSystem +
                " is not Carried, not worn", LogLevel.Trace);
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

        string location;

        if (storageSystem == StorageSystem.Carried)
        {
            WornPosition wornPosition = (WornPosition)mainPosition;

            if (wornPosition.IsWorn())
            {
                location = "Worn: " + wornPosition.DisplayName();
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