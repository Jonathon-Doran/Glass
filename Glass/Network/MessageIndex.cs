namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////////
// MessageIndex
//
// Handle naming one decoded application-layer message by its arrival position in the capture.
// Wraps a uint so the compiler can distinguish a message index from other integers in method
// signatures, while implicit conversion to uint keeps indexing and other uint-typed call sites
// free of .Value noise.  Construction from a raw uint requires an explicit cast, which keeps the
// origin of every index deliberate.
//
// Value uint.MaxValue is reserved to mean "no message": used where a message index is absent,
// such as a cursor that names no row on an empty trace.  None exposes the reserved value and
// Exists tests against it; call sites check Exists before treating the value as a real arrival
// position.
///////////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct MessageIndex(uint Value)
{
    public static implicit operator uint(MessageIndex index) => index.Value;
    public static explicit operator MessageIndex(uint value) => new(value);

    public const uint NoneValue = uint.MaxValue;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // None
    //
    // The reserved index meaning "no message".
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static MessageIndex None
    {
        get { return new MessageIndex(NoneValue); }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Exists
    //
    // True when this index is not the reserved "no message" value.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool Exists
    {
        get { return Value != NoneValue; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ToString
    //
    // Renders the index as its numeric value, or "None" for the sentinel.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override string ToString()
    {
        if (Exists == false)
        {
            return "None";
        }
        return Value.ToString();
    }
}