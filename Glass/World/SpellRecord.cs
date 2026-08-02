namespace Glass.World;

///////////////////////////////////////////////////////////////////////////////////////////////
// SpellEffect
//
// One effect slot of a spell: the SPA number and its scaling inputs, stored exactly as
// parsed from the spell data file.  Base1 is the effect's primary value.  Base2 is
// SPA-specific auxiliary data, stored uninterpreted.  Calc selects the level-scaling
// formula applied to Base1; Max bounds the scaled magnitude, with zero meaning uncapped.
///////////////////////////////////////////////////////////////////////////////////////////////
public struct SpellEffect
{
    public uint Slot;
    public uint Spa;
    public int Base1;
    public int Base2;
    public uint Calc;
    public int Max;
}

///////////////////////////////////////////////////////////////////////////////////////////////
// SpellRecord
//
// The retained fields of one spell from the spell data file.  Times are in milliseconds
// as stored in the file.  DurationTicks is the buff duration in six-second ticks, zero
// for instant effects; DurationFormula is the level-scaling formula the client applies
// to it.  Effects holds only the effect slots whose SPA passed the parse-time whitelist,
// in slot order; a spell whose effects were all filtered has an empty array.
///////////////////////////////////////////////////////////////////////////////////////////////
public class SpellRecord
{
    public uint Id;
    public string Name = string.Empty;
    public uint Range;
    public uint CastTimeMs;
    public uint RecastTimeMs;
    public uint DurationFormula;
    public uint DurationTicks;
    public uint Mana;
    public SpellEffect[] Effects = Array.Empty<SpellEffect>();
}
