namespace Glass.Data.Models;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// SlotPlacement
//
// Describes the screen position and size of a numbered slot within a window layout.
// A slot belongs to a specific monitor and has a fixed rectangle on that display.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class SlotPlacement
{
    public int Id { get; set; }
    public int LayoutId { get; set; }
    public int MonitorId { get; set; }
    public int SlotNumber { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}