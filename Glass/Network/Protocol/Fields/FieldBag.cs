using Glass.Core.Logging;
using System.Runtime.CompilerServices;

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
    private string _currentOpcodeName = string.Empty;

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
    // CurrentOpcodeName
    //
    // The opcode name of the handler currently using this bag.  Set by FieldExtractor.Rent
    // immediately before returning the bag to the handler.  The bag's accessor methods
    // include this name in any log lines they produce, so type mismatches and other slot
    // read failures are attributable to a specific opcode.  Cleared on Release.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string CurrentOpcodeName
    {
        get { return _currentOpcodeName; }
        set { _currentOpcodeName = value; }
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
    // TryGetSlotRef
    //
    // Returns a ref to the slot at the given index, or a null ref if the index is out of
    // range.  Callers must check via Unsafe.IsNullRef before dereferencing.
    //
    // Used by extractors that write to slots allocated earlier in the same packet's
    // extraction pass — for example, the optional-block helper, which writes to slots
    // whose FieldDefinition entries already triggered AddSlot during the main switch.
    //
    // Parameters:
    //   slotIndex  - The cached index of the slot.
    //
    // Returns:
    //   A ref to the slot, or a null ref if slotIndex is out of range.  Check with
    //   Unsafe.IsNullRef(ref result) before use.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ref FieldSlot TryGetSlotRef(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slotCount)
        {
            DebugLog.Write(LogChannel.Fields, _currentOpcodeName + " FieldBag.TryGetSlotRef: slotIndex "
                + slotIndex + " out of range [0, " + _slotCount + "), returning null ref");
            return ref Unsafe.NullRef<FieldSlot>();
        }
        return ref _slots[slotIndex];
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetIntAt
    //
    // Reads the slot at the given index as a 32-bit signed integer.  Logs and returns 0 if
    // the index is out of range or the slot's type does not match.  Used by handlers that
    // have cached field indices at construction time.
    //
    // slotIndex:  The cached index of the field, typically resolved via
    //             FieldDefinitionExtensions.IndexOfField at handler construction.  A value
    //             of -1 (the "field not found" sentinel) returns 0.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public int GetIntAt(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slotCount)
        {
            DebugLog.Write(LogChannel.Fields, _currentOpcodeName + " FieldBag.GetIntAt: slotIndex "
                + slotIndex + " out of range [0, " + _slotCount + "), returning 0");
            return 0;
        }

        int value;
        SlotReadResult result = _slots[slotIndex].TryGetInt32(out value);
        switch (result)
        {
            case SlotReadResult.Success:
                return value;

            case SlotReadResult.TypeMismatch:
                DebugLog.Write(LogChannel.Fields, _currentOpcodeName
                    + " FieldBag.GetIntAt: type mismatch reading '"
                    + _slots[slotIndex].GetName() + "' at index " + slotIndex
                    + ", slot type is " + _slots[slotIndex].Type + ", returning 0");
                return 0;

            default:
                DebugLog.Write(LogChannel.Fields, _currentOpcodeName
                    + " FieldBag.GetIntAt: unhandled SlotReadResult " + result
                    + " for '" + _slots[slotIndex].GetName() + "' at index " + slotIndex
                    + ", returning 0");
                return 0;
        }
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
            DebugLog.Write(LogChannel.Fields, _currentOpcodeName + " FieldBag.GetUIntAt: slotIndex "
                + slotIndex + " out of range [0, " + _slotCount + "), returning 0");
            return 0;
        }

        uint value;
        SlotReadResult result = _slots[slotIndex].TryGetUInt32(out value);
        switch (result)
        {
            case SlotReadResult.Success:
                return value;

            case SlotReadResult.TypeMismatch:
                DebugLog.Write(LogChannel.Fields, _currentOpcodeName
                    + " FieldBag.GetIntAt: type mismatch reading '"
                    + _slots[slotIndex].GetName() + "' at index " + slotIndex
                    + ", slot type is " + _slots[slotIndex].Type + ", returning 0");
                return 0;

            default:
                DebugLog.Write(LogChannel.Fields, _currentOpcodeName
                    + " FieldBag.GetIntAt: unhandled SlotReadResult " + result
                    + " for '" + _slots[slotIndex].GetName() + "' at index " + slotIndex
                    + ", returning 0");
                return 0;
        }
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
            DebugLog.Write(LogChannel.Fields, _currentOpcodeName + " FieldBag.GetFloatAt: slotIndex "
                + slotIndex + " out of range [0, " + _slotCount + "), returning 0");
            return 0;
        }

        float value;
        SlotReadResult result = _slots[slotIndex].TryGetFloat(out value);
        switch (result)
        {
            case SlotReadResult.Success:
                return value;

            case SlotReadResult.TypeMismatch:
                DebugLog.Write(LogChannel.Fields, _currentOpcodeName
                    + " FieldBag.GetIntAt: type mismatch reading '"
                    + _slots[slotIndex].GetName() + "' at index " + slotIndex
                    + ", slot type is " + _slots[slotIndex].Type + ", returning 0");
                return 0;

            default:
                DebugLog.Write(LogChannel.Fields, _currentOpcodeName
                    + " FieldBag.GetIntAt: unhandled SlotReadResult " + result
                    + " for '" + _slots[slotIndex].GetName() + "' at index " + slotIndex
                    + ", returning 0");
                return 0;
        }
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
            DebugLog.Write(LogChannel.Fields, _currentOpcodeName + " FieldBag.GetBytesAt: slotIndex "
                + slotIndex + " out of range [0, " + _slotCount + "), returning 0");
            return ReadOnlySpan<byte>.Empty;
        }

        ReadOnlySpan<byte> value;
        SlotReadResult result = _slots[slotIndex].TryGetAsciiBytes(out value);
        switch (result)
        {
            case SlotReadResult.Success:
                return value;

            case SlotReadResult.TypeMismatch:
                DebugLog.Write(LogChannel.Fields, _currentOpcodeName
                    + " FieldBag.GetIntAt: type mismatch reading '"
                    + _slots[slotIndex].GetName() + "' at index " + slotIndex
                    + ", slot type is " + _slots[slotIndex].Type + ", returning 0");
                return ReadOnlySpan<byte>.Empty;

            default:
                DebugLog.Write(LogChannel.Fields, _currentOpcodeName
                    + " FieldBag.GetIntAt: unhandled SlotReadResult " + result
                    + " for '" + _slots[slotIndex].GetName() + "' at index " + slotIndex
                    + ", returning 0");
                return ReadOnlySpan<byte>.Empty;
        }
    }
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // IsPresent
    //
    // Returns true if the slot at the given index holds a value, false if the slot is in
    // its default Empty state.  Used by handlers to distinguish "field was absent from the
    // packet" from "field was present with a zero value" — a distinction the GetXxxAt
    // accessors collapse, since they return 0 (or an empty span) for both cases.
    //
    // Optional fields whose flag bit was clear in the packet appear as Empty slots after
    // extraction.  Handlers check IsPresent on the optional fields they care about and
    // skip reads on absent ones.
    //
    // Returns false if the slot index is out of range, matching the GetXxxAt accessors'
    // behavior on the same condition.
    //
    // Parameters:
    //   slotIndex  - The cached index of the field, typically resolved via
    //                FieldDefinitionExtensions.IndexOfField at handler construction.  A
    //                value of -1 (the "field not found" sentinel) returns false.
    //
    // Returns:
    //   true   - The slot holds a value.
    //   false  - The slot is Empty, or the index is out of range.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool IsPresent(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slotCount)
        {
            return false;
        }
        return _slots[slotIndex].Type != FieldType.Empty;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Release
    //
    // Returns the bag to its owning pool.  The bag must not be accessed after this call.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Release()
    {
        _currentOpcodeName = string.Empty;

        if (_pool == null)
        {
            DebugLog.Write(LogChannel.Fields, "FieldBag.Release: no pool to return to, dropping bag");
            return;
        }
        _pool.Return(this);
    }
}
