namespace Glass.World;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Entity
//
// Base of every class that represents a creature or object in the world.  EntityId is the entity's identifier for
// the run and does not change when the entity moves between zones.  ZoneId is the zone the entity is currently in,
// and SpawnId is the server's identifier for it within that zone; both change when the entity moves to another zone.
// All three are None until assigned.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public abstract class Entity
{
    public EntityId EntityId { get; set; } = EntityId.None;
    public ZoneId ZoneId { get; set; } = ZoneId.None;
    public SpawnId SpawnId { get; set; } = SpawnId.None;
}
