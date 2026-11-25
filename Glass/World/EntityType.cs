namespace Glass.World;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// EntityType
//
// The kind of entity an EntityId identifies, stored in the identifier's high byte.  The type selects the repository
// that holds the entity, so an identifier alone is enough to find its entity.
//
// None is the default value (zero) and marks an identifier that was never assigned.  Character is a player
// character, whether played by this Glass instance or by anyone else.  Mob is a non-player creature that no one
// owns.  Pet is a creature summoned to fight for its owner.  Familiar is a non-combat creature that follows its
// owner.  Mercenary is a hired creature that can join a group but cannot be controlled.  Mount is a creature ridden
// by a player character, which takes no damage.  Corpse is the remains of any creature.  Aura is a spell object
// placed in the world.  Warder is a beastlord animal companion.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public enum EntityType : byte
{
    None = 0,
    Character,
    Mob,
    Pet,
    Familiar,
    Mercenary,
    Mount,
    Warder,
    Corpse,
    Aura
}