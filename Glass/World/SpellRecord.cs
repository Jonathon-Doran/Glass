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

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// SpellTargetType
//
// Target type values from spells_us.txt field 30. Determines what a spell may be cast upon and how
// recipients are selected. Values are the raw wire/file encodings.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public enum SpellTargetType : uint
{
    LineOfSight = 1,
    AePcV1 = 2,
    CasterGroup = 3,
    PointBlankAe = 4,
    Single = 5,
    Self = 6,
    TargetedAe = 8,
    Animal = 9,
    Undead = 10,
    Summoned = 11,
    Lifetap = 13,
    Pet = 14,
    Corpse = 15,
    Plant = 16,
    UberGiants = 17,
    UberDragons = 18,
    UndeadL55Max = 21,
    NpcHatelist = 32,
    NpcHatelist2 = 33,
    Chest = 34,
    PcOnly = 36,
    AreaNpcOnly = 37,
    SummonedPet = 38,
    NearbyPlayers = 40,
    TargetGroup = 41,
    DirectionalAe = 42,
    SinglePartyMember = 43,
    Beam = 44,
    TargetedAe2 = 45,
    TargetsTarget = 46,
    PetOwner = 47,
    TargetAeNotPlayerPet = 50,
    SingleFriendlyOrSelf = 51,
    SingleFriendlyOrTargetsTarget = 52
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// SpellCastRestriction
//
// Cast restriction values from spells_us.txt field 136. A nonzero value further constrains what the
// spell may be cast upon beyond the target type. Values are the raw file encodings; the value space
// is sparse and undefined values may appear in future patches.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public enum SpellCastRestriction : uint
{
    None = 0,
    AnimalOrHumanoid = 100,
    AnimalOrInsect = 102,
    Animal = 104,
    Plant = 105,
    Bixie = 109,
    Harpy = 110,
    Gnoll = 111,
    Sporali = 112,
    Kobold = 113,
    Shade = 114,
    Drakkin = 115,
    AnimalOrPlant = 117,
    Summoned = 118,
    FirePet = 119,
    Undead = 120,
    Living = 121,
    Fairy = 122,
    UndeadHpBelow10Pct = 124,
    ClockworkHpBelow45Pct = 125,
    WispHpBelow10Pct = 126,
    MeleeClassExceptBard = 127,
    PureMeleeClass = 128,
    PureCasterClass = 129,
    HybridClass = 130,
    NotWarriorPaladinShadowKnight = 148,
    NotRaidBoss = 190,
    HpBelow20Pct = 203,
    NotInCombat = 216,
    HpBelow35Pct = 250,
    ChainAndPlateClasses = 304,
    HpBelow35Pct2 = 507,
    HpBelow45Pct = 509,
    Humanoid = 601,
    Undead2 = 603,
    Dragon = 626,
    Npc = 700,
    NotPet = 701,
    Treant = 815,
    Bixie2 = 816,
    Scarecrow = 817,
    Undead3 = 818,
    NotUndead = 819,
    KnightAndHybridMeleeClasses = 820,
    WarriorCasterPriestClasses = 821,
    HasCrystallizedFlameBuff = 845,
    HasIncendiaryOozeBuff = 847,
    EsiantiAccess = 997
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

    // Target type from file column 30 — what the spell may be cast upon.
    public SpellTargetType TargetType { get; set; }

    // Cast restriction from file column 136 — additional castability constraint, None when unrestricted.
    public SpellCastRestriction CastRestriction { get; set; }
}
