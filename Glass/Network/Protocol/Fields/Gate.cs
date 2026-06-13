namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// Gate
//
// One node of an extraction's gate tree: the runtime record of one decoded collection
// instance.  A Times-N gate produces N sibling Gates, one per iteration, each holding
// the bag its iteration filled.
//
// Parent, FirstChild, and NextSibling are indices into the GateTree that owns this
// node; they are meaningless outside that tree.  Sibling order is wire order.
// GateTree.NoneIndex marks an absent link.
//
// Fully blittable: six 4-byte members, no references.
///////////////////////////////////////////////////////////////////////////////////////////////
public struct Gate
{
    public GateDefinitionHandle Definition;
    public uint Parent;
    public uint FirstChild;
    public uint NextSibling;
    public BagHandle Bag;
    public uint BitsConsumed;
}
