namespace Glass.Data.Models;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// MonitorOrientation
//
// The physical orientation of a monitor.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public enum MonitorOrientation
{
    Landscape,
    Portrait
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Monitor
//
// Represents a physical monitor attached to a machine.
// AdapterName is the Windows adapter identifier e.g. \\.\DISPLAY1.
// PnpId is the hardware manufacturer and model code e.g. SAM1015.
// Serial is the EDID serial string e.g. HCJWA09337, or empty if not available.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class Monitor
{
    public int Id { get; set; }
    public int MachineId { get; set; }
    public string AdapterName { get; set; } = string.Empty;
    public string PnpId { get; set; } = string.Empty;
    public string Serial { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public MonitorOrientation Orientation { get; set; } = MonitorOrientation.Landscape;
}