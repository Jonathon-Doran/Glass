namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// OptionalGroup
//
// Per-opcode metadata describing the contents of a flag-controlled optional block.  One
// instance per opcode that has an optional block; opcodes without one have a null entry
// in PatchData's parallel optional-group array.
//
// Loaded once at PatchData construction from PacketOptionalGroup and PacketOptionalField.
// Read-only afterward; safe to use from any thread without locking.
//
// FlagSlotIndex is the bag slot index where the flag word lives, resolved from
// PacketOptionalGroup.flag_field_name during LoadFields.  The extractor's optional-block
// helper reads from that slot directly with no name lookup on the hot path.
//
// SubFields holds the optional sub-fields in sequence_order from PacketOptionalField.
// The flag bit gating each sub-field is implicit in its position: index 0 is gated by
// bit 0 of the flag word, index 1 by bit 1, and so on.
///////////////////////////////////////////////////////////////////////////////////////////////
public class OptionalGroup
{
    public int FlagSlotIndex;
    public uint FlagBitLength;
    public OptionalSubField[] SubFields;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OptionalGroup (constructor)
    //
    // Parameters:
    //   flagSlotIndex - The bag slot index of the flag word, resolved from the flag field
    //                   name during LoadFields via PatchData.IndexOfField.
    //   subFields     - The optional sub-fields in sequence_order from the database.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OptionalGroup(int flagSlotIndex, uint flagBitLength, OptionalSubField[] subFields)
    {
        FlagSlotIndex = flagSlotIndex;
        FlagBitLength = flagBitLength;
        SubFields = subFields;
    }
}

///////////////////////////////////////////////////////////////////////////////////////////////
// OptionalSubField
//
// One sub-field within an optional block.  Carries the slot index to write to, the bit
// length to consume, and the encoding for the helper to dispatch on.
//
// SlotIndex is the bag slot index for this sub-field, resolved during LoadFields via
// PatchData.IndexOfField using the sub-field's name.  Same hot-path principle as
// OptionalGroup.FlagSlotIndex — name lookup happens at load time, not per packet.
// Divisor is used to scale integer values to create sign-magnitude floats.
///////////////////////////////////////////////////////////////////////////////////////////////
public struct OptionalSubField
{
    public int SlotIndex;
    public uint BitLength;
    public FieldEncoding Encoding;
    public float Divisor;
    public string Name;
}