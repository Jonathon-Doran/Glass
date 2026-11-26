using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.World;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// CompanionRepository
//
// In-memory repository of the companions seen this run, held in a dictionary keyed by EntityId.  A companion with
// no known owner is also held as an orphan until its owner is placed.  The EntityId of each owner's companion of each
// type is remembered, so a companion keeps its EntityId when its owner changes zones.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class CompanionRepository
{
    private const LogLevel defaultLoglevel = LogLevel.Info;

    private static CompanionRepository? _instance = null;

    private readonly List<Companion> _orphans;
    private readonly Dictionary<EntityId, Dictionary<EntityType, EntityId>> _companionIdsByOwner;
    private readonly Dictionary<EntityId, Companion> _companionsByEntityId;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // Lazy singleton accessor.  The instance is created on first access with an empty table.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static CompanionRepository Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new CompanionRepository();
            }
            return _instance;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CompanionRepository
    //
    // Private constructor.  Initializes an empty table.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private CompanionRepository()
    {
        _companionsByEntityId = new Dictionary<EntityId, Companion>();
        _orphans = new List<Companion>();
        _companionIdsByOwner = new Dictionary<EntityId, Dictionary<EntityType, EntityId>>();
        DebugLog.Write(LogChannel.Database, "CompanionRepository: singleton instance created with an empty table.",
            defaultLoglevel);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AddCompanion
    //
    // Stores a companion under its EntityId.  A companion with no EntityId is issued a new EntityId of the given type
    // for this run. A companion whose EntityId is already stored is rejected and the stored companion is left
    // unchanged. A stored companion that has no owner yet is held as an orphan until it is adopted.
    //
    // companion:  The companion to store.  ZoneId, SpawnId and any known field values are set by the caller.
    // type:       The kind of companion, used as the type of an issued EntityId.
    //
    // Returns true if the companion was stored, false if its EntityId was already stored.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool AddCompanion(Companion companion, EntityType type)
    {
        if (companion.EntityId.Exists == false)
        {
            companion.EntityId = EntityRepository.Instance.IssueId(type);
        }
        else if (_companionsByEntityId.ContainsKey(companion.EntityId))
        {
            DebugLog.Write(LogChannel.Database, "CompanionRepository.AddCompanion: entityId=" + companion.EntityId
                + " already stored, '" + companion.Name + "' zoneId=" + companion.ZoneId + " spawnId="
                + companion.SpawnId + " not added.", LogLevel.Error);
            return false;
        }

        _companionsByEntityId[companion.EntityId] = companion;

        if (companion.Owner == null)
        {
            _orphans.Add(companion);
            DebugLog.Write(LogChannel.Database, "CompanionRepository.AddCompanion: '" + companion.Name
                + "' entityId=" + companion.EntityId + " has no owner, held as an orphan for owner spawnId="
                + companion.OwnerSpawnId + ".", defaultLoglevel);
        }

        DebugLog.Write(LogChannel.Database, "CompanionRepository.AddCompanion: stored '" + companion.Name
            + "' entityId=" + companion.EntityId + " zoneId=" + companion.ZoneId + " spawnId=" + companion.SpawnId
            + ", owner spawnId=" + companion.OwnerSpawnId + ".", defaultLoglevel);
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetByEntityId
    //
    // Finds the companion with the given EntityId.
    //
    // entityId:  The EntityId of the companion.
    //
    // Returns the companion, or null when no companion has that EntityId.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Companion? GetByEntityId(EntityId entityId)
    {
        if (_companionsByEntityId.TryGetValue(entityId, out Companion? companion))
        {
            return companion;
        }

        DebugLog.Write(LogChannel.Database, "CompanionRepository.GetByEntityId: no companion with entityId=" + entityId
            + ".", defaultLoglevel);
        return null;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LinkOwner
    //
    // Adopts every orphan whose owner spawn id and zone match the given character's placement.
    //
    // owner:  The placed character.  Its ZoneId and SpawnId are its current placement.
    //
    // Returns the number of companions adopted by the character.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public uint LinkOwner(Character owner)
    {
        List<Companion> matches = new List<Companion>();
        for (int orphanIndex = 0; orphanIndex < _orphans.Count; orphanIndex++)
        {
            Companion orphan = _orphans[orphanIndex];
            if (orphan.ZoneId == owner.ZoneId && orphan.OwnerSpawnId == owner.SpawnId)
            {
                matches.Add(orphan);
            }
        }

        for (int matchIndex = 0; matchIndex < matches.Count; matchIndex++)
        {
            Adopt(matches[matchIndex], owner);
        }

        DebugLog.Write(LogChannel.Database, "CompanionRepository.LinkOwner: '" + owner.Name + "' adopted "
            + matches.Count + " of " + (_orphans.Count + matches.Count) + " orphans.", defaultLoglevel);
        return (uint)matches.Count;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Adopt
    //
    // Sets the given character as the owner of a companion and takes the companion off the orphan list.  When the owner
    // had a companion of the same type before, recorded under a different EntityId, this companion takes over that
    // EntityId: any companion still stored under it is discarded along with its placement, this companion is stored
    // under the old EntityId, and its placement is pointed at it.  Otherwise this companion's EntityId is recorded for
    // the owner and type.  A companion already owned by the character is left unchanged.
    //
    // companion:  The companion to adopt.  Its ZoneId and SpawnId are its current placement.
    // owner:      The placed character that owns the companion.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Adopt(Companion companion, Character owner)
    {
        // other sessions may receive the same companion zone entry.  Do not log duplicates.
        if (ReferenceEquals(companion.Owner, owner))
        {
            return;
        }

        companion.Owner = owner;
        companion.OwnerId = owner.CharacterId;
        _orphans.Remove(companion);

        EntityType type = companion.EntityId.Type;
        if (!_companionIdsByOwner.TryGetValue(owner.EntityId, out Dictionary<EntityType, EntityId>? idsByType))
        {
            idsByType = new Dictionary<EntityType, EntityId>();
            _companionIdsByOwner[owner.EntityId] = idsByType;
        }

        if (!idsByType.TryGetValue(type, out EntityId previousId) || previousId == companion.EntityId)
        {
            idsByType[type] = companion.EntityId;
            DebugLog.Write(LogChannel.Database, "CompanionRepository.Adopt: '" + companion.Name + "' entityId="
                + companion.EntityId + " adopted by '" + owner.Name + "', recorded as its " + type + ".",
                defaultLoglevel);
            return;
        }

        if (_companionsByEntityId.TryGetValue(previousId, out Companion? previous))
        {
            ZoneRepository.Instance.RemovePlacement(previous.ZoneId, previous.SpawnId, previousId);
            _companionsByEntityId.Remove(previousId);
            _orphans.Remove(previous);
            DebugLog.Write(LogChannel.Database, "CompanionRepository.Adopt: discarded earlier '" + previous.Name
                + "' entityId=" + previousId + " at zoneId=" + previous.ZoneId + " spawnId=" + previous.SpawnId
                + ".", defaultLoglevel);
        }

        EntityId issuedId = companion.EntityId;
        _companionsByEntityId.Remove(issuedId);
        companion.EntityId = previousId;
        _companionsByEntityId[previousId] = companion;

        if (!ZoneRepository.Instance.PlaceEntity(companion.ZoneId, companion.SpawnId, previousId))
        {
            DebugLog.Write(LogChannel.Database, "CompanionRepository.Adopt: '" + companion.Name + "' entityId="
                + previousId + " could not be placed at zoneId=" + companion.ZoneId + " spawnId="
                + companion.SpawnId + ".", LogLevel.Warn);
        }

        DebugLog.Write(LogChannel.Database, "CompanionRepository.Adopt: '" + companion.Name + "' adopted by '"
            + owner.Name + "', took over entityId=" + previousId + " in place of " + issuedId + ".",
            defaultLoglevel);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Remove
    //
    // Discards the companion stored under the given EntityId.
    //
    // entityId:  The EntityId of the companion to discard.
    //
    // Returns true if a companion was discarded, false if none was stored under the EntityId.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool Remove(EntityId entityId)
    {
        if (!_companionsByEntityId.Remove(entityId, out Companion? companion))
        {
            DebugLog.Write(LogChannel.Database, "CompanionRepository.Remove: entityId=" + entityId + " not stored.",
                LogLevel.Warn);
            return false;
        }
        _orphans.Remove(companion);

        DebugLog.Write(LogChannel.Database, "CompanionRepository.Remove: entityId=" + entityId + " discarded.",
            defaultLoglevel);
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Clear
    //
    // Discards every companion held.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Clear()
    {
        uint discarded = (uint)_companionsByEntityId.Count;
        _orphans.Clear();
        _companionsByEntityId.Clear();

        DebugLog.Write(LogChannel.Database, "CompanionRepository.Clear: discarded " + discarded + " companions.",
            defaultLoglevel);
    }
}