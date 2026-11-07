namespace Glass.Data.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// Companion
//
// A spawn owned by a character, such as a pet, familiar or warder, seen in one zone under one
// spawn id.  A companion moves between zones with its owner, so it is not a mob type and is
// not tied to a zone beyond its current placement.  Its name may change between summons or
// stay fixed, depending on the companion.
//
// OwnerSpawnId is the owner's spawn id as sent in the companion's ZoneEntry.  OwnerId is the
// owner's persistent character id, and Owner the user's own character, each set when the
// owner can be identified; OwnerId stays None and Owner null otherwise.
//
// Companions are held for the run only and are never stored in the database.
///////////////////////////////////////////////////////////////////////////////////////////////
public class Companion
{
    public ZoneId ZoneId;
    public SpawnId SpawnId;
    public string Name = string.Empty;
    public ushort Level;
    public uint Class;
    public uint Model;
    public SpawnId OwnerSpawnId;
    public CharacterId OwnerId = CharacterId.None;
    public Character? Owner = null;
}