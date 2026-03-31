namespace Glass.Data.Models;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Profile
//
// A named, portable group of characters with a window layout and keyboard configuration.
// Not tied to any machine — machine association is stored separately.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class Profile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? MachineId { get; set; }
    public int? LayoutId { get; set; }
    public int? StartPageId { get; set; }
    public List<SlotAssignment> Slots { get; set; } = new();
    public int? UISkinId { get; set; }
}