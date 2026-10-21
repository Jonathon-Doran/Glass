namespace Glass.Data.Models;

public enum EQClass
{
    Warrior = 1,
    Cleric = 2,
    Paladin = 3,
    Ranger = 4,
    Shadowknight = 5,
    Druid = 6,
    Monk = 7,
    Bard = 8,
    Rogue = 9,
    Shaman = 10,            // a
    Necromancer = 11,       // b
    Wizard = 12,            // c
    Magician = 13,          // d
    Enchanter = 14,         // e
    Beastlord = 15,         // f
    Berserker = 16          // 10
}
public static class EQClassExtensions
{
    public static EQClass ToEQClass(this string className)
    {
        return className.ToLower() switch
        {
            "warrior" => EQClass.Warrior,
            "cleric" => EQClass.Cleric,
            "paladin" => EQClass.Paladin,
            "ranger" => EQClass.Ranger,
            "shadowknight" => EQClass.Shadowknight,
            "druid" => EQClass.Druid,
            "monk" => EQClass.Monk,
            "bard" => EQClass.Bard,
            "rogue" => EQClass.Rogue,
            "shaman" => EQClass.Shaman,
            "necromancer" => EQClass.Necromancer,
            "wizard" => EQClass.Wizard,
            "mage" => EQClass.Magician,
            "enchanter" => EQClass.Enchanter,
            "beastlord" => EQClass.Beastlord,
            "berserker" => EQClass.Berserker,
            _ => throw new ArgumentException($"Unknown class: {className}")
        };
    }

    public static string ToDisplayString(this EQClass eqClass)
    {
        return eqClass switch
        {
            EQClass.Shadowknight => "Shadow Knight",
            _ => eqClass.ToString()
        };
    }
}
