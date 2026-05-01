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

    public int? Level { get; set; }
    public int? PracticePoints { get; set; }
    public int? MaxHP { get; set; }
    public int? MaxMana { get; set; }

    public int? Strength { get; set; }
    public int? Stamina { get; set; }
    public int? Charisma { get; set; }
    public int? Dexterity { get; set; }
    public int? Intelligence { get; set; }
    public int? Agility { get; set; }
    public int? Wisdom { get; set; }

    public int? Platinum { get; set; }
    public int? Gold { get; set; }
    public int? Silver { get; set; }
    public int? Copper { get; set; }
    public float? XPos { get; set; }
    public float? YPos { get; set; }
    public float? ZPos { get; set; }
    public float? Heading { get; set; }         // in degrees

    public int? SpawnId { get; set; }
}