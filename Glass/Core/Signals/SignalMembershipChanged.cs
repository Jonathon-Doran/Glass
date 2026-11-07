using Glass.World;

namespace Glass.Core.Signals;

///////////////////////////////////////////////////////////////////////////////////////////////
// SignalMembershipChanged
//
// Raised whenever group or raid membership changes: a group or raid is created, a member
// is added to or removed from a group or a raid's ungrouped members, or a group is placed
// in or removed from a raid.
//
// Published on the thread that made the change.  Subscribers that need a specific thread
// (UI, for example) marshal in their handler.
//
// Carries the group and the raid the change concerns; either is None when the change does
// not involve one.
///////////////////////////////////////////////////////////////////////////////////////////////
public class SignalMembershipChanged
{
    public GroupId GroupId
    {
        get;
    }

    public RaidId RaidId
    {
        get;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SignalMembershipChanged (constructor)
    //
    // groupId:  The group the change concerns, or None.
    // raidId:   The raid the change concerns, or None.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SignalMembershipChanged(GroupId groupId, RaidId raidId)
    {
        GroupId = groupId;
        RaidId = raidId;
    }
}