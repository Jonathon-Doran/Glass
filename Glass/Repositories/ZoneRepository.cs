using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.World;
using System.Collections.Generic;
using System.Numerics;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ZoneRepository
//
// In-memory repository of zone data. Currently holds only a static zone id to zone name mapping.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class ZoneRepository
{
    private static ZoneRepository? _instance = null;

    private readonly Dictionary<ZoneId, string> _zoneNames;
    private readonly Dictionary<ZoneId, uint> _sessionCounts;
    private readonly Dictionary<ZoneId, Dictionary<SpawnId, EntityId>> _entityIdsByZoneSpawn;

    private const uint DynamicZoneThreshold = 1000000;
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // Lazy singleton accessor. The instance is created on first access.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static ZoneRepository Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new ZoneRepository();
            }
            return _instance;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ZoneRepository
    //
    // Private constructor. Populates the zone name mapping with the known zones.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private ZoneRepository()
    {
        _sessionCounts = new Dictionary<ZoneId, uint>();
        _entityIdsByZoneSpawn = new Dictionary<ZoneId, Dictionary<SpawnId, EntityId>>();

        _zoneNames = new Dictionary<ZoneId, string>()
        {
            { (ZoneId) 1, "South Qeynos" },
            { (ZoneId) 2, "North Qeynos" },
            { (ZoneId) 3, "Surefall Glade" },
            { (ZoneId) 4, "Qeynos Hills" },
            { (ZoneId) 5, "Highpass Hold" },
            { (ZoneId) 6, "Highkeep" },
            { (ZoneId) 8, "North Freeport" },
            { (ZoneId) 9, "West Freeport" },
            { (ZoneId) 10, "East Freeport" },
            { (ZoneId) 11, "Clan RunnyEye" },
            { (ZoneId) 12, "West Karana" },
            { (ZoneId) 13, "North Karana" },
            { (ZoneId) 14, "South Karana" },
            { (ZoneId) 15, "East Karana" },
            { (ZoneId) 16, "Gorge of King Xorbb" },
            { (ZoneId) 17, "BlackBurrow" },
            { (ZoneId) 18, "Infected Paw" },
            { (ZoneId) 19, "Rivervale" },
            { (ZoneId) 20, "Kithicor Forest" },
            { (ZoneId) 21, "West Commonlands" },
            { (ZoneId) 22, "East Commonlands" },
            { (ZoneId) 23, "Erudin Palace" },
            { (ZoneId) 24, "Erudin"},
            { (ZoneId) 25, "Nektulos Forest" },
            { (ZoneId) 26, "Sunset Home" },
            { (ZoneId) 27, "Lavastorm Mountains" },
            { (ZoneId) 28, "Nektropos" },
            { (ZoneId) 29, "Halas" },
            { (ZoneId) 30, "Everfrost Peaks" },
            { (ZoneId) 31, "Solusek's Eye" },
            { (ZoneId) 32, "Nagafen's Lair" },
            { (ZoneId) 33, "Misty Thicket" },
            { (ZoneId) 34, "North Ro" },
            { (ZoneId) 35, "South Ro" },
            { (ZoneId) 36, "Befallen" },
            { (ZoneId) 37, "Oasis of Marr" },
            { (ZoneId) 38, "Toxxulia Forest" },
            { (ZoneId) 39, "The Ruins of Old Paineel" },
            { (ZoneId) 40, "Neriak Foreign Quarter" },
            { (ZoneId) 41, "Neriak Commons" },
            { (ZoneId) 42, "Neriak Third Gate" },
            { (ZoneId) 43, "Neriak Palace" },
            { (ZoneId) 44, "Najena" },
            { (ZoneId) 45, "Qeynos Catacombs" },
            { (ZoneId) 46, "Innothule Swamp" },
            { (ZoneId) 47, "The Feerott" },
            { (ZoneId) 48, "Cazic-Thule" },
            { (ZoneId) 49, "Oggok" },
            { (ZoneId) 50, "Mountains of Rathe" },
            { (ZoneId) 51, "Lake Rathetear" },
            { (ZoneId) 52, "Gukta" },
            { (ZoneId) 53, "Aviak Village" },
            { (ZoneId) 54, "Greater Faydark" },
            { (ZoneId) 55, "Ak'Anon" },
            { (ZoneId) 56, "Steamfont Mountains" },
            { (ZoneId) 57, "Lesser Faydark" },
            { (ZoneId) 58, "Clan Crushbone" },
            { (ZoneId) 59, "Castle Mistmoore" },
            { (ZoneId) 60, "Kaladim A" },
            { (ZoneId) 61, "Felwithe A" },
            { (ZoneId) 62, "Felwithe B" },
            { (ZoneId) 63, "Estate of Unrest" },
            { (ZoneId) 64, "Kedge Keep" },
            { (ZoneId) 65, "Upper Guk" },
            { (ZoneId) 66, "Lower Guk" },
            { (ZoneId) 67, "Kaladim B" },
            { (ZoneId) 68, "Butcherblock Mountains" },
            { (ZoneId) 69, "Ocean of Tears" },
            { (ZoneId) 70, "Dagnor's Cauldron" },
            { (ZoneId) 71, "Plane of Sky" },
            { (ZoneId) 72, "Plane of Fear" },
            { (ZoneId) 73, "Permafrost Keep" },
            { (ZoneId) 74, "Kerra Isle" },
            { (ZoneId) 75, "Paineel" },
            { (ZoneId) 76, "The Plane of Hate" },
            { (ZoneId) 77, "The Arena" },
            { (ZoneId) 78, "The Field of Bone" },
            { (ZoneId) 79, "Warsliks Woods" },
            { (ZoneId) 80, "Temple of Solusek Ro" },
            { (ZoneId) 81, "Temple of Droga" },
            { (ZoneId) 82, "West Cabilis" },
            { (ZoneId) 83, "Swamp of No Hope" },
            { (ZoneId) 84, "Firiona Vie" },
            { (ZoneId) 85, "Lake of Ill Omen" },
            { (ZoneId) 86, "Dreadlands" },
            { (ZoneId) 87, "Burning Woods" },
            { (ZoneId) 88, "Kaesora" },
            { (ZoneId) 89, "Old Sebilis" },
            { (ZoneId) 90, "City of Mist" },
            { (ZoneId) 91, "Skyfire Mountains" },
            { (ZoneId) 92, "Frontier Mountains" },
            { (ZoneId) 93, "The Overthere" },
            { (ZoneId) 94, "The Emerald Jungle" },
            { (ZoneId) 95, "Trakanon's Teeth" },
            { (ZoneId) 96, "Timorous Deep" },
            { (ZoneId) 97, "Kurn's Tower" },
            { (ZoneId) 98, "Erud's Crossing" },
            { (ZoneId) 100, "Stonebrunt Mountains" },
            { (ZoneId) 101, "The Warrens" },
            { (ZoneId) 102, "Karnor's Castle" },
            { (ZoneId) 103, "Chardok" },
            { (ZoneId) 104, "Dalnir" },
            { (ZoneId) 105, "Howling Stones" },
            { (ZoneId) 106, "East Cabilis" },
            { (ZoneId) 107, "The Mines of Nurga" },
            { (ZoneId) 108, "Veeshan's Peak" },
            { (ZoneId) 109, "Veksar" },
            { (ZoneId) 110, "Iceclad Ocean" },
            { (ZoneId) 111, "Tower of Frozen Shadow" },
            { (ZoneId) 112, "Velketor's Labyrinth" },
            { (ZoneId) 113, "Kael Drakkal" },
            { (ZoneId) 114, "Skyshrine" },
            { (ZoneId) 115, "Thurgadin" },
            { (ZoneId) 116, "Eastern Wastes" },
            { (ZoneId) 117, "Cobalt Scar"},
            { (ZoneId) 118, "Great Divide" },
            { (ZoneId) 119, "The Wakening Lands" },
            { (ZoneId) 120, "Western Wastes" },
            { (ZoneId) 121, "Crystal Caverns" },
            { (ZoneId) 123, "Dragon Necropolis" },
            { (ZoneId) 124, "Temple of Veeshan" },
            { (ZoneId) 125, "Siren's Grotto" },
            { (ZoneId) 126, "Plane of Mischief" },
            { (ZoneId) 127, "Plane of Growth" },
            { (ZoneId) 128, "Sleeper's Tomb" },
            { (ZoneId) 129, "Icewell Keep" },
            { (ZoneId) 130, "Marauder's Mire" },
            { (ZoneId) 150, "Shadow Haven" },
            { (ZoneId) 151, "The Bazaar" },
            { (ZoneId) 152, "The Nexus" },
            { (ZoneId) 153, "Echo Caverns"},
            { (ZoneId) 154, "Acrylia Caverns" },
            { (ZoneId) 155, "Shar'Vahl" },
            { (ZoneId) 156, "Paludal Caverns" },
            { (ZoneId) 157, "Fungus Grove" },
            { (ZoneId) 158, "Vex Thal" },
            { (ZoneId) 159, "Sanctus Seru" },
            { (ZoneId) 160, "Katta Castellum" },
            { (ZoneId) 161, "Netherbian Lair" },
            { (ZoneId) 162, "Ssraeshza Temple" },
            { (ZoneId) 163, "Grieg's End" },
            { (ZoneId) 164, "The Deep" },
            { (ZoneId) 165, "Shadeweaver's Thicket" },
            { (ZoneId) 166, "Hollowshade Moor" },
            { (ZoneId) 167, "Grimling Forest" },
            { (ZoneId) 168, "Marus Seru" },
            { (ZoneId) 169, "Mons Letalis" },
            { (ZoneId) 170, "The Twilight Sea" },
            { (ZoneId) 171, "The Grey" },
            { (ZoneId) 172, "The Tenebrous Mountains" },
            { (ZoneId) 173, "The Maiden's Eye" },
            { (ZoneId) 174, "Dawnshroud Peaks" },
            { (ZoneId) 175, "The Scarlet Desert" },
            { (ZoneId) 176, "The Umbral Plains" },
            { (ZoneId) 179, "Akeva Ruins" },
            { (ZoneId) 180, "The Arena 2" },
            { (ZoneId) 181, "The Jaggedpine Forest" },
            { (ZoneId) 182, "Nedaria's Landing" },
            { (ZoneId) 183, "Tutorial" },
            { (ZoneId) 184, "Loading 1" },
            { (ZoneId) 185, "Loading 2" },
            { (ZoneId) 186, "Plane of Hate B" },
            { (ZoneId) 187, "Shadowrest" },
            { (ZoneId) 188, "The Mines of Gloomingdeep A" },
            { (ZoneId) 189, "The Mines of Gloomingdeep B" },
            { (ZoneId) 190, "Loading 3" },
            { (ZoneId) 200, "Ruins of Lxanvom" },
            { (ZoneId) 201, "The Plane of Justice"},
            { (ZoneId) 202, "The Plane of Knowledge" },
            { (ZoneId) 203, "The Plane of Tranquility" },
            { (ZoneId) 204, "The Plane of Nightmare" },
            { (ZoneId) 205, "The Plane of Disease" },
            { (ZoneId) 206, "The Plane of Innovation" },
            { (ZoneId) 207, "The Plane of Torment" },
            { (ZoneId) 208, "The Plane of Valor" },
            { (ZoneId) 209, "The Bastion of Thunder" },
            { (ZoneId) 210, "The Plane of Storms" },
            { (ZoneId) 211, "The Halls of Honor" },
            { (ZoneId) 212, "Solusek Ro's Tower" },
            { (ZoneId) 213, "The Plane of War" },
            { (ZoneId) 214, "Drunder, Fortress of Zek" },
            { (ZoneId) 215, "Eryslai, the Kingdom of Wind" },
            { (ZoneId) 216, "Reef of Coirnav" },
            { (ZoneId) 217, "Doomfire, The Burning Lands" },
            { (ZoneId) 218, "Degarlson, The Earthen Badlands" },
            { (ZoneId) 219, "Plane of Time A" },
            { (ZoneId) 220, "Temple of Marr" },
            { (ZoneId) 221, "Lair of Terris Thule" },
            { (ZoneId) 222, "Stronghold of the Twelve" },
            { (ZoneId) 223, "Plane of Time B" },
            { (ZoneId) 224, "The Gulf of Gunthak" },
            { (ZoneId) 225, "Dulak's Harbor" },
            { (ZoneId) 226, "Torgiran Mines" },
            { (ZoneId) 227, "Crypt of Nadox" },
            { (ZoneId) 228, "Hate's Fury" },
            { (ZoneId) 229, "LDON The Cauldron of Lost Souls" },
            { (ZoneId) 230, "LDON The Bloodied Quarries" },
            { (ZoneId) 231, "LDON The Sunken Library" },
            { (ZoneId) 232, "LDON The Silent Gallery" },
            { (ZoneId) 233, "LDON The Forlorn Caverns" },
            { (ZoneId) 234, "LDON The Drowning Crypt" },
            { (ZoneId) 235, "LDON The Halls of War" },
            { (ZoneId) 236, "LDON The Shifting Tower" },
            { (ZoneId) 237, "LDON The Maw of the Menagerie" },
            { (ZoneId) 238, "LDON The Dreary Grotto" },
            { (ZoneId) 239, "LDON The Ancient Aqueducts" },
            { (ZoneId) 240, "LDON The Wind Bridges" },
            { (ZoneId) 241, "LDON The Fading Temple" },
            { (ZoneId) 242, "LDON The Spider Den" },
            { (ZoneId) 243, "LDON The Asylum of Invoked Stone" },
            { (ZoneId) 244, "LDON The Mushroom Grove" },
            { (ZoneId) 245, "LDON The Gladiator Pits" },
            { (ZoneId) 246, "LDON The Royal Observatory" },
            { (ZoneId) 247, "LDON The Hushed Banquet" },
            { (ZoneId) 248, "LDON The Chambers of Eternal Affliction" },
            { (ZoneId) 249, "LDON The Foreboding Prison" },
            { (ZoneId) 250, "LDON The Drudge Hollows" },
            { (ZoneId) 251, "LDON The River of Recollection" },
            { (ZoneId) 252, "LDON The Frosted Halls" },
            { (ZoneId) 253, "LDON The Sepulcher of the Damned" },
            { (ZoneId) 254, "LDON The Chapel of the Witnesses" },
            { (ZoneId) 255, "LDON The Fortified Lair of the Taskmasters" },
            { (ZoneId) 256, "LDON The Sandfall Corridors" },
            { (ZoneId) 257, "LDON The Forgotten Wastes" },
            { (ZoneId) 258, "LDON The Ritualistic Summoning Grounds" },
            { (ZoneId) 259, "LDON The Root Garden" },
            { (ZoneId) 260, "LDON The Hidden Vale" },
            { (ZoneId) 261, "LDON The Balancing Chamber" },
            { (ZoneId) 262, "LDON The Heart of the Menagerie" },
            { (ZoneId) 263, "LDON The Cesspits of Putrescence" },
            { (ZoneId) 264, "LDON The Accursed Sanctuary" },
            { (ZoneId) 265, "LDON The Blazing Forge" },
            { (ZoneId) 266, "LDON The Sweeping Tides" },
            { (ZoneId) 267, "LDON The Morbid Laboratory" },
            { (ZoneId) 268, "LDON The Aisles of Blood" },
            { (ZoneId) 269, "LDON The Arena of Chance" },
            { (ZoneId) 270, "LDON The Antiquated Palace" },
            { (ZoneId) 271, "LDON The Theater of Imprisoned Horrors" },
            { (ZoneId) 272, "LDON The Halls of Sanguinary Rites" },
            { (ZoneId) 273, "LDON The Barracks of War" },
            { (ZoneId) 274, "LDON The Prismatic Corridors" },
            { (ZoneId) 275, "LDON The Grand Library" },
            { (ZoneId) 276, "LDON The Infernal Sanctuary" },
            { (ZoneId) 277, "Chardok-B The Halls of Betrayal" },
            { (ZoneId) 278, "The Caverns of Exile" },
            { (ZoneId) 279, "Abysmal Sea" },
            { (ZoneId) 280, "Natimbi, The Broken Shores" },
            { (ZoneId) 281, "Qinimi, Court of Nihilia" },
            { (ZoneId) 282, "Riwwi, Coliseum of Games" },
            { (ZoneId) 283, "Barindu, Hanging Gardens" },
            { (ZoneId) 284, "Ferubi, Forgotten Temple of Taelosia" },
            { (ZoneId) 285, "Sewers of Nihilia, Pool of Sludge" },
            { (ZoneId) 286, "Sewers of Nihilia, Lair of Trapped Ones" },
            { (ZoneId) 287, "Sewers of Nihilia, Purifying Plant" },
            { (ZoneId) 288, "Sewers of Nihilia, the Crematory" },
            { (ZoneId) 289, "Tipt, Treacherous Crags" },
            { (ZoneId) 290, "Vxed, The Crumbling Caverns" },
            { (ZoneId) 291, "Yxtta, Pulpit of Exiles" },
            { (ZoneId) 292, "Uqua, The Ocean God Chantry" },
            { (ZoneId) 293, "Kod'Taz, Broken Trial Grounds" },
            { (ZoneId) 294, "Ikkinz, Chambers of Destruction" },
            { (ZoneId) 295, "Qvic, Prayer Grounds of Calling" },
            { (ZoneId) 296, "Inktu`Ta, The Unmasked Chapel" },
            { (ZoneId) 297, "Txevu, Lair of the Elite" },
            { (ZoneId) 298, "Tacvi, Seat of the Slaver" },
            { (ZoneId) 299, "Qvic, the Hidden Vault" },
            { (ZoneId) 300, "Wall of Slaughter" },
            { (ZoneId) 301, "The Bloodfields" },
            { (ZoneId) 302, "Dranik's Scar" },
            { (ZoneId) 303, "Nobles' Causeway" },
            { (ZoneId) 304, "MPG-A" },
            { (ZoneId) 305, "MPG-B" },
            { (ZoneId) 306, "MPG-C" },
            { (ZoneId) 307, "MPG-D" },
            { (ZoneId) 308, "MPG-E" },
            { (ZoneId) 309, "MPG-F" },
            { (ZoneId) 344, "The Guild Lobby" },
            { (ZoneId) 394, "Crescent Reach" }
        };
        DebugLog.Write(LogChannel.Database, "ZoneRepository: singleton instance created with "
            + _zoneNames.Count + " zone names.", defaultLoglevel);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetZoneIds
    //
    // Returns the zone ids present in the zone name mapping, in ascending numeric order.  The returned
    // list is a new copy on every call.
    //
    // Returns a List<ZoneId> of every mapped zone id; empty if the mapping is empty.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<ZoneId> GetZoneIds()
    {
        List<ZoneId> zoneIds = new List<ZoneId>(_zoneNames.Keys);

        zoneIds.Sort((ZoneId left, ZoneId right) => left.Value.CompareTo(right.Value));

        DebugLog.Write(LogChannel.Database, "ZoneRepository.GetZoneIds: returning " + zoneIds.Count
            + " zone ids.", defaultLoglevel);

        return zoneIds;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetZoneName
    //
    // Looks up a zone name by zone id. Returns a descriptive string for unknown values rather
    // than throwing.
    //
    // zoneId:  The zone id to query.
    //
    // Returns the zone name, or "Unknown(0x..)" when the zone id is not in the mapping.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public string GetZoneName(ZoneId zoneId)
    {
        if (_zoneNames.TryGetValue(zoneId, out string? name))
        {
            return name;
        }

        DebugLog.Write(LogChannel.Database, "ZoneRepository.GetZoneName: zoneId=0x" + zoneId.Value.ToString("X2")
            + " not in map, returning 'Unknown'.", LogLevel.Warn);
        return "Unknown(0x" + zoneId.Value.ToString("X2") + ")";
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LookupSpawnName
    //
    // Resolves a spawn id within a zone to a display name.  A spawn id of 0 refers to nothing and yields "None".
    // Otherwise the entity holding the spawn id in the zone supplies the name; an entity whose name is empty yields
    // "<unnamed>", and a spawn id with no entity, or a zone id that is None, yields "<unknown>".
    //
    // zoneId:   The zone the spawn id is valid in.
    // spawnId:  The server assigned spawn id within that zone.
    //
    // Returns the spawn's name, "None", "<unnamed>", or "<unknown>".
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public string LookupSpawnName(ZoneId zoneId, SpawnId spawnId)
    {
        // A spawn id of 0 refers to nothing, such as a cleared target
        if (spawnId.Value == 0)
        {
            DebugLog.Write(LogChannel.Opcodes, "ZoneRepository.LookupSpawnName: zoneId=" + zoneId
                + " spawnId is 0, name is None.", defaultLoglevel);
            return "None";
        }

        Entity? entity = LookupEntity(zoneId, spawnId);
        if (entity == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "ZoneRepository.LookupSpawnName: zoneId=" + zoneId + " spawnId="
                + spawnId + " has no entity, name is <unknown>.", defaultLoglevel);
            return "<unknown>";
        }

        if (entity.Name.Length == 0)
        {
            DebugLog.Write(LogChannel.Opcodes, "ZoneRepository.LookupSpawnName: zoneId=" + zoneId + " spawnId="
                + spawnId + " entityId=" + entity.EntityId + " has an empty name, name is <unnamed>.",
                defaultLoglevel);
            return "<unnamed>";
        }

        return entity.Name;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LookupEntity
    //
    // Resolves a spawn id within a zone to the entity holding it: the spawn id is converted to the EntityId placed
    // there, and the entity is found by that EntityId.  A zone id that is None, a spawn id of 0, or a spawn id with no
    // placement in the zone yields null.
    //
    // zoneId:   The zone the spawn id is valid in.
    // spawnId:  The server assigned spawn id within that zone.
    //
    // Returns the entity holding the spawn id in that zone, or null when there is none.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Entity? LookupEntity(ZoneId zoneId, SpawnId spawnId)
    {
        if (zoneId.Exists == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "ZoneRepository.LookupEntity: zoneId is None for spawnId=" + spawnId
                + ", no entity.", defaultLoglevel);
            return null;
        }

        // A spawn id of 0 refers to nothing, such as a cleared target
        if (spawnId.Value == 0)
        {
            DebugLog.Write(LogChannel.Opcodes, "ZoneRepository.LookupEntity: zoneId=" + zoneId
                + " spawnId is 0, no entity.", defaultLoglevel);
            return null;
        }

        EntityId entityId = ResolveEntityId(zoneId, spawnId);
        if (entityId.Exists == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "ZoneRepository.LookupEntity: zoneId=" + zoneId + " spawnId=" + spawnId
                + " has no placement, no entity.", defaultLoglevel);
            return null;
        }

        Entity? entity = EntityRepository.Instance.Lookup(entityId);
        if (entity == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "ZoneRepository.LookupEntity: zoneId=" + zoneId + " spawnId=" + spawnId
                + " is placed as entityId=" + entityId + ", but no entity has that id.", LogLevel.Warn);
            return null;
        }

        DebugLog.Write(LogChannel.Opcodes, "ZoneRepository.LookupEntity: zoneId=" + zoneId + " spawnId=" + spawnId
            + " is '" + entity.Name + "' entityId=" + entityId + ".", defaultLoglevel);
        return entity;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetZoneEntityIds
    //
    // Returns the EntityIds placed in the given zone, read from the placement table.  The list is a new copy on every
    // call, in no particular order.  A zone id that is None or has no placements yields an empty list.
    //
    // zoneId:  The zone whose placed EntityIds are returned.
    //
    // Returns a new list of the zone's placed EntityIds; empty when there are none.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<EntityId> GetZoneEntityIds(ZoneId zoneId)
    {
        List<EntityId> entityIds = new List<EntityId>();

        if (!zoneId.Exists)
        {
            DebugLog.Write(LogChannel.Database, "ZoneRepository.GetZoneEntityIds: zoneId is None.", LogLevel.Warn);
            return entityIds;
        }

        if (!_entityIdsByZoneSpawn.TryGetValue(zoneId, out Dictionary<SpawnId, EntityId>? placements))
        {
            DebugLog.Write(LogChannel.Database, "ZoneRepository.GetZoneEntityIds: zoneId=" + zoneId
                + " has no placements.", defaultLoglevel);
            return entityIds;
        }

        entityIds.AddRange(placements.Values);

        DebugLog.Write(LogChannel.Database, "ZoneRepository.GetZoneEntityIds: zoneId=" + zoneId + " returning "
            + entityIds.Count + " entityIds.", defaultLoglevel);
        return entityIds;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetZoneEntityIds
    //
    // Returns the EntityIds of the given type placed in the given zone, read from the placement table.  The list is a
    // new copy on every call, in no particular order.  A zone id that is None or has no placements of the type yields
    // an empty list.
    //
    // zoneId:  The zone whose placed EntityIds are returned.
    // type:    The entity type to return.
    //
    // Returns a new list of the zone's placed EntityIds of the type; empty when there are none.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<EntityId> GetZoneEntityIds(ZoneId zoneId, EntityType type)
    {
        List<EntityId> entityIds = new List<EntityId>();

        if (!zoneId.Exists)
        {
            DebugLog.Write(LogChannel.Database, "ZoneRepository.GetZoneEntityIds: zoneId is None, type=" + type + ".",
                LogLevel.Warn);
            return entityIds;
        }

        if (!_entityIdsByZoneSpawn.TryGetValue(zoneId, out Dictionary<SpawnId, EntityId>? placements))
        {
            DebugLog.Write(LogChannel.Database, "ZoneRepository.GetZoneEntityIds: zoneId=" + zoneId
                + " has no placements, type=" + type + ".", defaultLoglevel);
            return entityIds;
        }

        foreach (EntityId entityId in placements.Values)
        {
            if (entityId.Type == type)
            {
                entityIds.Add(entityId);
            }
        }

        DebugLog.Write(LogChannel.Database, "ZoneRepository.GetZoneEntityIds: zoneId=" + zoneId + " returning "
            + entityIds.Count + " of " + placements.Count + " entityIds of type " + type + ".", defaultLoglevel);
        return entityIds;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PlaceEntity
    //
    // Records that the entity with the given EntityId holds the given spawn id in the given zone.  The zone's table is
    // created on first use.  A spawn id already held by another entity in that zone is reassigned to this one, since
    // the server reuses spawn ids.  A zone id that is None, a spawn id that is None or 0, or an EntityId that is None
    // is rejected and nothing is recorded.
    //
    // zoneId:    The zone the spawn id is valid in.
    // spawnId:   The server assigned spawn id within that zone.
    // entityId:  The entity holding the spawn id.
    //
    // Returns true when the placement was recorded, false when it was rejected.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool PlaceEntity(ZoneId zoneId, SpawnId spawnId, EntityId entityId)
    {
        if ((zoneId.Exists == false) || (spawnId.Exists == false) || (spawnId.Value == 0) || (entityId.Exists == false))
        {
            DebugLog.Write(LogChannel.Database, "ZoneRepository.PlaceEntity: zoneId=" + zoneId + " spawnId=" + spawnId
                + " entityId=" + entityId + " is not a valid placement, not recorded.", LogLevel.Error);
            return false;
        }

        if (_entityIdsByZoneSpawn.TryGetValue(zoneId, out Dictionary<SpawnId, EntityId>? zoneTable) == false)
        {
            zoneTable = new Dictionary<SpawnId, EntityId>();
            _entityIdsByZoneSpawn[zoneId] = zoneTable;
            DebugLog.Write(LogChannel.Database, "ZoneRepository.PlaceEntity: created placement table for zoneId="
                + zoneId + ".", defaultLoglevel);
        }

        if (zoneTable.TryGetValue(spawnId, out EntityId previous) && (previous != entityId))
        {
            DebugLog.Write(LogChannel.Database, "ZoneRepository.PlaceEntity: zoneId=" + zoneId + " spawnId=" + spawnId
                + " reassigned from entityId=" + previous + " to " + entityId + ".", defaultLoglevel);
        }

        zoneTable[spawnId] = entityId;
        DebugLog.Write(LogChannel.Database, "ZoneRepository.PlaceEntity: zoneId=" + zoneId + " spawnId=" + spawnId
            + " is entityId=" + entityId + ".", defaultLoglevel);
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RemovePlacement
    //
    // Removes the record that the given entity holds the given spawn id in the given zone.  Nothing is removed when
    // the spawn id is not recorded in that zone, or is recorded for a different entity.  A zone table left empty is
    // removed.
    //
    // zoneId:    The zone the spawn id is valid in.
    // spawnId:   The server assigned spawn id within that zone.
    // entityId:  The entity expected to hold the spawn id.
    //
    // Returns true when the placement was removed, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool RemovePlacement(ZoneId zoneId, SpawnId spawnId, EntityId entityId)
    {
        if (_entityIdsByZoneSpawn.TryGetValue(zoneId, out Dictionary<SpawnId, EntityId>? zoneTable) == false)
        {
            DebugLog.Write(LogChannel.Database, "ZoneRepository.RemovePlacement: zoneId=" + zoneId
                + " has no placement table, nothing removed for entityId=" + entityId + ".", defaultLoglevel);
            return false;
        }

        if (zoneTable.TryGetValue(spawnId, out EntityId held) == false)
        {
            DebugLog.Write(LogChannel.Database, "ZoneRepository.RemovePlacement: zoneId=" + zoneId + " spawnId="
                + spawnId + " is not recorded, nothing removed for entityId=" + entityId + ".", defaultLoglevel);
            return false;
        }

        if (held != entityId)
        {
            DebugLog.Write(LogChannel.Database, "ZoneRepository.RemovePlacement: zoneId=" + zoneId + " spawnId="
                + spawnId + " is held by entityId=" + held + ", not " + entityId + ", nothing removed.",
                defaultLoglevel);
            return false;
        }

        zoneTable.Remove(spawnId);
        if (zoneTable.Count == 0)
        {
            _entityIdsByZoneSpawn.Remove(zoneId);
        }

        DebugLog.Write(LogChannel.Database, "ZoneRepository.RemovePlacement: zoneId=" + zoneId + " spawnId=" + spawnId
            + " entityId=" + entityId + " removed, " + zoneTable.Count + " placements left in the zone.",
            defaultLoglevel);
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ClearZonePlacements
    //
    // Removes every placement recorded for the given zone.  The entities themselves are not affected.  A zone id that
    // is None, or a zone with no placement table, removes nothing.
    //
    // zoneId:  The zone whose placements are removed.
    //
    // Returns the number of placements removed.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public uint ClearZonePlacements(ZoneId zoneId)
    {
        if (zoneId.Exists == false)
        {
            DebugLog.Write(LogChannel.Database, "ZoneRepository.ClearZonePlacements: zoneId is None, nothing removed.",
                LogLevel.Warn);
            return 0;
        }

        if (_entityIdsByZoneSpawn.TryGetValue(zoneId, out Dictionary<SpawnId, EntityId>? zoneTable) == false)
        {
            DebugLog.Write(LogChannel.Database, "ZoneRepository.ClearZonePlacements: zoneId=" + zoneId
                + " has no placement table, nothing removed.", defaultLoglevel);
            return 0;
        }

        uint removed = (uint)zoneTable.Count;
        _entityIdsByZoneSpawn.Remove(zoneId);

        DebugLog.Write(LogChannel.Database, "ZoneRepository.ClearZonePlacements: zoneId=" + zoneId + " removed "
            + removed + " placements.", defaultLoglevel);
        return removed;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ResolveEntityId
    //
    // Finds the EntityId of the entity holding the given spawn id in the given zone.
    //
    // zoneId:   The zone the spawn id is valid in.
    // spawnId:  The server assigned spawn id within that zone.
    //
    // Returns the EntityId, or EntityId.None when no entity holds the spawn id in that zone.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public EntityId ResolveEntityId(ZoneId zoneId, SpawnId spawnId)
    {
        if ((_entityIdsByZoneSpawn.TryGetValue(zoneId, out Dictionary<SpawnId, EntityId>? zoneTable))
            && (zoneTable.TryGetValue(spawnId, out EntityId entityId)))
        {
            DebugLog.Write(LogChannel.Database, "ZoneRepository.ResolveEntityId: zoneId=" + zoneId + " spawnId="
                + spawnId + " is entityId=" + entityId + ".", defaultLoglevel);
            return entityId;
        }

        DebugLog.Write(LogChannel.Database, "ZoneRepository.ResolveEntityId: zoneId=" + zoneId + " spawnId=" + spawnId
            + " has no entity.", defaultLoglevel);
        return EntityId.None;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // IsKnownSpawn
    //
    // Tests whether a spawn id is currently held by an entity in a zone.  A zone id that is None, or a spawn id of
    // 0, is never known.
    //
    // zoneId:   The zone the spawn id is valid in.
    // spawnId:  The server assigned spawn id within that zone.
    //
    // Returns true when an entity holds the spawn id in that zone, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool IsKnownSpawn(ZoneId zoneId, SpawnId spawnId)
    {
        Entity? entity = LookupEntity(zoneId, spawnId);
        if (entity == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "ZoneRepository.IsKnownSpawn: zoneId=" + zoneId + " spawnId="
                + spawnId + " is not known.", defaultLoglevel);
            return false;
        }

        DebugLog.Write(LogChannel.Opcodes, "ZoneRepository.IsKnownSpawn: zoneId=" + zoneId + " spawnId=" + spawnId
            + " is known as entityId=" + entity.EntityId + ".", defaultLoglevel);
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SessionEntered
    //
    // Records one more live zone session connected to the given zone.  A zone id that is None is rejected
    // and the count is unchanged.
    //
    // zoneId:  The zone the session connected to.
    //
    // Returns the number of live sessions connected to the zone after this one.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public uint SessionEntered(ZoneId zoneId)
    {
        if (!zoneId.Exists)
        {
            DebugLog.Write(LogChannel.Sessions, "ZoneRepository.SessionEntered: zoneId is None, count unchanged.",
                LogLevel.Error);
            return 0;
        }

        uint count = 0;
        _sessionCounts.TryGetValue(zoneId, out count);
        count++;
        _sessionCounts[zoneId] = count;

        DebugLog.Write(LogChannel.Sessions, "ZoneRepository.SessionEntered: zoneId=" + zoneId + " now " + count
            + " sessions.", defaultLoglevel);
        return count;
    }
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SessionsLeftInZone
    //
    // Records one fewer live zone session connected to the given zone.  A zone id that is None, or a
    // zone with no recorded sessions, is rejected and the count is unchanged.  When the count reaches
    // zero the zone's entry is removed.
    //
    // zoneId:  The zone the session disconnected from.
    //
    // Returns the number of live sessions still connected to the zone; zero when this was the last.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public uint SessionsLeftInZone(ZoneId zoneId)
    {
        if (!zoneId.Exists)
        {
            DebugLog.Write(LogChannel.Sessions, "ZoneRepository.SessionsLeft: zoneId is None, count unchanged.",
                LogLevel.Error);
            return 0;
        }

        uint count = 0;
        if (!_sessionCounts.TryGetValue(zoneId, out count) || (count == 0))
        {
            DebugLog.Write(LogChannel.Sessions, "ZoneRepository.SessionsLeft: zoneId=" + zoneId
                + " has no recorded sessions, count unchanged.", LogLevel.Error);
            return 0;
        }

        count--;
        if (count == 0)
        {
            _sessionCounts.Remove(zoneId);
        }
        else
        {
            _sessionCounts[zoneId] = count;
        }

        DebugLog.Write(LogChannel.Database, "ZoneRepository.SessionsLeft: zoneId=" + zoneId + " now " + count
            + " sessions.", defaultLoglevel);
        return count;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // IsDynamicZone
    //
    // Tests whether a zone id belongs to a dynamically created zone, such as a guild hall.  Such ids are
    // at or above DynamicZoneThreshold.  A zone id that is None is not dynamic.
    //
    // zoneId:  The zone id to test.
    //
    // Returns true when the zone is dynamic, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static bool IsDynamicZone(ZoneId zoneId)
    {
        if (zoneId.Exists == false)
        {
            return false;
        }

        return zoneId.Value >= DynamicZoneThreshold;
    }
}
