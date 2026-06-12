using Glass.Core.Logging;
using Glass.Core;
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
    public const int DefaultSlotCount = 500;
    public const int DefaultPoolSize = 64;
    public const uint ArenaCapacity = 65535;

    private readonly FieldSlot[] _slots;
    private readonly byte[] _arena;
    private uint _arenaCursor;
    private readonly FieldBagPool _pool;
    private uint _slotsInUse;
    private string _currentOpcodeName = string.Empty;
    private readonly int[] _displayOrder;
    private uint _displayOrderCount;

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
        _arena = new byte[ArenaCapacity];
        _arenaCursor = 0;
        _pool = pool;
        _slotsInUse = 0;
        _displayOrder = new int[capacity];
        _displayOrderCount = 0;
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
    // SlotsInUse
    //
    // The number of slots currently populated.  Slots at indices [0, SlotsInUse) are live;
    // slots at [SlotsInUse, Capacity) are unused.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public uint SlotsInUse
    {
        get { return _slotsInUse; }
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
        for (int slotIndex = 0; slotIndex < _slotsInUse; slotIndex++)
        {
            _slots[slotIndex].Clear();
        }
        _slotsInUse = 0;
        _displayOrderCount = 0;
        _arenaCursor = 0;
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
        if (_slotsInUse >= _slots.Length)
        {
            DebugLog.Write(LogChannel.Fields, "FieldBag.AddSlot: capacity " + _slots.Length
                + " exceeded; returning ref to last slot, field will be overwritten");
            return ref _slots[_slots.Length - 1];
        }

        ref FieldSlot slot = ref _slots[_slotsInUse];
        _slotsInUse++;
        return ref slot;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // InsertIntoArena
    //
    // Copies the given bytes into the bag's arena at the current cursor and returns the
    // arena index where they begin, advancing the cursor past them.  The arena stores raw
    // concatenated bytes with no prefixes or terminators; the caller is responsible for
    // recording the returned index and the byte count.  An empty input inserts nothing and
    // returns the current cursor position.
    //
    // A request that would advance the cursor past ArenaCapacity is an integrity failure:
    // the condition is logged with the request size and cursor position, then the process
    // is brought down via Environment.FailFast.
    //
    // bytes:    The bytes to copy into the arena.
    //
    // Returns:  The arena index of the first copied byte.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    internal ushort InsertIntoArena(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            DebugLog.Write(LogChannel.Fields, _currentOpcodeName
                + " FieldBag.InsertIntoArena: empty input, returning cursor "
                + _arenaCursor + " with no insertion");
            return (ushort)_arenaCursor;
        }

        if (_arenaCursor + (uint)bytes.Length > ArenaCapacity)
        {
            string failure = _currentOpcodeName
                + " FieldBag.InsertIntoArena: insertion of " + bytes.Length
                + " bytes at cursor " + _arenaCursor
                + " would exceed ArenaCapacity " + ArenaCapacity;
            DebugLog.Write(LogChannel.Fields, failure);
            Environment.FailFast(failure);
        }

        ushort insertionIndex = (ushort)_arenaCursor;
        bytes.CopyTo(_arena.AsSpan((int)_arenaCursor));
        _arenaCursor += (uint)bytes.Length;
        return insertionIndex;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SliceArena
    //
    // Returns a read-only span over the given range of the bag's arena.  A valid range
    // lies entirely within the written region [0, cursor).  A range that starts at the
    // NoArenaData sentinel, has zero length, or extends past the cursor is logged and
    // yields an empty span.
    //
    // The returned span is valid only until the bag is cleared or released; after that the
    // arena bytes may belong to a new tenant.
    //
    // offset:   Arena index of the first byte of the range.
    // length:   Number of bytes in the range.
    //
    // Returns:  A read-only span over the range, or an empty span on any invalid range.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    internal ReadOnlySpan<byte> SliceArena(ushort offset, uint length)
    {
        if (offset == FieldSlot.NoArenaData)
        {
            DebugLog.Write(LogChannel.Fields, _currentOpcodeName
                + " FieldBag.SliceArena: offset is the NoArenaData sentinel, returning empty span");
            return ReadOnlySpan<byte>.Empty;
        }

        if (length == 0)
        {
            DebugLog.Write(LogChannel.Fields, _currentOpcodeName
                + " FieldBag.SliceArena: zero length at offset " + offset
                + ", returning empty span");
            return ReadOnlySpan<byte>.Empty;
        }

        if ((ulong) offset + length > _arenaCursor)
        {
            DebugLog.Write(LogChannel.Fields, _currentOpcodeName
                + " FieldBag.SliceArena: range [" + offset + ", " + (offset + length)
                + ") extends past cursor " + _arenaCursor + ", returning empty span");
            return ReadOnlySpan<byte>.Empty;
        }

        return new ReadOnlySpan<byte>(_arena, offset, (int)length);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SliceArenaString
    //
    // Returns a read-only span over the null-terminated string beginning at the given
    // arena offset, with the terminator excluded.  The string's extent is found by
    // scanning for the null byte within the written region [offset, cursor).  A
    // zero-length result is a stored empty string (the terminator is the first byte).
    //
    // An offset at or past the cursor, or a scan that reaches the cursor without finding
    // a terminator, is an integrity failure: every string stored in the arena is
    // guaranteed null-terminated, so a missing terminator means the offset is wrong or
    // the arena is corrupt.  The condition is logged with the offset and cursor, then the
    // process is brought down via Environment.FailFast.
    //
    // offset:   Arena index of the first byte of the string.
    //
    // Returns:  A read-only span over the string's bytes, terminator excluded; empty for
    //           a stored empty string.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    internal ReadOnlySpan<byte> SliceArenaString(ushort offset)
    {
        if (offset >= _arenaCursor)
        {
            string positionFailure = _currentOpcodeName
                + " FieldBag.SliceArenaString: offset " + offset
                + " is at or past cursor " + _arenaCursor + ", arena reference is corrupt";
            DebugLog.Write(LogChannel.Fields, positionFailure);
            Environment.FailFast(positionFailure);
        }

        ReadOnlySpan<byte> writtenRegion = new ReadOnlySpan<byte>(_arena, offset,
            (int)(_arenaCursor - offset));
        int terminatorIndex = writtenRegion.IndexOf((byte)0);
        if (terminatorIndex < 0)
        {
            string terminatorFailure = _currentOpcodeName
                + " FieldBag.SliceArenaString: no terminator found from offset " + offset
                + " to cursor " + _arenaCursor + ", arena is corrupt";
            DebugLog.Write(LogChannel.Fields, terminatorFailure);
            Environment.FailFast(terminatorFailure);
        }

        return writtenRegion.Slice(0, terminatorIndex);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // BuildDisplayOrder
    //
    // Fills the bag's display-order map with the live slot indices ordered by each slot's
    // Sequence, ascending, stable so slots with equal Sequence keep their physical order.  Called
    // once after extraction has finished filling the bag, before the bag is walked.  NextAfter
    // walks this map so iteration visits slots in Sequence order rather than physical order.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void BuildDisplayOrder()
    {
        for (int slotIndex = 0; slotIndex < _slotsInUse; slotIndex++)
        {
            _displayOrder[slotIndex] = slotIndex;
        }
        _displayOrderCount = _slotsInUse;

        for (int sortedIndex = 1; sortedIndex < _displayOrderCount; sortedIndex++)
        {
            int currentSlot = _displayOrder[sortedIndex];
            uint currentSequence = _slots[currentSlot].Sequence;
            int scanIndex = sortedIndex - 1;

            while (scanIndex >= 0 && _slots[_displayOrder[scanIndex]].Sequence > currentSequence)
            {
                _displayOrder[scanIndex + 1] = _displayOrder[scanIndex];
                scanIndex = scanIndex - 1;
            }

            _displayOrder[scanIndex + 1] = currentSlot;
        }
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
    //   slot.Index  - The cached index of the slot.
    //
    // Returns:
    //   A ref to the slot, or a null ref if slot.Index is out of range.  Check with
    //   Unsafe.IsNullRef(ref result) before use.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ref FieldSlot TryGetSlotRef(SlotId slot)
    {
        if (slot.Index >= _slotsInUse)
        {
            DebugLog.Write(LogChannel.Fields, _currentOpcodeName + " FieldBag.TryGetSlotRef: slot.Index "
                + slot.Index + " out of range [0, " + _slotsInUse + "), returning null ref");
            return ref Unsafe.NullRef<FieldSlot>();
        }
        return ref _slots[slot.Index];
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetIntAt
    //
    // Reads the slot at the given index as a 32-bit signed integer.  The slot is required:
    // any failure to read it is a schema or extraction integrity violation and halts the
    // process via FailFast with the failure details preserved in the Fields log channel.
    //
    // slot:     The slot identifier; slot.Index is typically resolved via
    //           FieldDefinitionExtensions.IndexOfField at handler construction.
    //
    // Returns:  The slot's value.  Does not return on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public int GetIntAt(SlotId slot)
    {
        if (slot.Index >= _slotsInUse)
        {
            string rangeFailure = _currentOpcodeName + " FieldBag.GetIntAt: slot.Index "
                + slot.Index + " out of range [0, " + _slotsInUse + ")";
            DebugLog.Write(LogChannel.Fields, rangeFailure);
            Environment.FailFast(rangeFailure);
        }
        int value;
        SlotReadResult result = _slots[slot.Index].TryGetInt(out value);
        if (result != SlotReadResult.Success)
        {
            string readFailure = _currentOpcodeName + " FieldBag.GetIntAt: required slot '"
                + _slots[slot.Index].GetName(this) + "' at index " + slot.Index
                + " failed with " + result + ", slot type is " + _slots[slot.Index].Type;
            DebugLog.Write(LogChannel.Fields, readFailure);
            Environment.FailFast(readFailure);
        }
        return value;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetUIntAt
    //
    // Reads the slot at the given index as a 32-bit unsigned integer.  The slot is required:
    // an out-of-range index or a failed slot-level read is a schema or extraction integrity
    // violation and halts the process via FailFast with the failure details preserved in the
    // Fields log channel.
    //
    // slot:     The slot identifier; slot.Index is typically resolved via
    //           FieldDefinitionExtensions.IndexOfField at handler construction.
    //
    // Returns:  The slot's value.  Does not return on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public uint GetUIntAt(SlotId slot)
    {
        if (slot.Index >= _slotsInUse)
        {
            string rangeFailure = _currentOpcodeName + " FieldBag.GetUIntAt: slot.Index "
                + slot.Index + " out of range [0, " + _slotsInUse + ")";
            DebugLog.Write(LogChannel.Fields, rangeFailure);
            Environment.FailFast(rangeFailure);
        }
        uint value;
        SlotReadResult result = _slots[slot.Index].TryGetUInt(out value);
        if (result != SlotReadResult.Success)
        {
            string readFailure = _currentOpcodeName + " FieldBag.GetUIntAt: required slot '"
                + _slots[slot.Index].GetName(this) + "' at index " + slot.Index
                + " failed with " + result + ", slot type is " + _slots[slot.Index].Type;
            DebugLog.Write(LogChannel.Fields, readFailure);
            Environment.FailFast(readFailure);
        }
        return value;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetFloatAt
    //
    // Reads the slot at the given index as a 32-bit float.  The slot is required: an
    // out-of-range index or a failed slot-level read is a schema or extraction integrity
    // violation and halts the process via FailFast with the failure details preserved in the
    // Fields log channel.
    //
    // slot:     The slot identifier; slot.Index is typically resolved via
    //           FieldDefinitionExtensions.IndexOfField at handler construction.
    //
    // Returns:  The slot's value.  Does not return on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public float GetFloatAt(SlotId slot)
    {
        if (slot.Index >= _slotsInUse)
        {
            string rangeFailure = _currentOpcodeName + " FieldBag.GetFloatAt: slot.Index "
                + slot.Index + " out of range [0, " + _slotsInUse + ")";
            DebugLog.Write(LogChannel.Fields, rangeFailure);
            Environment.FailFast(rangeFailure);
        }
        float value;
        SlotReadResult result = _slots[slot.Index].TryGetFloat(out value);
        if (result != SlotReadResult.Success)
        {
            string readFailure = _currentOpcodeName + " FieldBag.GetFloatAt: required slot '"
                + _slots[slot.Index].GetName(this) + "' at index " + slot.Index
                + " failed with " + result + ", slot type is " + _slots[slot.Index].Type;
            DebugLog.Write(LogChannel.Fields, readFailure);
            Environment.FailFast(readFailure);
        }
        return value;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetBytesAt
    //
    // Reads the slot at the given index as a span over its ASCII string bytes, resolved
    // from this bag's arena.  The slot is required: an out-of-range index or a failed
    // slot-level read is a schema or extraction integrity violation and halts the process
    // via FailFast with the failure details preserved in the Fields log channel.
    //
    // The returned span is valid only until this bag is cleared or released; after that
    // the arena bytes may belong to a new tenant.
    //
    // slot:     The slot identifier carrying the index of the slot to read.
    //
    // Returns:  The span over the slot's string bytes.  Does not return on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ReadOnlySpan<byte> GetBytesAt(SlotId slot)
    {
        if (slot.Index >= _slotsInUse)
        {
            string rangeFailure = _currentOpcodeName + " FieldBag.GetBytesAt: slot.Index "
                + slot.Index + " out of range [0, " + _slotsInUse + ")";
            DebugLog.Write(LogChannel.Fields, rangeFailure);
            Environment.FailFast(rangeFailure);
        }
        ReadOnlySpan<byte> value;
        SlotReadResult result = _slots[slot.Index].TryGetAsciiBytes(this, out value);
        if (result != SlotReadResult.Success)
        {
            string readFailure = _currentOpcodeName + " FieldBag.GetBytesAt: required slot '"
                + _slots[slot.Index].GetName(this) + "' at index " + slot.Index
                + " in collection '" + GetCollectionForSlot(slot) + "' failed with " + result
                + ", slot type is " + _slots[slot.Index].Type;
            DebugLog.Write(LogChannel.Fields, readFailure);
            Environment.FailFast(readFailure);
        }
        return value;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetLengthAt
    //
    // Returns the stored byte count of the slot at the given index.
    // AsciiString slots report their exact stored byte count with no terminator
    // included; all other slot types report zero.
    //
    // slot:     The slot identifier carrying the index of the slot to query.
    //
    // Returns:  Stored byte count for arena-backed slots; zero for all other types or if
    //           the index is out of range.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public uint GetLengthAt(SlotId slot)
    {
        if (slot.Index >= _slotsInUse)
        {
            DebugLog.Write(LogChannel.Fields, _currentOpcodeName + " FieldBag.GetLengthAt: slot.Index "
                + slot.Index + " out of range [0, " + _slotsInUse + "), returning 0");
            return 0;
        }

        CollectionHandle collectionHandle = slot.Collection;
        PatchLevel patchLevel = GlassContext.CurrentPatchLevel;
        string name = GlassContext.PatchRegistry.GetCollectionName(patchLevel,  collectionHandle);

        return _slots[slot.Index].GetLength(this);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetTypeAt
    //
    // Returns the FieldType of the slot at the given index.  Logs and returns FieldType.Empty
    // if the index is out of range.  Used by callers that must read a slot in its own type —
    // for example, predicate evaluation, which reads a signed source field as signed and an
    // unsigned source field as unsigned so the comparison preserves the field's sign.
    //
    // slot:  The slot to query
    //
    // Returns:  The slot's FieldType, or FieldType.Empty if the index is out of range.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public FieldType GetTypeAt(SlotId slot)
    {
        if (slot.Index >= _slotsInUse)
        {
            DebugLog.Write(LogChannel.Fields, _currentOpcodeName + " FieldBag.GetTypeAt: slot.Index "
                + slot.Index + " out of range [0, " + _slotsInUse + "), returning Empty");
            return FieldType.Empty;
        }
        return _slots[slot.Index].Type;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetCollectionForSlot
    //
    // Returns the name of the collection containing the specified slot.
    //
    // slot:  The ID of the slot to query
    //
    // Returns:  The slot's name
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string GetCollectionForSlot(SlotId slot)
    {
        CollectionHandle collectionHandle = slot.Collection;
        PatchLevel patchLevel = GlassContext.CurrentPatchLevel;
        string name = GlassContext.PatchRegistry.GetCollectionName(patchLevel, collectionHandle);
        return name;
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
    //   slot.Index  - The cached index of the field, typically resolved via
    //                FieldDefinitionExtensions.IndexOfField at handler construction.  A
    //                value of -1 (the "field not found" sentinel) returns false.
    //
    // Returns:
    //   true   - The slot holds a value.
    //   false  - The slot is Empty, or the index is out of range.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool IsPresent(SlotId slot)
    {
        if (slot.Index >= _slotsInUse)
        {
            return false;
        }
        return _slots[slot.Index].Type != FieldType.Empty;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Walk
    //
    // Returns a fresh BagWalker positioned at the start of the bag.  Multiple walkers may
    // exist over the same bag at the same time; each carries its own position.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public BagWalker Walk()
    {
        return new BagWalker(this);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // NextAfter
    //
    // Advances the caller-owned position past any Empty slots and returns the next filled slot as
    // a FieldBinding, or null when no more filled slots remain.  Visits slots in the order set by
    // BuildDisplayOrder.  Called only by BagWalker.Next.
    //
    // position:  Caller-owned iteration position, advanced as slots are consumed.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    internal FieldBinding? NextAfter(ref int position)
    {
        while (position < _displayOrderCount)
        {
            int slotIndex = _displayOrder[position];
            position++;

            ref FieldSlot slot = ref _slots[slotIndex];

            if (slot.Type == FieldType.Empty)
            {
                continue;
            }

            return new FieldBinding(slot.GetName(this), slot.AsString(this));
        }

        return null;
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
