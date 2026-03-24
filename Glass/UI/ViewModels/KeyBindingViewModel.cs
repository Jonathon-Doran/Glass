using Glass.Data.Models;

namespace Glass.UI.ViewModels;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyBindingViewModel
//
// View model for a key binding entry in the Keyboard Layout tab binding list.
// Wraps a KeyBinding and provides a formatted summary for the binding list,
// and the command short name for display in the keyboard layout control.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class KeyBindingViewModel
{
    public KeyBinding Binding { get; set; } = new();
    public string CommandTargetText { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
}