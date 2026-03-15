namespace Glass.Data.Models;

// Describes the screen position of a slot within a window layout.
public class SlotPlacement
{
    public int SlotNumber { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}