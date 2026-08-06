using Glass.Core.Logging;
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

    private readonly Dictionary<uint, string> _zoneNames;

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
        _zoneNames = new Dictionary<uint, string>()
        {
            { 1, "South Qeynos" },
            { 2, "North Qeynos" },
            { 3, "Surefall Glade" },
            { 4, "Qeynos Hills" },
            { 5, "Highpass Hold" },
            { 6, "Highkeep" },
            { 8, "North Freeport" },
            { 9, "West Freeport" },
            { 10, "East Freeport" },
            { 11, "Clan RunnyEye" },
            { 12, "West Karana" },
            { 13, "North Karana" },
            { 14, "South Karana" },
            { 15, "East Karana" },
            { 16, "Gorge of King Xorbb" },
            { 17, "BlackBurrow" },
            { 18, "Infected Paw" },
            { 19, "Rivervale" },
            { 20, "Kithicor Forest" },
            { 21, "West Commonlands" },
            { 22, "East Commonlands" },
            { 23, "Erudin Palace" },
            { 24, "Erudin"},
            { 25, "Nektulos Forest" },
            { 26, "Sunset Home" },
            { 27, "Lavastorm Mountains" },
            { 28, "Nektropos" },
            { 29, "Halas" },
            { 30, "Everfrost Peaks" },
            { 31, "Solusek's Eye" },
            { 32, "Nagafen's Lair" },
            { 33, "Misty Thicket" },
            { 34, "North Ro" },
            { 35, "South Ro" },
            { 36, "Befallen" },
            { 37, "Oasis of Marr" },
            { 38, "Toxxulia Forest" },
            { 39, "The Ruins of Old Paineel" },
            { 40, "Neriak Foreign Quarter" },
            { 41, "Neriak Commons" },
            { 42, "Neriak Third Gate" },
            { 43, "Neriak Palace" },
            { 44, "Najena" },
            { 45, "Qeynos Catacombs" },
            { 46, "Innothule Swamp" },
            { 47, "The Feerott" },
            { 48, "Cazic-Thule" },
            { 49, "Oggok" },
            { 50, "Mountains of Rathe" },
            { 51, "Lake Rathetear" },
            { 52, "Gukta" },
            { 53, "Aviak Village" },
            { 54, "Greater Faydark" },
            { 55, "Ak'Anon" },
            { 56, "Steamfont Mountains" },
            { 57, "Lesser Faydark" },
            { 58, "Clan Crushbone" },
            { 59, "Castle Mistmoore" },
            { 60, "Kaladim A" },
            { 61, "Felwithe A" },
            { 62, "Felwithe B" },
            { 63, "Estate of Unrest" },
            { 64, "Kedge Keep" },
            { 65, "Upper Guk" },
            { 66, "Lower Guk" },
            { 67, "Kaladim B" },
            { 68, "Butcherblock Mountains" },
            { 69, "Ocean of Tears" },
            { 70, "Dagnor's Cauldron" },
            { 71, "Plane of Sky" },
            { 72, "Plane of Fear" },
            { 73, "Permafrost Keep" },
            { 74, "Kerra Isle" },
            { 75, "Paineel" },
            { 76, "The Plane of Hate" },
            { 77, "The Arena" },
            { 78, "The Field of Bone" },
            { 79, "Warsliks Woods" },
            { 80, "Temple of Solusek Ro" },
            { 81, "Temple of Droga" },
            { 82, "West Cabilis" },
            { 83, "Swamp of No Hope" },
            { 84, "Firiona Vie" },
            { 85, "Lake of Ill Omen" },
            { 86, "Dreadlands" },
            { 87, "Burning Woods" },
            { 88, "Kaesora" },
            { 89, "Old Sebilis" },
            { 90, "City of Mist" },
            { 91, "Skyfire Mountains" },
            { 92, "Frontier Mountains" },
            { 93, "The Overthere" },
            { 94, "The Emerald Jungle" },
            { 95, "Trakanon's Teeth" },
            { 96, "Timorous Deep" },
            { 97, "Kurn's Tower" },
            { 98, "Erud's Crossing" },
            { 100, "Stonebrunt Mountains" },
            { 101, "The Warrens" },
            { 102, "Karnor's Castle" },
            { 103, "Chardok" },
            { 104, "Dalnir" },
            { 105, "Howling Stones" },
            { 106, "East Cabilis" },
            { 107, "The Mines of Nurga" },
            { 108, "Veeshan's Peak" },
            { 109, "Veksar" },
            { 110, "Iceclad Ocean" },
            { 111, "Tower of Frozen Shadow" },
            { 112, "Velketor's Labyrinth" },
            { 113, "Kael Drakkal" },
            { 114, "Skyshrine" },
            { 115, "Thurgadin" },
            { 116, "Eastern Wastes" },
            { 117, "Cobalt Scar"},
            { 118, "Great Divide" },
            { 119, "The Wakening Lands" },
            { 120, "Western Wastes" },
            { 121, "Crystal Caverns" },
            { 123, "Dragon Necropolis" },
            { 124, "Temple of Veeshan" },
            { 125, "Siren's Grotto" },
            { 126, "Plane of Mischief" },
            { 127, "Plane of Growth" },
            { 128, "Sleeper's Tomb" },
            { 129, "Icewell Keep" },
            { 130, "Marauder's Mire" },
            { 150, "Shadow Haven" },
            { 151, "The Bazaar" },
            { 152, "The Nexus" },
            { 153, "Echo Caverns"},
            { 154, "Acrylia Caverns" },
            { 155, "Shar'Vahl" },
            { 156, "Paludal Caverns" },
            { 157, "Fungus Grove" },
            { 158, "Vex Thal" },
            { 159, "Sanctus Seru" },
            { 160, "Katta Castellum" },
            { 161, "Netherbian Lair" },
            { 162, "Ssraeshza Temple" },
            { 163, "Grieg's End" },
            { 164, "The Deep" },
            { 165, "Shadeweaver's Thicket" },
            { 166, "Hollowshade Moor" },
            { 167, "Grimling Forest" },
            { 168, "Marus Seru" },
            { 169, "Mons Letalis" },
            { 170, "The Twilight Sea" },
            { 171, "The Grey" },
            { 172, "The Tenebrous Mountains" },
            { 173, "The Maiden's Eye" },
            { 174, "Dawnshroud Peaks" },
            { 175, "The Scarlet Desert" },
            { 176, "The Umbral Plains" },
            { 179, "Akeva Ruins" },
            { 180, "The Arena 2" },
            { 181, "The Jaggedpine Forest" },
            { 182, "Nedaria's Landing" },
            { 183, "Tutorial" },
            { 184, "Loading 1" },
            { 185, "Loading 2" },
            { 186, "Plane of Hate B" },
            { 187, "Shadowrest" },
            { 188, "The Mines of Gloomingdeep A" },
            { 189, "The Mines of Gloomingdeep B" },
            { 190, "Loading 3" },
            { 200, "Ruins of Lxanvom" },
            { 201, "The Plane of Justice"},
            { 202, "The Plane of Knowledge" },
            { 203, "The Plane of Tranquility" },
            { 204, "The Plane of Nightmare" },
            { 205, "The Plane of Disease" },
            { 206, "The Plane of Innovation" },
            { 207, "The Plane of Torment" },
            { 208, "The Plane of Valor" },
            { 209, "The Bastion of Thunder" },
            { 210, "The Plane of Storms" },
            { 211, "The Halls of Honor" },
            { 212, "Solusek Ro's Tower" },
            { 213, "The Plane of War" },
            { 214, "Drunder, Fortress of Zek" },
            { 215, "Eryslai, the Kingdom of Wind" },
            { 216, "Reef of Coirnav" },
            { 217, "Doomfire, The Burning Lands" },
            { 218, "Degarlson, The Earthen Badlands" },
            { 219, "Plane of Time A" },
            { 220, "Temple of Marr" },
            { 221, "Lair of Terris Thule" },
            { 222, "Stronghold of the Twelve" },
            { 223, "Plane of Time B" },
            { 224, "The Gulf of Gunthak" },
            { 225, "Dulak's Harbor" },
            { 394, "Crescent Reach" }
        };
        DebugLog.Write(LogChannel.Database, "ZoneRepository: singleton instance created with "
            + _zoneNames.Count + " zone names.", LogLevel.Trace);
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
}
