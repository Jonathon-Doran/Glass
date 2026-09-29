namespace Glass.Data.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// ItemEffectCategory
//
// The kind of effect an item grants.  Each value corresponds to one pairing of effect
// position and effect type on the wire.  Values are stored in the database and must not
// be renumbered.
///////////////////////////////////////////////////////////////////////////////////////////////
public enum ItemEffectCategory : uint
{
    Clicky = 1,                     // Effect 1, type 1
    Expendable = 2,                 // Effect 1, type 3
    ClickyEquipRestricted = 3,      // Effect 1, type 5
    Proc = 4,                       // Effect 2, type 0
    Worn = 5,                       // Effect 3, type 2
    Focus = 6,                      // Effect 4, type 6
    Scribable = 7,                  // Effect 5, type 7
    PetIllusion = 8,                // Effect 5, type 12
    BardFocus = 9                   // Effect 6, type 8
}