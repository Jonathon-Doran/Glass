namespace Glass.Data.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// ItemInstance
//
// One item belonging to a character.  Children are the items inside this one:
// bag contents and socketed augments.
///////////////////////////////////////////////////////////////////////////////////////////////
public class ItemInstance
{
    public ItemInstanceId InstanceId { get; set; } = ItemInstanceId.None;

    // Parent instance, set if this item is a child of another item
    public ItemInstance? Parent { get; set; }
    public ItemId Id { get; set; } = ItemId.None;                           // 34

    // Location of this instance.  None until the instance has been placed.
    public ItemLocation Location { get; set; } = ItemLocation.None;         // 4,5,6

    public Boolean IsAttuned { get; set; }                                  // 13
    // Per-instance state
    public uint StackSize { get; set; }                                     // 2
    public uint RemainingCharges { get; set; }                              // 12
    public Boolean IsCopied { get; set; }                                   // 26


    // Children associated with this instance (augments, contents of containers)
    public List<ItemInstance> Children { get; set; } = new List<ItemInstance>();
}