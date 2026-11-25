using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.World;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// CompanionRepository
//
// In-memory repository of the companions seen this run, held per zone in a dictionary keyed by spawn id.
// Nothing is read from or written to the database.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class CompanionRepository
{
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    private static CompanionRepository? _instance = null;

    private readonly Dictionary<ZoneId, Dictionary<SpawnId, Companion>> _companionsByZone;
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
        _companionsByZone = new Dictionary<ZoneId, Dictionary<SpawnId, Companion>>();
        _companionsByEntityId = new Dictionary<EntityId, Companion>();
        DebugLog.Write(LogChannel.Database, "CompanionRepository: singleton instance created with an empty table.",
            defaultLoglevel);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AddCompanion
    //
    // Stores a companion under its EntityId.  A companion with no EntityId is issued a new EntityId of the given type
    // for this run.  A companion whose EntityId is already stored is rejected and the stored companion is left
    // unchanged.
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
            DebugLog.Write(LogChannel.Database, "CompanionRepository.GetByEntityId: entityId=" + entityId + " is '"
                + companion.Name + "'.", defaultLoglevel);
            return companion;
        }

        DebugLog.Write(LogChannel.Database, "CompanionRepository.GetByEntityId: no companion with entityId=" + entityId
            + ".", defaultLoglevel);
        return null;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LinkOwner
    //
    // Sets the given character as the owner of every stored companion, among the given EntityIds, whose owner spawn id
    // equals the character's spawn id.  EntityIds with no stored companion are skipped.
    //
    // entityIds:  The EntityIds to examine.
    // owner:      The placed character.  Its SpawnId is the spawn id it holds in its zone.
    //
    // Returns the number of companions linked to the character.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public uint LinkOwner(List<EntityId> entityIds, Character owner)
    {
        uint linked = 0;
        for (int idIndex = 0; idIndex < entityIds.Count; idIndex++)
        {
            if (!_companionsByEntityId.TryGetValue(entityIds[idIndex], out Companion? companion))
            {
                continue;
            }

            if (companion.OwnerSpawnId != owner.SpawnId)
            {
                continue;
            }

            companion.Owner = owner;
            companion.OwnerId = owner.CharacterId;
            linked++;

            DebugLog.Write(LogChannel.Database, "CompanionRepository.LinkOwner: '" + companion.Name + "' entityId="
                + companion.EntityId + " linked to owner '" + owner.Name + "'.", defaultLoglevel);
        }

        DebugLog.Write(LogChannel.Database, "CompanionRepository.LinkOwner: examined " + entityIds.Count
            + " entityIds, linked " + linked + " companions to '" + owner.Name + "'.", defaultLoglevel);
        return linked;
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
        if (!_companionsByEntityId.Remove(entityId))
        {
            DebugLog.Write(LogChannel.Database, "CompanionRepository.Remove: entityId=" + entityId + " not stored.",
                LogLevel.Warn);
            return false;
        }

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
        _companionsByEntityId.Clear();

        DebugLog.Write(LogChannel.Database, "CompanionRepository.Clear: discarded " + discarded + " companions.",
            defaultLoglevel);
    }
}