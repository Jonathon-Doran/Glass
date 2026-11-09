namespace Glass.Data.Models;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ProfileSummary
//
// The identifying details of one profile shown in a profile list: its name, description, server,
// and the number of characters assigned to it.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class ProfileSummary
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Server { get; init; } = string.Empty;
    public uint CharacterCount { get; init; }
}