using Glass.Core;

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
// GateDefinition -- data structure that takes us from one Collection to another
//
// Static policy describing how a field references a child FieldCollection and the
// multiplicity rule applied to it.  Built once at load time and read-only thereafter.
//
// Kind selects the multiplicity rule.  ChildCollection identifies the child FieldCollection
// to decode.  FieldSlot is the resolved index of the field a kind consults; it is None when the gate consults no field.
///////////////////////////////////////////////////////////////////////////////////////////////
public struct GateDefinition
{
    public string Name;
    public PatchLevel PatchLevel;
    public MultiplicityKind Kind;
    public uint Count;
    public CollectionHandle ChildCollection;
    public SlotId FieldSlot;
    public Boolean FieldSlotLocal;

    // The bare count field name for a local Times gate whose slot cannot be resolved at
    // load time.  Non-empty when FieldSlot is None and FieldSlotLocal is true; empty
    // otherwise.
    public string CountFieldName;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ToString
    //
    // Renders the gate as a single log-friendly line: name, multiplicity kind, count,
    // child collection, and the consulted field slot with its scope.  Scope is local when
    // the slot's collection is None and ancestor-qualified otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public override string ToString()
    {
        string scope;
        if (FieldSlotLocal == true)
        {
            scope = "local";
        }
        else
        {
            scope = "parent";
        }

        PatchRegistry registry = GlassContext.PatchRegistry;

        return "Gate '" + Name
            + "' kind=" + Kind
            + " count=" + Count
            + " child=" + registry.GetCollectionName(ChildCollection)
            + " fieldSlot=" + FieldSlot
            + " countFieldName=" + CountFieldName
            + " (" + scope + ")";
    }
}