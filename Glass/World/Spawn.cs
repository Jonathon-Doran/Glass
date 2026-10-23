//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Spawn
//
// Record for a single observed spawn instance of a mob in a zone. MobId is the Glass-assigned identifier
// for this instance and is unique for the lifetime of the process. SpawnId is the server-assigned id and
// is only meaningful within the zone identified by ZoneId. Position, level, hp, and race reflect the most
// recently decoded values. LastSeen is the time of the most recent packet that referenced this instance.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
using Glass.Network.Protocol;

public class Spawn
{
    public MobId MobId;
    public ZoneId ZoneId;
    public SpawnId SpawnId;
    public string? Name;
    public float X;
    public float Y;
    public float Z;
    public ushort Level;
    public int CurrentHp;
    public int Race;
    public DateTime Observed;
    public uint A;
    public uint PersistentId;
    public uint IsPlayer;
    public uint Type;
    public float Unknown1;
    public uint Unknown2;
    public uint Unknown3;
    public uint C;
    public uint Model;
    public uint Class;
    public string? LastName;
    public uint OwnerId;
}