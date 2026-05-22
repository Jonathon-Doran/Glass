using Glass.Core.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Glass.Core.Memory;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////
// BufferPool
//
// Pool of byte[] storage organized into size classes.  Each class has its own
// queue of available BufferStates and a counting semaphore that blocks renters
// when the queue is empty.  Returns to a class's queue release one slot on
// that class's semaphore, waking the next waiter.
//
// The ladder is fixed at construction.  Depth per class is also fixed; the
// pool does not grow on demand.  A Rent request larger than the largest
// class throws — the pool's purpose is bounded, predictable storage and an
// out-of-range request indicates a bug or corrupted size that should surface.
//
// Thread safety: one lock per queue, scoped to the dequeue/enqueue itself.
// The semaphore handles wait-on-exhaustion; the queue lock only protects
// the Queue<T> mutation.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////
public sealed class BufferPool
{
    private readonly uint[] _sizes;
    private readonly uint[] _depths;
    private readonly ConcurrentQueue<BufferState>[] _queues;
    private readonly SemaphoreSlim[] _waiters;

    // performance counters
    private readonly uint[] _inUse;
    private readonly uint[] _highWater;
    private readonly uint[] _totalRents;
    private readonly uint[] _waitCount;
    //private readonly Timer _statsTimer;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // BufferPool ctor
    //
    // Builds the size class ladder and pre-allocates the configured depth of
    // BufferStates for each class.  Each class's semaphore starts with a count
    // equal to that class's depth, matching the number of BufferStates sitting
    // in its queue.
    //
    // sizes:   Byte capacities for each class, in ascending order.  No
    //          ordering check is performed; the caller is responsible for
    //          passing a sensible ladder.
    // depths:  Number of BufferStates to pre-allocate per class.  Must be the
    //          same length as sizes.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    public BufferPool(uint[] sizes, uint[] depths)
    {
        if (sizes.Length != depths.Length)
        {
            throw new ArgumentException("BufferPool.ctor: sizes and depths must be the same length.");
        }

        _sizes = (uint[])sizes.Clone();
        _depths = (uint[])depths.Clone();
        _queues = new ConcurrentQueue<BufferState>[sizes.Length];
        _waiters = new SemaphoreSlim[sizes.Length];

        _inUse = new uint[sizes.Length];
        _highWater = new uint[sizes.Length];
        _totalRents = new uint[sizes.Length];
        _waitCount = new uint[sizes.Length];

        for (uint i = 0; i < sizes.Length; i++)
        {
            _queues[i] = new ConcurrentQueue<BufferState>();
            _waiters[i] = new SemaphoreSlim((int) depths[i], (int) _depths[i]);

            _inUse[i] = 0;
            _highWater[i] = 0;
            _totalRents[i] = 0;
            _waitCount[i] = 0;

            for (uint j = 0; j < _depths[i]; j++)
            {
                BufferState state = new BufferState(this, i, sizes[i]);
                _queues[i].Enqueue(state);
            }
        }
       //  _statsTimer = new Timer(LogStatisticsCallback, null, 1000, 1000);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ClassFor
    //
    // Returns the index of the smallest size class whose capacity is at least
    // the requested size.  Linear scan over the ladder; with fewer than ten
    // classes this is faster and simpler than any indirected lookup.  Throws
    // when the request exceeds the largest class — that case indicates either
    // a bug or a corrupted size upstream and should surface rather than be
    // silently rounded or masked.
    //
    // size:  The requested minimum byte capacity.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    private int ClassFor(uint size)
    {
        for (int i = 0; i < _sizes.Length; i++)
        {
            if (_sizes[i] >= size)
            {
                return i;
            }
        }

        DebugLog.Write(LogChannel.Memory, $"BufferPool.ClassFor: requested size {size} exceeds largest class {_sizes[_sizes.Length - 1]}.");
        throw new ArgumentOutOfRangeException(nameof(size), size, $"requested size {size} exceeds largest pool class {_sizes[_sizes.Length - 1]}.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Rent
    //
    // Returns a Lease over a BufferState whose capacity is at least minimumSize.
    // Picks the smallest fitting size class via ClassFor.  Waits on that class's
    // semaphore until a BufferState is available; the semaphore guarantees a
    // matching item is present in the queue, so the TryDequeue that follows
    // succeeds without any further synchronization.  The BufferState is Reset
    // (refcount restored to one) before being wrapped in a Lease.
    //
    // The returned Lease views the full capacity of the underlying buffer,
    // not just minimumSize.  Callers that have a smaller working region can
    // slice the Lease down as needed.
    //
    // minimumSize:  The requested minimum byte capacity.  Throws via ClassFor
    //               if it exceeds the largest size class.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    public BufferLease Rent(uint minimumSize)
    {
        int cls = ClassFor(minimumSize);

        if (!_waiters[cls].Wait(0))
        {
            Interlocked.Increment(ref _waitCount[cls]);
            _waiters[cls].Wait();
        }

        if (!_queues[cls].TryDequeue(out BufferState? state))
        {
            DebugLog.Write(LogChannel.Memory, $"BufferPool.Rent: semaphore for class {cls} signalled with empty queue.");
            throw new InvalidOperationException($"BufferPool.Rent: invariant violation, semaphore signalled but queue empty for class {cls}.");
        }

        state.Reset();

        Interlocked.Increment(ref _totalRents[cls]);
        uint nowInUse = Interlocked.Increment(ref _inUse[cls]);

        uint currentHigh = _highWater[cls];
        while (nowInUse > currentHigh)
        {
            uint observed = (uint)Interlocked.CompareExchange(
                ref Unsafe.As<uint, int>(ref _highWater[cls]),
                (int)nowInUse,
                (int)currentHigh);

            if (observed == currentHigh)
            {
                break;
            }
            currentHigh = observed;
        }


        // note that the lease covers this size, but can grow up to capacity if needed
        return new BufferLease(state, 0, minimumSize);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Return
    //
    // Places a BufferState back on the queue for its size class and signals
    // the class's semaphore so a waiting renter can proceed.  Called by
    // BufferState.Release when the reference count reaches zero; no other
    // caller is legitimate.  The BufferState's contents are not cleared
    // here — the next rent will overwrite them, and clearing the bytes
    // earns nothing.
    //
    // state:  The BufferState whose refcount has just reached zero.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    internal void Return(BufferState state)
    {
        uint cls = state.SizeClass;

        Interlocked.Decrement(ref _inUse[cls]);

        _queues[cls].Enqueue(state);
        _waiters[cls].Release();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LogStatisticsCallback
    //
    // Timer callback that invokes LogStatistics on each tick.  The Timer
    // signature requires a TimerCallback delegate taking a state object;
    // this method exists solely to bridge that signature to the parameterless
    // LogStatistics call.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LogStatisticsCallback(object? state)
    {
        LogStatistics();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LogStatistics
    //
    // Writes one log line per size class to the Memory channel.  Each line
    // reports the class's size, current in-use count, peak in-use count
    // observed, cumulative rent count, and the number of rents that had to
    // wait on the class's semaphore.  Counters are read without locking;
    // values may be slightly stale relative to each other on a busy thread,
    // which is acceptable for diagnostics.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void LogStatistics()
    {
        for (uint i = 0; i < _sizes.Length; i++)
        {
            DebugLog.Write(LogChannel.Memory,
                $"BufferPool stats: class={i} size={_sizes[i]} "
                + $"inUse={_inUse[i]} highWater={_highWater[i]} "
                + $"totalRents={_totalRents[i]} waits={_waitCount[i]}.");
        }
    }

}