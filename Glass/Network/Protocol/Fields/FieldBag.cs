using Glass.Core.Logging;
using System;
using System.Text;

namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldBag
//
// A pooled container of named field values.  The extractor fills bags during decode;
// handlers read fields by name and release the bag when finished.  All storage is inline
// in the bag's slot array — no per-field allocation occurs during fill or read.
//
// Lifetime: a bag is rented from a FieldBagPool, filled by the extractor, handed to a
// handler, read by the handler, and released back to the pool.  A bag must not be retained
// past Release.  Spans returned by the bag are valid only until Release.
//
// Thread safety: a single bag instance is single-threaded by convention.  The pool that
// owns it is thread-safe; bags themselves are not.  A bag must not be shared across
// threads while in use.
///////////////////////////////////////////////////////////////////////////////////////////////
public class FieldBag
{
    public const int DefaultSlotCount = 64;
    public const int DefaultPoolSize = 64;

    private readonly FieldSlot[] _slots;
    private readonly FieldBagPool _pool;
    private int _slotCount;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // FieldBag (constructor)
    //
    // Creates a bag with the given slot capacity, owned by the given pool.  Called by the
    // pool during pre-warm; not called from the hot path.
    //
    // capacity:  Number of slots in the bag.
    // pool:      The pool this bag returns to on Release.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public FieldBag(int capacity, FieldBagPool pool)
    {
        if (capacity <= 0)
        {
            DebugLog.Write(LogChannel.Fields, "FieldBag ctor: invalid capacity " + capacity
                + ", clamping to 1");
            capacity = 1;
        }

        _slots = new FieldSlot[capacity];
        _pool = pool;
        _slotCount = 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Capacity
    //
    // The maximum number of slots this bag can hold.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public int Capacity
    {
        get { return _slots.Length; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SlotCount
    //
    // The number of slots currently populated.  Slots at indices [0, SlotCount) are live;
    // slots at [SlotCount, Capacity) are unused.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public int SlotCount
    {
        get { return _slotCount; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Clear
    //
    // Resets all populated slots to the Empty state and zeroes the slot count.  Called by
    // the pool when a bag is rented out, ensuring the bag starts fresh regardless of its
    // prior tenant's state.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Clear()
    {
        for (int slotIndex = 0; slotIndex < _slotCount; slotIndex++)
        {
            _slots[slotIndex].Clear();
        }
        _slotCount = 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AddSlot
    //
    // Returns a ref to the next available slot, advancing the populated-slot count.  The
    // extractor calls SetName and one of the SetX methods on the returned ref to populate
    // the slot.  Logs and returns a ref to the last slot if the bag is at capacity — this
    // is a sizing bug that should be fixed by increasing capacity, not handled at runtime.
    //
    // Returns:  A ref to the newly-allocated slot, or to the last slot on overflow.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ref FieldSlot AddSlot()
    {
        if (_slotCount >= _slots.Length)
        {
            DebugLog.Write(LogChannel.Fields, "FieldBag.AddSlot: capacity " + _slots.Length
                + " exceeded; returning ref to last slot, field will be overwritten");
            return ref _slots[_slots.Length - 1];
        }

        ref FieldSlot slot = ref _slots[_slotCount];
        _slotCount++;
        return ref slot;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetIntAt
    //
    // Reads the slot at the given index as a 32-bit signed integer.  Logs and returns 0 if
    // the index is out of range.  Used by handlers that have cached field indices at
    // construction time.
    //
    // slotIndex:  The cached index of the field, typically resolved via
    //             FieldDefinitionExtensions.IndexOfField at handler construction.  A value
    //             of -1 (the "field not found" sentinel) returns 0.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public int GetIntAt(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slotCount)
        {
            DebugLog.Write(LogChannel.Fields, "FieldBag.GetIntAt: slotIndex " + slotIndex
                + " out of range [0, " + _slotCount + "), returning 0");
            return 0;
        }
        return _slots[slotIndex].GetInt32();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetUInt3At
    //
    // Reads the slot at the given index as a 32-bit unsigned integer.  Logs and returns 0
    // if the index is out of range.  Used by handlers that have cached field indices at
    // construction time.
    //
    // slotIndex:  The cached index of the field, typically resolved via
    //             FieldDefinitionExtensions.IndexOfField at handler construction.  A value
    //             of -1 (the "field not found" sentinel) returns 0.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public uint GetUIntAt(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slotCount)
        {
            DebugLog.Write(LogChannel.Fields, "FieldBag.GetUIntAt: slotIndex " + slotIndex
                + " out of range [0, " + _slotCount + "), returning 0");
            return 0;
        }
        return _slots[slotIndex].GetUInt32();
    }


    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetFloatAt
    //
    // Reads the slot at the given index as a 32-bit IEEE float.  Logs and returns 0.0f if
    // the index is out of range.  Used by handlers that have cached field indices at
    // construction time.
    //
    // slotIndex:  The cached index of the field, typically resolved via
    //             FieldDefinitionExtensions.IndexOfField at handler construction.  A value
    //             of -1 (the "field not found" sentinel) returns 0.0f.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public float GetFloatAt(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slotCount)
        {
            DebugLog.Write(LogChannel.Fields, "FieldBag.GetFloatAt: slotIndex " + slotIndex
                + " out of range [0, " + _slotCount + "), returning 0.0f");
            return 0.0f;
        }
        return _slots[slotIndex].GetFloat();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetBytesAt
    //
    // Returns a read-only span over the ASCII string bytes stored in the slot at the given
    // index.  Logs and returns an empty span if the index is out of range.  Used by handlers
    // that have cached field indices at construction time.
    //
    // The returned span is valid only while this bag is rented.  After Release the bag's
    // slots may be reused; callers that need the bytes past Release must copy them out
    // (e.g. via Encoding.ASCII.GetString or span.CopyTo).
    //
    // slotIndex:  The cached index of the field, typically resolved via
    //             FieldDefinitionExtensions.IndexOfField at handler construction.  A value
    //             of -1 (the "field not found" sentinel) returns an empty span.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ReadOnlySpan<byte> GetBytesAt(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slotCount)
        {
            DebugLog.Write(LogChannel.Fields, "FieldBag.GetBytesAt: slotIndex " + slotIndex
                + " out of range [0, " + _slotCount + "), returning empty span");
            return ReadOnlySpan<byte>.Empty;
        }
        return _slots[slotIndex].GetAsciiBytes();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Release
    //
    // Returns the bag to its owning pool.  The bag must not be accessed after this call.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Release()
    {
        if (_pool == null)
        {
            DebugLog.Write(LogChannel.Fields, "FieldBag.Release: no pool to return to, dropping bag");
            return;
        }
        _pool.Return(this);
    }
}
