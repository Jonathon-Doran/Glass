using Glass.Core.Logging;
using Glass.Data;
using Microsoft.Data.Sqlite;
using System.Buffers.Binary;
using System.Collections.Generic;

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
    private readonly Dictionary<PatchLevel, PatchData> _patchDataByLevel;
    private readonly FieldBagPool _bagPool;

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
        _patchDataByLevel = new Dictionary<PatchLevel, PatchData>();
        _bagPool = new FieldBagPool(FieldBag.DefaultPoolSize, FieldBag.DefaultSlotCount);

        DebugLog.Write(LogChannel.Network, "FieldExtractor ctor: empty patch cache, bag pool sized "
            + FieldBag.DefaultPoolSize + " x " + FieldBag.DefaultSlotCount + " slots");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeValue
    //
    // Returns the wire opcode value for the given logical name in the given patch.  Looks
    // up the patch's PatchData in the cache and delegates to it.  Called by handlers at
    // construction time, once per opcode they care about.  Cold path.
    //
    // Throws InvalidOperationException if the patch level has not been loaded.  Callers
    // must invoke LoadPatchLevel (or LoadLatestPatchLevel) for the patch before any
    // handler that needs it is constructed; a missing entry here is a programmer error,
    // not a runtime condition to recover from.
    //
    // Returns 0 if the opcode name is unknown to the loaded patch.  Handlers check for 0
    // and skip their own dispatch registration if their opcode is missing — this is the
    // expected runtime case when a patch genuinely lacks an opcode the handler knows
    // about.
    //
    // Parameters:
    //   patchLevel  - The patch identifier.  Must already be loaded.
    //   opcodeName  - The logical name (e.g. "OP_PlayerProfile").
    //
    // Returns:
    //   The wire opcode value (e.g. 0x6FA1), or 0 if the name is not in the patch.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ushort GetOpcodeValue(PatchLevel patchLevel, string opcodeName)
    {
        PatchData patchData;
        bool found = _patchDataByLevel.TryGetValue(patchLevel, out patchData!);
        if (found == false)
        {
            throw new InvalidOperationException("FieldExtractor.GetOpcodeValue: patchLevel "
                + patchLevel + " is not loaded");
        }

        return patchData.GetOpcodeValue(opcodeName);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadPatchLevel
    //
    // Ensures the PatchData for the given patch level is loaded and cached.  If the patch
    // is already in the cache, returns immediately without rebuilding.  Otherwise constructs
    // a PatchData (which runs the database queries to populate its opcode map and field
    // definitions) and stores it.
    //
    // Idempotent: calling twice for the same patch level is safe and cheap on the second
    // call.  Callers that may run before or after others doing the same load do not need
    // to coordinate.
    //
    // Parameters:
    //   patchLevel  - The patch identifier to load.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void LoadPatchLevel(PatchLevel patchLevel)
    {
        if (_patchDataByLevel.ContainsKey(patchLevel) == true)
        {
            DebugLog.Write(LogChannel.Network, "FieldExtractor.LoadPatchLevel: patchLevel "
                + patchLevel + " already loaded, returning");
            return;
        }

        PatchData patchData = new PatchData(patchLevel);
        _patchDataByLevel[patchLevel] = patchData;

        DebugLog.Write(LogChannel.Network, "FieldExtractor.LoadPatchLevel: loaded patchLevel "
            + patchLevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadLatestPatchLevel
    //
    // Resolves the most recent patch_date in the database for the given server type,
    // constructs the corresponding PatchLevel identifier, ensures its PatchData is loaded
    // and cached, and returns the identifier.  Used by Glass at session launch when
    // "use the latest patch for this server type" is the desired default.
    //
    // The cache check inside LoadPatchLevel makes this idempotent — calling twice with
    // the same server type is safe and only does the database resolve a second time.
    // The resolve query itself is cheap (one MAX() against an indexed column) so the
    // duplicate cost is negligible.
    //
    // Throws InvalidOperationException via ResolveLatestPatchDate if no patches exist for
    // the server type.  This means the database has no patch data for the server type at
    // all — for example, a freshly-seeded Test server before any patches have been
    // imported.  The caller may catch this and present a UI for selecting a different
    // server type or for manually importing patch data.
    //
    // Parameters:
    //   serverType  - The server_type column value (e.g. "live", "test").
    //
    // Returns:
    //   The PatchLevel identifier for the loaded patch.  Caller typically stores this
    //   on GlassContext.CurrentPatchLevel for handlers to read at construction time.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchLevel LoadLatestPatchLevel(string serverType)
    {
        DebugLog.Write(LogChannel.Network, "FieldExtractor.LoadLatestPatchLevel: serverType="
            + serverType);

        string latestDate = ResolveLatestPatchDate(serverType);
        PatchLevel patchLevel = new PatchLevel(latestDate, serverType);
        LoadPatchLevel(patchLevel);

        DebugLog.Write(LogChannel.Network, "FieldExtractor.LoadLatestPatchLevel: returning "
            + patchLevel);
        return patchLevel;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ResolveLatestPatchDate
    //
    // Queries PatchOpcode for the maximum patch_date for the given server_type.  Used by
    // LoadLatestPatchLevel to pick the date before constructing the PatchLevel identifier.
    // patch_date is stored as an ISO-8601 string (yyyy-MM-dd) which sorts lexicographically
    // and is therefore safe to use with MAX().
    //
    // Throws InvalidOperationException if the query returns no rows for the server type.
    // The thrown exception propagates out of LoadLatestPatchLevel to the caller, which is
    // the right behavior — Glass startup wants to fail visibly if the database has no
    // patch data for the configured server type.
    //
    // Parameters:
    //   serverType  - The server_type column value to filter on.
    //
    // Returns:
    //   The most recent patch_date string for that server type.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private static string ResolveLatestPatchDate(string serverType)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(patch_date)"
            + " FROM PatchOpcode"
            + " WHERE server_type = @serverType";
        cmd.Parameters.AddWithValue("@serverType", serverType);

        object? result = cmd.ExecuteScalar();
        if (result == null || result == DBNull.Value)
        {
            throw new InvalidOperationException("FieldExtractor.ResolveLatestPatchDate: "
                + "no patches for serverType='" + serverType + "'");
        }

        string latestDate = (string)result;
        DebugLog.Write(LogChannel.Network, "FieldExtractor.ResolveLatestPatchDate: latest "
            + "patch_date for serverType=" + serverType + " is " + latestDate);
        return latestDate;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetFields
    //
    // Returns the field definitions for the given OpcodeId in the given patch.  Looks up
    // the patch's PatchData in the cache and delegates to it.  Called by handlers at
    // construction time, once per OpcodeId the handler cares about, and by Inference code
    // that views field definitions without decoding.  Cold path.
    //
    // The return type is IReadOnlyList rather than an array because nothing — handler or
    // viewer — has a legitimate reason to mutate the cached definitions.  FieldDefinition
    // is a value type, so element reads copy out cleanly; the cached data stays
    // unmutated regardless of what callers do with the elements they read.
    //
    // Throws InvalidOperationException if the patch level has not been loaded.  Callers
    // must invoke LoadPatchLevel (or LoadLatestPatchLevel) for the patch before any
    // handler that needs it is constructed; a missing entry here is a programmer error,
    // not a runtime condition to recover from.
    //
    // Returns null if the OpcodeId is not in the patch's cache.  Callers check for null
    // and skip their own decode for that variant.
    //
    // Parameters:
    //   patchLevel  - The patch identifier.  Must already be loaded.
    //   opcodeId    - The OpcodeId whose field definitions to return.
    //
    // Returns:
    //   The field definitions for the OpcodeId, or null if absent.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<FieldDefinition>? GetFields(PatchLevel patchLevel, PatchOpcode opcodeId)
    {
        PatchData patchData;
        bool found = _patchDataByLevel.TryGetValue(patchLevel, out patchData!);
        if (found == false)
        {
            throw new InvalidOperationException("FieldExtractor.GetFields: patchLevel "
                + patchLevel + " is not loaded");
        }

        return patchData.GetFieldDefinitions(opcodeId);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Extract
    //
    // Walks the field definition list and writes one FieldSlot per definition into the
    // caller-provided FieldBag, preserving definition-to-slot index alignment.  Called
    // from the hot path on every decoded packet.
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
    // the log with non-anomalies.  Glass-runtime diagnostics belong in the handler, where
    // the context to know whether a failure is surprising lives.
    //
    // The definitions parameter is IReadOnlyList rather than an array.  Handlers cache
    // the value returned by FieldExtractor.GetFields, which is the same reference held
    // in PatchData's per-opcode cache.  No copy happens at the boundary.
    //
    // Reentrancy: this method is safe to call concurrently from multiple threads on the
    // same FieldExtractor instance, provided each thread passes its own FieldBag.  The
    // bag is mutated; sharing a bag across threads is not supported.
    //
    // Parameters:
    //   definitions - The field definitions, in the order they should appear in the bag.
    //                 Typically the same reference returned by GetFields and cached by
    //                 the handler.  No copy is made.
    //   payload     - The raw packet payload bytes.  Bit offsets in definitions are
    //                 relative to the start of this span.
    //   bag         - The bag to fill.  Must have been rented from a FieldBagPool by the
    //                 caller and must have enough capacity to hold one slot per
    //                 definition.  The caller is responsible for releasing the bag when
    //                 done reading.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Extract(IReadOnlyList<FieldDefinition> definitions, ReadOnlySpan<byte> payload,
        FieldBag bag)
    {
        if (definitions == null)
        {
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.Extract: null definitions, nothing to extract");
            return;
        }

        if (bag == null)
        {
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.Extract: null bag, nothing to extract");
            return;
        }

        for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
        {
            FieldDefinition definition = definitions[definitionIndex];

            ref FieldSlot slot = ref bag.AddSlot();
            slot.SetName(definition.Name);

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
    // Rent
    //
    // Rents a FieldBag from the extractor's internal pool, stamps it with the caller's
    // opcode name for diagnostic logging, and returns it.  The bag is cleared by the pool
    // before being returned, so the caller always sees a fresh bag.  The caller must
    // eventually call Release on the bag to return it to the pool.
    //
    // Handlers use this in the hot path to get a bag for filling via Extract, reading,
    // and releasing — the pool itself is an internal detail of FieldExtractor and is not
    // exposed.
    //
    // Parameters:
    //   opcodeName  - The opcode name of the calling handler (e.g. "OP_NpcMoveUpdate").
    //                 Stamped on the bag so any log lines the bag produces during use are
    //                 attributable to a specific opcode.
    //
    // Returns:
    //   A bag with SlotCount == 0 and CurrentOpcodeName set, ready to be filled.
    ///////////////////////////////////////////////////////////////////////////////////////
    public FieldBag Rent(string opcodeName)
    {
        FieldBag bag = _bagPool.Rent();
        bag.CurrentOpcodeName = opcodeName;
        return bag;
    }
}
