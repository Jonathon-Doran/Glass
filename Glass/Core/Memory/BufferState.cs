using System;
using System.Threading;
using Glass.Core.Logging;

namespace Glass.Core.Memory;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////
// BufferState
//
// Internal storage and reference count for a buffer rented from the BufferPool.
// One instance owns one byte[].  Multiple Leases can share a single BufferState
// when slices are derived; each derivation calls AddRef and each Lease.Dispose
// calls Release.  When the refcount reaches zero, the BufferState asks the
// pool to put it back on its size-class queue for reuse.
//
// BufferState is never seen by callers outside the memory subsystem.  Callers
// hold Leases, which carry a BufferState reference and a slice view.
//
// Thread safety: AddRef and Release use Interlocked on _refCount.  Reset is
// only called under the pool's lock on the rent path, after the prior holder's
// Release took the count to zero, so a non-atomic write is sufficient there.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////
internal sealed class BufferState
{
    private readonly BufferPool _pool;
    private readonly uint _sizeClass;
    private readonly byte[] _buffer;
    private uint _refCount;

    public byte[] Buffer => _buffer;
    public uint Capacity => (uint) _buffer.Length;
    public uint SizeClass => _sizeClass;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // BufferState ctor
    //
    // First-time initialization for a buffer that will live in the BufferPool's
    // queues.  Allocates the underlying byte[] at the requested size and sets
    // the reference count to one, matching the state a freshly-rented buffer
    // is expected to be in.  The pool hands the BufferState out from here on
    // first allocation without further bookkeeping; subsequent re-rents call
    // Reset to restore the same state.
    //
    // pool:       The owning BufferPool.  Release calls back into pool.Return
    //             when the refcount hits zero so this state goes onto its queue.
    // sizeClass:  Index of this state's size class in the pool's ladder.
    //             Used by the pool to choose the correct return queue.
    // size:       Byte length of the underlying buffer.  Equals the size class's
    //             capacity; passed in so the constructor stays decoupled from the
    //             pool's class table.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    public BufferState(BufferPool pool, uint sizeClass, uint size)
    {
        _pool = pool;
        _sizeClass = sizeClass;
        _buffer = new byte[size];
        _refCount = 1;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AsSpan
    //
    // Returns a span over a region of the underlying buffer.  Callers use this
    // to materialize a working view from a Lease's stored offset and length.
    // Bounds checking is delegated to Span<byte>.Slice and will throw
    // ArgumentOutOfRangeException for an out-of-range region.
    //
    // offset:  Byte offset from the start of the buffer.
    // length:  Number of bytes in the returned span.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Span<byte> AsSpan(uint offset, uint length)
    {
        return _buffer.AsSpan((int) offset, (int) length);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Reset
    //
    // Restores the reference count to its post-construction value of one, so
    // this BufferState can be handed out by BufferPool.Rent as if freshly
    // allocated.  Called only on the rent path, under the pool's lock and
    // after the prior holder's Release has taken the count to zero.  Under
    // those preconditions there are no concurrent readers of _refCount, so
    // Volatile.Write is sufficient and Interlocked is not required.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Reset()
    {
        Volatile.Write(ref _refCount, 1);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AddRef
    //
    // Increments the reference count atomically.  Called by code that already
    // holds a live reference to this BufferState — Lease.Slice when deriving
    // a child view, or fan-out points that distribute the same buffer to
    // multiple subscribers.  The "caller already holds a live ref" invariant
    // means the refcount cannot transition through zero between the caller's
    // decision to call AddRef and the increment itself, so a single
    // Interlocked.Increment is sufficient with no further synchronization.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void AddRef()
    {
        Interlocked.Increment(ref _refCount);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Release
    //
    // Decrements the reference count atomically.  When the count reaches zero,
    // the last holder has gone away and this BufferState is returned to the
    // BufferPool for reuse.  The pool is responsible for placing it on the
    // queue for its size class and signalling any thread waiting on a rental
    // from that class.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Release()
    {
        uint now = Interlocked.Decrement(ref _refCount);

        if (now == 0)
        {
            _pool.Return(this);
        }
    }
}