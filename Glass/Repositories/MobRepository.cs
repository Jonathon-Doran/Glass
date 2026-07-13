using Glass.Core.Logging;
using System;
using System.Collections.Generic;

namespace Glass.Data.Repositories;
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// MobRepository
//
// In-memory repository of Spawn records. Records are held in a dictionary keyed by identifier, with a
// secondary index keyed by (zoneId, spawnId).
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class MobRepository
{
    private static MobRepository? _instance = null;

    private readonly Dictionary<uint, Spawn> _mobsById;
    private readonly Dictionary<uint, Dictionary<uint, uint>> _spawnIndex;
    private uint _nextMobId;

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
    // Private constructor. Initializes empty caches and sets the identifier counter to 1.
    // Zero is not issued as an identifier. The spawn index is a nested dictionary keyed by zone id in the
    // outer dictionary and spawn id in the inner dictionary, mapping to a record identifier.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private MobRepository()
    {
        _mobsById = new Dictionary<uint, Spawn>();
        _spawnIndex = new Dictionary<uint, Dictionary<uint, uint>>();
        _nextMobId = 1;
        DebugLog.Write(LogChannel.Database, "MobRepository: singleton instance created with empty caches.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Add
    //
    // Assigns the next identifier to the given Spawn record and stores it in the dictionary and the
    // zone/spawn index. The inner index dictionary for the record's zone is created on first use. If the
    // index already contains an entry for the record's ZoneId and SpawnId, no record is stored, the
    // existing entry is left unchanged, and the existing identifier is returned.
    //
    // spawn:  The record to add. ZoneId, SpawnId, and any known field values are set by the caller.
    //
    // Returns the identifier assigned to the record, or the identifier of the existing record on a duplicate.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public uint Add(Spawn spawn)
    {
        if (!_spawnIndex.TryGetValue(spawn.ZoneId, out Dictionary<uint, uint>? zoneIndex))
        {
            zoneIndex = new Dictionary<uint, uint>();
            _spawnIndex[spawn.ZoneId] = zoneIndex;
            DebugLog.Write(LogChannel.Database, "MobRepository.Add: created index for zoneId=" + spawn.ZoneId + ".", LogLevel.Trace);
        }

        if (zoneIndex.TryGetValue(spawn.SpawnId, out uint existingId))
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.Add: duplicate for zoneId=" + spawn.ZoneId
                + " spawnId=" + spawn.SpawnId + ", existing id=" + existingId + " retained.", LogLevel.Error);
            return existingId;
        }

        spawn.MobId = _nextMobId;
        _nextMobId++;

        _mobsById[spawn.MobId] = spawn;
        zoneIndex[spawn.SpawnId] = spawn.MobId;

        DebugLog.Write(LogChannel.Database, "MobRepository.Add: added id=" + spawn.MobId
            + " zoneId=" + spawn.ZoneId + " spawnId=" + spawn.SpawnId
            + " name=" + (spawn.Name ?? "null"), LogLevel.Trace);

        return spawn.MobId;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetByMobId
    //
    // Looks up the Spawn record with the given identifier.
    //
    // mobId:  Identifier of the record to look up.
    // spawn:  Receives the record if found, null otherwise.
    //
    // Returns true if the record was found, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool TryGetByMobId(uint mobId, out Spawn? spawn)
    {
        if (_mobsById.TryGetValue(mobId, out spawn))
        {
            return true;
        }

        DebugLog.Write(LogChannel.Database, "MobRepository.TryGetByMobId: id=" + mobId + " not found.", LogLevel.Trace);
        return false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetBySpawnId
    //
    // Looks up the Spawn record indexed under the given zone id and spawn id. The zone id selects an inner
    // index dictionary, and the spawn id selects a record identifier within it.
    //
    // zoneId:   Zone in which the spawn id is valid.
    // spawnId:  Server-assigned spawn id.
    // spawn:    Receives the record if found, null otherwise.
    //
    // Returns true if the record was found, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool TryGetBySpawnId(uint? zoneId, uint spawnId, out Spawn? spawn)
    {
        spawn = null;

        if (zoneId == null)
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.TryGetBySpawnId: zoneId is null.", LogLevel.Trace);
            return false;
        }

        if (!_spawnIndex.TryGetValue(zoneId.Value, out Dictionary<uint, uint>? zoneIndex))
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.TryGetBySpawnId: zoneId=" + zoneId
                + " has no index.", LogLevel.Trace);
            return false;
        }

        if (!zoneIndex.TryGetValue(spawnId, out uint mobId))
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.TryGetBySpawnId: zoneId=" + zoneId
                + " spawnId=" + spawnId + " not in index.", LogLevel.Trace);
            return false;
        }

        if (!_mobsById.TryGetValue(mobId, out spawn))
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.TryGetBySpawnId: index maps zoneId=" + zoneId
                + " spawnId=" + spawnId + " to id=" + mobId + " but no record exists.", LogLevel.Error);
            return false;
        }

        return true;
    }
}
