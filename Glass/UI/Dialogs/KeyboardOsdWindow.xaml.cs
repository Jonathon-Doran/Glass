using Glass.Controls;
using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace Glass;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyboardOsdWindow
//
// A standalone always-on-top borderless window showing the current page
// bindings for one keyboard device type.
// Created by KeyboardManager on profile load, shown/hidden on trigger.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public partial class KeyboardOsdWindow : Window
{
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // KeyboardOsdWindow
    //
    // keyboardType:  The keyboard type — determines the grid layout
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public KeyboardOsdWindow(KeyboardType keyboardType)
    {
        InitializeComponent();
        KeyLayoutControl.Device = keyboardType;
        DebugLog.Write(LogChannel.Input, $"KeyboardOsdWindow: created for {keyboardType}.");

        KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Hide();
            }
        };
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetPage
    //
    // Updates the OSD to display the given page name and key bindings.
    //
    // pageName:  The name of the active page
    // keys:      The key display data for the page
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void SetPage(string pageName, Dictionary<string, KeyDisplay> keys)
    {
        DebugLog.Write(LogChannel.Input, $"KeyboardOsdWindow.SetPage: page='{pageName}'.");

        Dispatcher.Invoke(() =>
        {
            KeyLayoutControl.PageName = pageName;
            KeyLayoutControl.Keys = keys;
        });
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UpdateKey
    //
    // Updates the display state of a single key in the OSD.
    //
    // keyDisplay:  The new display state for the key
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void UpdateKey(KeyDisplay keyDisplay)
    {
        DebugLog.Write(LogChannel.Input, $"KeyboardOsdWindow.UpdateKey: key='{keyDisplay.KeyName}'.");
        Dispatcher.Invoke(() =>
        {
            KeyLayoutControl.UpdateKey(keyDisplay);
        });
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Window_MouseLeftButtonDown
    //
    // Allows the window to be dragged by clicking anywhere on it.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetKeyDown
    //
    // Sets or clears the momentary pressed visual for a single key.  Safe to
    // call from any thread — the update is queued onto the window's
    // dispatcher without blocking the caller.
    //
    // keyName:  The physical key name e.g. "G1", "X-14"
    // isDown:   True while the physical key is held
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void SetKeyDown(string keyName, bool isDown)
    {
        Dispatcher.BeginInvoke(() =>
        {
            KeyLayoutControl.SetKeyDown(keyName, isDown);
        });
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ToggleVisibility
    //
    // Shows the window if hidden, hides it if visible.  Safe to call from any
    // thread — the work is marshaled onto the window's dispatcher.
    //
    // Returns:  True if the window is visible after the toggle.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool ToggleVisibility()
    {
        return Dispatcher.Invoke(() =>
        {
            if (IsVisible)
            {
                Hide();
                DebugLog.Write(LogChannel.Input, "KeyboardOsdWindow.ToggleVisibility: hidden.", LogLevel.Trace);
                return false;
            }

            Show();
            DebugLog.Write(LogChannel.Input, "KeyboardOsdWindow.ToggleVisibility: shown.", LogLevel.Trace);
            return true;
        });
    }
}