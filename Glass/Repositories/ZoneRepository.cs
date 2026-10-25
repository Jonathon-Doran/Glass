using Glass.Core.Logging;
using Glass.Data.Models;
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
            { (ZoneId) 394, "Crescent Reach" }
        };
        DebugLog.Write(LogChannel.Database, "ZoneRepository: singleton instance created with "
            + _zoneNames.Count + " zone names.", LogLevel.Trace);
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
            + " zone ids.", LogLevel.Trace);

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
    // Resolves a spawn id within a zone to a display name.  The zone's mob spawns are checked first, then
    // the characters placed in the zone.  A zone id that is None, or a spawn id matching neither, yields
    // "<unknown>".
    //
    // zoneId:   The zone the spawn id is valid in.
    // spawnId:  The server assigned spawn id within that zone.
    //
    // Returns the spawn's name, or "<unknown>" when the spawn id is not known in that zone.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public string LookupSpawnName(ZoneId zoneId, SpawnId spawnId)
    {
        if (zoneId.Exists == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "ZoneRepository.LookupSpawnName: zoneId is None for spawnId="
                + spawnId, LogLevel.Trace);
            return "<unknown>";
        }

        // Return the name from the character repo if foiund
        Character? character = CharacterRepository.Instance.GetBySpawnId(zoneId, spawnId);
        if (character != null)
        {
            return character.Name;
        }

        // Otherwise search the mob repo
        if (MobRepository.Instance.TryGetBySpawnId(zoneId, spawnId, out Spawn? spawn))
        {
            return spawn.Name ?? "<unnamed>";
        }

        DebugLog.Write(LogChannel.Opcodes, "ZoneRepository.LookupSpawnName: zoneId=" + zoneId + " spawnId="
            + spawnId + " is not a known mob or character.", LogLevel.Trace);
        return "<unknown>";
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // IsKnownSpawn
    //
    // Tests whether a spawn id is currently known in a zone, either as a character placed in the zone or as
    // one of the zone's mob spawns.  A zone id that is None is never known.
    //
    // zoneId:   The zone the spawn id is valid in.
    // spawnId:  The server assigned spawn id within that zone.
    //
    // Returns true when the spawn id is a known character or mob in that zone.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool IsKnownSpawn(ZoneId zoneId, SpawnId spawnId)
    {
        if (!zoneId.Exists)
        {
            return false;
        }

        if (CharacterRepository.Instance.GetBySpawnId(zoneId, spawnId) != null)
        {
            return true;
        }

        return MobRepository.Instance.TryGetBySpawnId(zoneId, spawnId, out Spawn? spawn);
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
            + " sessions.", LogLevel.Trace);
        return count;
    }
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SessionLeft
    //
    // Records one fewer live zone session connected to the given zone.  A zone id that is None, or a
    // zone with no recorded sessions, is rejected and the count is unchanged.  When the count reaches
    // zero the zone's entry is removed.
    //
    // zoneId:  The zone the session disconnected from.
    //
    // Returns the number of live sessions still connected to the zone; zero when this was the last.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public uint SessionLeft(ZoneId zoneId)
    {
        if (!zoneId.Exists)
        {
            DebugLog.Write(LogChannel.Sessions, "ZoneRepository.SessionLeft: zoneId is None, count unchanged.",
                LogLevel.Error);
            return 0;
        }

        uint count = 0;
        if (!_sessionCounts.TryGetValue(zoneId, out count) || (count == 0))
        {
            DebugLog.Write(LogChannel.Sessions, "ZoneRepository.SessionLeft: zoneId=" + zoneId
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

        DebugLog.Write(LogChannel.Database, "ZoneRepository.SessionLeft: zoneId=" + zoneId + " now " + count
            + " sessions.", LogLevel.Trace);
        return count;
    }

}
