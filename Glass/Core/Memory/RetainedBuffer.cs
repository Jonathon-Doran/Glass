using System;

namespace Glass.Core.Memory;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////
// RetainedBuffer
//
// Public-facing handle to a long-lived byte[] copy issued by RetainedBuffers.
// Wraps a single byte[] sized exactly to the retained payload; the whole
// array is the view, no offset or length needed.
//
// Held by value.  No Dispose, no refcount, no pool return: when the holder
// drops the struct, the underlying array becomes eligible for GC.  Spans
// obtained from AsReadOnlySpan are valid for the lifetime of any holder of
// the struct.
//
// default(RetainedBuffer) is not a meaningful value; obtaining one indicates
// an uninitialized field or a forgotten Retain call.  Members will throw on
// such an instance.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////
public readonly struct RetainedBuffer
{
    private readonly byte[] _buffer;

    public int Length => _buffer.Length;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RetainedBuffer ctor
    //
    // Constructed only by RetainedBuffers.Retain after the copy completes.
    // The array passed in is owned solely by this RetainedBuffer; the issuer
    // does not keep a reference, and no other caller has one.
    //
    // buffer:  Newly-allocated array containing the retained bytes.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    internal RetainedBuffer(byte[] buffer)
    {
        _buffer = buffer;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AsReadOnlySpan
    //
    // Returns a read-only span over the retained bytes.  Lifetime tracks the
    // RetainedBuffer struct: as long as any holder keeps the struct alive,
    // the array is not collectable and the span remains valid.  Callers that
    // need to retain bytes past their own scope should keep the struct, not
    // capture the span.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    public ReadOnlySpan<byte> AsReadOnlySpan()
    {
        return _buffer;
    }
}
