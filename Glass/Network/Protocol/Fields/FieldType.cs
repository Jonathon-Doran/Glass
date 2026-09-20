using Glass.Core.Logging;

namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldType
//
// Identifies the data type stored in a FieldSlot.  Empty indicates an unused slot.
///////////////////////////////////////////////////////////////////////////////////////////////
public enum FieldType : byte
{
    Empty = 0,
    Int = 1,
    UInt = 2,
    Int64 = 3,
    UInt64 = 4,
    Float = 5,
    Double = 6,
    AsciiString = 7,
    Gate = 8,
    Blob = 9,
    UIntArray = 10
}