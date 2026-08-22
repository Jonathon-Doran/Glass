using Glass.Core.Logging;

namespace Glass.Data.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// WornItem
//
// One item worn by a character.  Holds the identity of the item and the
// hitpoint adjustment it grants while worn.
///////////////////////////////////////////////////////////////////////////////////////////////
public class WornItem
{
    public ItemId ItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public WornPosition WornPosition { get; set; } = WornPosition.None;
    public int DeltaHP { get; set; }
}
