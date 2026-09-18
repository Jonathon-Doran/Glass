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
    public ItemId Id { get; set; } = ItemId.None;

    // Location of this instance.  None until the instance has been placed.
    public ItemPosition Position { get; set; } = ItemPosition.None;

    // Per-instance state
    public uint StackSize { get; set; }
    public uint RemainingCharges { get; set; }

    // Children associated with this instance (augments, contents of containers)
    public List<ItemInstance> Children { get; set; } = new List<ItemInstance>();
}