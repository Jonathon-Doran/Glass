using Glass.Core;
using Glass.Core.Logging;
using Glass.Core.Signals;
using Glass.World;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// RaidRepository
//
// In-memory repository of the raids seen this run, held in a dictionary keyed by RaidId.  Each raid manages
// its own groups and ungrouped members.  Raid ids are assigned here in sequence, independent of group ids.
// Nothing is read from or written to the database.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class RaidRepository
{
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    private static RaidRepository? _instance = null;

    private readonly Dictionary<RaidId, Raid> _raids;
    private uint _nextRaidId = 0;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // Lazy singleton accessor.  The instance is created on first access with an empty table.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static RaidRepository Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new RaidRepository();
            }
            return _instance;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RaidRepository
    //
    // Private constructor.  Initializes an empty table.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private RaidRepository()
    {
        _raids = new Dictionary<RaidId, Raid>();
        DebugLog.Write(LogChannel.Database, "RaidRepository: singleton instance created with an empty table.",
            defaultLoglevel);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CreateRaid
    //
    // Creates an empty raid with the next raid id in sequence and stores it.
    //
    // Returns the new raid.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Raid CreateRaid()
    {
        Raid raid = new Raid();
        raid.Id = new RaidId(_nextRaidId);
        _nextRaidId++;

        _raids[raid.Id] = raid;
        GlassContext.SignalBus.Publish(new SignalMembershipChanged(GroupId.None, raid.Id));

        DebugLog.Write(LogChannel.Database, "RaidRepository.CreateRaid: created raidId=" + raid.Id + ".",
            defaultLoglevel);
        return raid;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetRaid
    //
    // Looks up the raid with the given id.
    //
    // raidId:  The raid id to look up.
    // raid:    Receives the raid if found, null otherwise.
    //
    // Returns true if the raid is held, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool TryGetRaid(RaidId raidId, [NotNullWhen(true)] out Raid? raid)
    {
        if (_raids.TryGetValue(raidId, out raid))
        {
            return true;
        }

        DebugLog.Write(LogChannel.Database, "RaidRepository.TryGetRaid: raidId=" + raidId + " is not held.",
            defaultLoglevel);
        return false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetRaidOf
    //
    // Looks up the raid a character belongs to.  A character in a group belongs to the raid that holds its
    // group; a character in no group belongs to the raid that holds it as an ungrouped member.  A group
    // whose raid id is not held here is logged at Error and treated as in no raid.
    //
    // characterId:  The persistent character id of the character.
    // raid:         Receives the raid if found, null otherwise.
    //
    // Returns true if the character belongs to a raid, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool TryGetRaidOf(CharacterId characterId, [NotNullWhen(true)] out Raid? raid)
    {
        raid = null;

        if (GroupRepository.Instance.TryGetGroupOf(characterId, out Group? group))
        {
            if (group.RaidId.Exists == false)
            {
                DebugLog.Write(LogChannel.Database, "RaidRepository.TryGetRaidOf: characterId=" + characterId
                    + " is in groupId=" + group.Id + ", which is in no raid.", defaultLoglevel);
                return false;
            }

            if (TryGetRaid(group.RaidId, out raid) == false)
            {
                DebugLog.Write(LogChannel.Database, "RaidRepository.TryGetRaidOf: characterId=" + characterId
                    + " is in groupId=" + group.Id + ", whose raidId=" + group.RaidId + " is not held.",
                    LogLevel.Error);
                return false;
            }

            return true;
        }

        foreach (Raid candidate in _raids.Values)
        {
            if (candidate.HasUngroupedMember(characterId))
            {
                raid = candidate;
                return true;
            }
        }

        DebugLog.Write(LogChannel.Database, "RaidRepository.TryGetRaidOf: characterId=" + characterId
            + " belongs to no raid.", defaultLoglevel);
        return false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetAllRaids
    //
    // Returns every raid held, in ascending raid id order.  The list is a new copy on every call; the raids
    // themselves are the live objects.
    //
    // Returns the raids; empty when none are held.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<Raid> GetAllRaids()
    {
        List<Raid> raids = new List<Raid>(_raids.Values);
        raids.Sort((Raid left, Raid right) => left.Id.Value.CompareTo(right.Id.Value));

        DebugLog.Write(LogChannel.Database, "RaidRepository.GetAllRaids: returning " + raids.Count + " raids.",
            defaultLoglevel);
        return raids;
    }
}