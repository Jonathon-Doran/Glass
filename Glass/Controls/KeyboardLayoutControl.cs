using Glass.Core;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Glass.Data.Models;

namespace Glass.Controls;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyboardLayoutControl
//
// A custom control that renders a physical keyboard layout for a given device type.
// Displays key labels or key names depending on ShowLabel.
// Fires events when keys are pressed, released, or toggled.
// The control has no knowledge of bindings, pages, or profiles.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

[TemplatePart(Name = PartG13Grid, Type = typeof(Grid))]
[TemplatePart(Name = PartG15Grid, Type = typeof(Grid))]
[TemplatePart(Name = PartX36Grid, Type = typeof(Grid))]
public class KeyboardLayoutControl : Control
{
    private const string PartG13Grid = "PART_G13Grid";
    private const string PartG15Grid = "PART_G15Grid";
    private const string PartX36Grid = "PART_X36Grid";

    private Grid? _g13Grid;
    private Grid? _g15Grid;
    private Grid? _x36Grid;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Static constructor
    //
    // Overrides the default style key so WPF looks for this control's template
    // in Generic.xaml.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    static KeyboardLayoutControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(KeyboardLayoutControl),
            new FrameworkPropertyMetadata(typeof(KeyboardLayoutControl)));
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnApplyTemplate
    //
    // Called by WPF after the control template is applied.
    // Finds named template parts and wires up initial state.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _g13Grid = GetTemplateChild(PartG13Grid) as Grid;
        _g15Grid = GetTemplateChild(PartG15Grid) as Grid;
        _x36Grid = GetTemplateChild(PartX36Grid) as Grid;

        RebuildGrid();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Device dependency property
    //
    // The device type to render. Determines the grid layout.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static readonly DependencyProperty DeviceProperty =
        DependencyProperty.Register(
            nameof(Device),
            typeof(KeyboardType),
            typeof(KeyboardLayoutControl),
            new FrameworkPropertyMetadata(
                KeyboardType.G15,
                FrameworkPropertyMetadataOptions.AffectsMeasure |
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnKeyboardTypeChanged));

    public KeyboardType Device
    {
        get => (KeyboardType)GetValue(DeviceProperty);
        set => SetValue(DeviceProperty, value);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnKeyboardTypeChanged
    //
    // Called when the KeyboardType property changes.
    // Triggers a rebuild of the key grid.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private static void OnKeyboardTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (KeyboardLayoutControl)d;
        control.RebuildGrid();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Keys dependency property
    //
    // The collection of key display states to render.
    // The caller populates this and updates it when state changes.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static readonly DependencyProperty KeysProperty =
        DependencyProperty.Register(
            nameof(Keys),
            typeof(Dictionary<string, KeyDisplay>),
            typeof(KeyboardLayoutControl),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnKeysChanged));

    public Dictionary<string, KeyDisplay> Keys
    {
        get => (Dictionary<string, KeyDisplay>)GetValue(KeysProperty);
        set => SetValue(KeysProperty, value);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PageName dependency property
    //
    // The name of the currently active page, displayed in the header.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static readonly DependencyProperty PageNameProperty =
        DependencyProperty.Register(
            nameof(PageName),
            typeof(string),
            typeof(KeyboardLayoutControl),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public string PageName
    {
        get => (string)GetValue(PageNameProperty);
        set => SetValue(PageNameProperty, value);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnKeysChanged
    //
    // Called when the Keys collection is replaced.
    // Triggers a refresh of all key cell appearances.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private static void OnKeysChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (KeyboardLayoutControl)d;
        control.UpdateChildShowLabel();
        control.RefreshKeys();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ShowLabel dependency property
    //
    // When true, key cells display the Label from KeyDisplay.
    // When false, key cells display the KeyName.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static readonly DependencyProperty ShowLabelProperty =
        DependencyProperty.Register(
            nameof(ShowLabel),
            typeof(bool),
            typeof(KeyboardLayoutControl),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnShowLabelChanged));

    public bool ShowLabel
    {
        get => (bool)GetValue(ShowLabelProperty);
        set => SetValue(ShowLabelProperty, value);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnShowLabelChanged
    //
    // Called when ShowLabel changes. Refreshes all key cell text.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private static void OnShowLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (KeyboardLayoutControl)d;
        control.UpdateChildShowLabel();
        control.RefreshKeys();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SelectionEnabled dependency property
    //
    // When false, key activation events are not fired.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static readonly DependencyProperty SelectionEnabledProperty =
        DependencyProperty.Register(
            nameof(SelectionEnabled),
            typeof(bool),
            typeof(KeyboardLayoutControl),
            new FrameworkPropertyMetadata(true));

    public bool SelectionEnabled
    {
        get => (bool)GetValue(SelectionEnabledProperty);
        set => SetValue(SelectionEnabledProperty, value);
    }
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // KeyPressed routed event
    //
    // Fired when a key cell receives a mouse down event.
    // Carries the key name and IsPressed=true.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static readonly RoutedEvent KeyPressedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(KeyPressed),
            RoutingStrategy.Bubble,
            typeof(LayoutEventHandler),
            typeof(KeyboardLayoutControl));

    public event LayoutEventHandler KeyPressed
    {
        add => AddHandler(KeyPressedEvent, value);
        remove => RemoveHandler(KeyPressedEvent, value);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // KeyReleased routed event
    //
    // Fired when the mouse button is released after a key press.
    // Fires regardless of cursor position at release time.
    // Carries the key name and IsPressed=false.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static readonly RoutedEvent KeyReleasedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(KeyReleased),
            RoutingStrategy.Bubble,
            typeof(LayoutEventHandler),
            typeof(KeyboardLayoutControl));

    public event LayoutEventHandler KeyReleased
    {
        add => AddHandler(KeyReleasedEvent, value);
        remove => RemoveHandler(KeyReleasedEvent, value);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UpdateKey
    //
    // Updates the display state of a single key without refreshing the entire grid.
    // Also updates the Keys dictionary so state is consistent.
    //
    // keyDisplay:  The new display state for the key
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void UpdateKey(KeyDisplay keyDisplay)
    {
        if (Keys == null)
        {
            DebugLog.Write("KeyboardLayoutControl.UpdateKey: Keys dictionary is null, ignoring.");
            return;
        }

        Keys[keyDisplay.KeyName] = keyDisplay;

        Grid? activeGrid = GetActiveGrid();
        if (activeGrid == null)
        {
            DebugLog.Write("KeyboardLayoutControl.UpdateKey: no active grid.");
            return;
        }

        KeyDisplayControl? cell = activeGrid.Children
            .OfType<KeyDisplayControl>()
            .FirstOrDefault(c => c.KeyName == keyDisplay.KeyName);

        if (cell == null)
        {
            DebugLog.Write($"KeyboardLayoutControl.UpdateKey: key='{keyDisplay.KeyName}' not found in grid.");
            return;
        }

        cell.Label = keyDisplay.Label;
        cell.KeyType = keyDisplay.KeyType;
        cell.IsSelected = keyDisplay.IsSelected;
        cell.IsPressed = keyDisplay.IsPressed;

        DebugLog.Write($"KeyboardLayoutControl.UpdateKey: key='{keyDisplay.KeyName}' updated.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ClearKey
    //
    // Clears the display state of a single key, removing it from the Keys dictionary
    // and resetting the corresponding KeyDisplayControl to its default appearance.
    //
    // keyName:  The key to clear
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void ClearKey(string keyName)
    {
        DebugLog.Write($"KeyboardLayoutControl.ClearKey: key='{keyName}'.");

        if (Keys != null)
        {
            Keys.Remove(keyName);
        }

        Grid? activeGrid = GetActiveGrid();
        if (activeGrid == null)
        {
            return;
        }

        KeyDisplayControl? cell = activeGrid.Children
            .OfType<KeyDisplayControl>()
            .FirstOrDefault(c => c.KeyName == keyName);

        if (cell == null)
        {
            return;
        }

        cell.Label = "-";
        cell.KeyType = KeyType.Momentary;
        cell.IsSelected = false;
        cell.IsPressed = false;

        DebugLog.Write($"KeyboardLayoutControl.ClearKey: key='{keyName}' cleared.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RebuildGrid
    //
    // Shows the correct keyboard grid based on the current KeyboardType.
    // Safe to call before OnApplyTemplate — null checks guard against missing parts.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void RebuildGrid()
    {
        if ((_g13Grid == null) && (_g15Grid == null) && (_x36Grid == null))
        {
            DebugLog.Write("KeyboardLayoutControl.RebuildGrid: template not yet applied, deferring.");
            return;
        }

        if (_g13Grid != null)
        {
            _g13Grid.Visibility = (Device == KeyboardType.G13) ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_g15Grid != null)
        {
            _g15Grid.Visibility = (Device == KeyboardType.G15) ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_x36Grid != null)
        {
            _x36Grid.Visibility = (Device == KeyboardType.DominatorX36) ? Visibility.Visible : Visibility.Collapsed;
        }

        RefreshKeys();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UpdateChildShowLabel
    //
    // Pushes the current ShowLabel value to all KeyDisplayControl children in the active grid.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void UpdateChildShowLabel()
    {
        Grid? activeGrid = GetActiveGrid();

        if (activeGrid == null)
        {
            DebugLog.Write("KeyboardLayoutControl.UpdateChildShowLabel: no active grid.");
            return;
        }

        foreach (var child in activeGrid.Children.OfType<KeyDisplayControl>())
        {
            child.ShowLabel = ShowLabel;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RefreshKeys
    //
    // Walks the active grid and updates each KeyDisplayControl from the Keys dictionary.
    // Keys not present in the dictionary are shown with empty label and default state.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void RefreshKeys()
    {
        Grid? activeGrid = GetActiveGrid();

        if (activeGrid == null)
        {
            DebugLog.Write("KeyboardLayoutControl.RefreshKeys: no active grid, returning.");
            return;
        }

        foreach (var child in activeGrid.Children.OfType<KeyDisplayControl>())
        {
            string keyName = child.KeyName;

            if ((Keys != null) && Keys.TryGetValue(keyName, out var keyDisplay))
            {
                child.Label = keyDisplay.Label;
                child.KeyType = keyDisplay.KeyType;
                child.IsSelected = keyDisplay.IsSelected;
                child.IsPressed = keyDisplay.IsPressed;
            }
            else
            {
                child.Label = "-";
                child.KeyType = KeyType.Momentary;
                child.IsSelected = false;
                child.IsPressed = false;
            }
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetActiveGrid
    //
    // Returns the currently visible grid based on KeyboardType, or null if none is active.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private Grid? GetActiveGrid()
    {
        return Device switch
        {
            KeyboardType.G13 => _g13Grid,
            KeyboardType.G15 => _g15Grid,
            KeyboardType.DominatorX36 => _x36Grid,
            _ => null
        };
    }
}
