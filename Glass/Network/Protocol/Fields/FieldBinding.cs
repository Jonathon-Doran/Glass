namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldBinding
//
// One name/value pair returned by FieldBag's iterator.  Produced by the bag for each filled
// slot when a caller walks the bag for display.  The bag formats slot values internally;
// callers receive already-formatted strings and compose their own layout.
//
// Held by value.  A binding that exists carries a non-null Name and Value.  The None binding
// is the absence of a pair, returned by the iterator when no filled slot remains; its Exists
// is false and its Name and Value are null.  Callers test Exists before reading Name or Value.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct FieldBinding(string Name, string Value)
{
    ///////////////////////////////////////////////////////////////////////////////////////////
    // Exists
    //
    // True when this binding holds a field.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool Exists
    {
        get { return Name != null; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ToString
    //
    // Renders the binding as "Name = Value", or "None" for the sentinel.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public override string ToString()
    {
        if (Exists == false)
        {
            return "None";
        }
        return Name + " = " + Value;
    }
}
