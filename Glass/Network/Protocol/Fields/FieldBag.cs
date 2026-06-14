using Glass.Core;
using Glass.Core.Logging;
using Glass.Core.Memory;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldBag
//
// Holds the decoded fields of one collection instance: an array of FieldSlots followed by
// a byte arena for variable-length payloads, both living in a single BufferPool lease.
// The slot region begins at lease offset zero; the arena region follows it.  Slots are
// accessed by reinterpreting the slot region as a span of FieldSlot.  Arena bytes are
// appended at the cursor and referenced by the offsets recorded in slots.
//
// A reference type: callers hold a reference to one bag and mutate it in place through its
// methods, with no copying.  The bag's storage is the lease it owns; Release returns that
// lease to the pool, after which the bag must not be accessed.
///////////////////////////////////////////////////////////////////////////////////////////////
public sealed class FieldBag
{
    public const uint SlotSizeBytes = 18;

    private BufferLease _lease;
    private uint _arenaOffset;
    private uint _arenaCapacity;
    private uint _arenaCursor;
    private ushort _slotCapacity;
    private ushort _slotsInUse;
    private CollectionHandle _collectionHandle;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Initialize
    //
    // Prepares the bag for one use over the given lease: slots at offset zero, arena
    // immediately after.  The lease must be at least large enough for both regions; a
    // shorter lease is an integrity failure and halts the process via FailFast.  Called each
    // time a bag is created or taken from the free list, since a recycled bag carries the
    // prior use's lease and counts until this resets them.
    //
    // lease:          Pooled storage for the slot and arena regions.  The bag holds the
    //                 lease until Release returns it.
    // collection:     The collection this bag decodes.
    // slotCapacity:   Number of FieldSlots in the slot region.
    // arenaCapacity:  Byte length of the arena region.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Initialize(BufferLease lease, CollectionHandle collection,
        ushort slotCapacity, uint arenaCapacity)
    {
        _lease = lease;
        _arenaOffset = slotCapacity * SlotSizeBytes;
        _arenaCapacity = arenaCapacity;
        _arenaCursor = 0;
        _slotCapacity = slotCapacity;
        _slotsInUse = 0;
        _collectionHandle = collection;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CollectionName
    //
    // The collection name associated with this bag. The bag's accessor methods
    // include this name in any log lines they produce, so type mismatches and other slot
    // read failures are attributable to a specific collection.  Cleared on Release.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string CollectionName
    {
        get 
        {
            PatchLevel patchLevel = GlassContext.CurrentPatchLevel;
            return GlassContext.PatchRegistry.GetCollectionName(patchLevel, _collectionHandle); 
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Slots
    //
    // Returns a span of FieldSlot over the slot region of the lease, reinterpreting the
    // region's bytes in place.  The span covers the full slot capacity; live slots occupy
    // indices [0, _slotsInUse).  Private — all slot access goes through the bag's own
    // methods, which index into this span by ref.
    //
    // The returned span is valid only for the caller's synchronous scope, per the lease's
    // own span rules.
    //
    // Returns:  A span over all slotCapacity FieldSlots in the slot region.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private Span<FieldSlot> Slots()
    {
        return MemoryMarshal.Cast<byte, FieldSlot>(
            _lease.AsSpan().Slice(0, (int)(_slotCapacity * SlotSizeBytes)));
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BytesNeeded
    //
    // Returns the lease size required for a bag with the given slot and arena capacities:
    // the slot region's bytes followed by the arena's.  Callers pass the result to
    // BufferPool.Rent before constructing the bag over the returned lease.
    //
    // slotCapacity:   Number of FieldSlots in the slot region.
    // arenaCapacity:  Byte length of the arena region.
    //
    // Returns:  Total bytes the bag requires from its lease.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static uint BytesNeeded(ushort slotCapacity, uint arenaCapacity)
    {
        return (slotCapacity * SlotSizeBytes) + arenaCapacity;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Clear
    //
    // Resets all populated slots to the Empty state and zeroes the slot and arena cursors.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Clear()
    {
        Span<FieldSlot> slots = Slots();
        for (int slotIndex = 0; slotIndex < _slotsInUse; slotIndex++)
        {
            slots[slotIndex].Clear();
        }
        _slotsInUse = 0;
        _arenaCursor = 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddSlot
    //
    // Returns a ref to the next available slot, advancing the populated-slot count.  The
    // extractor calls SetName and one of the SetX methods on the returned ref to populate
    // the slot.  Exceeding the bag's slot capacity is an integrity failure — capacity is
    // the collection's field count, so an overflow means more slots were added than the
    // collection has fields — and halts the process via FailFast.
    //
    // Returns:  A ref to the newly-allocated slot.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public ref FieldSlot AddSlot()
    {
        if (_slotsInUse >= _slotCapacity)
        {
            string failure = CollectionName + " FieldBag.AddSlot: slot capacity "
                + _slotCapacity + " exceeded";
            DebugLog.Write(LogChannel.Fields, failure);
            Environment.FailFast(failure);
        }

        ref FieldSlot slot = ref Slots()[_slotsInUse];
        slot.Clear();
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
            DebugLog.Write(LogChannel.Fields, CollectionName
                + " FieldBag.InsertIntoArena: empty input, returning cursor "
                + _arenaCursor + " with no insertion");
            return (ushort)_arenaCursor;
        }
        if (_arenaCursor + (uint)bytes.Length > _arenaCapacity)
        {
            string failure = CollectionName
                + " FieldBag.InsertIntoArena: insertion of " + bytes.Length
                + " bytes at cursor " + _arenaCursor
                + " would exceed arena capacity " + _arenaCapacity;
            DebugLog.Write(LogChannel.Fields, failure);
            Environment.FailFast(failure);
        }
        ushort insertionIndex = (ushort)_arenaCursor;
        bytes.CopyTo(_lease.AsSpan().Slice((int)(_arenaOffset + _arenaCursor)));
        _arenaCursor += (uint)bytes.Length;
        return insertionIndex;
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
            string positionFailure = CollectionName
                + " FieldBag.SliceArenaString: offset " + offset
                + " is at or past cursor " + _arenaCursor + ", arena reference is corrupt";
            DebugLog.Write(LogChannel.Fields, positionFailure);
            Environment.FailFast(positionFailure);
        }

        ReadOnlySpan<byte> writtenRegion = _lease.AsReadOnlySpan().Slice(
                    (int)(_arenaOffset + offset), (int)(_arenaCursor - offset));
        int terminatorIndex = writtenRegion.IndexOf((byte)0);
        if (terminatorIndex < 0)
        {
            string terminatorFailure = CollectionName
                + " FieldBag.SliceArenaString: no terminator found from offset " + offset
                + " to cursor " + _arenaCursor + ", arena is corrupt";
            DebugLog.Write(LogChannel.Fields, terminatorFailure);
            Environment.FailFast(terminatorFailure);
        }

        return writtenRegion.Slice(0, terminatorIndex);
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
            DebugLog.Write(LogChannel.Fields, CollectionName + " FieldBag.TryGetSlotRef: slot.Index "
                + slot.Index + " out of range [0, " + _slotsInUse + "), returning null ref");
            return ref Unsafe.NullRef<FieldSlot>();
        }
        return ref Slots()[(int) slot.Index];
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
            string rangeFailure = CollectionName + " FieldBag.GetIntAt: slot.Index "
                + slot.Index + " out of range [0, " + _slotsInUse + ")";
            DebugLog.Write(LogChannel.Fields, rangeFailure);
            Environment.FailFast(rangeFailure);
        }

        ref FieldSlot fieldSlot = ref Slots()[(int)slot.Index];
        int value;
        SlotReadResult result = fieldSlot.TryGetInt(out value);
        if (result != SlotReadResult.Success)
        {
            string readFailure = CollectionName + " FieldBag.GetIntAt: required slot '"
                + fieldSlot.GetName(this) + "' at index " + slot.Index
                + " failed with " + result + ", slot type is " + fieldSlot.Type;
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
            string rangeFailure = CollectionName + " FieldBag.GetUIntAt: slot.Index "
                + slot.Index + " out of range [0, " + _slotsInUse + ")";
            DebugLog.Write(LogChannel.Fields, rangeFailure);
            Environment.FailFast(rangeFailure);
        }
        ref FieldSlot fieldSlot = ref Slots()[(int)slot.Index];
        uint value;
        SlotReadResult result = fieldSlot.TryGetUInt(out value);
        if (result != SlotReadResult.Success)
        {
            string readFailure = CollectionName + " FieldBag.GetUIntAt: required slot '"
                + fieldSlot.GetName(this) + "' at index " + slot.Index
                + " failed with " + result + ", slot type is " + fieldSlot.Type;
            DebugLog.Write(LogChannel.Fields, readFailure);
            // FIXME -- here to keep us from crashing until Tracking fully implemented
            // Environment.FailFast(readFailure);
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
            string rangeFailure = CollectionName + " FieldBag.GetFloatAt: slot.Index "
                + slot.Index + " out of range [0, " + _slotsInUse + ")";
            DebugLog.Write(LogChannel.Fields, rangeFailure);
            Environment.FailFast(rangeFailure);
        }
        ref FieldSlot fieldSlot = ref Slots()[(int)slot.Index];
        float value;
        SlotReadResult result = fieldSlot.TryGetFloat(out value);
        if (result != SlotReadResult.Success)
        {
            string readFailure = CollectionName + " FieldBag.GetFloatAt: required slot '"
                + fieldSlot.GetName(this) + "' at index " + slot.Index
                + " failed with result " + result + ", slot type expected " + fieldSlot.Type;
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
            string rangeFailure = CollectionName + " FieldBag.GetBytesAt: slot.Index "
                + slot.Index + " out of range [0, " + _slotsInUse + ")";
            DebugLog.Write(LogChannel.Fields, rangeFailure);
            Environment.FailFast(rangeFailure);
        }
        ref FieldSlot fieldSlot = ref Slots()[(int) slot.Index];
        ReadOnlySpan<byte> value;
        SlotReadResult result = fieldSlot.TryGetAsciiBytes(this, out value);
        if (result != SlotReadResult.Success)
        {
            string readFailure = CollectionName + " FieldBag.GetBytesAt: required slot '"
                + fieldSlot.GetName(this) + "' at index " + slot.Index
                + " in collection '" + CollectionName + "' failed with " + result
                + ", slot type is " + fieldSlot.Type;
            DebugLog.Write(LogChannel.Fields, readFailure);
            Environment.FailFast(readFailure);
        }
        return value;
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
            DebugLog.Write(LogChannel.Fields, CollectionName + " FieldBag.GetTypeAt: slot.Index "
                + slot.Index + " out of range [0, " + _slotsInUse + "), returning Empty");
            return FieldType.Empty;
        }

        ref FieldSlot fieldSlot = ref Slots()[(int)slot.Index];
        return fieldSlot.Type;
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
        ref FieldSlot fieldSlot = ref Slots()[(int)slot.Index];
        return fieldSlot.Type != FieldType.Empty;
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
    // Returns the next filled slot in Sequence order as a FieldBinding, or null when no
    // filled slots remain.  Selects, among all filled slots, the one with the smallest
    // Sequence strictly greater than the position, then advances the position to that
    // Sequence.  Empty slots are never returned.  Called only by BagWalker.Next.
    //
    // position:  Caller-owned iteration position holding the last-returned slot's Sequence.
    //            Must start below any real Sequence (-1) so the first call visits the
    //            lowest-Sequence filled slot; advanced to the returned slot's Sequence on
    //            each call.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    internal FieldBinding? NextAfter(ref int position)
    {
        Span<FieldSlot> slots = Slots();

        int bestIndex = -1;
        uint bestSequence = 0;

        for (int slotIndex = 0; slotIndex < _slotsInUse; slotIndex++)
        {
            ref FieldSlot candidate = ref slots[slotIndex];
            if (candidate.Type == FieldType.Empty)
            {
                continue;
            }

            uint candidateSequence = candidate.Sequence;
            if ((int)candidateSequence <= position)
            {
                continue;
            }

            if ((bestIndex < 0) || (candidateSequence < bestSequence))
            {
                bestIndex = slotIndex;
                bestSequence = candidateSequence;
            }
        }

        if (bestIndex < 0)
        {
            return null;
        }

        position = (int)bestSequence;

        ref FieldSlot slot = ref slots[bestIndex];
        return new FieldBinding(slot.GetName(this), slot.AsString(this));
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Release
    //
    // Returns the bag to its owning pool.  The bag must not be accessed after this call.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Release()
    {
        _lease.Dispose();
    }
}
