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
// In-memory repository of the raids seen this run, held in a dictionary keyed by RaidId, the server's raid
// id.  Each raid manages its own groups and ungrouped members.  Nothing is read from or written to the
// database.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class RaidRepository
{
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    private static RaidRepository? _instance = null;

    private readonly Dictionary<RaidId, Raid> _raids;

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

        GlassContext.SessionRegistry.AllSessionsDisconnected += Clear;

        DebugLog.Write(LogChannel.Database, "RaidRepository: singleton instance created with an empty table.",
            defaultLoglevel);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CreateRaid
    //
    // Creates an empty raid with the given raid id and stores it.  A raid id of None, and a raid id already
    // held, are each rejected and nothing changes.
    //
    // raidId:  The raid id the server uses for the raid.
    //
    // Returns the new raid, or null if the raid id was rejected.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Raid? CreateRaid(RaidId raidId)
    {
        if (raidId.Exists == false)
        {
            DebugLog.Write(LogChannel.Database, "RaidRepository.CreateRaid: raid id is None, no raid created.",
                LogLevel.Error);
            return null;
        }

        if (_raids.ContainsKey(raidId))
        {
            DebugLog.Write(LogChannel.Database, "RaidRepository.CreateRaid: raid id 0x"
                + raidId.Value.ToString("X") + " is already held, no raid created.", LogLevel.Warn);
            return null;
        }

        Raid raid = new Raid();
        raid.Id = raidId;

        lock (GlassContext.MembershipLock)
        {
            _raids[raid.Id] = raid;
        }

        GlassContext.SignalBus.Publish(new SignalMembershipChanged(GroupId.None, raid.Id));

        DebugLog.Write(LogChannel.Database, "RaidRepository.CreateRaid: created raid id 0x"
            + raidId.Value.ToString("X") + ".", defaultLoglevel);
        return raid;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RemoveRaid
    //
    // Removes the given raid and takes each of its groups out of it.  A raid not held is rejected and nothing
    // changes.
    //
    // raid:  The raid to remove.
    //
    // Returns true if the raid was removed, false if it was not held.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool RemoveRaid(Raid raid)
    {
        bool removed;

        lock (GlassContext.MembershipLock)
        {
            removed = _raids.Remove(raid.Id);
        }

        if (removed == false)
        {
            DebugLog.Write(LogChannel.General, "RaidRepository.RemoveRaid: raid id 0x" + raid.Id.Value.ToString("X")
                + " is not held, nothing removed.", LogLevel.Warn);
            return false;
        }

        List<Group> groups = new List<Group>(raid.Groups);
        foreach (Group group in groups)
        {
            raid.RemoveGroup(group);
        }

        GlassContext.SignalBus.Publish(new SignalMembershipChanged(GroupId.None, raid.Id));

        DebugLog.Write(LogChannel.General, "RaidRepository.RemoveRaid: removed raid id 0x" + raid.Id.Value.ToString("X")
            + " and " + groups.Count + " groups from it.", defaultLoglevel);
        return true;
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
        lock (GlassContext.MembershipLock)
        {
            if (_raids.TryGetValue(raidId, out raid))
            {
                return true;
            }
        }

        DebugLog.Write(LogChannel.Database, "RaidRepository.TryGetRaid: raid id 0x" + raidId.Value.ToString("X")
            + " is not held.", defaultLoglevel);
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
                DebugLog.Write(LogChannel.Database, "RaidRepository.TryGetRaidOf: characterId=0x"
                    + characterId.Value.ToString("X") + " is in group id 0x" + group.Id.Value.ToString("X")
                    + ", which is in no raid.", defaultLoglevel);
                return false;
            }

            if (TryGetRaid(group.RaidId, out raid) == false)
            {
                DebugLog.Write(LogChannel.Database, "RaidRepository.TryGetRaidOf: characterId=0x"
                    + characterId.Value.ToString("X") + " is in group id 0x" + group.Id.Value.ToString("X")
                    + ", whose raid id 0x" + group.RaidId.Value.ToString("X") + " is not held.", LogLevel.Error);
                return false;
            }

            return true;
        }

        lock (GlassContext.MembershipLock)
        {
            foreach (Raid candidate in _raids.Values)
            {
                if (candidate.HasUngroupedMember(characterId))
                {
                    raid = candidate;
                    break;
                }
            }
        }

        if (raid != null)
        {
            return true;
        }

        DebugLog.Write(LogChannel.Database, "RaidRepository.TryGetRaidOf: characterId=0x"
            + characterId.Value.ToString("X") + " belongs to no raid.", defaultLoglevel);
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
        List<Raid> liveRaids;
        lock (GlassContext.MembershipLock)
        {
            liveRaids = new List<Raid>(_raids.Values);
        }

        List<Raid> raids = new List<Raid>();
        foreach (Raid raid in liveRaids)
        {
            raids.Add(raid.Copy());
        }
        raids.Sort((Raid left, Raid right) => left.Id.Value.CompareTo(right.Id.Value));

        DebugLog.Write(LogChannel.Database, "RaidRepository.GetAllRaids: returning " + raids.Count + " raids.",
            defaultLoglevel);
        return raids;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Clear
    //
    // Removes every raid.  A membership change is published so that views of the raids rebuild.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Clear()
    {
        int raidCount = _raids.Count;

        lock (GlassContext.MembershipLock)
        {
            _raids.Clear();
        }

        GlassContext.SignalBus.Publish(new SignalMembershipChanged(GroupId.None, RaidId.None));

        DebugLog.Write(LogChannel.Database, "RaidRepository.Clear: removed " + raidCount + " raids.",
            defaultLoglevel);
    }
}