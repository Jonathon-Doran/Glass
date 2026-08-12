using Glass.Core.Logging;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;

namespace Glass.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// ProtocolStackBootstrap
//
// Common setup and teardown for the protocol stack and its dependencies.
// Called from each application's MainWindow constructor and from each
// application's all-sessions-disconnected handler.  Centralizes the order
// of construction and disposal so both applications behave identically.
///////////////////////////////////////////////////////////////////////////////////////////////
public static class ProtocolStackBootstrap
{
    ///////////////////////////////////////////////////////////////////////////////////////////
    // Initialize
    //
    // Constructs the shared protocol stack dependencies in canonical order
    // and assigns them to GlassContext.  Call once at application startup,
    // after the database has been opened and the SignalBus and PacketBus
    // have been constructed.
    //
    // Does not set CurrentPatchLevel.  The patch level is determined per
    // launch (live capture uses the profile's server type; pcap replay
    // will use the file's metadata) and is the caller's responsibility to
    // set before any path that depends on it runs.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static void Initialize()
    {
        DebugLog.Write(LogChannel.General, "ProtocolStackBootstrap.Initialize: starting");

        CharacterRepository.Instance.Load();
        DebugLog.Write(LogChannel.General,
            "ProtocolStackBootstrap.Initialize: CharacterRepository loaded", LogLevel.Trace);

        GlassContext.FieldExtractor = new FieldExtractor();
        DebugLog.Write(LogChannel.General,
            "ProtocolStackBootstrap.Initialize: FieldExtractor constructed", LogLevel.Trace);

        GlassContext.PatchRegistry = new PatchRegistry();
        DebugLog.Write(LogChannel.General,
            "ProtocolStackBootstrap.Initialize: PatchRegistry constructed", LogLevel.Trace);

        GlassContext.FocusTracker = new FocusTracker();
        DebugLog.Write(LogChannel.General,
            "ProtocolStackBootstrap.Initialize: FocusTracker constructed", LogLevel.Trace);

        GlassContext.SessionRegistry = new SessionRegistry();
        DebugLog.Write(LogChannel.General,
            "ProtocolStackBootstrap.Initialize: SessionRegistry constructed", LogLevel.Trace);

        DebugLog.Write(LogChannel.General, "ProtocolStackBootstrap.Initialize: complete", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Teardown
    //
    // Clears the active profile, stops focus tracking, asks the video pipe
    // to clear its slot assignments, and disposes the opcode dispatcher so
    // it rebuilds fresh on the next launch.  Called from each application's
    // all-sessions-disconnected handler; application-specific UI updates
    // (such as menu state) run separately in each handler.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static void Teardown()
    {
        DebugLog.Write(LogChannel.Sessions,
            "ProtocolStackBootstrap.Teardown: starting", LogLevel.Trace);

        if (GlassContext.PacketCapture == null)
        {
            DebugLog.Write(LogChannel.Sessions,
                "ProtocolStackBootstrap.Teardown: no live capture active", LogLevel.Trace);
        }
        else if (!GlassContext.PacketCapture.IsCapturing)
        {
            GlassContext.PacketCapture = null;
        }
        else
        {
            DebugLog.Write(LogChannel.Sessions,
                "ProtocolStackBootstrap.Teardown: stopping packet capture", LogLevel.Trace);

            GlassContext.PacketCapture.Stop();
            GlassContext.PacketCapture = null;

            DebugLog.Write(LogChannel.Sessions,
                "ProtocolStackBootstrap.Teardown: packet capture stopped", LogLevel.Trace);
        }

        GlassContext.ProfileManager.ClearActiveProfile();
        DebugLog.Write(LogChannel.Sessions,
            "ProtocolStackBootstrap.Teardown: active profile cleared", LogLevel.Trace);

        GlassContext.FocusTracker.Stop();
        GlassContext.FocusTracker.ClearActiveSession();
        DebugLog.Write(LogChannel.Sessions,
            "ProtocolStackBootstrap.Teardown: focus tracker stopped", LogLevel.Trace);

        GlassContext.GlassVideoPipe.Send("clear_all");
        DebugLog.Write(LogChannel.Sessions,
            "ProtocolStackBootstrap.Teardown: GlassVideo clear_all sent", LogLevel.Trace);

        GlassContext.PatchRegistry.LogPoolStatistics();

        OpcodeDispatch.DisposeInstance();
        DebugLog.Write(LogChannel.Sessions,
            "ProtocolStackBootstrap.Teardown: OpcodeDispatch disposed", LogLevel.Trace);

        DebugLog.Write(LogChannel.Sessions,
            "ProtocolStackBootstrap.Teardown: complete");
    }
}