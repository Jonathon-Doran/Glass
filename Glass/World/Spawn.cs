//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Spawn
//
// Record for a single observed spawn instance of a mob in a zone. MobId is the Glass-assigned identifier
// for this instance and is unique for the lifetime of the process. SpawnId is the server-assigned id and
// is only meaningful within the zone identified by ZoneId. Position, level, hp, and race reflect the most
// recently decoded values. LastSeen is the time of the most recent packet that referenced this instance.
// IsSpawnPoint is true when the spawn appeared after the observing session was established.
// SpawnPosition is the first position decoded for such a spawn, and is null until one is decoded.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.World;

public class Spawn
{
    public MobId MobId;
    public ZoneId ZoneId;
    public SpawnId SpawnId;
    public string? Name;
    public Position? CurrentPosition = null;
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
    public bool IsSpawnPoint = false;
    public Position? SpawnPosition = null;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UpdatePosition
    //
    // Records a newly decoded position as the current position.  When this spawn is a spawn point and has
    // no spawn position yet, the same position is also recorded as the spawn position.
    //
    // position:  The decoded position
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void UpdatePosition(Position position)
    {
        CurrentPosition = position;

        if (IsSpawnPoint && (SpawnPosition == null))
        {
            SpawnPosition = position;
            DebugLog.Write(LogChannel.Opcodes, "Spawn.UpdatePosition: '" + (Name ?? "null") + "' zoneId=" + ZoneId
                + " spawnId=" + SpawnId + " spawn position (" + position.X.ToString("F2") + ","
                + position.Y.ToString("F2") + "," + position.Z.ToString("F2") + ")", LogLevel.Trace);
        }
    }
}