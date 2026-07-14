using Glass.Controls;
using Glass.Core;
using Glass.Core.Logging;
using Glass.Input;
using System.Windows;

namespace Glass;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyTestDialog
//
// Temporary test dialog for verifying KeyDisplayControl rendering.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public partial class KeyTestDialog : Window
{
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // KeyTestDialog
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public KeyTestDialog()
    {
        InitializeComponent();

        KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                DebugLog.Write(LogChannel.Input, "KeyTestDialog: ESC pressed, closing.");
                Close();
            }
        };

        Loaded += KeyTestDialog_Loaded;
        GlassContext.KeyboardManager.KeyEvent += OnHidKeyEvent;
        Closed += (s, e) =>
        {
            GlassContext.KeyboardManager.KeyEvent -= OnHidKeyEvent;
            DebugLog.Write(LogChannel.Input, "KeyTestDialog: unsubscribed from KeyEvent.", LogLevel.Trace);
        };
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // KeyTestDialog_Loaded
    //
    // Pushes test data to the control after the window is fully loaded.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void KeyTestDialog_Loaded(object sender, RoutedEventArgs e)
    {
        DebugLog.Write(LogChannel.Input, "KeyTestDialog_Loaded: pushing test data.");

        TestControl.ShowLabel = true;
        TestControl.Keys = new Dictionary<string, KeyDisplay>
        {
            { "G1", new KeyDisplay { KeyName = "G1", Label = "Nuke" } },
            { "G2", new KeyDisplay { KeyName = "G2", Label = "DoT", IsSelected = true } },
            { "G3", new KeyDisplay { KeyName = "G3", Label = "Slow", KeyType = KeyType.Toggle, IsPressed = true } },
            { "G4", new KeyDisplay { KeyName = "G4", Label = "Assist" } },
        };
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Key_Pressed
    //
    // Fires when a key cell is pressed.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Key_Pressed(object sender, LayoutEventArgs e)
    {
        DebugLog.Write(LogChannel.Input, $"KeyTestDialog.Key_Pressed: keyName='{e.KeyName}' isPressed={e.IsPressed}.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Key_Released
    //
    // Fires when a key cell is released.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Key_Released(object sender, LayoutEventArgs e)
    {
        DebugLog.Write(LogChannel.Input, $"KeyTestDialog.Key_Released: keyName='{e.KeyName}' isPressed={e.IsPressed}.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Window_MouseLeftButtonDown
    //
    // Allows the window to be dragged by clicking anywhere on it.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DragMove();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnHidKeyEvent
    //
    // Receives key state changes from KeyboardManager and updates the matching
    // key cell.  Fires on the HID dispatcher thread, so the cell update is
    // marshaled to the UI thread.
    //
    // sender:  The KeyboardManager raising the event
    // e:       The key state change
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void OnHidKeyEvent(object? sender, HidKeyEventArgs e)
    {
        DebugLog.Write(LogChannel.Input, $"KeyTestDialog.OnHidKeyEvent: key='{e.KeyName}' isPressed={e.IsPressed}.", LogLevel.Trace);

        Dispatcher.Invoke(() =>
        {
            KeyDisplay keyDisplay = new KeyDisplay
            {
                KeyName = e.KeyName,
                Label = e.KeyName,
                IsPressed = e.IsPressed
            };

            TestControl.UpdateKey(keyDisplay);
        });
    }
}