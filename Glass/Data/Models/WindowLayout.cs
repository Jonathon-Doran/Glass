using Glass.Core;
using Glass.Data.Repositories;

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
    public int? UISkinId { get; set; }
    public List<SlotPlacement> Slots { get; set; } = new();
    public string DisplayName => GetDisplayName();

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetDisplayName
    //
    // Returns a formatted display name for this layout, including slot count.
    // If the layout belongs to a different machine than the current machine,
    // the layout's machine name is prepended in brackets.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public string GetDisplayName()
    {
        int slotCount = Slots.Count;
        string slotLabel = $"({slotCount} slot{(slotCount == 1 ? "" : "s")})";

        if (MachineId == null)
        {
            return $"{Name} {slotLabel}";
        }

        if (GlassContext.CurrentMachine != null && MachineId == GlassContext.CurrentMachine.Id)
        {
            return $"{Name} {slotLabel}";
        }

        MachineRepository machineRepo = new MachineRepository();
        Machine? machine = machineRepo.GetById(MachineId.Value);
        string machineTag = machine != null ? $"[{machine.Name}] " : $"[machine {MachineId}] ";

        return $"{machineTag}{Name} {slotLabel}";
    }


}