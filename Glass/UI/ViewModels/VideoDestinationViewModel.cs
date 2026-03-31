using Glass.Data.Models;

namespace Glass.UI.ViewModels;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// VideoDestinationViewModel
//
// View model for displaying a VideoDestination with its source name resolved.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class VideoDestinationViewModel
{
    public VideoDestination Destination { get; set; } = null!;
    public string SourceName { get; set; } = string.Empty;
    public int X => Destination.X;
    public int Y => Destination.Y;
    public int Width => Destination.Width;
    public int Height => Destination.Height;
}
