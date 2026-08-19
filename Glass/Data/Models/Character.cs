using Glass.Core.Logging;

namespace Glass.Data.Models;

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
    // WornPositionNames
    //
    // Names for main positions within the possessions container that correspond
    // to worn equipment, keyed by the raw wire value.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private static readonly Dictionary<uint, string> WornPositionNames = new Dictionary<uint, string>()
    {
        { 0,  "Charm" },
        { 1,  "Left Ear" },
        { 2,  "Head" },
        { 3,  "Face" },
        { 4,  "Right Ear" },
        { 5,  "Neck" },
        { 6,  "Shoulders" },
        { 7,  "Arms" },
        { 8,  "Back" },
        { 9,  "Left Wrist" },
        { 10, "Right Wrist" },
        { 11, "Range" },
        { 12, "Hands" },
        { 13, "Primary" },
        { 14, "Secondary" },
        { 15, "Left Ring" },
        { 16, "Right Ring" },
        { 17, "Chest" },
        { 18, "Legs" },
        { 19, "Feet" },
        { 20, "Waist" },
        { 21, "Power Source" },
        { 22, "Ammo" }
    };

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ContainerTypeNames
    //
    // Names for the container type field of the item serialization header,
    // keyed by the raw wire value.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private static readonly Dictionary<uint, string> ContainerTypeNames = new Dictionary<uint, string>()
    {
        { 0,  "Carried" },
        { 1,  "Bank" },
        { 2,  "Shared Bank" },
        { 3,  "Trade" },
        { 4,  "World Container" },
        { 5,  "Limbo" },
        { 6,  "Tribute" },
        { 7,  "Trophy Tribute" },
        { 8,  "Guild Tribute" },
        { 9,  "Merchant" },
        { 10, "Deleted" },
        { 11, "Corpse" },
        { 12, "Bazaar" },
        { 13, "Inspect" },
        { 14, "Real Estate" },
        { 15, "ViewMod PC" },
        { 16, "ViewMod Bank" },
        { 17, "ViewMod Shared Bank" },
        { 18, "ViewMod Limbo" },
        { 19, "Alt Storage" },
        { 20, "Archived" },
        { 21, "Mail" },
        { 22, "Guild Trophy Tribute" },
        { 23, "Krono" },
        { 24, "Other" },
        { 25, "Mercenary Items" },
        { 26, "ViewMod Mercenary Items" },
        { 27, "Mount Key Ring" },
        { 28, "ViewMod Mount Key Ring" },
        { 29, "Illusion Key Ring" },
        { 30, "ViewMod Illusion Key Ring" },
        { 31, "Familiar Key Ring" },
        { 32, "ViewMod Familiar Key Ring" },
        { 33, "Hero's Forge Key Ring" },
        { 34, "ViewMod Hero's Forge Key Ring" },
        { 35, "Teleportation Key Ring" },
        { 36, "ViewMod Teleportation Key Ring" },
        { 37, "Overflow" },
        { 38, "Dragon's Hoard" },
        { 39, "Tradeskill Depot" },
        { 40, "Guild Tradeskill Depot" }
    };

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // DescribeStorageLocation
    //
    // Returns a printable string describing a storage system
    //
    // storageSystem:  Storage system holding an item, per ContainerTypeNames
    //
    // Returns the printable string.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static string DescribeStorageLocation(uint storageSystem)
    {
        string description;

        if (ContainerTypeNames.TryGetValue(storageSystem, out string? containerName))
        {
            description = containerName;
        }
        else
        {
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
    // 0-based wire values.  Worn positions within the possessions container are
    // reported by name.  Sub position and aug position are appended only when
    // present (not 0xFFFF).
    //
    // storageSystem:  Storage system holding the item, per ContainerTypeNames
    // mainPosition:   Index within the container
    // subPosition:    Index within a bag at mainPosition, 0xFFFF if none
    // augPosition:    Augment socket index within the item at the location,
    //                 0xFFFF if none
    //
    // Returns the printable location string.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static string DescribeLocation(uint containerType, uint mainPosition, uint subPosition, uint augPosition)
    {
        const uint NoPosition = 0xFFFF;

        string location;

        if (containerType == 0)
        {
            if (WornPositionNames.TryGetValue(mainPosition, out string? wornName))
            {
                location = "Worn: " + wornName;
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
                DebugLog.Write(LogChannel.Fields, "DescribeLocation: unknown possessions position " + mainPosition, LogLevel.Warn);
                location = "Possessions " + mainPosition;
            }
        }
        else
        {
            if (ContainerTypeNames.TryGetValue(containerType, out string? containerName))
            {
                location = containerName + " " + mainPosition;
            }
            else
            {
                DebugLog.Write(LogChannel.Fields, "DescribeLocation: unknown container type " + containerType, LogLevel.Warn);
                location = "Container " + containerType + " " + mainPosition;
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