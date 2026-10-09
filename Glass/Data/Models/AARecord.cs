using Glass.World;

namespace Glass.Data.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// AAEffect
//
// One effect of an alternate advancement ability.  Slot is the effect's position number
// within the ability, starting at 1.  Spa identifies the effect and Base is its primary
// value.  The three unknown values are stored as raw wire values, uninterpreted.
///////////////////////////////////////////////////////////////////////////////////////////////
public class AAEffect
{
    public uint Slot { get; set; }
    public SPAId Spa { get; set; }
    public int Base { get; set; }
    public uint Unknown_1 { get; set; }
    public uint Unknown_2 { get; set; }
    public uint Unknown_3 { get; set; }
}

///////////////////////////////////////////////////////////////////////////////////////////////
// AARecord
//
// One alternate advancement ability.  Id is AAId.None until a real identifier is assigned.
// Name and Description hold display text and are empty when no text is known.  Effects
// holds the ability's effects in slot order and is empty for an ability with none.
///////////////////////////////////////////////////////////////////////////////////////////////
public class AARecord
{
    public AAId Id { get; set; } = AAId.None;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public uint RequiredLevel { get; set; }
    public uint Cost { get; set; }
    public uint Seq { get; set; }
    public List<AAEffect> Effects { get; set; } = new List<AAEffect>();
}