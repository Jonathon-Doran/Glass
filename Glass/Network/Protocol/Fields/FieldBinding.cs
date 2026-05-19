namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldBinding
//
// One name/value pair returned by FieldBag's iterator.  Produced by the bag
// for each filled slot when a caller walks the bag for display.  The bag
// formats slot values internally; callers receive already-formatted
// strings and compose their own layout.
//
// Held by value.  Name and Value are non-null; the bag never yields a
// binding for an unfilled slot.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly struct FieldBinding
{
    public string Name { get; }
    public string Value { get; }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FieldBinding (constructor)
    //
    // name:   Field name from the slot's definition.
    // value:  Formatted string representation of the slot's value.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public FieldBinding(string name, string value)
    {
        Name = name;
        Value = value;
    }
}
