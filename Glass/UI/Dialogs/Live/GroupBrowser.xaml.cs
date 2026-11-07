using Glass.Core;
using Glass.Core.Logging;
using Glass.Core.Signals;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Client;
using Glass.World;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Glass;

///////////////////////////////////////////////////////////////////////////////////////////////
// GroupBrowser
//
// Modeless read-only browser over group and raid membership.  Each raid is a tree whose
// branches are its numbered groups and its ungrouped members.  Each group in no raid is its
// own tree.  Each character with a session that is in no group and no raid is its own root.
// Pressing Refresh rebuilds every tree from the repositories.  The window never changes any
// repository.
///////////////////////////////////////////////////////////////////////////////////////////////
public partial class GroupBrowser : Window
{
    private int _refreshPending = 0;

    private const LogLevel defaultLoglevel = LogLevel.Trace;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GroupBrowser (constructor)
    //
    // Builds the window and fills the tree once.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public GroupBrowser()
    {
        InitializeComponent();
        RefreshTree();
        GlassContext.SignalBus.Subscribe<SignalMembershipChanged>(OnMembershipChanged);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OnClosed
    //
    // Unsubscribes from membership changes when the window closes.
    //
    // e:  The event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    protected override void OnClosed(System.EventArgs e)
    {
        GlassContext.SignalBus.Unsubscribe<SignalMembershipChanged>(OnMembershipChanged);
        DebugLog.Write(LogChannel.General, "GroupBrowser.OnClosed: unsubscribed from membership changes",
            defaultLoglevel);
        base.OnClosed(e);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_Refresh_Click
    //
    // Rebuilds the tree from the repositories.
    //
    // sender:  The refresh button.
    // e:       The routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_Refresh_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.General, "GroupBrowser.Button_Refresh_Click: refresh requested",
            defaultLoglevel);
        RefreshTree();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BuildMemberNode
    //
    // Builds the tree node for one group or raid member: name, class, level and persistent
    // character id, followed by any designations, and marked as one of the user's characters
    // when it is one.
    //
    // member:  The member to show.
    //
    // Returns the node.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static TreeViewItem BuildMemberNode(GroupMember member)
    {
        string text = member.Name + "  " + member.Class + " " + member.Level + "  [" + member.Id + "]";

        if (member.Designations != GroupDesignation.None)
        {
            text += "  " + member.Designations;
        }

        if (member.Character != null)
        {
            text += "  (yours)";
        }

        TreeViewItem node = new TreeViewItem();
        node.Header = text;
        return node;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BuildGroupNode
    //
    // Builds the tree node for one group, with one child node per member.  The node is titled
    // with the given label, the group id and the member count.
    //
    // group:  The group to show.
    // label:  The title the caller has composed, such as "Group 3" or "Group".
    //
    // Returns the node, expanded.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static TreeViewItem BuildGroupNode(Group group, string label)
    {
        TreeViewItem node = new TreeViewItem();
        node.Header = label + "  [groupId " + group.Id + "]  " + group.Members.Count + " of "
            + Group.MaxMembers + " members";
        node.IsExpanded = true;

        foreach (GroupMember member in group.Members)
        {
            node.Items.Add(BuildMemberNode(member));
        }

        return node;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BuildRaidNode
    //
    // Builds the tree for one raid: a branch per occupied numbered group, in number order,
    // and an Ungrouped branch holding the raid members in no group when there are any.  Empty
    // group positions are not shown.
    //
    // raid:  The raid to show.
    //
    // Returns the root node of the raid's tree, expanded.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static TreeViewItem BuildRaidNode(Raid raid)
    {
        TreeViewItem node = new TreeViewItem();
        node.IsExpanded = true;

        uint groupCount = 0;
        for (uint raidGroupNumber = 1; raidGroupNumber <= Raid.MaxGroups; raidGroupNumber++)
        {
            Group? group = raid.GroupAt(raidGroupNumber);
            if (group == null)
            {
                continue;
            }

            node.Items.Add(BuildGroupNode(group, "Group " + raidGroupNumber));
            groupCount++;
        }

        if (raid.Ungrouped.Count > 0)
        {
            TreeViewItem ungroupedNode = new TreeViewItem();
            ungroupedNode.Header = "Ungrouped  " + raid.Ungrouped.Count + " members";
            ungroupedNode.IsExpanded = true;

            foreach (GroupMember member in raid.Ungrouped)
            {
                ungroupedNode.Items.Add(BuildMemberNode(member));
            }

            node.Items.Add(ungroupedNode);
        }

        node.Header = "Raid  [raidId " + raid.Id + "]  leader " + raid.LeaderId + ", " + groupCount
            + " groups, " + raid.Ungrouped.Count + " ungrouped";
        return node;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RefreshTree
    //
    // Rebuilds every tree: one per raid, one per group that is in no raid, and one root per
    // character with a session that belongs to no group and no raid.  The count line reports
    // how many of each are shown.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void RefreshTree()
    {
        MembershipTree.Items.Clear();

        List<Raid> raids = RaidRepository.Instance.GetAllRaids();
        foreach (Raid raid in raids)
        {
            MembershipTree.Items.Add(BuildRaidNode(raid));
        }

        uint freeGroups = 0;
        List<Group> groups = GroupRepository.Instance.GetAllGroups();
        foreach (Group group in groups)
        {
            if (group.RaidId.Exists)
            {
                continue;
            }

            MembershipTree.Items.Add(BuildGroupNode(group, "Group"));
            freeGroups++;
        }

        uint soloCharacters = 0;
        List<Character> characters = SessionStore.Instance.GetSessionCharacters();
        foreach (Character character in characters)
        {
            if (GroupRepository.Instance.TryGetGroupOf(character.CharacterId, out Group? group))
            {
                continue;
            }

            if (RaidRepository.Instance.TryGetRaidOf(character.CharacterId, out Raid? raid))
            {
                continue;
            }

            TreeViewItem node = new TreeViewItem();
            node.Header = character.Name + "  [" + character.CharacterId + "]  no group";
            MembershipTree.Items.Add(node);
            soloCharacters++;
        }

        ResultCount.Text = raids.Count + " raids, " + freeGroups + " groups in no raid, " + soloCharacters
            + " characters in no group";

        DebugLog.Write(LogChannel.General, "GroupBrowser.RefreshTree: " + raids.Count + " raids, " + freeGroups
            + " groups in no raid, " + soloCharacters + " of " + characters.Count
            + " session characters in no group", defaultLoglevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OnMembershipChanged
    //
    // Schedules one tree rebuild on the UI thread for a membership change published on any
    // thread.  While a rebuild is already scheduled, further changes are folded into it.
    //
    // signal:  The membership change.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void OnMembershipChanged(SignalMembershipChanged signal)
    {
        if (Interlocked.Exchange(ref _refreshPending, 1) == 1)
        {
            return;
        }

        DebugLog.Write(LogChannel.General, "GroupBrowser.OnMembershipChanged: groupId=" + signal.GroupId
            + " raidId=" + signal.RaidId + ", rebuild scheduled", defaultLoglevel);

        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            Interlocked.Exchange(ref _refreshPending, 0);
            RefreshTree();
        }));
    }
}