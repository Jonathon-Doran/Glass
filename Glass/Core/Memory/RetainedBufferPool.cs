using Glass.Core.Logging;
using System.Threading;

namespace Glass.Core.Memory;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////
// RetainedBufferPool
//
// Issuer of long-lived buffer copies for callers that need to hold packet
// bytes past the synchronous scope of a bus delivery.  Each Retain call
// allocates a fresh byte[] sized exactly to the input data, copies the bytes
// in, and returns a RetainedBuffer that views the new array.  No reference
// counting, no pooled queue, no release path: when the holder drops the
// RetainedBuffer, the underlying array becomes eligible for GC.
//
// This is the long-term-storage counterpart to BufferPool's short-term
// rentals.  BufferPool exists for in-flight working buffers that get returned
// for reuse; RetainedBuffers exists for snapshots that live for the duration
// of a capture session and are not sized to the pool's class ladder.
//
// Statistics are monotonic counters: Retain increments them, nothing
// decrements them.  This matches the no-release model and gives a simple
// session-cumulative view of retention pressure.
//
// Thread safety: counters are updated via Interlocked.  Retain is otherwise
// allocation-only and has no shared mutable state.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////
public sealed class RetainedBufferPool
{
    private long _bufferCount;
    private long _totalBytes;

    public long BufferCount => Interlocked.Read(ref _bufferCount);
    public long TotalBytes => Interlocked.Read(ref _totalBytes);

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RetainedBufferPool ctor
    //
    // Initializes the cumulative counters to zero.  Every Retain call
    // allocates its own array; there is no pre-allocation to perform here.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    public RetainedBufferPool()
    {
        _bufferCount = 0;
        _totalBytes = 0;
        DebugLog.Write(LogChannel.Memory, "RetainedBufferPool.ctor: created");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Retain
    //
    // Allocates a fresh byte[] sized exactly to data.Length, copies the bytes
    // in, and returns a RetainedBuffer over the new array.  The returned
    // buffer is independent of the source span: the span may be invalidated
    // after this call returns without affecting the retained copy.
    //
    // Updates the cumulative counters under Interlocked.  Zero-length input
    // is supported and produces a RetainedBuffer over a zero-length array.
    //
    // data:  The bytes to retain.  Contents are copied, not referenced.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    public RetainedBuffer Retain(ReadOnlySpan<byte> data)
    {
        byte[] buffer = new byte[data.Length];
        data.CopyTo(buffer);

        Interlocked.Increment(ref _bufferCount);
        Interlocked.Add(ref _totalBytes, data.Length);

        return new RetainedBuffer(buffer);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LogStatistics
    //
    // Writes one log line to the Memory channel reporting the cumulative
    // counters: total retained buffers issued and total bytes copied across
    // all Retain calls.  Counters are read via Interlocked.Read for
    // consistency with the accessor properties; values may be slightly stale
    // relative to each other on a busy thread, which is acceptable for
    // diagnostics.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void LogStatistics()
    {
        long buffers = Interlocked.Read(ref _bufferCount);
        long bytes = Interlocked.Read(ref _totalBytes);

        DebugLog.Write(LogChannel.Memory,
            "RetainedBufferPool stats: buffers=" + buffers
            + " totalBytes=" + bytes);
    }
}