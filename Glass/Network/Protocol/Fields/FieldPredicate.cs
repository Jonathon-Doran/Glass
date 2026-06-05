namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// PredicateOp
//
// The test a FieldPredicate applies between a source field's value and an operand to decide
// whether the predicated field is present.  Stored on the FieldPredicate and read by the
// FieldExtractor to select the comparison.
//
// None is the default value (zero) and means the field carries no predicate and is
// unconditionally present; the FieldExtractor skips predicate evaluation for it.
// BitmaskNonZero treats the operand as a mask and is satisfied when (source & operand) is
// non-zero.  Equal, NotEqual, Greater, GreaterOrEqual, Less, and LessOrEqual compare the
// source value against the operand directly.
//
///////////////////////////////////////////////////////////////////////////////////////////////
public enum PredicateOp
{
    None = 0,
    BitmaskNonZero,
    Equal,
    NotEqual,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual
}

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldPredicate
//
// Field-level presence condition.  A field carries one when its presence depends on the
// value of another field decoded earlier in the same collection.  The field is decoded
// only when the predicate holds; otherwise its slot is left empty.  A field whose Op is
// None carries no predicate and is unconditionally present.
//
// SourceSlot is the resolved slot index of the field whose value is tested, resolved at
// load via IndexOfField the same way relative anchors and gate count fields are resolved.
// Op selects the comparison.
//
// Operand and SignedOperand hold the same parsed constant under two interpretations, so the
// comparison runs in the source's own signedness without reinterpreting bits across the sign
// boundary.  BitmaskNonZero, Equal, and NotEqual test the raw bits and use Operand, read as
// unsigned.  Greater, GreaterOrEqual, Less, and LessOrEqual compare magnitude order and use
// SignedOperand, read as signed.  Carrying both is necessary because a single field cannot
// represent both a full-range unsigned mask and a signed ordered bound without loss.
//
// This is static policy known before any packet arrives; it reads no payload and performs
// no decode.  The FieldExtractor reads the source slot from the bag and applies Op against
// Operand to decide presence.
///////////////////////////////////////////////////////////////////////////////////////////////
public struct FieldPredicate
{
    public SlotId SourceSlot;
    public PredicateOp Op;
    public uint Operand;
    public int SignedOperand;
}
