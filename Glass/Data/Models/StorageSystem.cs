using Glass.Core.Logging;
using System.Collections.Generic;

namespace Glass.Data.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// StorageSystem
//
// Storage systems that can hold an item, with each member set to the raw wire
// value from the item serialization header.
///////////////////////////////////////////////////////////////////////////////////////////////
public enum StorageSystem : byte
{
    Carried = 0,
    Bank = 1,
    SharedBank = 2,
    Trade = 3,
    WorldContainer = 4,
    Limbo = 5,
    Tribute = 6,
    TrophyTribute = 7,
    GuildTribute = 8,
    Merchant = 9,
    Deleted = 10,
    Corpse = 11,
    Bazaar = 12,
    Inspect = 13,
    RealEstate = 14,
    ViewModPC = 15,
    ViewModBank = 16,
    ViewModSharedBank = 17,
    ViewModLimbo = 18,
    AltStorage = 19,
    Archived = 20,
    Mail = 21,
    GuildTrophyTribute = 22,
    Krono = 23,
    Other = 24,
    MercenaryItems = 25,
    ViewModMercenaryItems = 26,
    MountKeyRing = 27,
    ViewModMountKeyRing = 28,
    IllusionKeyRing = 29,
    ViewModIllusionKeyRing = 30,
    FamiliarKeyRing = 31,
    ViewModFamiliarKeyRing = 32,
    HerosForgeKeyRing = 33,
    ViewModHerosForgeKeyRing = 34,
    TeleportationKeyRing = 35,
    ViewModTeleportationKeyRing = 36,
    Overflow = 37,
    DragonsHoard = 38,
    TradeskillDepot = 39,
    GuildTradeskillDepot = 40
}

public static class StorageSystemExtensions
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // StorageSystemNames
    //
    // Printable names for the storage systems of the item serialization header,
    // keyed by storage system.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static readonly Dictionary<StorageSystem, string> StorageSystemNames = new Dictionary<StorageSystem, string>()
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
    // ToDisplayString
    //
    // Looks up the printable name of a storage system.  Returns a descriptive placeholder for
    // unknown values rather than throwing.
    //
    // storageSystem:  The storage system to query.
    //
    // Returns the storage system name, or "Unknown(n)" when the value is not in the table.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static string ToDisplayString(this StorageSystem storageSystem)
    {
        if (StorageSystemNames.TryGetValue(storageSystem, out string? name))
        {
            return name;
        }

        DebugLog.Write(LogChannel.Fields, "StorageSystemExtensions.ToDisplayString: storage system " +
            (uint)storageSystem + " not in table, returning 'Unknown'.", LogLevel.Warn);
        return "Unknown(" + (uint)storageSystem + ")";
    }
}