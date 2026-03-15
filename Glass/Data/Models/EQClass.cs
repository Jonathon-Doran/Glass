namespace Glass.Data.Models;

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
            "mage" => EQClass.Mage,
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
