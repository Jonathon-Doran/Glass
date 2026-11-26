using Glass.Core.Logging;
using Glass.World;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// EntityRepository
//
// Issues the EntityId of every entity seen this run.  The run number is taken once, when the instance is created, as
// the current UTC time in seconds since the Unix epoch, so two runs of Glass never share a run number.  Each issued
// identifier takes the next counter value, so no two identifiers issued in one run are equal.  Issuing is guarded by a
// lock, so identifiers may be requested from any thread.
//
// Nothing is read from or written to the database.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class EntityRepository
{
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    private static EntityRepository? _instance = null;

    private readonly uint _run;
    private readonly object _lock = new object();
    private uint _nextCounter = 0;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // Lazy singleton accessor.  The instance is created on first access, which fixes the run number.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static EntityRepository Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new EntityRepository();
            }
            return _instance;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // EntityRepository
    //
    // Private constructor.  Takes the run number from the current UTC time in seconds since the Unix epoch and starts
    // the counter at zero.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private EntityRepository()
    {
        _run = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        DebugLog.Write(LogChannel.Database, "EntityRepository: singleton instance created, run=0x"
            + _run.ToString("X8") + ".", defaultLoglevel);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // IssueId
    //
    // Issues a new identifier of the given type, using this run's number and the next counter value.  Each call
    // returns an identifier not issued before in this run.  A type of None is rejected, and once every counter value
    // has been issued no further identifiers can be formed; in either case nothing is issued.
    //
    // type:  The kind of entity the identifier is for.
    //
    // Returns the new identifier, or EntityId.None when the type is None or the counter is exhausted.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public EntityId IssueId(EntityType type)
    {
        if (type == EntityType.None)
        {
            DebugLog.Write(LogChannel.Database, "EntityRepository.IssueId: type None requested, nothing issued.",
                LogLevel.Error);
            return EntityId.None;
        }

        uint counter;
        lock (_lock)
        {
            if (_nextCounter > EntityId.MaxCounter)
            {
                DebugLog.Write(LogChannel.Database, "EntityRepository.IssueId: counter exhausted at " + _nextCounter
                    + " for run=0x" + _run.ToString("X8") + ", nothing issued for type " + type + ".",
                    LogLevel.Error);
                return EntityId.None;
            }

            counter = _nextCounter;
            _nextCounter++;
        }

        EntityId id = new EntityId(type, _run, counter);

        DebugLog.Write(LogChannel.Database, "EntityRepository.IssueId: issued " + id + ".", defaultLoglevel);
        return id;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Remove
    //
    // Discards the entity stored under the given EntityId from the repository selected by the EntityId's type.  Mobs and
    // companions (pets, familiars, mercenaries, warders) can be discarded.  Characters are persistent and are not
    // discarded; an EntityId of any other type is rejected.
    //
    // entityId:  The EntityId of the entity to discard.
    //
    // Returns true if an entity was discarded, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool Remove(EntityId entityId)
    {
        switch (entityId.Type)
        {
            case EntityType.Mob:
                return MobRepository.Instance.Remove(entityId);

            case EntityType.Pet:
            case EntityType.Familiar:
            case EntityType.Mercenary:
            case EntityType.Warder:
                return CompanionRepository.Instance.Remove(entityId);

            default:
                DebugLog.Write(LogChannel.Database, "EntityRepository.Remove: entityId=" + entityId + " type "
                    + entityId.Type + " is not discarded.", LogLevel.Warn);
                return false;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Lookup
    //
    // Finds the entity with the given EntityId.  The identifier's type selects the repository that holds it: a
    // character, a mob, or a companion (pet, familiar, mercenary or warder).  Mount, corpse and aura entities have no
    // repository, and an identifier that is None identifies nothing; in those cases nothing is found.
    //
    // entityId:  The EntityId of the entity.
    //
    // Returns the entity, or null when none is found.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Entity? Lookup(EntityId entityId)
    {
        Entity? entity;

        switch (entityId.Type)
        {
            case EntityType.Character:
                entity = CharacterRepository.Instance.GetByEntityId(entityId);
                break;

            case EntityType.Mob:
                entity = MobRepository.Instance.GetByEntityId(entityId);
                break;

            case EntityType.Pet:
            case EntityType.Familiar:
            case EntityType.Mercenary:
            case EntityType.Warder:
                entity = CompanionRepository.Instance.GetByEntityId(entityId);
                break;

            default:
                DebugLog.Write(LogChannel.Database, "EntityRepository.Lookup: entityId=" + entityId + " of type "
                    + entityId.Type + " has no repository, nothing found.", defaultLoglevel);
                return null;
        }

        if (entity == null)
        {
            DebugLog.Write(LogChannel.Database, "EntityRepository.Lookup: entityId=" + entityId + " not found.",
                defaultLoglevel);
            return null;
        }

        return entity;
    }
}