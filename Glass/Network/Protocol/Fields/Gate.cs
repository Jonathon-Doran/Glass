namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// Gate
//
// One node in the tree of collection instances built during a single extraction.  Carries
// the decoding policy lifted from its GateDefinition at creation — the multiplicity kind, the
// child collection to decode, and the resolved instance count — so the extraction runtime
// reads everything it needs from the gate and never consults the definition again.
//
// Kind is the multiplicity rule.  ChildCollection is the collection each instance decodes.
// Count is the concrete number of instances, resolved when the gate is created: the count
// field's value read from the resolved bag for a Times gate.  Count is unused for a Once gate,
// which decodes a single instance, and for an UntilEnd gate, which terminates positionally.
//
// ParentBag is the bag instance that spawned this gate — the instance being decoded when the
// gate's field was reached.  It is the upward step of the resolution spine: a gate reaches its
// enclosing instance through ParentBag, and an enclosing instance reaches its own ancestor
// through that bag's ParentGate, alternating bag and gate to the root.  ParentBag is None for a
// top-level gate.
//
// NextSibling is the next gate in the chain of child gates spawned by a single bag instance.
// The spawning bag holds the chain's head; each gate holds this forward link to the next.
// NextSibling is None for the last gate in the chain.
//
// Head and Tail bound the chain of instance bags this gate decoded, linked through each
// FieldBag's own forward link.  The gate produces one bag per instance, appended at Tail as it
// is decoded; the final count is known only when decoding stops.  Head and Tail are both None
// for a gate with no instances yet.
//
// Definition is retained for now as a fallback path back to the gate's static definition; the
// runtime does not read it.
///////////////////////////////////////////////////////////////////////////////////////////////
public struct Gate
{
    public GateDefinitionHandle Definition;
    public CollectionHandle ChildCollection;
    public MultiplicityKind Kind;
    public BagHandle ParentBag;
    public GateHandle NextSibling;
    public uint BagCount;
    public BagHandle Head;
    public BagHandle Tail;
    public GateHandle Self;
    public uint BitsConsumed;
}