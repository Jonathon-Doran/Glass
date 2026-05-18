using System;
using Glass.Core.Logging;

namespace Glass.Core.Memory;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////
// BufferLease
//
// Public-facing handle to a region of a pooled BufferState.  Carries the
// underlying BufferState plus an offset and length describing this lease's
// view.  The BufferState tracks the reference count; the lease translates
// caller actions (Slice, Transfer, Dispose) into the appropriate refcount
// operations.
//
// Held by value.  Slice returns a new BufferLease over the same BufferState
// after calling AddRef on it — parent and child are both live and each must
// Dispose.  Transfer hands ownership from this lease to a new one with no
// refcount change, clearing this lease's fields so the source can no longer
// resolve to bytes.  Dispose decrements the refcount and clears the fields;
// when the count reaches zero the BufferState returns itself to the pool.
//
// Spans obtained from AsSpan or AsReadOnlySpan are not tracked by the
// refcount.  Callers must not retain spans past their synchronous scope;
// retention should be expressed by taking a child lease via Slice.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////
public struct BufferLease : IDisposable
{
    private BufferState? _state;
    private uint _offset;
    private uint _length;

    public uint Length => _length;
    public bool IsValid => _state != null;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // BufferLease ctor
    //
    // Constructed by BufferPool.Rent on initial rental and by Slice when a
    // child view is derived.  No refcount manipulation here; the caller is
    // responsible for ensuring the BufferState's refcount already reflects
    // this lease's existence — Rent's call to Reset leaves it at one for the
    // initial rental, and Slice calls AddRef on the BufferState before
    // constructing the child lease.
    //
    // state:   The BufferState backing this lease.
    // offset:  Byte offset from the start of the BufferState's buffer.
    // length:  Byte length of this lease's view.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    internal BufferLease(BufferState state, uint offset, uint length)
    {
        _state = state;
        _offset = offset;
        _length = length;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AsSpan
    //
    // Returns a writable span over this lease's view of the underlying buffer.
    // Intended for the small set of callers that fill a freshly-rented buffer:
    // the pcap copy-in, the decompressor's output write, the reassembler.
    // Most consumers should use AsReadOnlySpan instead.
    //
    // The returned span is not refcount-tracked.  It is valid for the caller's
    // synchronous scope; callers that need to retain bytes past that scope
    // must take a child lease via Slice rather than capturing the span.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Span<byte> AsSpan()
    {
        return _state!.AsSpan(_offset, _length);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AsReadOnlySpan
    //
    // Returns a read-only span over this lease's view of the underlying
    // buffer.  This is the common reader path — handlers, field extractors,
    // anyone parsing bytes without modifying them.
    //
    // Lifetime rules match AsSpan: not refcount-tracked, valid only for the
    // caller's synchronous scope, retention requires a child lease via Slice.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    public ReadOnlySpan<byte> AsReadOnlySpan()
    {
        return _state!.AsSpan(_offset, _length);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Slice
    //
    // Returns a new BufferLease covering a sub-region of this lease's view.
    // Calls AddRef on the underlying BufferState: parent and child are both
    // live after this call, and each must Dispose independently.
    //
    // Offset is expressed in this lease's view, not in the underlying buffer,
    // matching Span<T>.Slice semantics.  A child slice's absolute position in
    // the BufferState is _offset + offset.
    //
    // offset:  Byte offset from the start of this lease's view.
    // length:  Byte length of the child view.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    public BufferLease Slice(uint offset, uint length)
    {
        if ((offset < 0) || (length < 0) || (offset + length > _length))
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                $"BufferLease.Slice: offset={offset} length={length} does not fit within view length={_length}.");
        }

        _state!.AddRef();
        return new BufferLease(_state, _offset + offset, length);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Transfer
    //
    // Hands ownership of this lease's BufferState off to a new BufferLease and
    // clears this lease's fields.  No reference count change: the count
    // already reflects this lease's existence, and the returned lease takes
    // its place without adding or removing a holder.
    //
    // Use when a caller has finished with its own working role but wants to
    // pass the buffer to another consumer for further processing — for
    // example, the promotion from a UdpDatagram-side lease to a Message-side
    // lease.  After Transfer returns, the source lease is invalid; any
    // subsequent AsSpan, Slice, Transfer, or Dispose on it will fault on the
    // null _state.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    public BufferLease Transfer()
    {
        BufferLease moved = new BufferLease(_state!, _offset, _length);

        _state = null;
        _offset = 0;
        _length = 0;

        return moved;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Dispose
    //
    // Releases this lease's reference on the underlying BufferState and
    // clears this lease's fields.  When the refcount reaches zero, the
    // BufferState returns itself to the BufferPool for reuse.
    //
    // Implements IDisposable so `using` blocks compose cleanly with lease
    // ownership: a renter that wants stack-scoped lifetime can `using
    // BufferLease lease = pool.Rent(size)` and trust Dispose runs at scope
    // exit on every path.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Dispose()
    {
        _state!.Release();

        _state = null;
        _offset = 0;
        _length = 0;
    }
}