using Glass.Core.Logging;
using Glass.World;
using System.Collections.Generic;

namespace Glass.Agents.Healing;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// HealingCoordinator
//
// Holds the wards the healing agent watches, each with its WardWatch, keyed by the ward's EntityId.  The ward table is
// guarded by one lock.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class HealingCoordinator
{
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    private static HealingCoordinator? _instance = null;

    private readonly object _lock = new object();
    private readonly Dictionary<EntityId, WardWatch> _watchesByWard = new Dictionary<EntityId, WardWatch>();
    private readonly List<long> _crossed = new List<long>();

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // Lazy singleton accessor.  The instance is created on first access with no wards.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static HealingCoordinator Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new HealingCoordinator();
            }
            return _instance;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // HealingCoordinator
    //
    // Private constructor.  Initializes an empty ward table.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private HealingCoordinator()
    {
        DebugLog.Write(LogChannel.Healing, "HealingCoordinator: singleton instance created with no wards.",
            defaultLoglevel);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RegisterWard
    //
    // Makes an entity a ward, creating its WardWatch.  An entity already registered keeps its existing WardWatch.  An
    // EntityId of None is rejected.
    //
    // ward:  The EntityId of the entity to watch.
    //
    // Returns the ward's WardWatch, or null when the EntityId is None.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public WardWatch? RegisterWard(EntityId ward)
    {
        if (ward.Exists == false)
        {
            DebugLog.Write(LogChannel.Healing, "HealingCoordinator.RegisterWard: entityId is None, not registered.",
                LogLevel.Warn);
            return null;
        }

        WardWatch? existing;
        lock (_lock)
        {
            _watchesByWard.TryGetValue(ward, out existing);
        }

        if (existing != null)
        {
            DebugLog.Write(LogChannel.Healing, "HealingCoordinator.RegisterWard: ward " + ward
                + " already registered, existing watch kept.", defaultLoglevel);
            return existing;
        }

        WardWatch created = new WardWatch(ward);
        bool added;
        lock (_lock)
        {
            added = _watchesByWard.TryAdd(ward, created);
            if (!added)
            {
                existing = _watchesByWard[ward];
            }
        }

        if (!added)
        {
            DebugLog.Write(LogChannel.Healing, "HealingCoordinator.RegisterWard: ward " + ward
                + " was registered by another thread, existing watch kept.", defaultLoglevel);
            return existing;
        }

        DebugLog.Write(LogChannel.Healing, "HealingCoordinator.RegisterWard: ward " + ward + " registered.",
            defaultLoglevel);
        return created;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UnregisterWard
    //
    // Stops watching an entity, discarding its WardWatch.
    //
    // ward:  The EntityId of the ward.
    //
    // Returns true if the ward was registered, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool UnregisterWard(EntityId ward)
    {
        bool removed;
        lock (_lock)
        {
            removed = _watchesByWard.Remove(ward);
        }

        if (removed == false)
        {
            DebugLog.Write(LogChannel.Healing, "HealingCoordinator.UnregisterWard: ward " + ward
                + " was not registered.", LogLevel.Warn);
            return false;
        }

        DebugLog.Write(LogChannel.Healing, "HealingCoordinator.UnregisterWard: ward " + ward + " unregistered.",
            defaultLoglevel);
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnHpUpdate
    //
    // Receives an entity's current HP.  An entity that is not a ward is ignored.  For a ward, the HP is sampled into its
    // WardWatch, and any thresholds crossed are handled.  The crossed thresholds are collected in a list held by the
    // coordinator, so this method must not be called from more than one thread at a time.
    //
    // entityId:   The EntityId of the entity the HP belongs to.
    // currentHp:  The entity's current HP.
    // time:       The time of the packet that carried the HP.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void OnHpUpdate(EntityId entityId, long currentHp, DateTime time)
    {
        WardWatch? watch;
        lock (_lock)
        {
            if (!_watchesByWard.TryGetValue(entityId, out watch))
            {
                return;
            }
        }

        uint crossedCount = watch.Sample(currentHp, time, _crossed);
        if (crossedCount > 0)
        {
            OnThresholdsCrossed(watch, currentHp, _crossed);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnThresholdsCrossed
    //
    // Receives the thresholds a ward's HP crossed in one sample, and logs each one.
    //
    // watch:      The ward's WardWatch.
    // currentHp:  The ward's HP in the sample.
    // crossed:    The thresholds crossed, highest first.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void OnThresholdsCrossed(WardWatch watch, long currentHp, List<long> crossed)
    {
        for (int crossedIndex = 0; crossedIndex < crossed.Count; crossedIndex++)
        {
            DebugLog.Write(LogChannel.Healing, "HealingCoordinator.OnThresholdsCrossed: ward " + watch.Ward + " hp="
                + currentHp + " crossed " + crossed[crossedIndex] + ".", defaultLoglevel);
        }
    }
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnDamage
    //
    // Receives damage, or an attempt at damage, done to an entity.  An entity that is not a ward is ignored, and an
    // attacker with no EntityId is not recorded.  For a ward, the attacker and time are recorded in its WardWatch.
    //
    // target:    The EntityId of the entity damaged.
    // attacker:  The EntityId of the attacker.
    // time:      The time of the packet that carried the damage.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void OnDamage(EntityId target, EntityId attacker, DateTime time)
    {
        if (attacker.Exists == false)
        {
            return;
        }

        WardWatch? watch;
        lock (_lock)
        {
            if (!_watchesByWard.TryGetValue(target, out watch))
            {
                return;
            }
        }

        watch.RecordDamage(attacker, time);
    }
}