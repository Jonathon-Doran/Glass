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

    private const LogLevel defaultLoglevel = LogLevel.Info;

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
        DebugLog.Write(LogChannel.UI, "GroupBrowser.OnClosed: unsubscribed from membership changes",
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
        DebugLog.Write(LogChannel.UI, "GroupBrowser.Button_Refresh_Click: refresh requested",
            defaultLoglevel);
        RefreshTree();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BuildMemberNode
    //
    // Builds the tree node for one group or raid member: name, class, level and persistent
    // character id, followed by the roles the member holds in its group (leader, main tank,
    // main assist).
    //
    // member:  The member to show.
    // group:   The group the member belongs to, or null for a member in no group.
    //
    // Returns the node.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static TreeViewItem BuildMemberNode(GroupMember member, Group? group)
    {
        EQClass memberClass = member.Class;
        if (member.Character != null)
        {
            memberClass = member.Character.Class;
        }

        string text = member.Name + "  " + memberClass.ToDisplayString() + " " + member.Level + "  [0x"
            + member.Id.Value.ToString("X") + "]";

        if (group != null)
        {
            if (group.LeaderId == member.Id)
            {
                text += "  Leader";
            }

            if (group.MainTankId == member.Id)
            {
                text += "  MainTank";
            }

            if (group.MainAssistId == member.Id)
            {
                text += "  MainAssist";
            }
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
    // label:  The title the caller has composed, such as "Group".
    //
    // Returns the node, expanded.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static TreeViewItem BuildGroupNode(Group group, string label)
    {
        TreeViewItem node = new TreeViewItem();
        node.Header = label + "  [group id 0x" + group.Id.Value.ToString("X") + "]  " + group.Members.Count
            + " members";
        node.IsExpanded = true;

        foreach (GroupMember member in group.Members)
        {
            node.Items.Add(BuildMemberNode(member, group));
        }

        return node;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BuildRaidNode
    //
    // Builds the tree for one raid: a branch per group, and an Ungrouped branch holding the
    // raid members in no group when there are any.
    //
    // raid:  The raid to show.
    //
    // Returns the root node of the raid's tree, expanded.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static TreeViewItem BuildRaidNode(Raid raid)
    {
        TreeViewItem node = new TreeViewItem();
        node.IsExpanded = true;

        foreach (Group group in raid.Groups)
        {
            node.Items.Add(BuildGroupNode(group, "Group"));
        }

        if (raid.Ungrouped.Count > 0)
        {
            TreeViewItem ungroupedNode = new TreeViewItem();
            ungroupedNode.Header = "Ungrouped  " + raid.Ungrouped.Count + " members";
            ungroupedNode.IsExpanded = true;

            foreach (GroupMember member in raid.Ungrouped)
            {
                ungroupedNode.Items.Add(BuildMemberNode(member, null));
            }

            node.Items.Add(ungroupedNode);
        }

        string leader = "None";
        if (raid.LeaderId.Exists)
        {
            Character? leaderCharacter = CharacterRepository.Instance.GetByCharacterId(raid.LeaderId);
            if (leaderCharacter != null)
            {
                leader = leaderCharacter.Name;
            }
            else
            {
                leader = "0x" + raid.LeaderId.Value.ToString("X");
            }
        }

        node.Header = "Raid  [raid id 0x" + raid.Id.Value.ToString("X") + "]  leader " + leader + ", "
            + raid.Groups.Count + " groups, " + raid.Ungrouped.Count + " ungrouped";
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
            node.Header = character.Name + "  [0x" + character.CharacterId.Value.ToString("X") + "]  no group";
            MembershipTree.Items.Add(node);
            soloCharacters++;
        }

        ResultCount.Text = raids.Count + " raids, " + freeGroups + " groups in no raid, " + soloCharacters
            + " characters in no group";

        DebugLog.Write(LogChannel.UI, "GroupBrowser.RefreshTree: " + raids.Count + " raids, " + freeGroups
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

        DebugLog.Write(LogChannel.UI, "GroupBrowser.OnMembershipChanged: groupId=" + signal.GroupId
            + " raidId=" + signal.RaidId + ", rebuild scheduled", defaultLoglevel);

        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            Interlocked.Exchange(ref _refreshPending, 0);
            RefreshTree();
        }));
    }
}

#if DEBUG
///////////////////////////////////////////////////////////////////////////////////////////////
// MembershipChangeLog
//
// Debug-only record of what the group browser would show.  On each membership change it
// builds the browser's lines from the repositories and logs each line that is gone and each
// line that is new, so the order and timing of visible changes can be compared with the
// packets.  It needs no window.
///////////////////////////////////////////////////////////////////////////////////////////////
public class MembershipChangeLog
{
    private const LogLevel defaultLoglevel = LogLevel.Info;

    private List<string> _shownLines = new List<string>();

    ///////////////////////////////////////////////////////////////////////////////////////////
    // MembershipChangeLog (constructor)
    //
    // Subscribes to membership changes.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public MembershipChangeLog()
    {
        GlassContext.SignalBus.Subscribe<SignalMembershipChanged>(OnMembershipChanged);

        DebugLog.Write(LogChannel.UI, "MembershipChangeLog: subscribed to membership changes.", defaultLoglevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // MemberText
    //
    // Builds the text the browser shows for one group or raid member: name, class, level and
    // character id, followed by the roles the member holds in its group (leader, main tank,
    // main assist).
    //
    // member:  The member to describe.
    // group:   The group the member belongs to, or null for a member in no group.
    //
    // Returns the text.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static string MemberText(GroupMember member, Group? group)
    {
        EQClass memberClass = member.Class;
        if (member.Character != null)
        {
            memberClass = member.Character.Class;
        }

        string text = member.Name + "  " + memberClass.ToDisplayString() + " " + member.Level + "  [0x"
            + member.Id.Value.ToString("X") + "]";

        if (group != null)
        {
            if (group.LeaderId == member.Id)
            {
                text += "  Leader";
            }

            if (group.MainTankId == member.Id)
            {
                text += "  MainTank";
            }

            if (group.MainAssistId == member.Id)
            {
                text += "  MainAssist";
            }
        }

        return text;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GroupText
    //
    // Builds the header text the browser shows for one group: the label, the group id and
    // the member count.
    //
    // group:  The group to describe.
    // label:  The title shown before the group id, such as "Group".
    //
    // Returns the text.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static string GroupText(Group group, string label)
    {
        return label + "  [group id 0x" + group.Id.Value.ToString("X") + "]  " + group.Members.Count + " members";
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RaidText
    //
    // Builds the header text the browser shows for one raid: the raid id, the leader's
    // character id or None, the group count and the ungrouped member count.
    //
    // raid:  The raid to describe.
    //
    // Returns the text.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static string RaidText(Raid raid)
    {
        string leader = "None";
        if (raid.LeaderId.Exists)
        {
            Character? leaderCharacter = CharacterRepository.Instance.GetByCharacterId(raid.LeaderId);
            if (leaderCharacter != null)
            {
                leader = leaderCharacter.Name;
            }
            else
            {
                leader = "0x" + raid.LeaderId.Value.ToString("X");
            }
        }

        return "Raid  [raid id 0x" + raid.Id.Value.ToString("X") + "]  leader " + leader + ", "
            + raid.Groups.Count + " groups, " + raid.Ungrouped.Count + " ungrouped";
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CollectLines
    //
    // Builds the lines the browser would show, in its order: each raid with its groups,
    // their members and its ungrouped members; each group in no raid with its members; and
    // each session character in no group and no raid.  Each line is preceded by the ids of
    // its parent lines, separated by " / ", so counts appear only on the line they belong to.
    //
    // Returns the lines.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static List<string> CollectLines()
    {
        List<string> lines = new List<string>();

        List<Raid> raids = RaidRepository.Instance.GetAllRaids();
        foreach (Raid raid in raids)
        {
            string raidPrefix = "Raid  [raid id 0x" + raid.Id.Value.ToString("X") + "]";
            lines.Add(RaidText(raid));

            foreach (Group group in raid.Groups)
            {
                string groupPrefix = raidPrefix + " / Group  [group id 0x" + group.Id.Value.ToString("X") + "]";
                lines.Add(raidPrefix + " / " + GroupText(group, "Group"));

                foreach (GroupMember member in group.Members)
                {
                    lines.Add(groupPrefix + " / " + MemberText(member, group));
                }
            }

            if (raid.Ungrouped.Count > 0)
            {
                string ungroupedPrefix = raidPrefix + " / Ungrouped";
                lines.Add(ungroupedPrefix + "  " + raid.Ungrouped.Count + " members");

                foreach (GroupMember member in raid.Ungrouped)
                {
                    lines.Add(ungroupedPrefix + " / " + MemberText(member, null));
                }
            }
        }

        List<Group> groups = GroupRepository.Instance.GetAllGroups();
        foreach (Group group in groups)
        {
            if (group.RaidId.Exists)
            {
                continue;
            }

            string groupPrefix = "Group  [group id 0x" + group.Id.Value.ToString("X") + "]";
            lines.Add(GroupText(group, "Group"));

            foreach (GroupMember member in group.Members)
            {
                lines.Add(groupPrefix + " / " + MemberText(member, group));
            }
        }

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

            lines.Add(character.Name + "  [0x" + character.CharacterId.Value.ToString("X") + "]  no group");
        }

        return lines;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OnMembershipChanged
    //
    // Builds the lines the browser would now show, logs each line that is gone and each line
    // that is new since the last change, and keeps the new lines for the next comparison.  A
    // change with no visible effect logs nothing.
    //
    // signal:  The membership change.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void OnMembershipChanged(SignalMembershipChanged signal)
    {
        List<string> lines = CollectLines();

        HashSet<string> previous = new HashSet<string>(_shownLines);
        HashSet<string> current = new HashSet<string>(lines);

        foreach (string line in _shownLines)
        {
            if (current.Contains(line) == false)
            {
                DebugLog.Write(LogChannel.UI, "MembershipChangeLog.OnMembershipChanged: gone  " + line,
                    defaultLoglevel);
            }
        }

        foreach (string line in lines)
        {
            if (previous.Contains(line) == false)
            {
                DebugLog.Write(LogChannel.UI, "MembershipChangeLog.OnMembershipChanged: shown  " + line,
                    defaultLoglevel);
            }
        }

        _shownLines = lines;
    }
}
#endif