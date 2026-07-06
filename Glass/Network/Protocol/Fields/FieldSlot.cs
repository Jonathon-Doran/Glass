using Glass.Core.Logging;
using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldSlot
//
// One named field value, stored as a 18-byte packed record.  A FieldBag holds an array of
// these.  Numeric payloads live in the 64-bit value field.  Variable-length payloads
// (strings, byte runs) live in the owning bag's arena; the slot holds a 16-bit arena
// offset and carries the byte length in the value field.  The field name's bytes likewise
// live in the arena, referenced by a separate 16-bit offset and an 8-bit length.  The
// slot's Type tag determines how the value field is interpreted.
//
// The slot holds no reference to the arena.  Setters and getters that touch arena-backed
// data take the arena as a span parameter supplied by the owning bag.
//
// On any type mismatch in a getter, the slot logs and returns a default value rather than
// throwing — a misconfigured field definition should not crash the dispatch loop.
///////////////////////////////////////////////////////////////////////////////////////////////
[StructLayout(LayoutKind.Sequential, Pack = 2, Size = 20)]
public struct FieldSlot
{
    public const ushort NoArenaData = 0xFFFF;

    private uint _value;
    private uint _wireBitOffset;
    private uint _wireBitLength;
    private ushort _sequence;
    private ushort _arenaOffset;
    private ushort _nameOffset;
    private byte _nameLength;
    private FieldType _type;
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Type
    //
    // The data type currently stored in the slot.  Empty indicates an unused slot.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public FieldType Type
    {
        get { return _type; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // WireBitOffset
    //
    // Absolute bit offset into the packet payload where this slot's value was read from.
    // Set by the extractor at decode time.  Reported for any consumer that needs the
    // field's on-wire position; in practice the extractor reads it to resolve the start
    // bit of a field anchored relative to this one.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public uint WireBitOffset
    {
        get { return _wireBitOffset; }
        set { _wireBitOffset = value; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // WireBitLength
    //
    // Number of bits this slot's value occupied on the wire.  Set by the extractor at
    // decode time, independent of decoded storage size (GetLength).  Zero for a field that
    // is absent because its predicate did not hold, so a following field anchored on it
    // resolves to the same start bit the absent field would have yielded.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public uint WireBitLength
    {
        get { return _wireBitLength; }
        set { _wireBitLength = value; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Sequence
    //
    // The field's display order within its collection.  Set by the extractor at decode time.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ushort Sequence
    {
        get { return _sequence; }
        set { _sequence = value; }
    }


    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Clear
    //
    // Resets the slot to the Empty state.  Arena offsets are set to the NoArenaData
    // sentinel so a cleared slot can never alias bytes left in the arena by a prior
    // tenant.  Called by FieldBag when a bag is recycled.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Clear()
    {
        _type = FieldType.Empty;
        _value = 0;
        _wireBitOffset = 0;
        _wireBitLength = 0;
        _sequence = 0;
        _arenaOffset = NoArenaData;
        _nameOffset = NoArenaData;
        _nameLength = 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetName
    //
    // Stores the field name's ASCII bytes in the owning bag's arena, null-terminated, and
    // records the arena offset of the first byte in the slot.  The name's identity is the
    // arena offset alone; its extent is the terminator.  Names longer than 255 characters
    // are truncated and logged.  A null or empty name stores nothing and records the
    // NoArenaData sentinel.
    //
    // bag:   The bag that owns this slot; receives the name bytes into its arena.
    // name:  The field name (e.g. "hp", "level"); ASCII expected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void SetName(FieldBag bag, string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            DebugLog.Write(LogChannel.Fields, "FieldSlot.SetName: null or empty name, treating as unnamed");
            _nameOffset = NoArenaData;
            return;
        }

        int characterCount = name.Length;
        if (characterCount > 255)
        {
            DebugLog.Write(LogChannel.Fields, "FieldSlot.SetName: name '" + name
                + "' length " + characterCount + " exceeds 255, truncating");
            characterCount = 255;
        }

        Span<byte> encoded = stackalloc byte[256];
        int byteCount = Encoding.ASCII.GetBytes(name.AsSpan(0, characterCount), encoded);
        encoded[byteCount] = 0;

        _nameOffset = bag.InsertIntoArena(encoded.Slice(0, byteCount + 1));
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetName
    //
    // Returns the slot's field name, resolved from the owning bag's arena by scanning to
    // the name's null terminator, as an ASCII string.  Returns "(unnamed)" if the bag is
    // null or no name has been set.
    //
    // bag:      The bag that owns this slot; supplies the arena bytes.
    //
    // Returns:  The field name, or "(unnamed)" as described above.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public string GetName(FieldBag bag)
    {
        if (_nameOffset == NoArenaData)
        {
            return "(unnamed)";
        }

        ReadOnlySpan<byte> nameBytes = bag.SliceArenaString(_nameOffset);
        return Encoding.ASCII.GetString(nameBytes);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetInt
    //
    // Stores a signed integer in the slot's value field, sign-extended to 64 bits.  The
    // arena offset is set to the NoArenaData sentinel — numeric slots own no arena bytes.
    //
    // value:  The integer to store.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void SetInt(int value)
    {
        _value = (uint) value;
        _arenaOffset = NoArenaData;
        _type = FieldType.Int;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetUInt
    //
    // Stores an unsigned integer in the slot's value field.  The arena offset is set to
    // the NoArenaData sentinel — numeric slots own no arena bytes.
    //
    // value:  The unsigned integer to store.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void SetUInt(uint value)
    {
        _value = value;
        _arenaOffset = NoArenaData;
        _type = FieldType.UInt;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetInt
    //
    // Reads the slot as a 64-bit signed integer.  Accepts slots of type Int or UInt; the
    // stored 64 bits are reinterpreted as signed, so an unsigned value with the high bit
    // set reads back negative.
    //
    // value:    Receives the slot's value on success, zero on type mismatch.
    //
    // Returns:  SlotReadResult.Success on success, SlotReadResult.TypeMismatch if the
    //           slot's type is neither Int nor UInt.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public SlotReadResult TryGetInt(out int value)
    {
        if ((_type != FieldType.UInt) && (_type != FieldType.Int))
        {
            value = 0;
            return SlotReadResult.TypeMismatch;
        }

        value = (int) _value;
        return SlotReadResult.Success;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetUInt
    //
    // Reads the slot as a 64-bit unsigned integer.  Accepts only slots of type UInt.
    //
    // value:    Receives the slot's value on success, zero on type mismatch.
    //
    // Returns:  SlotReadResult.Success on success, SlotReadResult.TypeMismatch if the
    //           slot's type is not UInt.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public SlotReadResult TryGetUInt(out uint value)
    {
        if (_type != FieldType.UInt)
        {
            value = 0;
            return SlotReadResult.TypeMismatch;
        }

        value = _value;
        return SlotReadResult.Success;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetFloat
    //
    // Stores a 32-bit IEEE float in the slot's value field as its raw bit pattern, widened
    // to 64 bits.  The encoding (LE or BE) of the source bytes is the extractor's concern;
    // by the time the value reaches this setter the bits have already been interpreted
    // into a host-order float.  The arena offset is set to the NoArenaData sentinel —
    // numeric slots own no arena bytes.
    //
    // value:  The float to store.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void SetFloat(float value)
    {
        _value = BitConverter.SingleToUInt32Bits(value);
        _arenaOffset = NoArenaData;
        _type = FieldType.Float;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetFloat
    //
    // Reads the slot as a 32-bit IEEE float, reconstructed from the raw bit pattern in the
    // low 32 bits of the value field.  Accepts only slots of type Float.
    //
    // value:    Receives the slot's value on success, zero on type mismatch.
    //
    // Returns:  SlotReadResult.Success on success, SlotReadResult.TypeMismatch if the
    //           slot's type is not Float.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public SlotReadResult TryGetFloat(out float value)
    {
        if (_type != FieldType.Float)
        {
            value = 0;
            return SlotReadResult.TypeMismatch;
        }

        value = BitConverter.UInt32BitsToSingle(_value);
        return SlotReadResult.Success;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetBlob
    //
    // Stores raw blob bytes in the owning bag's arena and records the arena offset in the slot.
    // The byte count is stored in the value field.  An empty input stores nothing and records
    // the NoArenaData sentinel.
    //
    // bag:    The bag that owns this slot; receives the blob bytes into its arena.
    // bytes:  The raw bytes to store.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void SetBlob(FieldBag bag, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            DebugLog.Write(LogChannel.Fields, "FieldSlot.SetBlob: zero-length blob, storing NoArenaData", LogLevel.Trace);
            _arenaOffset = NoArenaData;
            _value = 0u;
            _type = FieldType.Blob;
            return;
        }

        _arenaOffset = bag.InsertIntoArena(bytes);
        _value = (uint)bytes.Length;
        _type = FieldType.Blob;

        DebugLog.Write(LogChannel.Fields, "FieldSlot.SetBlob: stored " + bytes.Length
            + " bytes at arena offset " + _arenaOffset, LogLevel.Trace);
    }
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetBlobBytes
    //
    // Returns a read-only span over the slot's raw blob bytes, resolved from the owning bag's
    // arena.  The byte count is carried in the value field.
    //
    // bag:    The bag that owns this slot; supplies the arena bytes.
    // value:  Receives the span on success, an empty span on failure.
    //
    // Returns:  SlotReadResult.Success on success, SlotReadResult.TypeMismatch if the slot's
    //           type is not Blob, SlotReadResult.EmptyPayload if no bytes were stored.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public SlotReadResult TryGetBlobBytes(FieldBag bag, out ReadOnlySpan<byte> value)
    {
        if (_type != FieldType.Blob)
        {
            value = ReadOnlySpan<byte>.Empty;
            return SlotReadResult.TypeMismatch;
        }

        if (_arenaOffset == NoArenaData)
        {
            DebugLog.Write(LogChannel.Fields, "FieldSlot.TryGetBlobBytes: " + GetName(bag)
                + " has no stored bytes, returning empty span", LogLevel.Warn);
            value = ReadOnlySpan<byte>.Empty;
            return SlotReadResult.EmptyPayload;
        }

        value = bag.SliceArenaBytes(_arenaOffset, _value);
        return SlotReadResult.Success;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetGate
    //
    // Stores a GateHandle in the slot's value field as its raw uint.  The arena offset is
    // set to the NoArenaData sentinel — a gate slot owns no arena bytes.  The handle
    // addresses the gate whose instance bags this field decoded through.
    //
    // value:  The gate handle to store.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void SetGate(GateHandle value)
    {
        _value = (uint)value;
        _arenaOffset = NoArenaData;
        _type = FieldType.Gate;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetGate
    //
    // Reads the slot as a GateHandle, reconstructed from the raw uint in the value field.
    // Accepts only slots of type Gate.
    //
    // value:    Receives the slot's gate handle on success, GateHandle.None on type
    //           mismatch.
    //
    // Returns:  SlotReadResult.Success on success, SlotReadResult.TypeMismatch if the
    //           slot's type is not Gate.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public SlotReadResult TryGetGate(out GateHandle value)
    {
        if (_type != FieldType.Gate)
        {
            value = GateHandle.None;
            return SlotReadResult.TypeMismatch;
        }
        value = (GateHandle)_value;
        return SlotReadResult.Success;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetAsciiString
    //
    // Stores ASCII string bytes in the owning bag's arena, guaranteed null-terminated, and
    // records the arena offset of the first byte in the slot.  If the given bytes do not
    // already end with a null, a single null byte is appended in the arena immediately
    // after them.  The string's identity is the arena offset alone; the value field is not
    // touched.  An empty input stores just a terminator, so an empty string occupies one
    // arena byte at a real offset, distinguishable from an absent field.
    //
    // bag:    The bag that owns this slot; receives the string bytes into its arena.
    // bytes:  The ASCII bytes to store, with or without a trailing null.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void SetAsciiString(FieldBag bag, ReadOnlySpan<byte> bytes)
    {
        bool alreadyTerminated = (bytes.Length > 0) && (bytes[bytes.Length - 1] == 0);

        if (alreadyTerminated == true)
        {
            _arenaOffset = bag.InsertIntoArena(bytes);
        }
        else
        {
            Span<byte> terminator = stackalloc byte[1];
            terminator[0] = 0;
            if (bytes.Length > 0)
            {
                _arenaOffset = bag.InsertIntoArena(bytes);
                bag.InsertIntoArena(terminator);
            }
            else
            {
                _arenaOffset = bag.InsertIntoArena(terminator);
            }
        }

        _type = FieldType.AsciiString;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetAsciiBytes
    //
    // Returns a read-only span over the slot's ASCII string bytes, resolved from the
    // owning bag's arena by scanning to the string's null terminator, which is excluded
    // from the span.  A stored empty string yields Success with an empty span.  
    //
    // The returned span is valid only until the bag is cleared or released; after that the
    // arena bytes may belong to a new tenant.
    //
    // bag:      The bag that owns this slot; supplies the arena bytes.
    // value:    Receives the span on success, an empty span otherwise.
    //
    // Returns:  SlotReadResult.Success on success, SlotReadResult.TypeMismatch if the
    //           slot's type is not AsciiString or the bag is null,
    //           SlotReadResult.EmptyPayload if no string was ever stored in the slot.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public SlotReadResult TryGetAsciiBytes(FieldBag bag, out ReadOnlySpan<byte> value)
    {
        if (_type != FieldType.AsciiString)
        {
            value = ReadOnlySpan<byte>.Empty;
            return SlotReadResult.TypeMismatch;
        }

        if (_arenaOffset == NoArenaData)
        {
            DebugLog.Write(LogChannel.Fields,
                "FieldSlot.TryGetAsciiBytes: " + GetName(bag)
                + " has no stored string, returning empty span");
            value = ReadOnlySpan<byte>.Empty;
            return SlotReadResult.EmptyPayload;
        }

        value = bag.SliceArenaString(_arenaOffset);
        return SlotReadResult.Success;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetLength
    //
    // Returns the decoded payload length in bytes for this slot.  Numeric types (Int,
    // UInt, Float) report 4, the decoded width of a 32-bit value.  AsciiString reports
    // the stored byte count including the null terminator, resolved by scanning the
    // string in the owning bag's arena.  An AsciiString slot with no stored string
    // reports zero.  Empty slots report zero.
    //
    // bag:      The bag that owns this slot; supplies the arena bytes for string slots.
    //
    // Returns:  Payload length in bytes.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public uint GetLength(FieldBag bag)
    {
        switch (_type)
        {
            case FieldType.Int:
            case FieldType.UInt:
            case FieldType.Float:
                return 4;

            case FieldType.AsciiString:
                {
                    if (_arenaOffset == NoArenaData)
                    {
                        DebugLog.Write(LogChannel.Fields, "FieldSlot.GetLength: " + GetName(bag)
                            + " has no stored string, returning 0");
                        return 0;
                    }

                    return (uint)bag.SliceArenaString(_arenaOffset).Length + 1u;
                }

            default:
                return 0;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetByteRange
    //
    // Returns the half-open byte range [start, end) of whole payload bytes that enclose this
    // slot's wire bits, computed from WireBitOffset and WireBitLength.  A slot with zero wire
    // bit length yields an empty range whose start equals its end.
    //
    // Returns:  The enclosing byte range.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ByteRange GetByteRange()
    {
        if (_wireBitLength == 0)
        {
            uint emptyStart = _wireBitOffset / 8u;
            DebugLog.Write(LogChannel.Fields,
                "FieldSlot.GetWireByteRange: zero wire bit length at bit offset " + _wireBitOffset
                + ", returning empty range at byte " + emptyStart, LogLevel.Trace);
            return new ByteRange(emptyStart, emptyStart);
        }

        uint startByte = _wireBitOffset / 8u;
        uint endBitExclusive = _wireBitOffset + _wireBitLength;
        uint endByteExclusive = (endBitExclusive + 7u) / 8u;

        DebugLog.Write(LogChannel.Fields,
            "FieldSlot.GetWireByteRange: bitOffset=" + _wireBitOffset + " bitLength=" + _wireBitLength
            + " -> bytes [" + startByte + ", " + endByteExclusive + ")", LogLevel.Trace);

        return new ByteRange(startByte, endByteExclusive);
    }

    public void debugDump()
    {
        DebugLog.Write(LogChannel.Fields, "value = 0x" + _value.ToString("x8"));
        DebugLog.Write(LogChannel.Fields, "type = " + _type);
        DebugLog.Write(LogChannel.Fields, "nameOffset = " + _nameOffset + ", length = " + _nameLength);
        DebugLog.Write(LogChannel.Fields, "arenaOffset = " + _arenaOffset);
        DebugLog.Write(LogChannel.Fields, "wireBitOffset = " + _wireBitOffset + ", length = " + _wireBitLength);
    }
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AsString
    //
    // Returns the slot's value formatted as a string according to its current type.
    // Integer types render in hex with a "0x" prefix and eight-digit zero padding,
    // followed by the decimal value in parentheses.  Float renders as decimal with three
    // digits after the radix.  AsciiString renders as the stored characters, resolved
    // from the owning bag's arena; an empty stored string renders as an empty string.
    // Any read failure or unconvertible type is logged and renders as an empty string.
    //
    // bag:      The bag that owns this slot; supplies the arena bytes for the name and
    //           for AsciiString values.
    //
    // Returns:  The formatted value, or an empty string on any failure described above.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    internal string AsString(FieldBag bag)
    {
        switch (_type)
        {
            case FieldType.Int:
                {
                    int value;
                    SlotReadResult result = TryGetInt(out value);
                    if (result == SlotReadResult.Success)
                    {
                        return "0x" + value.ToString("X8") + " (" + value + ")";
                    }
                    DebugLog.Write(LogChannel.Fields, "FieldSlot.AsString: " + GetName(bag)
                        + " Int read failed: " + result);
                    return string.Empty;
                }

            case FieldType.UInt:
                {
                    uint value;
                    SlotReadResult result = TryGetUInt(out value);
                    if (result == SlotReadResult.Success)
                    {
                        return "0x" + value.ToString("X8") + " (" + value + ")";
                    }
                    DebugLog.Write(LogChannel.Fields, "FieldSlot.AsString: " + GetName(bag)
                        + " UInt read failed: " + result);
                    return string.Empty;
                }

            case FieldType.Float:
                {
                    float value;
                    SlotReadResult result = TryGetFloat(out value);
                    if (result == SlotReadResult.Success)
                    {
                        return value.ToString("F3");
                    }
                    DebugLog.Write(LogChannel.Fields, "FieldSlot.AsString: " + GetName(bag)
                        + " Float read failed: " + result);
                    return string.Empty;
                }

            case FieldType.AsciiString:
                {
                    ReadOnlySpan<byte> bytes;
                    SlotReadResult result = TryGetAsciiBytes(bag, out bytes);
                    if (result == SlotReadResult.Success)
                    {
                        return Encoding.ASCII.GetString(bytes);
                    }
                    if (result == SlotReadResult.EmptyPayload)
                    {
                        return string.Empty;
                    }
                    DebugLog.Write(LogChannel.Fields, "FieldSlot.AsString: " + GetName(bag)
                        + " AsciiString read failed: " + result);
                    return string.Empty;
                }
            case FieldType.Blob:
                {
                    ReadOnlySpan<byte> bytes;
                    SlotReadResult result = TryGetBlobBytes(bag, out bytes);
                    if (result == SlotReadResult.Success)
                    {
                        return SoeHexDump.Format(bytes);
                    }
                    if (result == SlotReadResult.EmptyPayload)
                    {
                        return string.Empty;
                    }
                    DebugLog.Write(LogChannel.Fields, "FieldSlot.AsString: " + GetName(bag)
                        + " Blob read failed: " + result, LogLevel.Warn);
                    return string.Empty;
                }
            default:
                {
                    DebugLog.Write(LogChannel.Fields, "FieldSlot.AsString: " + GetName(bag)
                        + " type " + _type + " has no string conversion");
                    return string.Empty;
                }
        }
    }
}
