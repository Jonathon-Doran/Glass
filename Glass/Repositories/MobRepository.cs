using Glass.Core.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

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

    private readonly Dictionary<MobId, Spawn> _mobsById;
    private readonly Dictionary<ZoneId, Dictionary<SpawnId, MobId>> _spawnIndex;
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
        _mobsById = new Dictionary<MobId, Spawn>();
        _spawnIndex = new Dictionary<ZoneId, Dictionary<SpawnId, MobId>>();
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
    public MobId Add(Spawn spawn)
    {
        if (!_spawnIndex.TryGetValue(spawn.ZoneId, out Dictionary<SpawnId, MobId>? zoneIndex))
        {
            zoneIndex = new Dictionary<SpawnId, MobId>();
            _spawnIndex[spawn.ZoneId] = zoneIndex;
            DebugLog.Write(LogChannel.Database, "MobRepository.Add: created index for zoneId=" + spawn.ZoneId + ".", LogLevel.Trace);
        }

        if (zoneIndex.TryGetValue(spawn.SpawnId, out MobId existingId))
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.Add: duplicate for zoneId=" + spawn.ZoneId
                + " spawnId=" + spawn.SpawnId + ", existing id=" + existingId + " retained.", LogLevel.Error);
            return existingId;
        }

        spawn.MobId = new MobId(_nextMobId);
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
    public bool TryGetByMobId(MobId mobId, [NotNullWhen(true)] out Spawn? spawn)
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
    // index dictionary, and the spawn id selects a mob identifier within it. A zone id that does not exist
    // is rejected before any index access.
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

        if (!_spawnIndex.TryGetValue(zoneId, out Dictionary<SpawnId, MobId>? zoneIndex))
        {
            DebugLog.Write(LogChannel.Database, "MobRepository.TryGetBySpawnId: zoneId=" + zoneId
                + " has no index.", LogLevel.Trace);
            return false;
        }

        if (!zoneIndex.TryGetValue(spawnId, out MobId mobId))
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
