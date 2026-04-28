using Glass.Core.Logging;

namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldType
//
// Identifies the data type stored in a FieldSlot.  The slot's inline payload buffer is
// interpreted according to this tag.  Empty indicates an unused slot.
//
// Numeric types are stored in their native byte representation in the payload buffer.
// AsciiString stores the bytes directly with the byte count in the slot's Length field;
// the bytes are NOT null-terminated in the slot.
///////////////////////////////////////////////////////////////////////////////////////////////
public enum FieldType : byte
{
    Empty = 0,
    Int8 = 1,
    UInt8 = 2,
    Int16 = 3,
    UInt16 = 4,
    Int32 = 5,
    UInt32 = 6,
    Int64 = 7,
    UInt64 = 8,
    Float = 9,
    AsciiString = 10,
    Bytes = 11
}