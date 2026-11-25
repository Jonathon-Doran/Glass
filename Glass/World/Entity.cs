namespace Glass.World;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Entity
//
// Base of every class that represents a creature or object in the world.  EntityId is the entity's identifier for
// the run and does not change when the entity moves between zones.  ZoneId is the zone the entity is currently in,
// and SpawnId is the server's identifier for it within that zone; both change when the entity moves to another zone.
// All three are None until assigned.  Name is the entity's name as the server sends it, empty until assigned.
// CurrentHP and MaxHP are the entity's hit points as last reported.  A MaxHP of 0 means no HP has been reported,
// which is the normal state for an entity whose HP the server does not send; CurrentHP has no meaning then.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public abstract class Entity
{
    public EntityId EntityId { get; set; } = EntityId.None;
    public ZoneId ZoneId { get; set; } = ZoneId.None;
    public SpawnId SpawnId { get; set; } = SpawnId.None;
    public string Name { get; set; } = string.Empty;
    public long CurrentHP { get; set; } = 0;
    public ulong MaxHP { get; set; } = 0;
}
