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
    // WornItems
    //
    // Items currently worn by this character, keyed by worn position.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public Dictionary<WornPosition, WornItem> WornItems { get; set; } = new Dictionary<WornPosition, WornItem>();


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
            else if (mainPosition >= 23 && mainPosition <= 32)
            {
                location = "Inventory " + (mainPosition - 23);
            }
            else if (mainPosition == 33)
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