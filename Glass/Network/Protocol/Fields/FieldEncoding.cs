namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldEncoding
//
// The set of decode operations the extractor knows how to perform.  Each value corresponds
// to a row in the FieldExtractor's encoding string table.  Database rows store the encoding
// as a human-readable string (e.g. "uint16_le"); handlers translate that string to this
// enum once at load time via FieldExtractor.GetEncodingId so the hot path can dispatch on
// an integer rather than a string.
//
// Unknown is the default value (zero) and is what GetEncodingId returns when an encoding
// string is not recognized.  Field definitions carrying Unknown are silently skipped at
// extraction time; the load-time log is the canonical record of the misconfiguration.
//
// New encodings are added here, in the FieldExtractor's string table, and in Extract's
// dispatch switch — all three sites must agree.
///////////////////////////////////////////////////////////////////////////////////////////////
public enum FieldEncoding
{
    Unknown = 0,
    UInt,
    Int,
    UIntMsb,
    Float,
    UIntMasked,
    CsvToken,
    SignMagnitudeLsb,
    SignMagnitudeMsb,
    OptSignMagnitudeMsb,
    StringNullTerminated,
    StringLengthPrefixed,
    OptionalGroup
}
