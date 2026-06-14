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
    Float,
    UIntMasked,
    CsvToken,
    SignMagnitudeLsb,
    SignMagnitudeMsb,
    OptSignMagnitudeMsb,
    StringNullTerminated,
    StringLengthPrefixed,
    OptionalGroup,
    Gate
}
