namespace Glass.Data.Models;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// WindowLayout
//
// A named arrangement of monitors and slots, independent of any profile.
// Multiple profiles may reference the same layout.
// MachineId identifies which machine this layout was created for.
// A null MachineId indicates no machine has been assigned.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class WindowLayout
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? MachineId { get; set; }
    public List<LayoutMonitorSettings> Monitors { get; set; } = new();
    public List<SlotPlacement> Slots { get; set; } = new();
}