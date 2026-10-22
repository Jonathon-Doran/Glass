using System;

namespace Glass.World;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// MobType
//
// Record for one type of mob in one zone, identified by the zone and the spawn name with its trailing
// instance digits removed.  MobTypeId is the Glass-assigned identifier and is the MobTypes row id.  The
// ZoneEntry fields hold the values from the most recent ZoneEntry of this type, except the level range,
// which widens to cover every level seen.  The combat fields accumulate across sightings and are zero
// until combat observations are recorded.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class MobType
{
    public MobId Id;
    public ZoneId ZoneId;
    public string BaseName = string.Empty;

    // From ZoneEntry.
    public uint Model;
    public uint Class;
    public uint BodyType;
    public float Size;
    public float WalkSpeed;
    public float RunSpeed;
    public ushort LevelMin;
    public ushort LevelMax;

    // Bookkeeping.
    public DateTime FirstSeen;
    public DateTime LastSeen;
    public uint Sightings;

    // Combat, accumulated.
    public uint HitsObserved;
    public ulong DamageTotal;
    public uint DamageMax;
    public ulong AttackIntervalSumMs;
    public uint AttackIntervalCount;
    public uint SpellsCast;
    public uint ManaPool;
    public uint MaxHp;
    public uint Kills;
    public uint Deaths;
}