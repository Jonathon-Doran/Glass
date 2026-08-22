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

    public ItemId Id { get; set; } = ItemId.None;

    // Sub and aug positions are 0xFFFF when absent.
    public StorageSystem Storage { get; set; }
    public uint MainPosition { get; set; }
    public uint SubPosition { get; set; }
    public uint AugPosition { get; set; }

    // Per-instance state
    public uint StackSize { get; set; }
    public uint RemainingCharges { get; set; }

    // Children associated with this instance (augments, contents of containers)
    public List<ItemInstance> Children { get; set; } = new List<ItemInstance>();
}