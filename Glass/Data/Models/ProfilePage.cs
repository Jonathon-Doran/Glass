namespace Glass.Data.Models;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ProfilePage
//
// Represents the association between a profile and a key page.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class ProfilePage
{
    public int Id { get; set; }
    public int CharacterSetId { get; set; }
    public int KeyPageId { get; set; }
    public bool IsStartPage { get; set; }

    // Denormalized for display convenience
    public string PageName { get; set; } = string.Empty;
    public KeyboardType Device { get; set; }
}
