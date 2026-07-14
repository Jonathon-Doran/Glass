using Glass.Core.Logging;
using System.Collections.Generic;

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
            { 13, "North Karana" },
            { 14, "South Karana" },
            { 29, "Halas" },
            { 118, "Great Divide" },
            { 202, "Plane of Knowledge" },
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
    public string GetZoneName(uint zoneId)
    {
        if (_zoneNames.TryGetValue(zoneId, out string? name))
        {
            return name;
        }

        DebugLog.Write(LogChannel.Database, "ZoneRepository.GetZoneName: zoneId=0x" + zoneId.ToString("X2")
            + " not in map, returning 'Unknown'.", LogLevel.Warn);
        return "Unknown(0x" + zoneId.ToString("X2") + ")";
    }
}
