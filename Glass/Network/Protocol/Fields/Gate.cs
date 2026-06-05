namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// MultiplicityKind
//
// The multiplicity rule a Gate applies to its child FieldCollection.  Stored on the Gate
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
// Gate -- data structure that takes us from one Collection to another
//
// Static policy describing how a field references a child FieldCollection and the
// multiplicity rule applied to it.  Built once at load time and read-only thereafter.
//
// Kind selects the multiplicity rule.  ChildCollection identifies the child FieldCollection
// to decode.  FieldSlot is the resolved index of the field a kind consults; it is
// SlotId.None when the gate consults no field.
///////////////////////////////////////////////////////////////////////////////////////////////
public struct Gate
{
    public string Name;
    public MultiplicityKind Kind;
    public CollectionHandle ChildCollection;
    public SlotId FieldSlot;
}