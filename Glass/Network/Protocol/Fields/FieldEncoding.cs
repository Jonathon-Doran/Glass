namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldEncoding
//
// The set of decode operations the extractor knows how to perform.
//
///////////////////////////////////////////////////////////////////////////////////////////////
public enum FieldEncoding
{
    Unknown = 0,
    UInt,
    Int,
    UIntMsb,
    UIntArray,
    Int64,
    UInt64,
    Float,
    Double,
    UIntMasked,
    CsvToken,
    SignMagnitudeLsb,
    SignMagnitudeMsb,
    OptSignMagnitudeMsb,
    StringNullTerminated,
    StringLengthPrefixed,
    Gate,
    Blob
}
