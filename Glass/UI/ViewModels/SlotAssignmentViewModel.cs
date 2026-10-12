namespace Glass.UI.ViewModels;

public class SlotAssignmentViewModel
{
    public int SlotNumber { get; set; }
    public DatabaseId DatabaseId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public int AccountId { get; set; }
}