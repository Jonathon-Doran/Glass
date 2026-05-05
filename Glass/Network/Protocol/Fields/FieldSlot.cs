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
// The setter methods (SetName, SetInt32, etc.) are used by the extractor when filling a
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
    private byte _payloadLength;
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
    // Copies the slot's ASCII string bytes into the caller-provided destination buffer.
    // The caller owns the destination; the slot holds no reference to it after the copy.
    // Logs and returns 0 if the slot's type is not AsciiString or if the destination is
    // too small to hold the stored bytes.
    //
    // destination:  Caller-owned buffer to receive the bytes; must be at least PayloadLength bytes.
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

        if (destination.Length < _payloadLength)
        {
            DebugLog.Write(LogChannel.Fields, "FieldSlot.CopyAsciiBytesTo: " + GetName() + " length "
                + destination.Length + " is smaller than payload length " + _payloadLength
                + ", returning 0");
            return 0;
        }

        Span<byte> source = _payload;
        source.Slice(0, _payloadLength).CopyTo(destination);
        return _payloadLength;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetInt32
    //
    // Stores a 32-bit signed integer in machine byte order in the payload buffer.
    //
    // value:  The integer to store.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void SetInt32(int value)
    {
        Span<byte> destination = _payload;
        BinaryPrimitives.WriteInt32LittleEndian(destination, value);
        _payloadLength = 4;
        _type = FieldType.Int32;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetUInt32
    //
    // Stores a 32-bit unsigned integer in the payload buffer.
    //
    // value:  The unsigned integer to store.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void SetUInt32(uint value)
    {
        Span<byte> destination = _payload;
        BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
        _payloadLength = 4;
        _type = FieldType.UInt32;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetUInt8
    //
    // Stores a single unsigned byte in the payload buffer.
    //
    // value:  The byte to store.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void SetUInt8(byte value)
    {
        Span<byte> destination = _payload;
        destination[0] = value;
        _payloadLength = 1;
        _type = FieldType.UInt8;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetAsciiString
    //
    // Stores ASCII string bytes in the payload buffer.  Source longer than PayloadCapacity
    // is truncated and logged.
    //
    // bytes:  The ASCII bytes to store; should not include a trailing null.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void SetAsciiString(ReadOnlySpan<byte> bytes)
    {
        int copyCount = bytes.Length;
        if (copyCount > PayloadCapacity)
        {
            DebugLog.Write(LogChannel.Fields, "FieldSlot.SetAsciiString: " + GetName() + " length " + copyCount
                + " exceeds PayloadCapacity " + PayloadCapacity + ", truncating");
            copyCount = PayloadCapacity;
        }

        Span<byte> destination = _payload;
        bytes.Slice(0, copyCount).CopyTo(destination);
        _payloadLength = (byte)copyCount;
        _type = FieldType.AsciiString;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetInt32
    //
    // Reads the slot as a 32-bit signed integer.  Returns SlotReadResult.Success and the
    // value on success, SlotReadResult.TypeMismatch and zero on type mismatch.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SlotReadResult TryGetInt32(out int value)
    {
        if (_type != FieldType.Int32)
        {
            value = 0;
            return SlotReadResult.TypeMismatch;
        }

        ReadOnlySpan<byte> source = _payload;
        value = BinaryPrimitives.ReadInt32LittleEndian(source);
        return SlotReadResult.Success;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetUInt32
    //
    // Reads the slot as a 32-bit unsigned integer.  Returns SlotReadResult.Success and the
    // value on success, SlotReadResult.TypeMismatch and zero on type mismatch.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SlotReadResult TryGetUInt32(out uint value)
    {
        if (_type != FieldType.UInt32)
        {
            value = 0;
            return SlotReadResult.TypeMismatch;
        }

        ReadOnlySpan<byte> source = _payload;
        value = BinaryPrimitives.ReadUInt32LittleEndian(source);
        return SlotReadResult.Success;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetUInt8
    //
    // Reads the slot as an 8-bit unsigned integer.  Returns SlotReadResult.Success and the
    // value on success, SlotReadResult.TypeMismatch and zero on type mismatch.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SlotReadResult TryGetUInt8(out int value)
    {
        if (_type != FieldType.UInt8)
        {
            value = 0;
            return SlotReadResult.TypeMismatch;
        }

        ReadOnlySpan<byte> source = _payload;
        value = BinaryPrimitives.ReadInt32LittleEndian(source);
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
    // Returns a read-only span over the slot's stored ASCII string bytes.  Logs and returns
    // an empty span if the slot's type is not AsciiString.
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

        return ((ReadOnlySpan<byte>)_payload).Slice(0, _payloadLength);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetAsciiBytes
    //
    // Returns a read-only span over the slot's stored ASCII string bytes.  Logs and returns
    // an empty span if the slot's type is not AsciiString.
    //
    // The returned span is valid only while the slot's storage is stable — practically, until
    // the bag containing this slot is released.  Callers that need to keep the bytes past
    // that point must copy them out (e.g. via Encoding.ASCII.GetString or span.CopyTo).
    //
    // Returns SlotReadResult.Success and the value on success,
    // SlotReadResult.TypeMismatch and zero on type mismatch.
    ///////////////////////////////////////////////////////////////////////////////////////////
    [UnscopedRef]
    public SlotReadResult TryGetAsciiBytes(out ReadOnlySpan<byte> value)
    {
        if (_type != FieldType.AsciiString)
        {
            value = ReadOnlySpan<byte>.Empty;
            return SlotReadResult.TypeMismatch;
        }

        ReadOnlySpan<byte> source = _payload;
        value = ((ReadOnlySpan<byte>)_payload).Slice(0, _payloadLength);
        return SlotReadResult.Success;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // MatchesName
    //
    // Returns true if the slot's stored name bytes are byte-for-byte equal to the supplied
    // query.  Comparison is performed inline; no span over the slot's name buffer is ever
    // returned to the caller, so there is no risk of a span outliving the slot.
    //
    // Parameters:
    //   query - The candidate name bytes to compare against this slot's name.  Typically
    //           produced by ASCII-encoding a field name string into a stack buffer at the
    //           call site.
    //
    // Returns:
    //   true  - The query length matches _nameLength and every byte is equal.
    //   false - Length differs, or any byte differs.  Also false for an empty (unused) slot
    //           unless the query is itself zero-length, which is not a valid field name and
    //           is logged.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public bool MatchesName(ReadOnlySpan<byte> query)
    {
        if (query.Length == 0)
        {
            DebugLog.Write(LogChannel.Fields, "FieldSlot.MatchesName: " + GetName() + " zero-length query, returning false");
            return false;
        }
        if (query.Length != _nameLength)
        {
            return false;
        }
        ReadOnlySpan<byte> nameBytes = _name;
        ReadOnlySpan<byte> activeName = nameBytes.Slice(0, _nameLength);
        bool isEqual = activeName.SequenceEqual(query);
        return isEqual;
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
}
