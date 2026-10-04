namespace Glass.Data.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// ActiveSpell
//
// One spell active on a character, as held in a spell slot. 
///////////////////////////////////////////////////////////////////////////////////////////////
public class ActiveSpell
{
    public uint Position { get; set; }
    public SpellId SpellId { get; set; } = SpellId.None;
    public uint CasterId { get; set; }
    public uint CasterLevel { get; set; }
    public uint RemainingTicks { get; set; }
    public uint TotalTicks { get; set; }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ActiveSpell (constructor)
    //
    // Builds an active spell from the values held in one spell slot.
    //
    // position:        The spell position holding the spell.
    // spellId:         The spell affecting the character.
    // casterId:        Identifier of the character who cast the spell.
    // casterLevel:     Level of the caster when the spell was cast.
    // totalTicks:      Full duration of the spell, in ticks.
    // remainingTicks:  Ticks left before the spell ends.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public ActiveSpell(uint position, SpellId spellId, uint casterId, uint casterLevel, uint totalTicks,
        uint remainingTicks)
    {
        Position = position;
        SpellId = spellId;
        CasterId = casterId;
        CasterLevel = casterLevel;
        TotalTicks = totalTicks;
        RemainingTicks = remainingTicks;
    }
}