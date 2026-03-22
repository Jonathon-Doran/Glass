using Glass.Core;
using System.Windows;
using System.Windows.Controls;

namespace Glass.Controls;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyDisplayControl
//
// A custom control that renders a single key cell in the keyboard layout.
// Visual appearance is driven by VisualStateManager states defined in Generic.xaml.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
[TemplatePart(Name = PartBorder, Type = typeof(System.Windows.Controls.Border))]
public class KeyDisplayControl : Control
{
    private const string PartBorder = "PART_Border";

    private System.Windows.Controls.Border? _border;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Static constructor
    //
    // Overrides the default style key so WPF looks for this control's template
    // in Generic.xaml.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    static KeyDisplayControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(KeyDisplayControl),
            new FrameworkPropertyMetadata(typeof(KeyDisplayControl)));
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnApplyTemplate
    //
    // Finds named template parts after the template is applied.
    // Detaches handlers from the previous border before wiring up the new one.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public override void OnApplyTemplate()
    {
        DebugLog.Write($"KeyDisplayControl.OnApplyTemplate: keyName='{KeyName}'.");

        base.OnApplyTemplate();

        if (_border != null)
        {
            _border.MouseLeftButtonDown -= Border_MouseLeftButtonDown;
            _border.MouseLeftButtonUp -= Border_MouseLeftButtonUp;
            DebugLog.Write($"KeyDisplayControl.OnApplyTemplate: detached handlers from previous border.");
        }

        _border = GetTemplateChild(PartBorder) as System.Windows.Controls.Border;

        if (_border != null)
        {
            _border.MouseLeftButtonDown += Border_MouseLeftButtonDown;
            _border.MouseLeftButtonUp += Border_MouseLeftButtonUp;
            DebugLog.Write($"KeyDisplayControl.OnApplyTemplate: attached handlers to new border.");
        }

        DebugLog.Write($"KeyDisplayControl.OnApplyTemplate: border={_border != null}.");

        UpdateVisualState();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Border_MouseLeftButtonDown
    //
    // Fires when the mouse button is pressed on the key cell.
    // Updates IsPressed and raises KeyPressed or KeyToggled as appropriate.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Border_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DebugLog.Write($"KeyDisplayControl.Border_MouseLeftButtonDown: keyName='{KeyName}'.");

        if (KeyType == KeyType.Toggle)
        {
            IsPressed = !IsPressed;
            DebugLog.Write($"KeyDisplayControl.Border_MouseLeftButtonDown: toggle keyName='{KeyName}' isPressed={IsPressed}.");
            RaiseEvent(new LayoutEventArgs(KeyboardLayoutControl.KeyPressedEvent, KeyName, IsPressed));
        }
        else
        {
            IsPressed = true;
            RaiseEvent(new LayoutEventArgs(KeyboardLayoutControl.KeyPressedEvent, KeyName, IsPressed));
        }

        e.Handled = true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Border_MouseLeftButtonUp
    //
    // Fires when the mouse button is released.
    // Fires regardless of cursor position.
    // Only meaningful for momentary keys.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Border_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DebugLog.Write($"KeyDisplayControl.Border_MouseLeftButtonUp: keyName='{KeyName}'.");

        if (KeyType == KeyType.Momentary)
        {
            IsPressed = false;
            RaiseEvent(new LayoutEventArgs(KeyboardLayoutControl.KeyReleasedEvent, KeyName, IsPressed));
        }

        e.Handled = true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // KeyName dependency property
    //
    // The physical key identifier, e.g. "G1", "X-14".
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static readonly DependencyProperty KeyNameProperty =
        DependencyProperty.Register(
            nameof(KeyName),
            typeof(string),
            typeof(KeyDisplayControl),
            new FrameworkPropertyMetadata(string.Empty));

    public string KeyName
    {
        get => (string)GetValue(KeyNameProperty);
        set => SetValue(KeyNameProperty, value);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // KeyType dependency property
    //
    // Whether this key behaves as a momentary press or a toggle.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static readonly DependencyProperty KeyTypeProperty =
        DependencyProperty.Register(
            nameof(KeyType),
            typeof(KeyType),
            typeof(KeyDisplayControl),
            new FrameworkPropertyMetadata(KeyType.Momentary));

    public KeyType KeyType
    {
        get => (KeyType)GetValue(KeyTypeProperty);
        set => SetValue(KeyTypeProperty, value);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Label dependency property
    //
    // The command label assigned to this key. Empty if unbound.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(
            nameof(Label),
            typeof(string),
            typeof(KeyDisplayControl),
            new FrameworkPropertyMetadata(string.Empty));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ShowLabel dependency property
    //
    // When true, the Label is displayed in the key cell.
    // When false, the KeyName is displayed.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static readonly DependencyProperty ShowLabelProperty =
        DependencyProperty.Register(
            nameof(ShowLabel),
            typeof(bool),
            typeof(KeyDisplayControl),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public bool ShowLabel
    {
        get => (bool)GetValue(ShowLabelProperty);
        set => SetValue(ShowLabelProperty, value);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UpdateVisualState
    //
    // Transitions the control to the correct VSM states based on current property values.
    // Called whenever IsSelected or IsPressed changes.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void UpdateVisualState()
    {
        DebugLog.Write($"KeyDisplayControl.UpdateVisualState: keyName='{KeyName}' isSelected={IsSelected} isPressed={IsPressed}.");

        if (IsPressed)
        {
            VisualStateManager.GoToState(this, "Pressed", true);
        }
        else if (IsSelected)
        {
            VisualStateManager.GoToState(this, "Selected", true);
        }
        else
        {
            VisualStateManager.GoToState(this, "Normal", true);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // IsSelected dependency property
    //
    // Whether this key is currently selected in the editor.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(
            nameof(IsSelected),
            typeof(bool),
            typeof(KeyDisplayControl),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnIsSelectedChanged));

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (KeyDisplayControl)d;
        DebugLog.Write($"KeyDisplayControl.OnIsSelectedChanged: keyName='{control.KeyName}' isSelected='{e.NewValue}'.");
        control.UpdateVisualState();
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // IsPressed dependency property
    //
    // For momentary keys: whether the key is currently held down.
    // For toggle keys: whether the toggle is currently on.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static readonly DependencyProperty IsPressedProperty =
        DependencyProperty.Register(
            nameof(IsPressed),
            typeof(bool),
            typeof(KeyDisplayControl),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnIsPressedChanged));

    private static void OnIsPressedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (KeyDisplayControl)d;
        DebugLog.Write($"KeyDisplayControl.OnIsPressedChanged: keyName='{control.KeyName}' isPressed='{e.NewValue}'.");
        control.UpdateVisualState();
    }

    public bool IsPressed
    {
        get => (bool)GetValue(IsPressedProperty);
        set => SetValue(IsPressedProperty, value);
    }
}
