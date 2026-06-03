namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// MultiplicityKind
//
// The multiplicity rule a Multiplicity applies to its child FieldCollection.  Stored on the Multiplicity
// and read by the FieldExtractor to select which decode loop to run.
//
// Always decodes the child exactly once.  Times decodes it a number of times read from a
// field.  UntilEnd decodes it until the payload is exhausted.
///////////////////////////////////////////////////////////////////////////////////////////////
public enum MultiplicityKind
{
    Once,
    Times,
    UntilEnd
}

///////////////////////////////////////////////////////////////////////////////////////////////
// Multiplicity
//
// Static policy describing how a field references a child FieldCollection and the
// multiplicity rule applied to it.  Built once at load time and read-only thereafter.
//
// Kind selects the multiplicity rule.  ChildCollection identifies the child FieldCollection
// to decode.  FieldSlot is the resolved index of the field a kind consults; it is
// FieldIndex.None when the kind consults no field.
///////////////////////////////////////////////////////////////////////////////////////////////
public struct Multiplicity
{
    public string Name;
    public MultiplicityKind Kind;
    public CollectionHandle ChildCollection;
    public FieldIndex FieldSlot;
}