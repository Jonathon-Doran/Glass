using Glass.Data.Models;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;

namespace Glass.Core;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// GlassContext
//
// Provides global access to the major singleton components of the Glass application.
// Initialized once during MainWindow startup before any component is started.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public static class GlassContext
{
    public static PipeManager ISXGlassPipe { get; set; } = null!;
    public static PipeManager GlassVideoPipe { get; set; } = null!;
    public static SessionRegistry SessionRegistry { get; set; } = null!;
    public static KeyboardManager KeyboardManager { get; set; } = null!;
    public static FocusTracker FocusTracker { get; set; } = null!;
    public static ProfileManager ProfileManager { get; set; } = null!;
    public static FieldExtractor FieldExtractor { get; set; } = null!;
    public static Machine? CurrentMachine { get; set; }
    public static PatchLevel CurrentPatchLevel { get; set; }
    public static PatchRegistry PatchRegistry { get; set; } = null!;
    public static AppPacketBus AppPacketBus { get; set; } = null!;
}