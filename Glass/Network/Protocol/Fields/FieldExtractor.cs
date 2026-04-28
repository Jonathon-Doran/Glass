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
    private readonly Dictionary<string, FieldEncoding> _encodingsByString;
    private readonly Dictionary<string, ushort> _opcodeValuesByName;
    private readonly string _patchDate;
    private readonly string _serverType;
    private readonly FieldBagPool _bagPool;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // FieldExtractor (constructor)
    //
    // Builds the encoding string table and loads the opcode-name-to-wire-value map for the
    // given patch level.  Both pieces of state are immutable after construction; the
    // extractor is safe to use from any thread without locking once this returns.
    //
    // The set of encoding strings here must stay in sync with the FieldEncoding enum and
    // with the dispatch switch in Extract — all three sites must agree.
    //
    // Parameters:
    //   patchDate   - The patch_date column value (e.g. "2026-04-15").  Stored for use by
    //                 GetFieldDefinitions when handlers query at construction time.
    //   serverType  - The server_type column value (e.g. "live", "test").  Stored for the
    //                 same reason.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public FieldExtractor(string patchDate, string serverType)
    {
        _patchDate = patchDate;
        _serverType = serverType;

        _encodingsByString = new Dictionary<string, FieldEncoding>();
        _encodingsByString.Add("uint8", FieldEncoding.UInt8);
        _encodingsByString.Add("uint16_le", FieldEncoding.UInt16LE);
        _encodingsByString.Add("uint16_be", FieldEncoding.UInt16BE);
        _encodingsByString.Add("int16_le", FieldEncoding.Int16LE);
        _encodingsByString.Add("int32_le", FieldEncoding.Int32LE);
        _encodingsByString.Add("int32_be", FieldEncoding.Int32BE);
        _encodingsByString.Add("uint32_le", FieldEncoding.UInt32LE);
        _encodingsByString.Add("float_le", FieldEncoding.FloatLE);
        _encodingsByString.Add("float_be", FieldEncoding.FloatBE);
        _encodingsByString.Add("uint_le_masked", FieldEncoding.UIntLEMasked);
        _encodingsByString.Add("sign_extend_fixed_div8", FieldEncoding.SignExtendFixedDiv8);
        _encodingsByString.Add("string_null_terminated", FieldEncoding.StringNullTerminated);
        _encodingsByString.Add("string_length_prefixed", FieldEncoding.StringLengthPrefixed);

        DebugLog.Write(LogChannel.Network, "FieldExtractor ctor: encoding string table built with "
            + _encodingsByString.Count + " entries, patchDate=" + patchDate
            + " serverType=" + serverType);

        _opcodeValuesByName = new Dictionary<string, ushort>();
        LoadOpcodeMap();

        if (_opcodeValuesByName.Count == 0)
        {
            DebugLog.Write(LogChannel.Network, "FieldExtractor ctor: no opcodes loaded for patchDate="
                + patchDate + " serverType=" + serverType + ", throwing");
            throw new InvalidOperationException("FieldExtractor: no PatchOpcode rows for patchDate='"
                + patchDate + "' serverType='" + serverType + "'");
        }

        _bagPool = new FieldBagPool(FieldBag.DefaultPoolSize, FieldBag.DefaultSlotCount);

        DebugLog.Write(LogChannel.Network, "FieldExtractor ctor: loaded "
            + _opcodeValuesByName.Count + " opcode names");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadOpcodeMap
    //
    // Populates _opcodeValuesByName from the PatchOpcode table for the active patch level.
    // Multiple versions of the same opcode share the same opcode_value, so SELECT DISTINCT
    // on (opcode_name, opcode_value) yields the correct one-to-one map.  Called once from
    // the constructor.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadOpcodeMap()
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT opcode_name, opcode_value"
            + " FROM PatchOpcode"
            + " WHERE patch_date = @patchDate"
            + " AND server_type = @serverType";
        cmd.Parameters.AddWithValue("@patchDate", _patchDate);
        cmd.Parameters.AddWithValue("@serverType", _serverType);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string opcodeName = reader.GetString(0);
            int opcodeValueRaw = reader.GetInt32(1);
            ushort opcodeValue = (ushort)opcodeValueRaw;
            _opcodeValuesByName[opcodeName] = opcodeValue;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeValue
    //
    // Returns the wire opcode value for the given logical name in the active patch level.
    // Called by handlers at construction time so they know which wire opcode to register
    // for.  Returns 0 if the name is unknown — handlers check for 0 and skip their own
    // registration if the active patch does not define their opcode.  0 is not a valid
    // wire opcode in EQ, so it serves as an unambiguous "not found" sentinel.
    //
    // Parameters:
    //   opcodeName - The logical name (e.g. "OP_PlayerProfile").
    //
    // Returns:
    //   The wire opcode value (e.g. 0x6FA1), or 0 if the name is not in the active patch.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ushort GetOpcodeValue(string opcodeName)
    {
        ushort opcodeValue;
        bool found = _opcodeValuesByName.TryGetValue(opcodeName, out opcodeValue);
        if (found == false)
        {
            DebugLog.Write(LogChannel.Network, "FieldExtractor.GetOpcodeValue: unknown opcode name '"
                + opcodeName + "' in patchDate=" + _patchDate
                + " serverType=" + _serverType + ", returning 0");
            return 0;
        }
        return opcodeValue;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Rent
    //
    // Rents a FieldBag from the extractor's internal pool.  The bag is cleared before being
    // returned so the caller always sees a fresh bag.  The caller must eventually call
    // Release on the bag to return it to the pool.
    //
    // Handlers use this in the hot path to get a bag for filling via Extract, reading,
    // and releasing — the pool itself is an internal detail of FieldExtractor and is not
    // exposed.
    //
    // Returns:
    //   A bag with SlotCount == 0, ready to be filled.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public FieldBag Rent()
    {
        return _bagPool.Rent();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetFieldDefinitions
    //
    // Loads the field definitions for the given opcode name and version in the active patch
    // level, returning them as an array ready for hot-path use.  The encoding string on each
    // PacketField row is resolved to a FieldEncoding via the extractor's own string table
    // during the load; the returned definitions can be passed straight to Extract.
    //
    // Called by handlers at construction time, once per (opcode name, version) pair the
    // handler cares about.  Each call hits the database fresh; the result is not cached
    // inside FieldExtractor — the handler is responsible for storing the array it receives.
    //
    // Returns null if the opcode name is unknown to the active patch.  In practice handlers
    // validate the opcode name first via GetOpcodeValue (checking for the 0 sentinel) and
    // skip the field-definition call entirely if the opcode is missing, so this null path
    // is defensive rather than expected.  A known opcode name with no matching version
    // returns an empty array, not null — that case is more plausibly an intentional probe.
    //
    // Parameters:
    //   opcodeName  - The logical name (e.g. "OP_PlayerProfile").
    //   version     - The version number to load.  Defaults to 1.
    //
    // Returns:
    //   An array of FieldDefinition with encodings resolved, possibly empty.  Null if the
    //   opcode name is not in the active patch.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public FieldDefinition[]? GetFieldDefinitions(string opcodeName, int version = 1)
    {
        if (_opcodeValuesByName.ContainsKey(opcodeName) == false)
        {
            DebugLog.Write(LogChannel.Network, "FieldExtractor.GetFieldDefinitions: unknown opcode name '"
                + opcodeName + "' in patchDate=" + _patchDate
                + " serverType=" + _serverType + ", returning null");
            return null;
        }

        List<FieldDefinition> definitions = new List<FieldDefinition>();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pf.field_name, pf.bit_offset, pf.bit_length, pf.encoding"
            + " FROM PacketField pf"
            + " JOIN PatchOpcode po ON pf.patch_opcode_id = po.id"
            + " WHERE po.patch_date = @patchDate"
            + " AND po.server_type = @serverType"
            + " AND po.opcode_name = @opcodeName"
            + " AND po.version = @version"
            + " ORDER BY pf.bit_offset";
        cmd.Parameters.AddWithValue("@patchDate", _patchDate);
        cmd.Parameters.AddWithValue("@serverType", _serverType);
        cmd.Parameters.AddWithValue("@opcodeName", opcodeName);
        cmd.Parameters.AddWithValue("@version", version);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            FieldDefinition definition;
            definition.Name = reader.GetString(0);
            definition.BitOffset = (uint)reader.GetInt32(1);
            definition.BitLength = (uint)reader.GetInt32(2);
            string encodingString = reader.GetString(3);
            definition.Encoding = GetEncodingId(encodingString);
            definitions.Add(definition);
        }

        DebugLog.Write(LogChannel.Network, "FieldExtractor.GetFieldDefinitions: loaded "
            + definitions.Count + " field(s) for opcodeName='" + opcodeName
            + "' version=" + version);

        return definitions.ToArray();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetEncodingId
    //
    // Translates a database encoding string (e.g. "uint16_le") into the corresponding
    // FieldEncoding enum value.  Called by handler load routines once per FieldDefinition
    // while reading rows from PacketField.  Not called from the hot path.
    //
    // On an unrecognized encoding string, logs and returns FieldEncoding.Unknown.  The handler
    // stores Unknown in the FieldDefinition; Extract silently skips fields with Unknown
    // encoding.  This keeps a single misconfigured row from blocking the rest of the load and
    // from spamming the hot-path log on every packet.
    //
    // Parameters:
    //   encodingString - The encoding column value from a PacketField row.  A null value is
    //                    treated as unrecognized and logged.
    //
    // Returns:
    //   The FieldEncoding enum value if the string is in the table; FieldEncoding.Unknown
    //   otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public FieldEncoding GetEncodingId(string encodingString)
    {
        if (encodingString == null)
        {
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.GetEncodingId: null encodingString, "
                + "returning Unknown");
            return FieldEncoding.Unknown;
        }

        FieldEncoding resolved;
        bool found = _encodingsByString.TryGetValue(encodingString, out resolved);
        if (found == false)
        {
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.GetEncodingId: unrecognized encoding '"
                + encodingString + "', returning Unknown");
            return FieldEncoding.Unknown;
        }

        return resolved;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Extract
    //
    // Walks the field definition array and writes one FieldSlot per definition into the
    // caller-provided FieldBag, preserving definition-to-slot index alignment.  Called from
    // the hot path on every decoded packet.
    //
    // Every definition produces exactly one slot.  Definitions whose decode does not succeed
    // (Unknown encoding, payload too short for the bit range, unsupported bit width, etc.)
    // produce slots that are added but left in their default Empty state.  This keeps cached
    // field indices stable: a handler that caches "hp is field 3" at load time can read
    // bag.GetXxxAt(3) without worrying that an upstream field failed to decode, and detect
    // failure by checking the slot's type tag.
    //
    // All per-field failure paths are silent.  Failures during Inference are the expected
    // case (every handler is tried against every packet) and logging them would flood the
    // log with non-anomalies.  Glass-runtime diagnostics belong in the handler, where the
    // context to know whether a failure is surprising lives.
    //
    // Reentrancy: this method is safe to call concurrently from multiple threads on the same
    // FieldExtractor instance, provided each thread passes its own FieldBag.  The bag is
    // mutated; sharing a bag across threads is not supported.
    //
    // Parameters:
    //   definitions - Array of resolved FieldDefinitions, in the order they should appear in
    //                 the bag.  The array reference is passed; no copy is made.
    //   payload     - The raw packet payload bytes.  Bit offsets in definitions are relative
    //                 to the start of this span.
    //   bag         - The bag to fill.  Must have been rented from a FieldBagPool by the
    //                 caller and must have enough capacity to hold one slot per definition.
    //                 The caller is responsible for releasing the bag when done reading.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Extract(FieldDefinition[] definitions, ReadOnlySpan<byte> payload, FieldBag bag)
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

        for (int definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
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

                case FieldEncoding.UInt8:
                case FieldEncoding.UInt16LE:
                case FieldEncoding.UInt32LE:
                    {
                        ulong raw;
                        if (TryReadBitsLE(payload, definition.BitOffset, definition.BitLength, out raw) == true)
                        {
                            slot.SetUInt32((uint)raw);
                        }
                        break;
                    }

                case FieldEncoding.Int16LE:
                case FieldEncoding.Int32LE:
                    {
                        ulong raw;
                        if (TryReadBitsLE(payload, definition.BitOffset, definition.BitLength, out raw) == true)
                        {
                            int signed = SignExtend(raw, definition.BitLength);
                            slot.SetInt32(signed);
                        }
                        break;
                    }

                case FieldEncoding.UInt16BE:
                case FieldEncoding.Int32BE:
                case FieldEncoding.FloatBE:
                    ExtractBigEndianPlaceholder(payload, definition, ref slot);
                    break;

                case FieldEncoding.FloatLE:
                    ExtractFloatLE(payload, definition, ref slot);
                    break;

                case FieldEncoding.UIntLEMasked:
                    {
                        ulong raw;
                        if (TryReadBitsLE(payload, definition.BitOffset, definition.BitLength, out raw) == true)
                        {
                            slot.SetUInt32((uint)raw);
                        }
                        break;
                    }

                case FieldEncoding.SignExtendFixedDiv8:
                    ExtractSignExtendedDiv8(payload, definition, ref slot);
                    break;

                case FieldEncoding.StringNullTerminated:
                    ExtractNullTerminatedString(payload, definition, ref slot);
                    break;

                case FieldEncoding.StringLengthPrefixed:
                    ExtractLengthPrefixedString(payload, definition, ref slot);
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
    // at the field's bit offset and stores it in the slot.  The bits are read via
    // TryReadBitsLE and reinterpreted as a float without any numeric conversion — the bit
    // pattern is the float.
    //
    // On any read failure the slot is left in its default Empty state.  HasBits has already
    // confirmed the payload has enough bytes; failures here would mean the bit length is
    // out of range for TryReadBitsLE, which for a properly defined float field (BitLength
    // of 32) cannot happen.
    //
    // Parameters:
    //   payload     - The packet payload being decoded.
    //   definition  - The field definition.  BitLength should be 32 for a standard IEEE
    //                 single; other widths are read but the result is undefined.
    //   slot        - The slot to fill.  Already has its name set.  Stays Empty on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ExtractFloatLE(ReadOnlySpan<byte> payload, FieldDefinition definition,
        ref FieldSlot slot)
    {
        ulong raw;
        if (TryReadBitsLE(payload, definition.BitOffset, definition.BitLength, out raw) == false)
        {
            return;
        }

        int bits = (int)raw;
        float value = BitConverter.Int32BitsToSingle(bits);
        slot.SetFloat(value);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractBigEndianPlaceholder
    //
    // Placeholder handler for the big-endian encodings (UInt16BE, Int32BE, FloatBE).  These
    // exist in the database but their semantics for non-byte-aligned fields have not yet
    // been verified against actual packet data — specifically, whether "BE" means byte-BE
    // (assemble bits as a stream then byte-swap) or bit-BE (most-significant-bit first
    // within the bit range).  Until a real BE field has been examined and the convention
    // confirmed, this method does nothing and the slot stays in its default Empty state.
    //
    // When BE is implemented, this method should split into per-encoding helpers
    // (ExtractUInt16BE, ExtractInt32BE, ExtractFloatBE) following the same pattern as the
    // LE helpers.  The single placeholder is intentional — it makes the unimplemented
    // status visible in one place rather than scattered across three.
    //
    // Parameters:
    //   payload     - The packet payload being decoded.  Currently unused.
    //   definition  - The field definition.  Currently unused.
    //   slot        - The slot to fill.  Left in its default Empty state.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ExtractBigEndianPlaceholder(ReadOnlySpan<byte> payload, FieldDefinition definition,
        ref FieldSlot slot)
    {
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractSignExtendedDiv8
    //
    // Reads a sign-extended fixed-point coordinate value at the field's bit offset and bit
    // length, then divides by 8.0 to convert to a floating-point world coordinate.  Used
    // by MobUpdate / NpcMoveUpdate position fields, which pack each coordinate as a
    // signed integer (commonly 19 bits) in units of 1/8 world unit.
    //
    // On any read failure the slot is left in its default Empty state.
    //
    // Parameters:
    //   payload     - The packet payload being decoded.
    //   definition  - The field definition.  BitOffset and BitLength describe the packed
    //                 signed integer; the divide by 8 is implicit in the encoding.
    //   slot        - The slot to fill.  Already has its name set.  Stays Empty on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ExtractSignExtendedDiv8(ReadOnlySpan<byte> payload, FieldDefinition definition,
        ref FieldSlot slot)
    {
        ulong raw;
        if (TryReadBitsLE(payload, definition.BitOffset, definition.BitLength, out raw) == false)
        {
            return;
        }

        int signedValue = SignExtend(raw, definition.BitLength);
        float worldCoord = signedValue / 8.0f;
        slot.SetFloat(worldCoord);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractNullTerminatedString
    //
    // Reads an ASCII string starting at the field's byte offset, terminated by a null byte.
    // BitLength is ignored for this encoding; the null byte determines the string length.
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
    //   payload     - The packet payload being decoded.
    //   definition  - The field definition.  BitOffset is used; BitLength is ignored.
    //   slot        - The slot to fill.  Already has its name set.  Stays Empty on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ExtractNullTerminatedString(ReadOnlySpan<byte> payload, FieldDefinition definition,
        ref FieldSlot slot)
    {
        uint byteOffset = definition.BitOffset / 8u;
        if (byteOffset >= (uint)payload.Length)
        {
            return;
        }

        ReadOnlySpan<byte> tail = payload.Slice((int)byteOffset);
        int nullIndex = tail.IndexOf((byte)0x00);
        if (nullIndex < 0)
        {
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExtractNullTerminatedString: field '"
                + definition.Name + "' at byteOffset " + byteOffset
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
    // Reads a uint32 little-endian length prefix at the field's byte offset, then reads
    // that many bytes immediately following as an ASCII string.  BitLength is ignored for
    // this encoding; the length prefix determines the string length.
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
    //   payload     - The packet payload being decoded.
    //   definition  - The field definition.  BitOffset is used; BitLength is ignored.
    //   slot        - The slot to fill.  Already has its name set.  Stays Empty on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ExtractLengthPrefixedString(ReadOnlySpan<byte> payload, FieldDefinition definition,
        ref FieldSlot slot)
    {
        uint byteOffset = definition.BitOffset / 8u;
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
}
