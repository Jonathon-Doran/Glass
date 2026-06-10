using Glass.Core.Logging;
using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// NameBuffer32
//
// Inline 32-byte buffer used to hold a field name without heap allocation.
// Exists only because [InlineArray] requires a wrapper struct with exactly one field.
// Accessed via implicit Span<byte> / ReadOnlySpan<byte> conversions.
///////////////////////////////////////////////////////////////////////////////////////////////
[InlineArray(32)]
public struct NameBuffer32
{
    private byte _element0;
}

///////////////////////////////////////////////////////////////////////////////////////////////
// PayloadBuffer80
//
// Inline 80-byte buffer used to hold a field's value without heap allocation.
// Numeric values occupy the first N bytes; string and byte values use up to all 80.
// Exists only because [InlineArray] requires a wrapper struct with exactly one field.
///////////////////////////////////////////////////////////////////////////////////////////////
[InlineArray(80)]
public struct PayloadBuffer80
{
    private byte _element0;
}

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldSlot
//
// One named field value, stored inline with no heap allocation.  A FieldBag holds an
// array of these.  The slot's Type tag determines how the payload bytes are interpreted.
//
// The setter methods (SetName, SetInt, etc.) are used by the extractor when filling a
// bag.  The getter methods (GetInt32, GetAsciiSpan, etc.) are called by FieldBag during
// handler-driven lookups.  Handlers do not access slots directly.
//
// On any type mismatch in a getter, the slot logs and returns a default value rather than
// throwing — a misconfigured field definition should not crash the dispatch loop.
///////////////////////////////////////////////////////////////////////////////////////////////
public struct FieldSlot
{
    public const int NameCapacity = 32;
    public const int PayloadCapacity = 80;

    private NameBuffer32 _name;
    private PayloadBuffer80 _payload;
    private byte _nameLength;
    private uint _payloadLength;
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
        get;
        set;
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
        get;
        set;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Sequence
    //
    // The field's display order within its collection.  Set by the extractor at decode time.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public uint Sequence
    {
        get;
        set;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Clear
    //
    // Resets the slot to the Empty state.  Does not zero the inline buffers — the type tag
    // and length fields are sufficient to mark the slot unused.  Called by FieldBag when
    // returning a bag to the pool.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Clear()
    {
        _type = FieldType.Empty;
        _nameLength = 0;
        _payloadLength = 0;
        WireBitLength = 0;
        WireBitOffset = 0;
        Sequence = 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetName
    //
    // Stores the field name in the inline name buffer.  Names longer than NameCapacity are
    // truncated and logged — field-name truncation is a configuration bug worth surfacing.
    //
    // name:  The field name (e.g. "hp", "level"); ASCII expected.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void SetName(string name)
    {
        if (name == null)
        {
            DebugLog.Write(LogChannel.Fields, "FieldSlot.SetName: null name, treating as empty");
            _nameLength = 0;
            return;
        }

        int byteCount = Encoding.ASCII.GetByteCount(name);
        if (byteCount > NameCapacity)
        {
            DebugLog.Write(LogChannel.Fields, "FieldSlot.SetName: name '" + name
                + "' length " + byteCount + " exceeds NameCapacity " + NameCapacity
                + ", truncating");
            byteCount = NameCapacity;
        }

        Span<byte> destination = _name;
        Encoding.ASCII.GetBytes(name.AsSpan(0, Math.Min(name.Length, byteCount)), destination);
        _nameLength = (byte)byteCount;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CopyAsciiBytesTo
    //
    // Copies the slot's ASCII string bytes into the caller-provided destination buffer,
    // with the trailing null terminator excluded.  The caller owns the destination; the
    // slot holds no reference to it after the copy.  Logs and returns 0 if the slot's
    // type is not AsciiString or if the destination is too small to hold the visible bytes.
    //
    // destination:  Caller-owned buffer to receive the bytes; must be at least
    //               (PayloadLength - 1) bytes.
    //
    // Returns:      The number of bytes copied, or 0 on type mismatch or undersized destination.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public int CopyAsciiBytesTo(Span<byte> destination)
    {
        if (_type != FieldType.AsciiString)
        {
            DebugLog.Write(LogChannel.Fields, "FieldSlot.CopyAsciiBytesTo: type mismatch, slot type is "
                + _type + ", returning 0");
            return 0;
        }

        if (_payloadLength == 0)
        {
            DebugLog.Write(LogChannel.Fields, "FieldSlot.CopyAsciiBytesTo: " + GetName()
                + " payload length is zero, returning 0");
            return 0;
        }

        uint visibleLength = _payloadLength - 1;
        if (destination.Length < visibleLength)
        {
            DebugLog.Write(LogChannel.Fields, "FieldSlot.CopyAsciiBytesTo: " + GetName() + " destination length "
                + destination.Length + " is smaller than visible length " + visibleLength
                + ", returning 0");
            return 0;
        }

        Span<byte> source = _payload;
        source.Slice(0, (int) visibleLength).CopyTo(destination);
        return (int) visibleLength;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetInt
    //
    // Stores a 32-bit signed integer in machine byte order in the payload buffer.
    //
    // value:  The integer to store.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void SetInt(int value)
    {
        Span<byte> destination = _payload;
        BinaryPrimitives.WriteInt32LittleEndian(destination, value);
        _payloadLength = 4;
        _type = FieldType.Int;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetUInt
    //
    // Stores a 32-bit unsigned integer in the payload buffer.
    //
    // value:  The unsigned integer to store.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void SetUInt(uint value)
    {
        Span<byte> destination = _payload;
        BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
        _payloadLength = 4;
        _type = FieldType.UInt;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetAsciiString
    //
    // Stores ASCII string bytes in the payload buffer, followed by a single null terminator.
    // _payloadLength counts the stored bytes including the null, so it reflects the on-wire
    // byte count for a null-terminated string field.  Consumer-facing getters slice the
    // null off; only the extractor reads the full length (via GetLength) when computing
    // anchor end positions for relative-anchored fields.
    //
    // Source longer than (PayloadCapacity - 1) are truncated and the truncation is logged.
    //
    // Parameters:
    //   bytes:  The ASCII bytes to store; should not include a trailing null — this
    //           method appends one.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void SetAsciiString(ReadOnlySpan<byte> bytes)
    {
        int maxPayload = PayloadCapacity - 1;
        int copyCount = bytes.Length;
        if (copyCount > maxPayload)
        {
            DebugLog.Write(LogChannel.Fields, "FieldSlot.SetAsciiString: " + GetName() + " length " + copyCount
                + " exceeds max payload " + maxPayload + " (PayloadCapacity " + PayloadCapacity
                + " minus null terminator), truncating");
            copyCount = maxPayload;
        }

        Span<byte> destination = _payload;
        bytes.Slice(0, copyCount).CopyTo(destination);
        destination[copyCount] = 0;
        _payloadLength = (byte)(copyCount + 1);
        _type = FieldType.AsciiString;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetInt
    //
    // Reads the slot as a 32-bit signed integer.  Returns SlotReadResult.Success and the
    // value on success, SlotReadResult.TypeMismatch and zero on type mismatch.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SlotReadResult TryGetInt(out int value)
    {
        if ((_type != FieldType.UInt) && (_type != FieldType.Int))
        {
            value = 0;
            return SlotReadResult.TypeMismatch;
        }

        ReadOnlySpan<byte> source = _payload;
        value = BinaryPrimitives.ReadInt32LittleEndian(source);
        return SlotReadResult.Success;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetUInt
    //
    // Reads the slot as a 32-bit unsigned integer.  Returns SlotReadResult.Success and the
    // value on success, SlotReadResult.TypeMismatch and zero on type mismatch.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SlotReadResult TryGetUInt(out uint value)
    {
        if (_type != FieldType.UInt)
        {
            value = 0;
            return SlotReadResult.TypeMismatch;
        }

        ReadOnlySpan<byte> source = _payload;
        value = BinaryPrimitives.ReadUInt32LittleEndian(source);
        return SlotReadResult.Success;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetFloat
    //
    // Stores a 32-bit IEEE float in the payload buffer in machine byte order.  The encoding
    // (LE or BE) of the source bytes is the extractor's concern; by the time the value
    // reaches this setter the bits have already been interpreted into a host-order float.
    //
    // value:  The float to store.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void SetFloat(float value)
    {
        Span<byte> destination = _payload;
        BinaryPrimitives.WriteSingleLittleEndian(destination, value);
        _payloadLength = 4;
        _type = FieldType.Float;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetFloat
    //
    // Reads the slot as a 32-bit IEEE float.  Returns SlotReadResult.Success and the
    // value on success, SlotReadResult.TypeMismatch and zero on type mismatch.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SlotReadResult TryGetFloat(out float value)
    {
        if (_type != FieldType.Float)
        {
            value = 0;
            return SlotReadResult.TypeMismatch;
        }

        ReadOnlySpan<byte> source = _payload;
        value = BinaryPrimitives.ReadSingleLittleEndian(source);
        return SlotReadResult.Success;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetAsciiBytes
    //
    // Returns a read-only span over the slot's stored ASCII string bytes, with the trailing
    // null terminator excluded.  Logs and returns an empty span if the slot's type is not
    // AsciiString.
    //
    // The returned span is valid only while the slot's storage is stable — practically, until
    // the bag containing this slot is released.  Callers that need to keep the bytes past
    // that point must copy them out (e.g. via Encoding.ASCII.GetString or span.CopyTo).
    ///////////////////////////////////////////////////////////////////////////////////////////////
    [UnscopedRef]
    public ReadOnlySpan<byte> GetAsciiBytes()
    {
        if (_type != FieldType.AsciiString)
        {
            DebugLog.Write(LogChannel.Fields, "FieldSlot.GetAsciiBytes: " + GetName() + " type mismatch, slot type is "
                + _type + ", returning empty span");
            return ReadOnlySpan<byte>.Empty;
        }

        if (_payloadLength == 0)
        {
            DebugLog.Write(LogChannel.Fields, "FieldSlot.GetAsciiBytes: " + GetName()
                + " payload length is zero, returning empty span");
            return ReadOnlySpan<byte>.Empty;
        }

        uint visibleLength = _payloadLength - 1;
        return ((ReadOnlySpan<byte>)_payload).Slice(0, (int) visibleLength);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetAsciiBytes
    //
    // Returns a read-only span over the slot's stored ASCII string bytes, with the trailing
    // null terminator excluded.  Logs and returns an empty span if the slot's type is not
    // AsciiString, or if the payload is empty.
    //
    // The returned span is valid only while the slot's storage is stable — practically, until
    // the bag containing this slot is released.  Callers that need to keep the bytes past
    // that point must copy them out (e.g. via Encoding.ASCII.GetString or span.CopyTo).
    //
    // Returns SlotReadResult.Success and the value on success,
    // SlotReadResult.TypeMismatch and an empty span on type mismatch,
    // SlotReadResult.EmptyPayload and an empty span when the slot is AsciiString but
    // has no stored bytes.
    ///////////////////////////////////////////////////////////////////////////////////////////
    [UnscopedRef]
    public SlotReadResult TryGetAsciiBytes(out ReadOnlySpan<byte> value)
    {
        if (_type != FieldType.AsciiString)
        {
            value = ReadOnlySpan<byte>.Empty;
            return SlotReadResult.TypeMismatch;
        }

        if (_payloadLength == 0)
        {
            DebugLog.Write(LogChannel.Fields, "FieldSlot.TryGetAsciiBytes: " + GetName()
                + " payload length is zero, returning empty span");
            value = ReadOnlySpan<byte>.Empty;
            return SlotReadResult.EmptyPayload;
        }

        uint visibleLength = _payloadLength - 1;
        value = ((ReadOnlySpan<byte>)_payload).Slice(0, (int) visibleLength);
        return SlotReadResult.Success;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetName
    //
    // Returns the slot's stored field name as an ASCII string.  Returns "(unnamed)" if no
    // name has been set on the slot.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public string GetName()
    {
        if (_nameLength == 0)
        {
            DebugLog.Write(LogChannel.Fields, "FieldSlot.GetName: name is empty, returning placeholder");
            return "(unnamed)";
        }

        ReadOnlySpan<byte> nameBytes = _name;
        ReadOnlySpan<byte> activeName = nameBytes.Slice(0, _nameLength);
        string result = Encoding.ASCII.GetString(activeName);
        return result;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetLength
    //
    // Returns the payload length in bytes for this slot.  For variable-length types such as
    // null-terminated strings, this is the actual stored byte count.  For fixed-width types
    // this is the width of the type.  Empty slots report zero.
    //
    // Returns: Payload length in bytes.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public uint GetLength()
    {
        return _payloadLength;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AsString
    //
    // Returns the slot's value formatted as a string according to its current type.
    // Integer types render in hex with a "0x" prefix and width-appropriate zero padding.
    // Float renders as decimal with three digits after the radix.  AsciiString renders
    // as the stored characters.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    internal string AsString()
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
                    DebugLog.Write(LogChannel.Fields, "FieldSlot.AsString: " + GetName()
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
                    DebugLog.Write(LogChannel.Fields, "FieldSlot.AsString: " + GetName()
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
                    DebugLog.Write(LogChannel.Fields, "FieldSlot.AsString: " + GetName()
                        + " Float read failed: " + result);
                    return string.Empty;
                }

            case FieldType.AsciiString:
                {
                    ReadOnlySpan<byte> bytes = GetAsciiBytes();
                    return Encoding.ASCII.GetString(bytes);
                }

            default:
                {
                    DebugLog.Write(LogChannel.Fields, "FieldSlot.AsString: " + GetName()
                        + " type " + _type + " has no string conversion");
                    return string.Empty;
                }
        }
    }
}
