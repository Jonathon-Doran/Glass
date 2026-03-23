using Glass.Data.Models;

namespace Glass.UI.ViewModels;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyPageViewModel
//
// View model for a key page entry in the Keyboard Layout tab page list.
// Tracks whether the page is associated with the current profile and whether
// it is the start page.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class KeyPageViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public KeyboardType Device { get; set; }
    public bool InProfile { get; set; }
    public bool IsStartPage { get; set; }
}