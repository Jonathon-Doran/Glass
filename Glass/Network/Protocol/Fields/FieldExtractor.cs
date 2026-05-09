using Glass.Core;
using Glass.Core.Logging;
using Glass.Data;
using Microsoft.Data.Sqlite;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldExtractor
//
// Decodes typed field values from a raw packet payload according to a list of
// FieldDefinitions, writing results into a caller-provided FieldBag.  Stateless apart from
// an internal string table that maps database encoding strings (e.g. "uint16_le") to the
// FieldEncoding enum.
//
// Lifetime: one instance is constructed at application startup and shared by all handlers.
// The string table is built once in the constructor and never modified afterward, so the
// extractor is safe to use from any thread without locking.
//
// Usage pattern:
//   - At handler construction: read FieldDefinition rows from the database; for each row,
//     call GetEncodingId(encodingString) to resolve the encoding to its enum value, then
//     store the resulting FieldDefinition in an array owned by the handler.
//   - In the hot path: call Extract(definitions, payload, bag) to fill the bag.  The bag
//     is rented from a FieldBagPool by the caller and released when reading is complete.
///////////////////////////////////////////////////////////////////////////////////////////////
public class FieldExtractor
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // FieldExtractor (constructor)
    //
    // Builds the empty patch-data cache and the bag pool.  Both are owned by this instance
    // for its lifetime; no per-patch state is loaded here.  Callers must invoke
    // LoadPatchLevel or LoadLatestPatchLevel before any handler is constructed, since
    // handlers consult their patch's data through this extractor in their constructors.
    //
    // The extractor itself is patch-independent.  The bag pool is reusable across patches
    // and the cache simply maps PatchLevel identifiers to fully-loaded PatchData objects.
    // Adding more patches at runtime is supported via additional LoadPatchLevel calls.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public FieldExtractor()
    {
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Extract
    //
    // Walks the field definitions for the given OpcodeHandle and writes one FieldSlot per
    // definition into the caller-provided FieldBag, preserving definition-to-slot index
    // alignment.  Called from the hot path on every decoded packet.
    //
    // Resolves the field definitions from the registry by patchLevel and handle.  Returns
    // silently if the opcode has no fields loaded for this patch.
    //
    // Every definition produces exactly one slot.  Definitions whose decode does not
    // succeed (Unknown encoding, payload too short for the bit range, unsupported bit
    // width, etc.) produce slots that are added but left in their default Empty state.
    // This keeps cached field indices stable: a handler that caches "hp is field 3" at
    // load time can read bag.GetXxxAt(3) without worrying that an upstream field failed
    // to decode, and detect failure by checking the slot's type tag.
    //
    // All per-field failure paths are silent.  Failures during Inference are the expected
    // case (every handler is tried against every packet) and logging them would flood
    // the log with non-anomalies.
    //
    // Reentrancy: safe to call concurrently from multiple threads, provided each thread
    // passes its own FieldBag.  The bag is mutated; sharing a bag across threads is not
    // supported.
    //
    // Parameters:
    //   patchLevel  - The patch identifier the handle belongs to.
    //   handle      - The OpcodeHandle whose field definitions to walk.
    //   payload     - The raw packet payload bytes.  Bit offsets in definitions are
    //                 relative to the start of this span.
    //   bag         - The bag to fill.  Must have been rented from the registry by the
    //                 caller and must have enough capacity to hold one slot per
    //                 definition.  The caller is responsible for releasing the bag when
    //                 done reading.
    ///////////////////////////////////////////////////////////////////////////////////////////

    public void Extract(PatchLevel patchLevel, OpcodeHandle opcode, ReadOnlySpan<byte> payload,
        FieldBag bag)
    {
        FieldDefinition[]? definitions = GlassContext.PatchRegistry.GetFields(patchLevel, opcode);
        if (definitions == null)
        {
            return;
        }

        if (bag == null)
        {
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.Extract: null bag, nothing to extract");
            return;
        }

        // Allocate one slot per definition up front, in definition order.  This guarantees
        // that any cached slot index resolved via IndexOfField at load time is valid by the
        // time decoding runs — including indices used by ExtractOptionalGroup to write
        // sub-fields whose definitions appear later in the array than the block field that
        // dispatches into the helper.
        for (int definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
        {
            ref FieldSlot newSlot = ref bag.AddSlot();
            newSlot.SetName(definitions[definitionIndex].Name);
        }

        for (int definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
        {
            FieldDefinition definition = definitions[definitionIndex];
            ref FieldSlot slot = ref bag.TryGetSlotRef(definitionIndex);

            if (HasBits(payload, definition.BitOffset, definition.BitLength) == false)
            {
                continue;
            }

            switch (definition.Encoding)
            {
                case FieldEncoding.Unknown:
                    break;

                case FieldEncoding.UInt:
                    {
                        ulong raw;
                        if (TryReadBitsLE(payload, definition.BitOffset, definition.BitLength, out raw) == true)
                        {
                            slot.SetUInt32((uint)raw);
                        }
                        break;
                    }

                case FieldEncoding.Int:
                    {
                        ulong raw;
                        if (TryReadBitsLE(payload, definition.BitOffset, definition.BitLength, out raw) == true)
                        {
                            int signed = SignExtend(raw, definition.BitLength);
                            slot.SetInt32(signed);
                        }
                        break;
                    }

                case FieldEncoding.Float:
                    ExtractFloatLE(payload, definition.BitOffset, definition.BitLength, ref slot);
                    break;

                case FieldEncoding.UIntMasked:
                    {
                        ulong raw;
                        if (TryReadBitsLE(payload, definition.BitOffset, definition.BitLength, out raw) == true)
                        {
                            slot.SetUInt32((uint)raw);
                        }
                        break;
                    }

                case FieldEncoding.SignExtendFixedDiv8:
                    ExtractSignExtendedDiv8(payload, definition.BitOffset, definition.BitLength, ref slot);
                    break;

                case FieldEncoding.SignMagagnitudeMsbDiv8:
                    ExtractSignMagnitudeMsbDiv8(payload, definition.BitOffset, definition.BitLength, ref slot);
                    break;

                case FieldEncoding.SignMagnitudeMsb:
                    ExtractSignMagnitudeMsb(payload, definition.BitOffset, definition.BitLength, ref slot);
                    break;

                case FieldEncoding.OptSignMagnitudeMsb:
                    break;

                case FieldEncoding.OptionalGroup:
                    {
                        OptionalGroup? group = GlassContext.PatchRegistry.GetOptionalGroup(patchLevel, opcode);
                        if (group != null)
                        {
                            ExtractOptionalGroup(payload, definition.BitOffset, group, bag);
                        }
                        break;
                    }

                case FieldEncoding.StringNullTerminated:
                    ExtractNullTerminatedString(payload, definition.BitOffset, ref slot);
                    break;

                case FieldEncoding.StringLengthPrefixed:
                    ExtractLengthPrefixedString(payload, definition.BitOffset, ref slot);
                    break;

                default:
                    DebugLog.Write(LogChannel.Fields, "FieldExtractor.Extract: unhandled encoding "
                        + definition.Encoding + " for field '" + definition.Name
                        + "', slot left empty");
                    break;
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HasBits
    //
    // Verifies that the payload contains at least bitLength bits starting at bitOffset.
    // Called from Extract before each per-encoding read to guard against truncated payloads
    // or field definitions that don't match the actual packet shape.
    //
    // The check converts the bit range to a byte range covering it (which may span a
    // partial byte at each end) and compares against the payload's byte length.
    //
    // On failure this method is silent; the caller leaves the slot in its default Empty
    // state, which consumers detect via the slot's type tag.  Per-field bounds failures
    // are an expected condition during Inference (every handler is tried against every
    // packet) and logging them would flood the log with non-anomalies.
    //
    // Parameters:
    //   payload    - The packet payload being decoded.
    //   bitOffset  - The bit offset at which the read would start.  Taken from the field
    //                definition unchanged.
    //   bitLength  - The number of bits the read needs.  Taken from the field definition
    //                unchanged.
    //
    // Returns:
    //   true  - The payload has enough bits; the caller may proceed with the read.
    //   false - The payload is too short; the caller must skip the decode and leave the
    //           slot in its default state.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private bool HasBits(ReadOnlySpan<byte> payload, uint bitOffset, uint bitLength)
    {
        uint lastBitExclusive = bitOffset + bitLength;
        uint requiredBytes = (lastBitExclusive + 7u) / 8u;
        if (requiredBytes > (uint)payload.Length)
        {
            return false;
        }
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TryReadBitsLE
    //
    // Reads bitLength bits from the payload starting at bitOffset, treating the payload
    // bytes as a little-endian bit stream — that is, byte 0 holds the lowest-addressed
    // bits, with bit 0 of each byte being the lowest bit of that byte's contribution.
    //
    // On success, the extracted bits are written to value, right-justified in a ulong with
    // all higher bits zero.  Callers interpret those bits according to the field's encoding
    // (signed, unsigned, fixed-point, IEEE float, etc.).  On failure, value is set to 0 and
    // the caller must treat the result as unread; the slot stays Empty.
    //
    // The caller must have already verified the read fits in the payload via HasBits.  This
    // method does not bounds-check the payload; out-of-range reads will throw at the
    // underlying payload access.
    //
    // Width support: this implementation reads at most 8 source bytes.  That allows up to
    // 64 bits at a byte-aligned offset, or up to 56 bits at a non-byte-aligned offset (the
    // worst case being bit offset 7 plus 56 bits = bit 63, the last bit of byte 7).  Reads
    // that would exceed those limits return false rather than silently truncating.  All
    // current field definitions are 32 bits or smaller, well within both limits.  If a
    // wider non-aligned read is ever needed, this method must be extended to read a 9th
    // source byte.
    //
    // Parameters:
    //   payload    - The packet payload.  Treated as a contiguous bit stream.
    //   bitOffset  - The starting bit offset within the payload.  May be non-byte-aligned.
    //   bitLength  - The number of bits to read.  Must be in [1, 64] for byte-aligned reads
    //                or [1, 56] for non-byte-aligned reads.
    //   value      - On success, receives the extracted bits right-justified in a ulong.
    //                On failure, set to 0 and must not be used.
    //
    // Returns:
    //   true   - The read succeeded; value holds the extracted bits.
    //   false  - bitLength is out of range (zero, or wider than this method supports at the
    //            given bit offset).  value is set to 0.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private bool TryReadBitsLE(ReadOnlySpan<byte> payload, uint bitOffset, uint bitLength,
        out ulong value)
    {
        value = 0ul;

        if (bitLength == 0u)
        {
            return false;
        }

        if (bitLength > 64u)
        {
            return false;
        }

        uint bitShift = bitOffset % 8u;
        if (bitShift > 0u && bitLength > 56u)
        {
            return false;
        }

        uint byteOffset = bitOffset / 8u;
        uint lastBitExclusive = bitShift + bitLength;
        uint byteCount = (lastBitExclusive + 7u) / 8u;

        ulong assembled = 0ul;
        for (uint byteIndex = 0u; byteIndex < byteCount; byteIndex++)
        {
            ulong sourceByte = payload[(int)(byteOffset + byteIndex)];
            assembled = assembled | (sourceByte << (int)(byteIndex * 8u));
        }

        ulong shifted = assembled >> (int)bitShift;

        ulong mask;
        if (bitLength == 64u)
        {
            mask = ulong.MaxValue;
        }
        else
        {
            mask = (1ul << (int)bitLength) - 1ul;
        }

        value = shifted & mask;
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SignExtend
    //
    // Treats the low bitLength bits of raw as a signed integer in two's-complement form and
    // returns the value sign-extended to a 32-bit signed int.  If the top bit of the bit
    // range (bit at position bitLength - 1) is set, the result is negative; otherwise
    // positive.
    //
    // Used by the signed integer encodings (Int16LE, Int32LE) to convert the raw bits
    // returned by TryReadBitsLE — which are right-justified and zero-extended — into a
    // properly-signed C# int.
    //
    // Parameters:
    //   raw        - The bits to interpret, right-justified in a ulong with all higher bits
    //                zero (the form returned by TryReadBitsLE on success).
    //   bitLength  - The width of the signed value in bits.  Must be in [1, 32]; widths
    //                outside that range fall through with the raw low bits cast to int,
    //                which is acceptable because Extract has already validated the field
    //                via TryReadBitsLE before calling this.
    //
    // Returns:
    //   The sign-extended 32-bit signed integer.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private int SignExtend(ulong raw, uint bitLength)
    {
        if (bitLength == 0u || bitLength >= 32u)
        {
            return (int)raw;
        }

        ulong signBit = 1ul << (int)(bitLength - 1u);
        if ((raw & signBit) == 0ul)
        {
            return (int)raw;
        }

        ulong fillMask = ~((1ul << (int)bitLength) - 1ul);
        ulong extended = raw | fillMask;
        return (int)extended;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractFloatLE
    //
    // Reads a 32-bit IEEE single-precision float in little-endian bit order from the payload
    // at the given bit offset and stores it in the slot.  The bits are read via
    // TryReadBitsLE and reinterpreted as a float without any numeric conversion — the bit
    // pattern is the float.
    //
    // On any read failure the slot is left in its default Empty state.  HasBits has already
    // confirmed the payload has enough bytes; failures here would mean the bit length is
    // out of range for TryReadBitsLE, which for a properly defined float field (BitLength
    // of 32) cannot happen.
    //
    // Parameters:
    //   payload    - The packet payload being decoded.
    //   bitOffset  - The bit offset to read from.  Caller-computed: definition.BitOffset
    //                for required fields, the running offset for optional fields.
    //   bitLength  - The number of bits to read.  Should be 32 for a standard IEEE single;
    //                other widths are read but the result is undefined.
    //   slot       - The slot to fill.  Already has its name set.  Stays Empty on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ExtractFloatLE(ReadOnlySpan<byte> payload, uint bitOffset, uint bitLength,
        ref FieldSlot slot)
    {
        ulong raw;
        if (TryReadBitsLE(payload, bitOffset, bitLength, out raw) == false)
        {
            return;
        }

        int bits = (int)raw;
        float value = BitConverter.Int32BitsToSingle(bits);
        slot.SetFloat(value);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractSignExtendedDiv8
    //
    // Reads a sign-extended fixed-point coordinate value at the given bit offset and bit
    // length, then divides by 8.0 to convert to a floating-point world coordinate.  Used
    // by MobUpdate / NpcMoveUpdate position fields, which pack each coordinate as a
    // signed integer (commonly 19 bits) in units of 1/8 world unit.
    //
    // On any read failure the slot is left in its default Empty state.
    //
    // Parameters:
    //   payload    - The packet payload being decoded.
    //   bitOffset  - The bit offset to read from.  Caller-computed: definition.BitOffset
    //                for required fields, the running offset for optional fields.
    //   bitLength  - The number of bits in the packed signed integer; the divide by 8 is
    //                implicit in the encoding.
    //   slot       - The slot to fill.  Already has its name set.  Stays Empty on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ExtractSignExtendedDiv8(ReadOnlySpan<byte> payload, uint bitOffset,
        uint bitLength, ref FieldSlot slot)
    {
        DebugLog.Write(LogChannel.Opcodes, "ExtractSignExtendedDiv8, offset = " + bitOffset +
            " length = " + bitLength);
        ulong raw;
        if (TryReadBitsLE(payload, bitOffset, bitLength, out raw) == false)
        {
            return;
        }


        int signedValue = SignExtend(raw, bitLength);
        float worldCoord = signedValue / 8.0f;
        slot.SetFloat(worldCoord);

        DebugLog.Write(LogChannel.Opcodes, "signedValue = " + signedValue +
             " worldCoord = " + worldCoord);

    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractNullTerminatedString
    //
    // Reads an ASCII string starting at the given byte offset (derived from the bit
    // offset), terminated by a null byte.  BitLength is not used by this encoding; the
    // null byte determines the string length.
    //
    // If no null byte is found in the payload from the byte offset onward, the slot is left
    // in its default Empty state and the event is logged.  A missing null terminator is a
    // real anomaly — either the payload is truncated or the field has been misidentified —
    // not a routine miss-by-handler, so the log is appropriate here even when other failure
    // paths in the extractor are silent.
    //
    // If the byte offset is at or past the end of the payload, the slot is left Empty
    // without logging; this is the routine-miss case (the field doesn't fit) and follows
    // the same silent-failure rule as the bounds-check helpers.
    //
    // Parameters:
    //   payload    - The packet payload being decoded.
    //   bitOffset  - The bit offset to read from.  Caller-computed: definition.BitOffset
    //                for required fields, the running offset for optional fields.  Divided
    //                by 8 to produce the byte offset; non-byte-aligned strings are not
    //                supported and would mis-locate the start of the string.
    //   slot       - The slot to fill.  Already has its name set.  Stays Empty on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ExtractNullTerminatedString(ReadOnlySpan<byte> payload, uint bitOffset,
        ref FieldSlot slot)
    {
        uint byteOffset = bitOffset / 8u;
        if (byteOffset >= (uint)payload.Length)
        {
            return;
        }

        ReadOnlySpan<byte> tail = payload.Slice((int)byteOffset);
        int nullIndex = tail.IndexOf((byte)0x00);
        if (nullIndex < 0)
        {
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExtractNullTerminatedString: field '"
                + slot.GetName() + "' at byteOffset " + byteOffset
                + " has no null terminator within remaining " + tail.Length
                + " bytes, slot left empty");
            return;
        }

        ReadOnlySpan<byte> stringBytes = tail.Slice(0, nullIndex);
        slot.SetAsciiString(stringBytes);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractLengthPrefixedString
    //
    // Reads a uint32 little-endian length prefix at the given byte offset (derived from
    // the bit offset), then reads that many bytes immediately following as an ASCII
    // string.  BitLength is not used by this encoding; the length prefix determines the
    // string length.
    //
    // On any failure the slot is left in its default Empty state.  Failures here are
    // routine during Inference (every handler reads every packet, length prefixes
    // interpreted at random offsets often produce nonsense values), so all paths are
    // silent.  Handlers that care about a specific failure can detect it by checking
    // the slot's type tag after extraction.
    //
    // A length prefix of zero is a successful decode producing a zero-length string;
    // the slot's type becomes AsciiString and its payload length is zero.  This is
    // distinguishable from a failure (which leaves the slot Empty).
    //
    // Parameters:
    //   payload    - The packet payload being decoded.
    //   bitOffset  - The bit offset to read from.  Caller-computed: definition.BitOffset
    //                for required fields, the running offset for optional fields.  Divided
    //                by 8 to produce the byte offset; non-byte-aligned length-prefixed
    //                strings are not supported and would mis-locate the prefix.
    //   slot       - The slot to fill.  Already has its name set.  Stays Empty on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ExtractLengthPrefixedString(ReadOnlySpan<byte> payload, uint bitOffset,
        ref FieldSlot slot)
    {
        uint byteOffset = bitOffset / 8u;
        if (byteOffset + 4u > (uint)payload.Length)
        {
            return;
        }

        uint declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice((int)byteOffset));
        uint stringStart = byteOffset + 4u;
        uint available = (uint)payload.Length - stringStart;
        if (declaredLength > available)
        {
            return;
        }

        ReadOnlySpan<byte> stringBytes = payload.Slice((int)stringStart, (int)declaredLength);
        slot.SetAsciiString(stringBytes);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractOptionalGroup
    //
    // Decodes an optional block at the given bit offset.  The flag word is read from a
    // slot already populated earlier in this packet's extraction; its name is on the
    // OptionalGroup.  The flag word is bit-reversed across its known width before use,
    // because the standard uint encoding decodes LSB-first while the surrounding packet
    // is MSB-first.  Sub-fields are walked in array order; sub-field at index N is
    // gated by flag bit N of the corrected value.  Present sub-fields are decoded at
    // the running offset and the offset advances by the sub-field's bit length; absent
    // sub-fields are skipped and the offset does not advance.
    //
    // Failures (missing flag slot, missing sub-field slot, decode error) leave the
    // affected slot empty.  When a sub-field's slot is missing the running offset still
    // advances, so subsequent sub-fields decode at the correct positions.
    //
    // Parameters:
    //   payload    - The packet payload being decoded.
    //   bitOffset  - The bit offset at which the optional block starts.
    //   group      - The OptionalGroup metadata for this opcode.  Must not be null.
    //   bag        - The bag the caller is filling.  Flag word and sub-field slots are
    //                looked up by name.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ExtractOptionalGroup(ReadOnlySpan<byte> payload, uint bitOffset,
        OptionalGroup group, FieldBag bag)
    {
        uint rawFlags = bag.GetUIntAt(group.FlagSlotIndex);
        uint flags = ReverseBits(rawFlags, group.FlagBitLength);
        uint runningBitOffset = bitOffset;
        DebugLog.Write(LogChannel.Opcodes, "optional group raw flags = 0x" + rawFlags.ToString("x2")
            + " width = " + group.FlagBitLength
            + " corrected flags = 0x" + flags.ToString("x2"));
        for (int subFieldIndex = 0; subFieldIndex < group.SubFields.Length; subFieldIndex++)
        {
            OptionalSubField subField = group.SubFields[subFieldIndex];
            uint flagBit = 1u << subFieldIndex;
            bool isPresent = (flags & flagBit) != 0u;
            DebugLog.Write(LogChannel.Opcodes, "subfield " + subFieldIndex + " (" +
                subField.Name + ") with mask 0x" + flagBit.ToString("x2") + " is " + (isPresent ? "present" : "not present") +
                " and has " + subField.BitLength + " bits");
            if (isPresent == false)
            {
                continue;
            }
            ref FieldSlot slot = ref bag.TryGetSlotRef(subField.SlotIndex);
            if (Unsafe.IsNullRef(ref slot) == true)
            {
                runningBitOffset = runningBitOffset + subField.BitLength;
                continue;
            }
            switch (subField.Encoding)
            {
                case FieldEncoding.OptSignMagnitudeMsb:
                    ExtractSignMagnitudeMsb(payload, runningBitOffset, subField.BitLength, ref slot);
                    break;
                default:
                    DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExtractOptionalGroup: unhandled sub-field encoding "
                        + subField.Encoding);
                    break;
            }
            runningBitOffset = runningBitOffset + subField.BitLength;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ReverseBits
    //
    // Reverses the low bitLength bits of value, returning the result right-justified with
    // all higher bits zero.  Used by ExtractOptionalGroup to correct the LSB-first decode
    // of a flag word whose surrounding packet is MSB-first.
    //
    // Parameters:
    //   value      - The bits to reverse, right-justified in a uint with all higher bits
    //                zero (the form returned by the standard uint encoding).
    //   bitLength  - The width in bits over which to reverse.  A bitLength of 0 returns 0;
    //                a bitLength of 32 reverses the full word.
    //
    // Returns:
    //   The bit-reversed value, right-justified.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private uint ReverseBits(uint value, uint bitLength)
    {
        if (bitLength == 0u)
        {
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.ReverseBits: bitLength is 0, returning 0");
            return 0u;
        }

        uint result = 0u;
        for (uint bitIndex = 0u; bitIndex < bitLength; bitIndex++)
        {
            uint sourceBit = (value >> (int)bitIndex) & 1u;
            result = result | (sourceBit << (int)(bitLength - 1u - bitIndex));
        }
        return result;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ExtractNpcMoveOptional
    //
    // Decodes the bit-packed tail of OP_NpcMoveUpdate starting at the given bit offset.
    // The tail begins with a 6-bit flags field, followed by three 19-bit sign-magnitude
    // fixed-point coordinates (divided by 8 for world units), a 12-bit signed heading, and
    // up to six conditional fields whose presence is controlled by individual flag bits.
    //
    // Bit layout from bitOffset:
    //   6 bits   flags                          (always present)
    //   19 bits  x  (sign-magnitude, /8)        (always present)
    //   19 bits  y  (sign-magnitude, /8)        (always present)
    //   19 bits  z  (sign-magnitude, /8)        (always present)
    //   12 bits  heading                        (always present)
    //   12 bits  pitch          if flags & 0x01
    //   10 bits  headingDelta   if flags & 0x02
    //   10 bits  velocity       if flags & 0x04
    //   13 bits  dy             if flags & 0x08
    //   13 bits  dx             if flags & 0x10
    //   13 bits  dz             if flags & 0x20
    //
    // Prints decoded values to the Opcodes log channel.  Does not write to the slot —
    // proof-of-concept only.
    //
    // Parameters:
    //   payload    - The packet payload being decoded.
    //   bitOffset  - The bit offset where the flags field begins.
    //   bitLength  - Unused.
    //   slot       - Unused.
    ///////////////////////////////////////////////////////////////////////////////////////////


    private void ExtractNpcMoveOptional(ReadOnlySpan<byte> payload, uint bitOffset,
        uint bitLength, ref FieldSlot slot)
    {
        byte[] buffer = new byte[payload.Length];
        payload.CopyTo(buffer);
        BitReader reader = new BitReader(buffer);

        // Skip past the leading bits (spawn_id + secondField in OP_NpcMoveUpdate).
        reader.ReadUInt((int)bitOffset);

        uint flags = reader.ReadUInt(6);
        int rawX = reader.ReadInt(19);
        int rawY = reader.ReadInt(19);
        int rawZ = reader.ReadInt(19);
        double x = rawX / 8.0;
        double y = rawY / 8.0;
        double z = rawZ / 8.0;
        int heading = reader.ReadInt(12);

        DebugLog.Write(LogChannel.Opcodes, "ExtractNpcMoveOptional: flags=0x"
            + flags.ToString("x2") + " x=" + x.ToString("F2") + " y=" + y.ToString("F2")
            + " z=" + z.ToString("F2") + " heading=" + heading);

        if ((flags & 0x01) != 0)
        {
            if (reader.BitsRemaining < 12)
            {
                DebugLog.Write(LogChannel.Opcodes, "ExtractNpcMoveOptional: underrun reading pitch");
                return;
            }
            int pitch = reader.ReadInt(12);
            DebugLog.Write(LogChannel.Opcodes, "ExtractNpcMoveOptional: pitch=" + pitch);
        }

        if ((flags & 0x02) != 0)
        {
            if (reader.BitsRemaining < 10)
            {
                DebugLog.Write(LogChannel.Opcodes, "ExtractNpcMoveOptional: underrun reading headingDelta");
                return;
            }
            int headingDelta = reader.ReadInt(10);
            DebugLog.Write(LogChannel.Opcodes, "ExtractNpcMoveOptional: headingDelta=" + headingDelta);
        }

        if ((flags & 0x04) != 0)
        {
            if (reader.BitsRemaining < 10)
            {
                DebugLog.Write(LogChannel.Opcodes, "ExtractNpcMoveOptional: underrun reading velocity");
                return;
            }
            int velocity = reader.ReadInt(10);
            DebugLog.Write(LogChannel.Opcodes, "ExtractNpcMoveOptional: velocity=" + velocity);
        }

        if ((flags & 0x08) != 0)
        {
            if (reader.BitsRemaining < 13)
            {
                DebugLog.Write(LogChannel.Opcodes, "ExtractNpcMoveOptional: underrun reading dy");
                return;
            }
            int dy = reader.ReadInt(13);
            DebugLog.Write(LogChannel.Opcodes, "ExtractNpcMoveOptional: dy=" + dy);
        }

        if ((flags & 0x10) != 0)
        {
            if (reader.BitsRemaining < 13)
            {
                DebugLog.Write(LogChannel.Opcodes, "ExtractNpcMoveOptional: underrun reading dx");
                return;
            }
            int dx = reader.ReadInt(13);
            DebugLog.Write(LogChannel.Opcodes, "ExtractNpcMoveOptional: dx=" + dx);
        }

        if ((flags & 0x20) != 0)
        {
            if (reader.BitsRemaining < 13)
            {
                DebugLog.Write(LogChannel.Opcodes, "ExtractNpcMoveOptional: underrun reading dz");
                return;
            }
            int dz = reader.ReadInt(13);
            DebugLog.Write(LogChannel.Opcodes, "ExtractNpcMoveOptional: dz=" + dz);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractSignMagnitudeMsbDiv8
    //
    // Reads a signed coordinate value packed MSB-first within each byte, sign-magnitude
    // encoded (first bit is the sign, 1 = negative; remaining bits are the unsigned
    // magnitude), then divides by 8.0 to produce a world coordinate.
    //
    // This is the bit-reading convention of the EQ client (FUN_1405ac1f0 / FUN_1405ac160
    // in eqgame.exe), shared with BitReader.  It is distinct from the LSB-first /
    // two's-complement encoding used by ExtractSignExtendedDiv8 — they must not be
    // confused; values decoded with the wrong convention will be wrong, sometimes
    // wildly so.
    //
    // On any read failure the slot is left in its default Empty state.
    //
    // Parameters:
    //   payload    - The packet payload being decoded.
    //   bitOffset  - The bit offset to read from.
    //   bitLength  - The total number of bits including the sign bit.  Must be at least 2
    //                (one sign bit + one magnitude bit) and at most 32.
    //   slot       - The slot to fill.  Already has its name set.  Stays Empty on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ExtractSignMagnitudeMsbDiv8(ReadOnlySpan<byte> payload, uint bitOffset,
        uint bitLength, ref FieldSlot slot)
    {
        if (bitLength < 2u || bitLength > 32u)
        {
            return;
        }

        uint bitPosition = bitOffset;
        uint bitsLeft = bitLength;
        uint result = 0u;

        while (bitsLeft > 0u)
        {
            uint byteIndex = bitPosition / 8u;
            uint bitInByte = bitPosition % 8u;
            uint bitsInThisByte = 8u - bitInByte;

            uint bitsToTake;
            if (bitsLeft < bitsInThisByte)
            {
                bitsToTake = bitsLeft;
            }
            else
            {
                bitsToTake = bitsInThisByte;
            }

            uint shift = bitsInThisByte - bitsToTake;
            uint mask = (1u << (int)bitsToTake) - 1u;
            uint chunk = ((uint)payload[(int)byteIndex] >> (int)shift) & mask;

            result = (result << (int)bitsToTake) | chunk;
            bitPosition = bitPosition + bitsToTake;
            bitsLeft = bitsLeft - bitsToTake;
        }

        uint signBit = result >> (int)(bitLength - 1u);
        uint magnitudeMask = (1u << (int)(bitLength - 1u)) - 1u;
        uint magnitude = result & magnitudeMask;

        int signedValue;
        if (signBit != 0u)
        {
            signedValue = -(int)magnitude;
        }
        else
        {
            signedValue = (int)magnitude;
        }

        float worldCoord = signedValue / 8.0f;
        slot.SetFloat(worldCoord);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractSignMagnitudeMsb
    //
    // Reads a signed integer value packed MSB-first within each byte, sign-magnitude
    // encoded (first bit is the sign, 1 = negative; remaining bits are the unsigned
    // magnitude).  No fixed-point divisor is applied; the value is stored as a signed
    // 32-bit integer.
    //
    // This is the bit-reading convention of the EQ client (FUN_1405ac1f0 / FUN_1405ac160
    // in eqgame.exe), shared with BitReader and ExtractSignMagnitudeMsbDiv8.  It is
    // distinct from the LSB-first / two's-complement encoding used by the Int encoding —
    // they must not be confused; values decoded with the wrong convention will be wrong,
    // sometimes wildly so.
    //
    // On any read failure the slot is left in its default Empty state.
    //
    // Parameters:
    //   payload    - The packet payload being decoded.
    //   bitOffset  - The bit offset to read from.
    //   bitLength  - The total number of bits including the sign bit.  Must be at least 2
    //                (one sign bit + one magnitude bit) and at most 32.
    //   slot       - The slot to fill.  Already has its name set.  Stays Empty on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ExtractSignMagnitudeMsb(ReadOnlySpan<byte> payload, uint bitOffset,
        uint bitLength, ref FieldSlot slot)
    {
        if (bitLength < 2u || bitLength > 32u)
        {
            return;
        }

        uint bitPosition = bitOffset;
        uint bitsLeft = bitLength;
        uint result = 0u;

        while (bitsLeft > 0u)
        {
            uint byteIndex = bitPosition / 8u;
            uint bitInByte = bitPosition % 8u;
            uint bitsInThisByte = 8u - bitInByte;

            uint bitsToTake;
            if (bitsLeft < bitsInThisByte)
            {
                bitsToTake = bitsLeft;
            }
            else
            {
                bitsToTake = bitsInThisByte;
            }

            uint shift = bitsInThisByte - bitsToTake;
            uint mask = (1u << (int)bitsToTake) - 1u;
            uint chunk = ((uint)payload[(int)byteIndex] >> (int)shift) & mask;

            result = (result << (int)bitsToTake) | chunk;
            bitPosition = bitPosition + bitsToTake;
            bitsLeft = bitsLeft - bitsToTake;
        }

        uint signBit = result >> (int)(bitLength - 1u);
        uint magnitudeMask = (1u << (int)(bitLength - 1u)) - 1u;
        uint magnitude = result & magnitudeMask;

        int signedValue;
        if (signBit != 0u)
        {
            signedValue = -(int)magnitude;
        }
        else
        {
            signedValue = (int)magnitude;
        }

        slot.SetInt32(signedValue);
    }
}
