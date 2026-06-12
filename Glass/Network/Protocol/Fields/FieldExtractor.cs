using Glass.Core;
using Glass.Core.Logging;
using Glass.Data;
using Microsoft.Data.Sqlite;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net.Sockets;
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
    // Walks the field definitions for the given CollectionHandle and writes one FieldSlot per
    // definition into the caller-provided FieldBag, preserving definition-to-slot index
    // alignment.  Called from the hot path on every decoded collection instance.
    //
    // Resolves the field definitions from the registry by patchLevel and handle.  Returns
    // silently if the collection has no fields loaded for this patch.
    //
    // Every definition produces exactly one slot.  Definitions whose decode does not succeed
    // (Unknown encoding, payload too short for the bit range, unsupported bit width) produce
    // slots that are added but left in their default Empty state.
    //
    // A field carrying a predicate is decoded only when its predicate holds.  The predicate's
    // source field is decoded earlier in this pass — the load-time order check guarantees the
    // source's slot precedes the gated field — so its value is available when the gate runs.
    // When the predicate does not hold the field is absent from the payload entirely: its slot
    // is left Empty, its start bit is not resolved, and no payload is consumed, so a following
    // field's offset is computed as though the absent field contributed nothing.
    //
    // A per-packet array of resolved start bits, one entry per definition, is populated by
    // ResolveFieldStartBit at the top of each decoded iteration.  Relative-anchored fields read
    // their anchor's resolved start bit from this array.
    //
    // Parameters:
    //   patchLevel  - The patch identifier the handle belongs to.
    //   collection  - The CollectionHandle whose field definitions to walk.
    //   payload     - The raw payload bytes.  Bit offsets in definitions are relative to the
    //                 start of this span.
    //   bag         - The bag to fill.  Must have enough capacity to hold one slot per
    //                 definition.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Extract(PatchLevel patchLevel, CollectionHandle collection, ReadOnlySpan<byte> payload,
        FieldBag bag)
    {
        FieldDefinition[]? definitions = GlassContext.PatchRegistry.GetFields(patchLevel, collection);
        if (definitions == null)
        {
            return;
        }

        if (bag == null)
        {
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.Extract: null bag, nothing to extract");
            return;
        }

        for (int definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
        {
            ref FieldSlot newSlot = ref bag.AddSlot();
            newSlot.SetName(bag, definitions[definitionIndex].Name);
        }

        Span<uint> resolvedStartBits = stackalloc uint[definitions.Length];

        // TEMPORARY - FOR TESTING ONLY - REMOVE
        // Tracks whether the first gate in this collection has been passed.  The first gate is
        // the predicated Once gate, handled by the normal predicate path; the second gate is the
        // multiplicity gate that has no implementation and must stop the pass.
        bool firstGateSeen = false;

        for (uint definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
        {
            FieldDefinition definition = definitions[definitionIndex];
            SlotId slotId = new SlotId(collection, definitionIndex);
            ref FieldSlot slot = ref bag.TryGetSlotRef(slotId);

            uint effectiveBitOffset = ResolveFieldStartBit(collection, definitions, bag, resolvedStartBits, definitionIndex);
            slot.WireBitOffset = effectiveBitOffset;
            slot.WireBitLength = 0;
            slot.Sequence = (ushort) definition.Sequence;

            // TEMPORARY - FOR TESTING ONLY - REMOVE
            // The first gate is the predicated Once gate; its presence is governed by the field
            // predicate already evaluated above, so when absent it contributes no wire bytes and
            // the following fields chain correctly with no adjustment here.  It is passed over
            // without stopping.  The second gate is the multiplicity gate, which has no decoding
            // implementation, so the pass stops there.
            if (definition.Gate.Exists == true)
            {
                if (firstGateSeen == false)
                {
                    firstGateSeen = true;
                    continue;
                }

               break;
            }

            if (definition.Predicate.Op != PredicateOp.None)
            {
                if (EvaluatePredicate(bag, definition.Predicate) == false)
                {
                    continue;
                }
            }

            // Not enough room in the payload for this value.
            if (HasBits(payload, effectiveBitOffset, definition.BitLength) == false)
            {
                DebugLog.Write(LogChannel.Fields, "FieldExtractor.Extract: field '"
                    + definition.Name + "' insufficient payload bits at offset "
                    + effectiveBitOffset + " for length " + definition.BitLength
                    + ", wire length 0, slot left empty");
                continue;
            }

            switch (definition.Encoding)
            {
                case FieldEncoding.Unknown:
                    break;

                case FieldEncoding.UInt:
                    {
                        ulong raw;
                        if (TryReadBitsLE(payload, effectiveBitOffset, definition.BitLength, out raw) == true)
                        {
                            slot.SetUInt((uint)raw);
                        }
                        slot.WireBitLength = (ushort)definition.BitLength;
                        break;
                    }

                case FieldEncoding.Int:
                    {
                        ulong raw;
                        if (TryReadBitsLE(payload, effectiveBitOffset, definition.BitLength, out raw) == true)
                        {
                            int signed = SignExtend(raw, definition.BitLength);
                            slot.SetInt(signed);
                        }
                        slot.WireBitLength = (ushort)definition.BitLength;
                        break;
                    }

                case FieldEncoding.UIntMsb:
                    ExtractUIntMsb(payload, effectiveBitOffset, definition.BitLength, ref slot);
                    slot.WireBitLength = (ushort)definition.BitLength;
                    break;

                case FieldEncoding.Float:
                    ExtractFloatLE(payload, effectiveBitOffset, definition.BitLength, ref slot);
                    slot.WireBitLength = (ushort)definition.BitLength;
                    break;

                case FieldEncoding.UIntMasked:
                    {
                        ulong raw;
                        if (TryReadBitsLE(payload, effectiveBitOffset, definition.BitLength, out raw) == true)
                        {
                            slot.SetUInt((uint)raw);
                        }
                        slot.WireBitLength = (ushort)definition.BitLength;
                        break;
                    }

                case FieldEncoding.SignMagnitudeLsb:
                    ExtractSignMagnitudeLsb(payload, effectiveBitOffset, definition.BitLength, definition.Divisor, ref slot);
                    slot.WireBitLength = (ushort)definition.BitLength;
                    break;

                case FieldEncoding.SignMagnitudeMsb:
                    ExtractSignMagnitudeMsb(payload, effectiveBitOffset, definition.BitLength, definition.Divisor, ref slot);
                    slot.WireBitLength = (ushort)definition.BitLength;
                    break;

                case FieldEncoding.OptSignMagnitudeMsb:
                    break;

                case FieldEncoding.CsvToken:
                    break;

                case FieldEncoding.StringNullTerminated:
                    slot.WireBitLength = (ushort) (ExtractNullTerminatedString(payload, effectiveBitOffset, bag, ref slot) * 8u);
                    break;

                case FieldEncoding.StringLengthPrefixed:
                    slot.WireBitLength = (ushort) (ExtractLengthPrefixedString(payload, effectiveBitOffset, bag, ref slot) * 8u);
                    break;

                default:
                    string failure = "FieldExtractor.Extract: unhandled encoding "
                        + definition.Encoding + " for field '" + definition.Name + "'";
                    DebugLog.Write(LogChannel.Fields, failure);
                    Environment.FailFast(failure);
                    break;
            }
        }

        bag.BuildDisplayOrder();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ResolveFieldStartBit
    //
    // Computes the absolute bit position in the payload where the field at definitionIndex
    // begins, stores it into resolvedStartBits[definitionIndex], and returns it.  Absolute
    // fields use their schema BitOffset directly.  Relative fields use the anchor field's
    // already-resolved start bit plus the anchor slot's wire bit length plus the schema
    // BitOffset.
    //
    // The anchor's wire bit length is read from the anchor slot's WireBitLength, set when
    // the anchor was decoded.  An anchor that was absent (predicate did not hold) carries a
    // wire bit length of zero, so a field relative to it resolves to the same start bit the
    // absent anchor occupied.
    //
    // Parameters:
    //   collection         - The collection whose slots hold the wire placement of decoded fields.
    //   definitions        - The field definitions for the collection being decoded.
    //   bag                - The bag whose slots hold the wire placement of already-decoded fields.
    //   resolvedStartBits  - Per-packet span of resolved start bits.  This method writes
    //                        the entry for definitionIndex.
    //   definitionIndex    - Index of the field whose start bit to resolve.
    //
    // Returns:
    //   The resolved start bit, also written to resolvedStartBits[definitionIndex].
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private static uint ResolveFieldStartBit(
        CollectionHandle collection,
        FieldDefinition[] definitions,
        FieldBag bag,
        Span<uint> resolvedStartBits,
        uint definitionIndex)
    {
        FieldDefinition definition = definitions[definitionIndex];
        uint startBit;

        if (definition.RelativeToSlot == null)
        {
            startBit = definition.BitOffset;
        }
        else
        {
            uint anchorIndex = definition.RelativeToSlot.Value;
            uint anchorStartBit = resolvedStartBits[(int)anchorIndex];
            SlotId anchorSlotId = new SlotId(collection, anchorIndex);
            ref FieldSlot anchorSlot = ref bag.TryGetSlotRef(anchorSlotId);
            uint anchorLengthBits = anchorSlot.WireBitLength;
            uint anchorEndBit = anchorStartBit + anchorLengthBits;
            startBit = anchorEndBit + definition.BitOffset;
        }

        resolvedStartBits[(int)definitionIndex] = startBit;
        return startBit;
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
    // ExtractNullTerminatedString
    //
    // Decodes a null-terminated ASCII string beginning at the given bit offset, which must
    // be byte-aligned.  The wire bytes up to but not including the terminator are stored
    // in the slot.  If the offset lies past the payload, or no terminator exists in the
    // remaining payload, the slot is left unset.
    //
    // payload:    The application payload being decoded.
    // bitOffset:  Bit offset into the payload where the string begins.
    // bag:        The bag that owns the slot; receives the string bytes into its arena.
    // slot:       The slot to receive the decoded string.
    //
    // Returns:    Number of bytes consumed including the terminator, or 0 if no string
    //             was decoded.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private uint ExtractNullTerminatedString(ReadOnlySpan<byte> payload, uint bitOffset,
        FieldBag bag, ref FieldSlot slot)
    {
        uint byteOffset = bitOffset / 8u;
        if (byteOffset >= (uint)payload.Length)
        {
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExtractNullTerminatedString: field '"
                + slot.GetName(bag) + "' byteOffset " + byteOffset + " is at or past payload length "
                + payload.Length + ", slot left empty");
            return 0;
        }
        ReadOnlySpan<byte> tail = payload.Slice((int)byteOffset);
        int nullIndex = tail.IndexOf((byte)0x00);
        if (nullIndex < 0)
        {
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExtractNullTerminatedString: field '"
                + slot.GetName(bag) + "' at byteOffset " + byteOffset
                + " has no null terminator within remaining " + tail.Length
                + " bytes, slot left empty");
            return 0;
        }
        ReadOnlySpan<byte> stringBytes = tail.Slice(0, nullIndex);
        slot.SetAsciiString(bag, stringBytes);
        return (uint)stringBytes.Length + 1;
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
    //   bag:       - The bag that owns the slot; receives the string bytes into its arena.
    //   slot       - The slot to fill.  Already has its name set.  Stays Empty on failure.
    //
    // Returns:
    //   The wire length of the extracted string (in bytes) including the prefix.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private uint ExtractLengthPrefixedString(ReadOnlySpan<byte> payload, uint bitOffset,
        FieldBag bag, ref FieldSlot slot)
    {
        uint byteOffset = bitOffset / 8u;
        if (byteOffset + 4u > (uint)payload.Length)
        {
            DebugLog.Write(LogChannel.Fields,
                $"ExtractLengthPrefixedString: length prefix at byte {byteOffset} " +
                $"exceeds payload length {(uint)payload.Length}, slot not set");
            return 0;
        }
        uint declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice((int)byteOffset));
        uint stringStart = byteOffset + 4u;
        uint available = (uint)payload.Length - stringStart;
        if (declaredLength > available)
        {
            DebugLog.Write(LogChannel.Fields,
                $"ExtractLengthPrefixedString: declared length {declaredLength} at byte {byteOffset} " +
                $"exceeds available {available}, slot not set");
            return 0;
        }

        ReadOnlySpan<byte> stringBytes = payload.Slice((int)stringStart, (int)declaredLength);
        slot.SetAsciiString(bag, stringBytes);
        return (uint)stringBytes.Length + 4;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractUIntMsb
    //
    // Reads an unsigned integer value packed MSB-first within each byte.  No sign bit;
    // all bits contribute to the unsigned magnitude.
    //
    // On any read failure the slot is left in its default Empty state.
    //
    // Parameters:
    //   payload    - The packet payload being decoded.
    //   bitOffset  - The bit offset to read from.
    //   bitLength  - The total number of bits.  Must be at least 1 and at most 32.
    //   slot       - The slot to fill.  Already has its name set.  Stays Empty on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ExtractUIntMsb(ReadOnlySpan<byte> payload, uint bitOffset,
        uint bitLength, ref FieldSlot slot)
    {
        if (bitLength < 1u || bitLength > 32u)
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

        slot.SetUInt(result);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractSignMagnitudeMsb
    //
    // Reads a signed coordinate value packed MSB-first within each byte, sign-magnitude
    // encoded (first bit is the sign, 1 = negative; remaining bits are the unsigned
    // magnitude), then divides by Divisor.
    //
    // This is the bit-reading convention of the EQ client shared with BitReader.
    // It is distinct from the LSB-first /
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
    //   divisor    - Divisor used to create a floating point value.
    //   slot       - The slot to fill.  Already has its name set.  Stays Empty on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ExtractSignMagnitudeMsb(ReadOnlySpan<byte> payload, uint bitOffset,
        uint bitLength, float divisor, ref FieldSlot slot)
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

        float fValue = signedValue / divisor;
        slot.SetFloat(fValue);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractSignMagnitudeLsb
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
    private void ExtractSignMagnitudeLsb(ReadOnlySpan<byte> payload, uint bitOffset,
        uint bitLength, float divisor, ref FieldSlot slot)
    {
        ulong raw;
        if (TryReadBitsLE(payload, bitOffset, bitLength, out raw) == false)
        {
            return;
        }

        int signedValue = SignExtend(raw, bitLength);
        float fValue = signedValue / divisor;
        slot.SetFloat(fValue);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // EvaluatePredicate
    //
    // Evaluates the predicate against its source slot in the given bag.  The source slot
    // must hold an integer type; any other type means the gating field is absent or
    // mistyped, which is a schema or extraction integrity violation and halts the process
    // via FailFast with the failure details preserved in the Fields log channel.
    //
    // bag:        The bag containing the predicate's source slot.
    // predicate:  The predicate to evaluate.
    //
    // Returns:    The predicate result.  Does not return if the source slot is not an
    //             integer type.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private bool EvaluatePredicate(FieldBag bag, FieldPredicate predicate)
    {
        FieldType sourceType = bag.GetTypeAt(predicate.SourceSlot);
        if (sourceType == FieldType.UInt)
        {
            uint sourceValue = bag.GetUIntAt(predicate.SourceSlot);
            return EvaluateUnsigned(sourceValue, predicate);
        }
        if (sourceType == FieldType.Int)
        {
            int sourceValue = bag.GetIntAt(predicate.SourceSlot);
            return EvaluateSigned(sourceValue, predicate);
        }
        string message = "FieldExtractor.EvaluatePredicate: source slot " + predicate.SourceSlot
            + " has type " + sourceType + ", which is not an integer the predicate can test; "
            + "the gating field is absent or mistyped, packet or schema is broken -- aborting";
        DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
            + Environment.StackTrace);
        Environment.FailFast(message);
        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // EvaluateUnsigned
    //
    // Applies the predicate's operator to an unsigned source value, comparing against the
    // unsigned operand.  BitmaskNonZero tests the raw bits; the ordered operators compare
    // unsigned magnitude.
    //
    // Parameters:
    //   sourceValue  - The source field's value, read as unsigned.
    //   predicate    - The resolved predicate carrying the operator and unsigned operand.
    //
    // Returns:
    //   true when the comparison holds, false otherwise.  Fail-fasts on an unhandled operator.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private bool EvaluateUnsigned(uint sourceValue, FieldPredicate predicate)
    {
        bool result;
        switch (predicate.Op)
        {
            case PredicateOp.BitmaskNonZero:
                result = (sourceValue & predicate.Operand) != 0u;
                break;

            case PredicateOp.Equal:
                result = sourceValue == predicate.Operand;
                break;

            case PredicateOp.NotEqual:
                result = sourceValue != predicate.Operand;
                break;

            case PredicateOp.Greater:
                result = sourceValue > predicate.Operand;
                break;

            case PredicateOp.GreaterOrEqual:
                result = sourceValue >= predicate.Operand;
                break;

            case PredicateOp.Less:
                result = sourceValue < predicate.Operand;
                break;

            case PredicateOp.LessOrEqual:
                result = sourceValue <= predicate.Operand;
                break;

            default:
                {
                    string message = "FieldExtractor.EvaluateUnsigned: unhandled predicate op "
                        + predicate.Op + " -- broken predicate definition, aborting";

                    DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                        + Environment.StackTrace);

                    Environment.FailFast(message);
                    result = false;
                    break;
                }
        }

        return result;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // EvaluateSigned
    //
    // Applies the predicate's operator to a signed source value, comparing against the signed
    // operand.  BitmaskNonZero tests the raw bits, reinterpreted as unsigned for the mask, so
    // a sign-extended source does not turn a narrow mask into a match on the extension bits;
    // the ordered operators compare signed magnitude.
    //
    // Parameters:
    //   sourceValue  - The source field's value, read as signed.
    //   predicate    - The resolved predicate carrying the operator and both operand views.
    //
    // Returns:
    //   true when the comparison holds, false otherwise.  Fail-fasts on an unhandled operator.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private bool EvaluateSigned(int sourceValue, FieldPredicate predicate)
    {
        bool result;
        switch (predicate.Op)
        {
            case PredicateOp.BitmaskNonZero:
                {
                    string message = "FieldExtractor.EvaluateSigned: BitmaskNonZero applied to a "
                        + "signed source field; a bitmask test requires an unsigned field -- "
                        + "broken schema definition, aborting";

                    DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                        + Environment.StackTrace);

                    Environment.FailFast(message);
                    result = false;
                    break;
                }

            case PredicateOp.Equal:
                result = sourceValue == predicate.SignedOperand;
                break;

            case PredicateOp.NotEqual:
                result = sourceValue != predicate.SignedOperand;
                break;

            case PredicateOp.Greater:
                result = sourceValue > predicate.SignedOperand;
                break;

            case PredicateOp.GreaterOrEqual:
                result = sourceValue >= predicate.SignedOperand;
                break;

            case PredicateOp.Less:
                result = sourceValue < predicate.SignedOperand;
                break;

            case PredicateOp.LessOrEqual:
                result = sourceValue <= predicate.SignedOperand;
                break;

            default:
                {
                    string message = "FieldExtractor.EvaluateSigned: unhandled predicate op "
                        + predicate.Op + " -- broken predicate definition, aborting";

                    DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                        + Environment.StackTrace);

                    Environment.FailFast(message);
                    result = false;
                    break;
                }
        }

        return result;
    }
}