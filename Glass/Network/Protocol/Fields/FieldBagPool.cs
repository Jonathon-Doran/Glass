using Glass.Core.Logging;
using System.Collections.Generic;

namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldBagPool
//
// A thread-safe pool of pre-allocated FieldBag instances.  Handlers (via the extractor)
// rent a bag at the start of decoding a packet, fill it with field values, read them, and
// return the bag for reuse.  The pool exists to make per-packet decoding zero-allocation
// in steady state.
//
// Thread safety: the underlying Stack<FieldBag> is guarded by a plain lock.  The critical
// section is one push or one pop and is expected to be uncontended at typical packet
// rates (single-digit packets per second per client).  If profiling later shows the lock
// is hot, ConcurrentStack<FieldBag> is a drop-in replacement with no public surface change.
//
// Pool exhaustion: if Rent is called when the stack is empty, a fresh bag is allocated and
// the event is logged loudly.  Exhaustion is treated as a sizing bug to fix at startup,
// not as a runtime condition to handle gracefully — quietly hiding it would let
// allocation pressure creep back into the hot path unnoticed.
///////////////////////////////////////////////////////////////////////////////////////////////
public class FieldBagPool
{
    private readonly Stack<FieldBag> _bags;
    private readonly object _lock;
    private readonly int _slotsPerBag;
    private int _allocatedBagCount;
    private int _exhaustionCount;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // FieldBagPool (constructor)
    //
    // Pre-allocates initialBagCount bags, each with slotsPerBag slots, and pushes them onto
    // the internal stack.  Construction is the one place where bag allocation is expected;
    // every subsequent allocation (via the exhaustion path in Rent) is logged as a warning.
    //
    // Parameters:
    //   initialBagCount - Number of bags to pre-allocate.  Sized to the expected peak
    //                     concurrent bag-in-flight count plus a small margin.  Values
    //                     less than 1 are clamped to 1 and logged.
    //   slotsPerBag     - Number of slots in each bag.  Passed through to the FieldBag
    //                     constructor.  Values less than 1 are clamped to 1 by the bag
    //                     constructor itself; this constructor does not double-check.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public FieldBagPool(int initialBagCount, int slotsPerBag)
    {
        if (initialBagCount < 1)
        {
            DebugLog.Write(LogChannel.Fields, "FieldBagPool ctor: invalid initialBagCount "
                + initialBagCount + ", clamping to 1");
            initialBagCount = 1;
        }

        _bags = new Stack<FieldBag>(initialBagCount);
        _lock = new object();
        _slotsPerBag = slotsPerBag;
        _allocatedBagCount = 0;
        _exhaustionCount = 0;

        DebugLog.Write(LogChannel.Fields, "FieldBagPool ctor: pre-allocating "
            + initialBagCount + " bags with " + slotsPerBag + " slots each");

        for (int bagIndex = 0; bagIndex < initialBagCount; bagIndex++)
        {
            FieldBag freshBag = new FieldBag(slotsPerBag, this);
            _bags.Push(freshBag);
            _allocatedBagCount++;
        }

        DebugLog.Write(LogChannel.Fields, "FieldBagPool ctor: pre-allocation complete, "
            + _allocatedBagCount + " bags ready");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Rent
    //
    // Returns a FieldBag for the caller to fill and read.  The bag is cleared before being
    // returned so the caller always sees a fresh bag regardless of the prior tenant's state.
    //
    // If the pool is empty, a fresh bag is allocated on the spot and the event is logged
    // loudly.  Repeated exhaustion is a sizing bug — increase initialBagCount at construction
    // to eliminate the runtime allocations.
    //
    // Returns:
    //   A bag with SlotCount == 0, ready to be filled.  The caller must eventually call
    //   Release on the returned bag to return it to this pool.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public FieldBag Rent()
    {
        FieldBag? rentedBag = null;

        lock (_lock)
        {
            if (_bags.Count > 0)
            {
                rentedBag = _bags.Pop();
            }
            else
            {
                _exhaustionCount++;
                DebugLog.Write(LogChannel.Fields, "FieldBagPool.Rent: pool exhausted, allocating "
                    + "fresh bag (exhaustion count now " + _exhaustionCount + ", lifetime allocated "
                    + "before this " + _allocatedBagCount + ")");
                rentedBag = new FieldBag(_slotsPerBag, this);
                _allocatedBagCount++;
            }
        }

        rentedBag.Clear();
        return rentedBag;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Return
    //
    // Pushes a bag back onto the pool's stack so it can be reused by a future Rent call.
    // Called by FieldBag.Release; handlers and the extractor should not call this directly.
    //
    // The bag is not cleared here — clearing happens at Rent time so that bags which never
    // get reused do not pay for a clear they will not benefit from.
    //
    // Parameters:
    //   bag - The bag to return.  Must have been originally rented from this pool.  A null
    //         bag is logged and ignored rather than throwing — a misbehaving caller should
    //         not crash the dispatch loop.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Return(FieldBag bag)
    {
        if (bag == null)
        {
            DebugLog.Write(LogChannel.Fields, "FieldBagPool.Return: null bag, ignoring");
            return;
        }

        lock (_lock)
        {
            _bags.Push(bag);
        }
    }
}
