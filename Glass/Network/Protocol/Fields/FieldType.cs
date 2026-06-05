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
    Float = 3,
    AsciiString = 4,
    Bytes = 5,
    Gate = 6
}