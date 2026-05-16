using System;
using System.Threading;
using Glass.Core.Logging;

namespace Glass.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// GcMonitor
//
// Periodically samples the runtime's GC counters and emits a log line per
// sample on the Memory channel.  Tracks generation collection counts and
// total allocated bytes; computes per-interval deltas so a spike in Gen 2
// collections or LOH growth is visible at a glance.
//
// One instance per process — implemented as a static class with a single
// timer.  Each application that wants the measurement calls Start once
// during initialization and Stop on shutdown.
///////////////////////////////////////////////////////////////////////////////////////////////
public static class GcMonitor
{
    private static readonly object _lock = new object();
    private static Timer? _timer;
    private static long _lastAllocatedBytes;
    private static int _lastGen0Count;
    private static int _lastGen1Count;
    private static int _lastGen2Count;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Start
    //
    // Begins periodic GC sampling.  The first tick fires after intervalSeconds;
    // subsequent ticks at the same period.  Idempotent: calling Start while
    // already running logs and returns without restarting the timer.
    //
    // intervalSeconds:  Seconds between samples.  Must be positive.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static void Start(int intervalSeconds)
    {
        if (intervalSeconds <= 0)
        {
            DebugLog.Write(LogChannel.Memory,
                "GcMonitor.Start: ignored, intervalSeconds must be positive (got "
                + intervalSeconds + ")");
            return;
        }

        lock (_lock)
        {
            if (_timer != null)
            {
                DebugLog.Write(LogChannel.Memory,
                    "GcMonitor.Start: ignored, already running");
                return;
            }

            _lastAllocatedBytes = GC.GetTotalAllocatedBytes();
            _lastGen0Count = GC.CollectionCount(0);
            _lastGen1Count = GC.CollectionCount(1);
            _lastGen2Count = GC.CollectionCount(2);

            int periodMs = intervalSeconds * 1000;
            _timer = new Timer(OnTick, null, periodMs, periodMs);

            DebugLog.Write(LogChannel.Memory,
                "GcMonitor.Start: sampling every " + intervalSeconds + " seconds");
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Stop
    //
    // Stops the periodic sampler.  Idempotent: calling Stop when not running
    // logs and returns.  Safe to call during shutdown.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static void Stop()
    {
        lock (_lock)
        {
            if (_timer == null)
            {
                DebugLog.Write(LogChannel.Memory,
                    "GcMonitor.Stop: ignored, not running");
                return;
            }

            _timer.Dispose();
            _timer = null;

            DebugLog.Write(LogChannel.Memory, "GcMonitor.Stop: stopped");
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OnTick
    //
    // Timer callback.  Samples the current GC counters, computes deltas against
    // the previous sample, and emits one log line.  Updates the stored
    // baselines for the next sample.
    //
    // The fields read here (_lastAllocatedBytes, _lastGen0Count, etc.) are only
    // touched by this method after Start has initialized them.  Start and Stop
    // guard the timer reference under _lock; this method does not contend for
    // that lock because the timer's existence is what gates whether this method
    // runs at all.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void OnTick(object? state)
    {
        long allocatedNow = GC.GetTotalAllocatedBytes();
        int gen0Now = GC.CollectionCount(0);
        int gen1Now = GC.CollectionCount(1);
        int gen2Now = GC.CollectionCount(2);

        long allocatedDelta = allocatedNow - _lastAllocatedBytes;
        int gen0Delta = gen0Now - _lastGen0Count;
        int gen1Delta = gen1Now - _lastGen1Count;
        int gen2Delta = gen2Now - _lastGen2Count;

        GCMemoryInfo memoryInfo = GC.GetGCMemoryInfo();
        long heapSize = memoryInfo.HeapSizeBytes;
        long lohSize = 0;
        if (memoryInfo.GenerationInfo.Length > 3)
        {
            lohSize = memoryInfo.GenerationInfo[3].SizeAfterBytes;
        }

        DebugLog.Write(LogChannel.Memory,
            "GcMonitor: alloc_delta=" + allocatedDelta
            + " gen0_delta=" + gen0Delta
            + " gen1_delta=" + gen1Delta
            + " gen2_delta=" + gen2Delta
            + " heap=" + heapSize
            + " loh=" + lohSize);

        _lastAllocatedBytes = allocatedNow;
        _lastGen0Count = gen0Now;
        _lastGen1Count = gen1Now;
        _lastGen2Count = gen2Now;
    }
}
