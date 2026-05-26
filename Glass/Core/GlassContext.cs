using Glass.Core.Logging;
using Glass.Core.Memory;
using Glass.Core.Signals;
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
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Packet source claim
    //
    // The application allows at most one packet source (live capture or pcap reader) to feed the
    // protocol pipeline at a time.  Concurrent sources would cause two threads to deliver packets
    // into shared SoeStream instances, which are single-threaded by design.  Packet sources claim
    // this flag before starting and release it when done.
    //
    // 0 = no source active, 1 = a source is active.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private static int _packetSourceActive;

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
    public static PacketBus PacketBus { get; set; } = null!;      // remove soon
    public static SignalBus SignalBus { get; set; } = null !;
    public static BufferPool BufferPool { get; set; } = null!;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // TryAcquirePacketSource
    //
    // Atomically claims the packet source slot.  Returns true if the caller now owns the slot
    // and must call ReleasePacketSource when finished; returns false if another source is already
    // active and the caller must not proceed.
    //
    // Callers should wrap their work in try/finally so the release runs even if the work throws.
    //
    // Returns:  True if the claim succeeded, false if another source already holds it.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static bool TryAcquirePacketSource()
    {
        int prior = Interlocked.CompareExchange(ref _packetSourceActive, 1, 0);
        if (prior == 0)
        {
            DebugLog.Write(LogChannel.LowNetwork, "GlassContext.TryAcquirePacketSource: claim granted");
            return true;
        }

        DebugLog.Write(LogChannel.LowNetwork, "GlassContext.TryAcquirePacketSource: claim rejected, another source is active");
        return false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ReleasePacketSource
    //
    // Releases the packet source claim previously acquired via TryAcquirePacketSource.  Callers
    // must invoke this when their packet source has finished, typically in a finally block so
    // the release runs even if the source work threw.
    //
    // Calling this without holding the claim is a programming error and is logged.  The flag is
    // still cleared so the system can recover, but the log entry indicates a bug in the caller.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static void ReleasePacketSource()
    {
        int prior = Interlocked.Exchange(ref _packetSourceActive, 0);
        if (prior == 0)
        {
            DebugLog.Write(LogChannel.LowNetwork, "GlassContext.ReleasePacketSource: release called but no claim was held (caller bug)");
            return;
        }

        DebugLog.Write(LogChannel.LowNetwork, "GlassContext.ReleasePacketSource: claim released");
    }
}