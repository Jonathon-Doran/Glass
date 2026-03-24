namespace Glass.Data.Models;

public class Machine
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;  // system hostname

    public List<Monitor> Monitors { get; set; } = new();
    public List<MachineDevice> Devices { get; set; } = new();
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// MachineDevice
//
// Represents a monitor connected to a machine.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class Monitor
{
    public int Id { get; set; }
    public int MachineId { get; set; }
    public string DisplayName { get; set; } = string.Empty;  // e.g. \\.\DISPLAY2
    public int Width { get; set; }
    public int Height { get; set; }
    public MonitorOrientation Orientation { get; set; } = MonitorOrientation.Landscape;
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// MachineDevice
//
// Represents a HID keyboard device connected to a machine.
// InstanceCount is the number of physical devices of this type connected.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class MachineDevice
{
    public int Id { get; set; }
    public int MachineId { get; set; }
    public KeyboardType KeyboardType { get; set; }
    public int InstanceCount { get; set; } = 1;
}