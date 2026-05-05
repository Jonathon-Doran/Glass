namespace Glass.Data.Models;

public enum EQClass
{
    Warrior = 1,
    Cleric,
    Paladin,
    Ranger,
    Shadowknight,
    Druid,
    Monk,
    Bard,
    Rogue,
    Shaman,
    Necromancer,
    Wizard,
    Magician,
    Enchanter,
    Beastlord,
    Berserker
}

public class Character
{
    public int CharacterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public EQClass Class { get; set; }
    public int AccountId { get; set; }
    public bool Progression { get; set; }
    public string Server { get; set; } = string.Empty;
    public List<RelayGroup> RelayGroups { get; set; } = new();

    public uint? Level { get; set; }
    public uint? PracticePoints { get; set; }
    public uint? CurrentHP { get; set; }

    public uint? MaxHP { get; set; }
    public uint? CurrentMana { get; set; }
    public uint? MaxMana { get; set; }
    public uint? Strength { get; set; }
    public uint? Stamina { get; set; }
    public uint? Charisma { get; set; }
    public uint? Dexterity { get; set; }
    public uint? Intelligence { get; set; }
    public uint? Agility { get; set; }
    public uint? Wisdom { get; set; }

    public uint? Platinum { get; set; }
    public uint? Gold { get; set; }
    public uint? Silver { get; set; }
    public uint? Copper { get; set; }
    public float? XPos { get; set; }
    public float? YPos { get; set; }
    public float? ZPos { get; set; }
    public float? Heading { get; set; }         // in degrees

    public uint? SpawnId { get; set; }
}