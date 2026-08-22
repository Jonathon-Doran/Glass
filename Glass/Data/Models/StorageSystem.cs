namespace Glass.Data.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// StorageSystem
//
// Storage systems that can hold an item, with each member set to the raw wire
// value from the item serialization header.
///////////////////////////////////////////////////////////////////////////////////////////////
public enum StorageSystem : uint
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