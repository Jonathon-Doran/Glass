//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Spawn
//
// Record for a single observed spawn instance of a mob in a zone. MobId is the Glass-assigned identifier
// for this instance and is unique for the lifetime of the process. SpawnId is the server-assigned id and
// is only meaningful within the zone identified by ZoneId. Position, level, hp, and race reflect the most
// recently decoded values. LastSeen is the time of the most recent packet that referenced this instance.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class Spawn
{
    public uint MobId;
    public uint ZoneId;
    public uint SpawnId;
    public string? Name;
    public float X;
    public float Y;
    public float Z;
    public ushort Level;
    public int CurrentHp;
    public int Race;
    public DateTime LastSeen;
}