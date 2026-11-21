namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// PredicateOp
//
// The test a FieldPredicate applies between a source field's value and its operands to
// decide whether the predicated field is present.
//
// None is the default value (zero) and means the field carries no predicate and is
// unconditionally present.  BitmaskNonZero treats the single operand as a mask and is
// satisfied when (source & operand) is non-zero.  Equal, NotEqual, Greater, GreaterOrEqual,
// Less, and LessOrEqual compare the source value against the single operand directly.  In is
// satisfied when the source value equals any one of the operands.
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
    LessOrEqual,
    In
}

///////////////////////////////////////////////////////////////////////////////////////////////
// PredicateOperandKind
//
// The type of a single predicate operand, which selects how its stored value is read.
//
// None is the default value (zero) and marks an operand that was never set.  UInt, Int,
// UInt64, and Int64 are integers held in the operand's raw bits.  Float and Double are
// floating values held in the operand's raw bits.  String is text held in the operand's
// text member.
///////////////////////////////////////////////////////////////////////////////////////////////
public enum PredicateOperandKind
{
    None = 0,
    UInt,
    Int,
    UInt64,
    Int64,
    Float,
    Double,
    String
}

///////////////////////////////////////////////////////////////////////////////////////////////
// PredicateOperand
//
// One constant from a predicate, tagged with its type.
//
// Kind selects how the value is read.  Bits holds every numeric kind by its raw bits: a
// 32-bit value occupies the low 32 bits, a 64-bit value occupies all 64, and a floating value
// is stored as the bit pattern of the float or double.  Text holds the value when Kind is
// String and is null for every other kind.
///////////////////////////////////////////////////////////////////////////////////////////////
public struct PredicateOperand
{
    public PredicateOperandKind Kind;
    public ulong Bits;
    public string? Text;
}

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldPredicate
//
// Field-level presence condition.  A field carries one when its presence depends on the
// value of another field decoded earlier in the same collection.  The field is decoded
// only when the predicate holds; otherwise its slot is left empty.  A field whose Op is
// None carries no predicate and is unconditionally present.
//
// SourceSlot is the resolved slot of the field whose value is tested.  Op selects the
// comparison.  Negated inverts the result of the comparison, so a predicate holds when the
// comparison fails.
//
// Operands holds the parsed constants, sized to the number of values written in the
// predicate.  Every operator other than In uses exactly one operand; In uses all of them.
// Operands is null when Op is None.
//
// This is static policy known before any packet arrives; it reads no payload and performs
// no decode.
///////////////////////////////////////////////////////////////////////////////////////////////
public struct FieldPredicate
{
    public SlotId SourceSlot;
    public PredicateOp Op;
    public bool Negated;
    public PredicateOperand[]? Operands;
}