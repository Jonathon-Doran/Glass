namespace Glass.Data.Models;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// LayoutMonitorSettings
//
// Describes how a physical monitor participates in a window layout.
// Captures the monitor's position within the layout and the slot width
// used for game windows on that monitor.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class LayoutMonitorSettings
{
    public int Id { get; set; }
    public int LayoutId { get; set; }
    public int MonitorId { get; set; }
    public int LayoutPosition { get; set; }
    public int SlotWidth { get; set; }
}