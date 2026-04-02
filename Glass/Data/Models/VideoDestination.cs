namespace Glass.Data.Models;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// VideoDestination
//
// Represents a named destination region within a slot, paired with a VideoSource
// of the same name and UI skin. Coordinates are slot-relative in absolute pixels.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class VideoDestination
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int UISkinId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
