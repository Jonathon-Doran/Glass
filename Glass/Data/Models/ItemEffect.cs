namespace Glass.Data.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// ItemEffect
//
// One effect granted by an item, as decoded from an effect stride of the item
// serialization.  The effect type is stored as the raw wire value until the
// value mapping is verified.
///////////////////////////////////////////////////////////////////////////////////////////////
public class ItemEffect
{
    public SpellId SpellId { get; set; } = SpellId.None;
    public string Name { get; set; } = string.Empty;
    public uint EffectType { get; set; }
    public uint Level { get; set; }
    public uint CastAsLevel { get; set; }
    public uint MaxCharges { get; set; }
    public uint CastTimeMs { get; set; }
    public uint RecastTimeSeconds { get; set; }
    public uint RecastType { get; set; }
    public uint RecastDelaySeconds { get; set; }
}