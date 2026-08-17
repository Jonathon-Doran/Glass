using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.World;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Inference.Dialogs;

///////////////////////////////////////////////////////////////////////////////////////////////
// SpellBrowser
//
// Modeless read-only browser over the loaded spell catalog.  The filter bar builds a
// SpellFilter and runs it through SpellCatalog.FindSpells; results fill the grid, and
// selecting a row renders the full record in the detail pane.  The window is a viewer
// only: it never mutates the catalog.
///////////////////////////////////////////////////////////////////////////////////////////////
public partial class SpellBrowser : Window
{
    private const string AnyItemLabel = "(any)";

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SpellBrowser  (constructor)
    //
    // Populates the SPA, target type, and class filter combos from their enums, with an
    // "(any)" entry first and selected.  Enum entries carry their value in the item Tag;
    // the "(any)" entry carries a null Tag.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SpellBrowser()
    {
        InitializeComponent();

        PopulateEnumCombo(FilterSpa, typeof(SPAId));
        PopulateEnumCombo(FilterTargetType, typeof(SpellTargetType));
        PopulateClassCombo();
        PopulateCategoryCombo();
        PopulateSubcategoryCombo();

        DebugLog.Write(LogChannel.InferenceDebug, "SpellBrowser: opened, catalog holds "
            + SpellCatalog.Instance.Count + " spells", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PopulateEnumCombo
    //
    // Fills a combo with the names of an enum's members, sorted alphabetically, preceded
    // by an "(any)" entry that is selected initially.  Each enum item's Tag holds the
    // boxed enum value; the "(any)" item's Tag is null.
    //
    // combo:     The combo box to fill.
    // enumType:  The enum whose members populate it.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void PopulateEnumCombo(ComboBox combo, Type enumType)
    {
        List<string> names = new List<string>(Enum.GetNames(enumType));
        names.Sort(StringComparer.OrdinalIgnoreCase);

        ComboBoxItem anyItem = new ComboBoxItem();
        anyItem.Content = AnyItemLabel;
        anyItem.Tag = null;
        combo.Items.Add(anyItem);

        foreach (string name in names)
        {
            ComboBoxItem item = new ComboBoxItem();
            item.Content = name;
            item.Tag = Enum.Parse(enumType, name);
            combo.Items.Add(item);
        }

        combo.SelectedIndex = 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PopulateClassCombo
    //
    // Fills the class filter combo with every EQClass in enum order, using the class
    // display names, preceded by an "(any)" entry that is selected initially.  Each class
    // item's Tag holds the boxed EQClass value; the "(any)" item's Tag is null.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void PopulateClassCombo()
    {
        ComboBoxItem anyItem = new ComboBoxItem();
        anyItem.Content = AnyItemLabel;
        anyItem.Tag = null;
        FilterClass.Items.Add(anyItem);

        foreach (EQClass eqClass in Enum.GetValues<EQClass>())
        {
            ComboBoxItem item = new ComboBoxItem();
            item.Content = eqClass.ToDisplayString();
            item.Tag = eqClass;
            FilterClass.Items.Add(item);
        }

        FilterClass.SelectedIndex = 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FilterName_KeyDown
    //
    // Runs the search when Enter is pressed in the name filter box.
    //
    // sender:  The name filter text box.
    // e:       The key event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void FilterName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RunSearch();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_Search_Click
    //
    // Runs the search with the current filter settings.
    //
    // sender:  The search button.
    // e:       The routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_Search_Click(object sender, RoutedEventArgs e)
    {
        RunSearch();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_Clear_Click
    //
    // Resets every filter control to its unconstrained state and empties the result grid,
    // result count, and detail pane.
    //
    // sender:  The clear button.
    // e:       The routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_Clear_Click(object sender, RoutedEventArgs e)
    {
        FilterName.Text = string.Empty;
        FilterSpa.SelectedIndex = 0;
        FilterTargetType.SelectedIndex = 0;
        FilterClass.SelectedIndex = 0;
        FilterMaxLevel.Text = string.Empty;
        FilterCategory.SelectedIndex = 0;
        FilterSubcategory.SelectedIndex = 0;
        ResultGrid.ItemsSource = null;
        ResultCount.Text = "No search yet";
        DetailText.Text = string.Empty;

        DebugLog.Write(LogChannel.InferenceDebug, "SpellBrowser: filters cleared",
            LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ResultGrid_SelectionChanged
    //
    // Renders the selected row's full record into the detail pane; clears the pane when
    // the selection empties.
    //
    // sender:  The result grid.
    // e:       The selection changed event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ResultGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SpellBrowserRow? row = ResultGrid.SelectedItem as SpellBrowserRow;

        if (row == null)
        {
            DetailText.Text = string.Empty;
            return;
        }

        DetailText.Text = BuildDetail(row.Record);

        DebugLog.Write(LogChannel.InferenceDebug, "SpellBrowser: detail shown for spell "
            + row.Id + " '" + row.Name + "'", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RunSearch
    //
    // Builds a SpellFilter from the filter controls, runs it against the catalog, and
    // fills the result grid sorted by spell name.  Unparseable max level text is
    // reported in the result count line and aborts the search.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void RunSearch()
    {
        SpellFilter filter = new SpellFilter();

        if (FilterName.Text.Length > 0)
        {
            filter.NameContains = FilterName.Text;
        }

        ComboBoxItem? spaItem = FilterSpa.SelectedItem as ComboBoxItem;
        if (spaItem != null && spaItem.Tag != null)
        {
            filter.Spa = (SPAId)spaItem.Tag;
        }

        ComboBoxItem? targetItem = FilterTargetType.SelectedItem as ComboBoxItem;
        if (targetItem != null && targetItem.Tag != null)
        {
            filter.TargetType = (SpellTargetType)targetItem.Tag;
        }

        ComboBoxItem? classItem = FilterClass.SelectedItem as ComboBoxItem;
        if (classItem != null && classItem.Tag != null)
        {
            filter.CastableClass = (EQClass)classItem.Tag;
        }

        if (FilterMaxLevel.Text.Length > 0)
        {
            byte maximumLevel = 0;
            if (byte.TryParse(FilterMaxLevel.Text, out maximumLevel) == false)
            {
                ResultCount.Text = "Max level is not a number: " + FilterMaxLevel.Text;
                DebugLog.Write(LogChannel.InferenceDebug,
                    "SpellBrowser.RunSearch: unparseable max level '" + FilterMaxLevel.Text
                    + "', search aborted", LogLevel.Warn);
                return;
            }

            filter.MaximumLevel = maximumLevel;
        }

        ComboBoxItem? categoryItem = FilterCategory.SelectedItem as ComboBoxItem;
        if (categoryItem != null && categoryItem.Tag != null)
        {
            filter.Category = (SpellCategoryId)categoryItem.Tag;
        }

        ComboBoxItem? subcategoryItem = FilterSubcategory.SelectedItem as ComboBoxItem;
        if (subcategoryItem != null && subcategoryItem.Tag != null)
        {
            filter.Subcategory = (SpellCategoryId)subcategoryItem.Tag;
        }

        List<SpellRecord> matches = SpellCatalog.Instance.FindSpells(filter);

        List<SpellBrowserRow> rows = new List<SpellBrowserRow>(matches.Count);
        foreach (SpellRecord record in matches)
        {
            byte? level = null;
            if (filter.CastableClass != null)
            {
                level = record.ClassLevels[(uint)filter.CastableClass.Value - 1];
            }
            rows.Add(new SpellBrowserRow(record, BuildEffectSummary(record), level));
        }

        rows.Sort(CompareRowsByName);

        ResultGrid.ItemsSource = rows;
        ResultCount.Text = rows.Count + " spell(s) matched";
        DetailText.Text = string.Empty;

        DebugLog.Write(LogChannel.InferenceDebug, "SpellBrowser.RunSearch: "
            + rows.Count + " spell(s) matched", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CompareRowsByName
    //
    // Orders two result rows by spell name, case-insensitive, with spell ID as the
    // tiebreaker so equal names order deterministically.
    //
    // left:     The first row.
    // right:    The second row.
    //
    // Returns:  Negative, zero, or positive per standard comparison semantics.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static int CompareRowsByName(SpellBrowserRow left, SpellBrowserRow right)
    {
        int nameOrder = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        if (nameOrder != 0)
        {
            return nameOrder;
        }

        return left.Id.CompareTo(right.Id);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PopulateCategoryCombo
    //
    // Fills the category filter combo with every loaded category name, sorted
    // alphabetically, preceded by an "(any)" entry that is selected initially.  Each
    // category item's Tag holds the boxed SpellCategoryId; the "(any)" item's Tag is
    // null.  When no names were loaded the combo holds only the "(any)" entry.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void PopulateCategoryCombo()
    {
        ComboBoxItem anyItem = new ComboBoxItem();
        anyItem.Content = AnyItemLabel;
        anyItem.Tag = null;
        FilterCategory.Items.Add(anyItem);

        List<KeyValuePair<SpellCategoryId, string>> entries =
            new List<KeyValuePair<SpellCategoryId, string>>(SpellCatalog.Instance.CategoryNames);
        entries.Sort(CompareCategoryEntriesByName);

        foreach (KeyValuePair<SpellCategoryId, string> entry in entries)
        {
            ComboBoxItem item = new ComboBoxItem();
            item.Content = entry.Value;
            item.Tag = entry.Key;
            FilterCategory.Items.Add(item);
        }

        FilterCategory.SelectedIndex = 0;
        DebugLog.Write(LogChannel.InferenceDebug, "SpellBrowser.PopulateCategoryCombo: "
            + entries.Count + " category name(s) loaded", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PopulateSubcategoryCombo
    //
    // Fills the subcategory filter combo with every loaded category name, sorted
    // alphabetically, preceded by an "(any)" entry that is selected initially.  The
    // category name table covers both levels, so the same entries apply here.  Each
    // item's Tag holds the boxed SpellCategoryId; the "(any)" item's Tag is null.  When
    // no names were loaded the combo holds only the "(any)" entry.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void PopulateSubcategoryCombo()
    {
        ComboBoxItem anyItem = new ComboBoxItem();
        anyItem.Content = AnyItemLabel;
        anyItem.Tag = null;
        FilterSubcategory.Items.Add(anyItem);

        List<KeyValuePair<SpellCategoryId, string>> entries =
            new List<KeyValuePair<SpellCategoryId, string>>(SpellCatalog.Instance.CategoryNames);
        entries.Sort(CompareCategoryEntriesByName);

        foreach (KeyValuePair<SpellCategoryId, string> entry in entries)
        {
            ComboBoxItem item = new ComboBoxItem();
            item.Content = entry.Value;
            item.Tag = entry.Key;
            FilterSubcategory.Items.Add(item);
        }

        FilterSubcategory.SelectedIndex = 0;
        DebugLog.Write(LogChannel.InferenceDebug, "SpellBrowser.PopulateSubcategoryCombo: "
            + entries.Count + " category name(s) loaded", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CompareCategoryEntriesByName
    //
    // Orders two category entries by name, case-insensitive, with category ID as the
    // tiebreaker so equal names order deterministically.
    //
    // left:     The first entry.
    // right:    The second entry.
    //
    // Returns:  Negative, zero, or positive per standard comparison semantics.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static int CompareCategoryEntriesByName(
        KeyValuePair<SpellCategoryId, string> left, KeyValuePair<SpellCategoryId, string> right)
    {
        int nameOrder = string.Compare(left.Value, right.Value, StringComparison.OrdinalIgnoreCase);
        if (nameOrder != 0)
        {
            return nameOrder;
        }
        return left.Key.Value.CompareTo(right.Key.Value);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BuildEffectSummary
    //
    // Formats the retained effect slots of a spell as a one-line summary for the grid:
    // each slot as "slot:SPA base1" with a "/max" suffix when a cap is present, slots
    // joined by semicolons.  A spell with no retained effects yields an empty string.
    //
    // record:   The spell whose effects are summarized.
    //
    // Returns:  The one-line summary.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static string BuildEffectSummary(SpellRecord record)
    {
        if (record.Effects.Length == 0)
        {
            return string.Empty;
        }

        List<string> parts = new List<string>(record.Effects.Length);
        foreach (SpellEffect effect in record.Effects)
        {
            string part = effect.Slot + ":" + effect.Spa + " " + effect.Base1;
            if (effect.Max != 0)
            {
                part += "/" + effect.Max;
            }

            parts.Add(part);
        }

        return string.Join("; ", parts);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BuildDetail
    //
    // Formats one spell's full record for the detail pane: identity, costs and times,
    // categories, targeting, castable classes with their levels, reagents when present,
    // and every retained effect slot with all of its fields, one item per line.
    //
    // record:   The spell to format.
    //
    // Returns:  The multi-line detail text.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static string BuildDetail(SpellRecord record)
    {
        List<string> lines = new List<string>();

        lines.Add(record.Name + "  (id " + record.Id + ")");
        lines.Add("Mana: " + record.Mana + "   Cast " + record.CastTimeMs + "ms   Recast "
            + record.RecastTimeMs + "ms   Range " + record.Range);
        lines.Add("Duration formula: " + record.DurationFormula + ", cap "
            + record.DurationCapTicks + " ticks");
        lines.Add("Categories: " + SpellCatalog.Instance.DescribeCategory(record.PrimaryCategory)
                    + " / " + SpellCatalog.Instance.DescribeCategory(record.SecondaryCategory)
                    + " / " + SpellCatalog.Instance.DescribeCategory(record.SecondaryCategory2));
        lines.Add("Target: " + record.TargetType + "   Restriction " + record.CastRestriction);

        List<string> classParts = new List<string>();
        foreach (EQClass eqClass in Enum.GetValues<EQClass>())
        {
            byte classLevel = record.ClassLevels[(uint)eqClass - 1];
            if (classLevel != SpellRecord.LevelUnusable)
            {
                classParts.Add(eqClass.ToDisplayString() + " " + classLevel);
            }
        }

        if (classParts.Count > 0)
        {
            lines.Add("Classes: " + string.Join(", ", classParts));
        }
        else
        {
            lines.Add("Classes: none");
        }

        for (uint reagentIndex = 0; reagentIndex < 4; reagentIndex++)
        {
            if (record.ReagentIds[reagentIndex] != -1)
            {
                lines.Add("Reagent: item " + record.ReagentIds[reagentIndex]
                    + " x" + record.ReagentCounts[reagentIndex]);
            }

            if (record.NoExpendReagentIds[reagentIndex] != -1)
            {
                lines.Add("Reagent (not expended): item "
                    + record.NoExpendReagentIds[reagentIndex]);
            }
        }

        if (record.Effects.Length == 0)
        {
            lines.Add("Effects: none retained");
        }
        else
        {
            foreach (SpellEffect effect in record.Effects)
            {
                lines.Add("Effect slot " + effect.Slot + ": " + effect.Spa
                    + "  base1 " + effect.Base1 + "  base2 " + effect.Base2
                    + "  calc " + effect.Calc + "  max " + effect.Max);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}

///////////////////////////////////////////////////////////////////////////////////////////////
// SpellBrowserRow
//
// One result row of the spell browser grid: the display projections of a SpellRecord,
// plus the record itself for the detail pane.  Rows are immutable snapshots built per
// search.
///////////////////////////////////////////////////////////////////////////////////////////////
public class SpellBrowserRow
{
    public uint Id { get; }
    public string Name { get; }
    public uint Mana { get; }
    public uint CastTimeMs { get; }
    public uint RecastTimeMs { get; }
    public string Categories { get; }
    public string TargetType { get; }
    public string CastRestriction { get; }
    public string EffectSummary { get; }
    public SpellRecord Record { get; }
    public byte? Level { get; }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SpellBrowserRow  (constructor)
    //
    // Builds the display projections from the given record.
    //
    // record:         The spell to present.
    // effectSummary:  Preformatted one-line effect summary for the grid column.
    // level:          The filtered class's level for the grid column, or null when no
    //                 class filter is active.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SpellBrowserRow(SpellRecord record, string effectSummary, byte? level)
    {
        Id = record.Id;
        Name = record.Name;
        Mana = record.Mana;
        CastTimeMs = record.CastTimeMs;
        RecastTimeMs = record.RecastTimeMs;
        Categories = record.PrimaryCategory + "/" + record.SecondaryCategory
            + "/" + record.SecondaryCategory2;
        TargetType = record.TargetType.ToString();
        CastRestriction = record.CastRestriction.ToString();
        EffectSummary = effectSummary;
        Record = record;
        Level = level;
    }
}