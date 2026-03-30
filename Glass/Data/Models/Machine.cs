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