namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// Gate
//
// One node in the tree of collection instances built during a single extraction.  Holds the
// definition it was built from, its links to neighboring nodes, the bag its collection
// decoded into, and the number of bits its collection consumed from the packet.
//
// Tree links are GateHandles into the FieldExtractor's gate list; the bag link is a
// BagHandle into that extractor's bag store.  A link of None terminates that direction:
// Parent is None at the root, FirstChild is None for a leaf, NextSibling is None for the
// last child in a sibling chain.
///////////////////////////////////////////////////////////////////////////////////////////////
public struct Gate
{
    public GateDefinitionHandle Definition;
    public GateHandle Parent;
    public GateHandle FirstChild;
    public GateHandle NextSibling;
    public BagHandle Bag;
    public GateHandle Self;
    public uint BitsConsumed;
}
