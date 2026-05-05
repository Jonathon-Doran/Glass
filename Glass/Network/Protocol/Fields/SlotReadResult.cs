namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// SlotReadResult
//
// Outcome of a slot read.  Returned by FieldSlot's TryGet methods alongside the value out
// parameter, so the caller can distinguish a successful read from a failure and react
// accordingly.  FieldBag's accessors translate this into log lines with opcode context.
///////////////////////////////////////////////////////////////////////////////////////////////
public enum SlotReadResult
{
    Success,
    TypeMismatch
}
