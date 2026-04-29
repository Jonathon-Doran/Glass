using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Glass.Controls;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// RelayGroupMatrix
//
// A custom templated control that renders a matrix of relay groups (rows) and characters (columns).
// The group name column and character name header row are both frozen.
// The cell grid scrolls in both directions. All scroll viewers are synchronized.
// Raises MembershipChanged when a cell is toggled.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class RelayGroupMatrix : Control
{
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MembershipChangedEventArgs
    //
    // Carries the relay group ID, character ID, and new membership state.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public class MembershipChangedEventArgs : EventArgs
    {
        public int GroupId { get; init; }
        public int CharacterId { get; init; }
        public bool Added { get; init; }
    }

    public event EventHandler<RelayGroupMatrix.MembershipChangedEventArgs>? MembershipChanged;

    private const string PartHeaderScrollViewer = "PART_HeaderScrollViewer";
    private const string PartLabelScrollViewer = "PART_LabelScrollViewer";
    private const string PartScrollViewer = "PART_ScrollViewer";
    private const string PartHeaderGrid = "PART_HeaderGrid";
    private const string PartLabelGrid = "PART_LabelGrid";
    private const string PartGrid = "PART_Grid";

    private const double RowHeight = 28.0;

    private ScrollViewer? _headerScrollViewer;
    private ScrollViewer? _labelScrollViewer;
    private ScrollViewer? _scrollViewer;
    private Grid? _headerGrid;
    private Grid? _labelGrid;
    private Grid? _grid;

    private List<RelayGroup> _groups = new();
    private List<Character> _characters = new();
    private HashSet<(int GroupId, int CharacterId)> _membership = new();

    // for header clicks
    private int _selectedCharacterId = -1;
    private readonly Dictionary<int, List<Border>> _cellsByCharacter = new();

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Static constructor
    //
    // Registers the default style key so WPF resolves the template from Generic.xaml.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    static RelayGroupMatrix()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(RelayGroupMatrix),
            new FrameworkPropertyMetadata(typeof(RelayGroupMatrix)));
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnApplyTemplate
    //
    // Finds named template parts, wires scroll synchronization, and triggers the initial grid build.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public override void OnApplyTemplate()
    {
        DebugLog.Write(LogChannel.Input, "RelayGroupMatrix.OnApplyTemplate.");

        base.OnApplyTemplate();

        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
            DebugLog.Write("RelayGroupMatrix.OnApplyTemplate: detached previous scroll handler.");
        }

        _headerScrollViewer = GetTemplateChild(PartHeaderScrollViewer) as ScrollViewer;
        _labelScrollViewer = GetTemplateChild(PartLabelScrollViewer) as ScrollViewer;
        _scrollViewer = GetTemplateChild(PartScrollViewer) as ScrollViewer;
        _headerGrid = GetTemplateChild(PartHeaderGrid) as Grid;
        _labelGrid = GetTemplateChild(PartLabelGrid) as Grid;
        _grid = GetTemplateChild(PartGrid) as Grid;

        DebugLog.Write(LogChannel.Input, $"RelayGroupMatrix.OnApplyTemplate: headerScrollViewer={_headerScrollViewer != null} labelScrollViewer={_labelScrollViewer != null} scrollViewer={_scrollViewer != null}.");

        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
        }

        BuildGrid();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ScrollViewer_ScrollChanged
    //
    // Synchronizes horizontal scroll to the header panel and vertical scroll to the label panel.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if ((_headerScrollViewer != null) && (e.HorizontalChange != 0))
        {
            _headerScrollViewer.ScrollToHorizontalOffset(e.HorizontalOffset);
        }

        if ((_labelScrollViewer != null) && (e.VerticalChange != 0))
        {
            _labelScrollViewer.ScrollToVerticalOffset(e.VerticalOffset);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Load
    //
    // Provides the data to display. Rebuilds the grid immediately if the template
    // has already been applied.
    //
    // groups:      Relay groups to display as rows
    // characters:  Characters to display as columns
    // membership:  Current set of (groupId, characterId) membership pairs
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Load(
        List<RelayGroup> groups,
        List<Character> characters,
        HashSet<(int GroupId, int CharacterId)> membership)
    {
        DebugLog.Write(LogChannel.Input, $"RelayGroupMatrix.Load: groups={groups.Count} characters={characters.Count} memberships={membership.Count}.");

        _groups = groups;
        _characters = characters;
        _membership = membership;

        BuildGrid();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // BuildGrid
    //
    // Clears and rebuilds all four grid panels from current data.
    // Uses a fixed row height so label and cell grids stay vertically aligned.
    // Does nothing if template parts are not yet available.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private void BuildGrid()
    {
        if ((_headerGrid == null) || (_labelGrid == null) || (_grid == null))
        {
            DebugLog.Write(LogChannel.Input, "RelayGroupMatrix.BuildGrid: grids not ready, deferring.");
            return;
        }

        DebugLog.Write($"RelayGroupMatrix.BuildGrid: building {_groups.Count} rows x {_characters.Count} columns.");

        _headerGrid.Children.Clear();
        _headerGrid.RowDefinitions.Clear();
        _headerGrid.ColumnDefinitions.Clear();

        _labelGrid.Children.Clear();
        _labelGrid.RowDefinitions.Clear();
        _labelGrid.ColumnDefinitions.Clear();

        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();

        _cellsByCharacter.Clear();
        _selectedCharacterId = -1;

        GridLength fixedRow = new GridLength(RowHeight);

        // Header grid: single fixed-height row
        _headerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(80) });

        // Label grid: one column, one row per group plus spacer
        _labelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var _ in _groups)
        {
            _labelGrid.RowDefinitions.Add(new RowDefinition { Height = fixedRow });
        }
        _labelGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });

        // Cell grid: one row per group plus spacer
        foreach (var _ in _groups)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = fixedRow });
        }
        _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });

        // Build interleaved column definitions for header and cell grids,
        // and populate header row. gridCol tracks actual grid column including dividers.
        int gridCol = 0;
        for (int col = 0; col < _characters.Count; col++)
        {
            _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            Character character = _characters[col];

            var header = new TextBlock
            {
                Text = character.Name,
                Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0xFF, 0xD7)),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(4, 4, 4, 2),
                LayoutTransform = new RotateTransform(-90),
                Cursor = Cursors.Hand,
                Tag = character.CharacterId
            };

            header.MouseLeftButtonDown += (s, e) =>
            {
                if (s is TextBlock tb && tb.Tag is int charId)
                {
                    SelectColumn(charId);
                }
            };

            Grid.SetRow(header, 0);
            Grid.SetColumn(header, gridCol);
            _headerGrid.Children.Add(header);

            gridCol++;
        }

        // Group rows
        for (int row = 0; row < _groups.Count; row++)
        {
            RelayGroup group = _groups[row];

            // Group name label in label grid
            var groupLabel = new TextBlock
            {
                Text = group.Name,
                Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0xFF, 0xD7)),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 10, 0)
            };
            Grid.SetRow(groupLabel, row);
            Grid.SetColumn(groupLabel, 0);
            _labelGrid.Children.Add(groupLabel);

            // Character cells in cell grid
            gridCol = 0;
            for (int col = 0; col < _characters.Count; col++)
            {
                Character character = _characters[col];
                bool isMember = _membership.Contains((group.Id, character.CharacterId));

                Border cell = BuildCell(group.Id, character.CharacterId, isMember);

                if (!_cellsByCharacter.ContainsKey(character.CharacterId))
                {
                    _cellsByCharacter[character.CharacterId] = new List<Border>();
                }
                _cellsByCharacter[character.CharacterId].Add(cell);

                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, gridCol);
                _grid.Children.Add(cell);

                gridCol++;
            }
            _labelGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
            _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });

            DebugLog.Write(LogChannel.Input, $"RelayGroupMatrix.BuildGrid: row={row} group='{group.Name}' built.");
        }

        DebugLog.Write(LogChannel.Input, "RelayGroupMatrix.BuildGrid: complete.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // BuildCell
    //
    // Creates a single matrix cell styled as a bordered button.
    // Member cells show a cyan check; non-member cells show a dim red cross.
    //
    // groupId:      The relay group ID for this cell
    // characterId:  The character ID for this cell
    // isMember:     Initial membership state
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private Border BuildCell(int groupId, int characterId, bool isMember)
    {
        var text = new TextBlock
        {
            Text = isMember ? "✓" : "✗",
            Foreground = MemberForeground(isMember),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x2A, 0x2A)),
            BorderBrush = MemberBorderBrush(isMember),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Width = 36,
            Height = 24,
            Margin = new Thickness(2),
            Child = text,
            Cursor = Cursors.Hand,
            Tag = (groupId, characterId)
        };

        border.MouseLeftButtonDown += Cell_Click;

        return border;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Cell_Click
    //
    // Toggles the membership state of the clicked cell.
    // Updates the cell visuals and raises MembershipChanged.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Cell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border)
        {
            return;
        }

        var (groupId, characterId) = ((int GroupId, int CharacterId))border.Tag;
        bool wasMember = _membership.Contains((groupId, characterId));
        bool nowMember = !wasMember;

        if (nowMember)
        {
            _membership.Add((groupId, characterId));
        }
        else
        {
            _membership.Remove((groupId, characterId));
        }

        if (border.Child is TextBlock text)
        {
            text.Text = nowMember ? "✓" : "✗";
            text.Foreground = MemberForeground(nowMember);
        }

        border.BorderBrush = MemberBorderBrush(nowMember);

        DebugLog.Write(LogChannel.Input, $"RelayGroupMatrix.Cell_Click: groupId={groupId} characterId={characterId} nowMember={nowMember}.");

        MembershipChanged?.Invoke(this, new MembershipChangedEventArgs
        {
            GroupId = groupId,
            CharacterId = characterId,
            Added = nowMember
        });
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SelectColumn
    //
    // Highlights all cells in the column for the given character ID.
    // Clears the previous selection first. Deselects if the same character is clicked again.
    //
    // characterId:  The character whose column to select, or -1 to clear
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void SelectColumn(int characterId)
    {
        DebugLog.Write(LogChannel.Input, $"RelayGroupMatrix.SelectColumn: characterId={characterId} previousSelection={_selectedCharacterId}.");

        // Clear previous selection
        if ((_selectedCharacterId != -1) && _cellsByCharacter.TryGetValue(_selectedCharacterId, out List<Border>? previous))
        {
            foreach (Border cell in previous)
            {
                cell.BorderBrush = MemberBorderBrush(cell.Child is TextBlock t && t.Text == "✓");
            }
            DebugLog.Write(LogChannel.Input, $"RelayGroupMatrix.SelectColumn: cleared previous selection for characterId={_selectedCharacterId}.");
        }

        if (_selectedCharacterId == characterId)
        {
            _selectedCharacterId = -1;
            DebugLog.Write(LogChannel.Input, "RelayGroupMatrix.SelectColumn: deselected.");
            return;
        }

        _selectedCharacterId = characterId;

        if (_cellsByCharacter.TryGetValue(characterId, out List<Border>? cells))
        {
            foreach (Border cell in cells)
            {
                cell.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0xFF, 0xD7));
            }
            DebugLog.Write(LogChannel.Input, $"RelayGroupMatrix.SelectColumn: highlighted {cells.Count} cells for characterId={characterId}.");
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MemberForeground
    //
    // Returns the text foreground brush for a given membership state.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private static SolidColorBrush MemberForeground(bool isMember)
    {
        return isMember
            ? new SolidColorBrush(Color.FromRgb(0x2A, 0xFF, 0xD7))
            : new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MemberBorderBrush
    //
    // Returns the border brush for a given membership state.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private static SolidColorBrush MemberBorderBrush(bool isMember)
    {
        return isMember
            ? new SolidColorBrush(Color.FromRgb(0x2A, 0xFF, 0xD7))
            : new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
    }
}