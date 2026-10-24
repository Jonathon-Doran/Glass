using Glass.Core.Logging;
using Glass.World;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// MobRepository
//
// In-memory repository of MobType records and the live Spawn instances of each zone.  Types are held in a
// dictionary keyed by MobId with a secondary index keyed by base name, and are read from the database one
// at a time as names are first encountered.  Spawns are held per zone in a dictionary keyed by SpawnId.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class MobRepository
{
    private static MobRepository? _instance = null;

    private readonly Dictionary<MobId, MobType> _typesById;
    private readonly Dictionary<(ZoneId, string), MobType> _typesByName;
    private readonly Dictionary<ZoneId, Dictionary<string, MobType>> _typesByZone;
    private readonly Dictionary<ZoneId, Dictionary<SpawnId, Spawn>> _spawnsByZone;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // Lazy singleton accessor. The instance is created on first access with empty caches.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static MobRepository Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new MobRepository();
            }
            return _instance;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MobRepository
    //
    // Private constructor. Initializes empty caches.  The spawn table is a nested dictionary keyed by zone
    // id in the outer dictionary and spawn id in the inner dictionary.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private MobRepository()
    {
        _typesById = new Dictionary<MobId, MobType>();
        _typesByName = new Dictionary<(ZoneId, string), MobType>();
        _typesByZone = new Dictionary<ZoneId, Dictionary<string, MobType>>();
        _spawnsByZone = new Dictionary<ZoneId, Dictionary<SpawnId, Spawn>>();
        DebugLog.Write(LogChannel.Database, "MobRepository: singleton instance created with empty caches.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Create
    //
    // Stores a MobType record for a zone and base name not seen before.  The record is inserted into the
    // database, which assigns its Id, and is then indexed by id and under its zone by base name.  A record
    // whose zone and base name are already cached is rejected, nothing is written, and the cached record's
    // id is returned.  A record the database refuses is rejected and MobId.None is returned.
    //
    // mobType:  The record to create.  Id must be None; ZoneId, BaseName and every other field are set by
    //           the caller.
    //
    // Returns the assigned MobId, the cached record's id on a duplicate, or MobId.None on failure.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public MobId Create(MobType mobType)
    {
        if (!_typesByZone.TryGetValue(mobType.ZoneId, out Dictionary<string, MobType>? zoneTypes))
        {
            zoneTypes = new Dictionary<string, MobType>();
            _typesByZone[mobType.ZoneId] = zoneTypes;
            DebugLog.Write(LogChannel.Database, "MobRepository.Create: created type table for zoneId=" + mobType.ZoneId
                + ".", LogLevel.Trace);
        }

        if (zoneTypes.TryGetValue(mobType.BaseName, out MobType? existing))
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.Create: zoneId=" + mobType.ZoneId + " base name '"
                + mobType.BaseName + "' already cached as id=" + existing.Id + ", nothing written.", LogLevel.Error);
            return existing.Id;
        }

        MobId mobId = MobTypeGateway.Instance.Insert(mobType);
        if (mobId.Exists == false)
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.Create: insert refused for zoneId=" + mobType.ZoneId
                + " base name '" + mobType.BaseName + "'.", LogLevel.Error);
            return MobId.None;
        }

        _typesById[mobId] = mobType;
        zoneTypes[mobType.BaseName] = mobType;

        DebugLog.Write(LogChannel.Database, "MobRepository.Create: created id=" + mobId + " zoneId=" + mobType.ZoneId
            + " base name '" + mobType.BaseName + "'.", LogLevel.Trace);
        return mobId;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AddSpawn
    //
    // Stores the given Spawn record under its zone and spawn id.  The inner dictionary for the record's
    // zone is created on first use.  If the zone already holds a record for the spawn id, no record is
    // stored and the existing record is left unchanged.
    //
    // spawn:  The record to add.  ZoneId, SpawnId, MobId, and any known field values are set by the caller.
    //
    // Returns true if the record was stored, false on a duplicate.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool AddSpawn(Spawn spawn)
    {
        if (!_spawnsByZone.TryGetValue(spawn.ZoneId, out Dictionary<SpawnId, Spawn>? zoneSpawns))
        {
            zoneSpawns = new Dictionary<SpawnId, Spawn>();
            _spawnsByZone[spawn.ZoneId] = zoneSpawns;
            DebugLog.Write(LogChannel.Database, "MobRepository.AddSpawn: created table for zoneId=" + spawn.ZoneId + ".", LogLevel.Trace);
        }

        if (zoneSpawns.ContainsKey(spawn.SpawnId))
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.AddSpawn: duplicate for zoneId=" + spawn.ZoneId
                + " spawnId=" + spawn.SpawnId + ", existing record retained.", LogLevel.Error);
            return false;
        }

        zoneSpawns[spawn.SpawnId] = spawn;

        DebugLog.Write(LogChannel.Database, "MobRepository.AddSpawn: added zoneId=" + spawn.ZoneId + " spawnId=" + spawn.SpawnId
            + " mobId=" + spawn.MobId + " name=" + (spawn.Name ?? "null"), LogLevel.Trace);
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetSpawnsInZone
    //
    // Returns the Spawn records held under the given zone id, in ascending spawn id order.  The returned
    // list is a new copy on every call; the records themselves are the live objects.  A zone id that is None
    // or has no table yields an empty list.
    //
    // zoneId:  Zone whose records are returned.
    //
    // Returns a List<Spawn> of the zone's records; empty when none exist.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ///
    public List<Spawn> GetSpawnsInZone(ZoneId zoneId)
    {
        List<Spawn> spawns = new List<Spawn>();

        if (!zoneId.Exists)
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.GetSpawnsInZone: zoneId is None.", LogLevel.Trace);
            return spawns;
        }

        if (!_spawnsByZone.TryGetValue(zoneId, out Dictionary<SpawnId, Spawn>? zoneSpawns))
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.GetSpawnsInZone: zoneId=" + zoneId
                + " has no table.", LogLevel.Trace);
            return spawns;
        }

        spawns.AddRange(zoneSpawns.Values);
        spawns.Sort((Spawn left, Spawn right) => left.SpawnId.Value.CompareTo(right.SpawnId.Value));

        DebugLog.Write(LogChannel.Database, "MobRepository.GetSpawnsInZone: zoneId=" + zoneId
            + " returning " + spawns.Count + " records.", LogLevel.Trace);

        return spawns;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetBySpawnId
    //
    // Looks up the Spawn record held under the given zone id and spawn id.  The zone id selects an inner
    // dictionary, and the spawn id selects a record within it.  A zone id that does not exist is rejected
    // before any table access.
    //
    // zoneId:   Zone in which the spawn id is valid. ZoneId.None means the zone is not known.
    // spawnId:  Server-assigned spawn id.
    // spawn:    Receives the record if found, null otherwise.
    //
    // Returns true if the record was found, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public bool TryGetBySpawnId(ZoneId zoneId, SpawnId spawnId, [NotNullWhen(true)] out Spawn? spawn)
    {
        spawn = null;

        if (!zoneId.Exists)
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.TryGetBySpawnId: zoneId is None.", LogLevel.Trace);
            return false;
        }

        if (!_spawnsByZone.TryGetValue(zoneId, out Dictionary<SpawnId, Spawn>? zoneSpawns))
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.TryGetBySpawnId: zoneId=" + zoneId
                + " has no table.", LogLevel.Trace);
            return false;
        }

        if (!zoneSpawns.TryGetValue(spawnId, out spawn))
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.TryGetBySpawnId: zoneId=" + zoneId
                + " spawnId=" + spawnId + " not in table.", LogLevel.Trace);
            return false;
        }

        return true;
    }
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetTypeByName
    //
    // Looks up the MobType record with the given zone and base name.  The cache is checked first; on a
    // miss the database is read, and a row found there is cached under both indexes before being
    // returned.  A zone and name in neither place is a miss, and the caller decides whether to create it.
    //
    // zoneId:    The zone the type belongs to.
    // baseName:  The base name to look up, matched exactly.
    // mobType:   Receives the record if found, null otherwise.
    //
    // Returns true if the record was found, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool TryGetTypeByName(ZoneId zoneId, string baseName, [NotNullWhen(true)] out MobType? mobType)
    {
        if (!_typesByZone.TryGetValue(zoneId, out Dictionary<string, MobType>? zoneTypes))
        {
            zoneTypes = new Dictionary<string, MobType>();
            _typesByZone[zoneId] = zoneTypes;
            DebugLog.Write(LogChannel.Database, "MobRepository.TryGetTypeByName: created type table for zoneId="
                + zoneId + ".", LogLevel.Trace);
        }

        if (zoneTypes.TryGetValue(baseName, out mobType))
        {
            return true;
        }

        if (MobTypeGateway.Instance.TryLoadByName(zoneId, baseName, out mobType) == false)
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.TryGetTypeByName: zoneId=" + zoneId + " base name '"
                + baseName + "' not cached and not in database.", LogLevel.Trace);
            return false;
        }

        _typesById[mobType.Id] = mobType;
        zoneTypes[baseName] = mobType;

        DebugLog.Write(LogChannel.Database, "MobRepository.TryGetTypeByName: loaded id=" + mobType.Id + " zoneId="
            + zoneId + " base name '" + baseName + "' into cache.", LogLevel.Trace);
        return true;
    }

   
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SaveZoneMobs
    //
    // Persists mob info for a zone.  Every Spawn held under the zone is written as a sighting,
    // every cached MobType referenced by those spawns is written back.
    // A zone id that is None or has no table does nothing.
    //
    // zoneId:  The zone being left.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void SaveZoneMobs(ZoneId zoneId)
    {
        if (!zoneId.Exists)
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.SaveZoneMobs: zoneId is None.", LogLevel.Warn);
            return;
        }

        if (!_spawnsByZone.TryGetValue(zoneId, out Dictionary<SpawnId, Spawn>? zoneSpawns))
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.SaveZoneMobs: zoneId=" + zoneId + " has no table.", LogLevel.Trace);
            return;
        }

        List<Spawn> spawns = new List<Spawn>(zoneSpawns.Values);
        uint sightings = MobSightingGateway.Instance.InsertAll(spawns);

        HashSet<MobId> touched = new HashSet<MobId>();
        foreach (Spawn spawn in spawns)
        {
            if (spawn.MobId.Exists)
            {
                touched.Add(spawn.MobId);
            }
        }

        uint updated = 0;
        foreach (MobId mobId in touched)
        {
            if (!_typesById.TryGetValue(mobId, out MobType? mobType))
            {
                DebugLog.Write(LogChannel.Database, "MobRepository.SaveZoneMobs: zoneId=" + zoneId + " references mobId="
                    + mobId + " which is not cached.", LogLevel.Error);
                continue;
            }

            if (MobTypeGateway.Instance.Update(mobType))
            {
                updated++;
            }
        }

        DebugLog.Write(LogChannel.Database, "MobRepository.SaveZoneMobs: zoneId=" + zoneId + " wrote " + sightings
            + " sightings, updated " + updated + " of " + touched.Count + " types.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ClearZoneMobs
    //
    // Removes the given zone's spawn table from the cache, discarding every Spawn record held under it.
    // Nothing is written.  A zone id that is None or has no table does nothing.
    //
    // zoneId:  The zone whose spawn table is removed.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void ClearZoneMobs(ZoneId zoneId)
    {
        if (!zoneId.Exists)
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.ClearZoneMobs: zoneId is None.", LogLevel.Warn);
            return;
        }

        if (!_spawnsByZone.TryGetValue(zoneId, out Dictionary<SpawnId, Spawn>? zoneSpawns))
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.ClearZoneMobs: zoneId=" + zoneId + " has no table.", LogLevel.Trace);
            return;
        }

        uint discarded = (uint)zoneSpawns.Count;
        _spawnsByZone.Remove(zoneId);

        DebugLog.Write(LogChannel.Database, "MobRepository.ClearZoneMobs: zoneId=" + zoneId + " discarded " + discarded
            + " records, table removed.", LogLevel.Trace);
    }
}
