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
    Berserker = 16,         // 10

    // NPC Classes

    Warrior_GM = 20,
    Cleric_GM = 21,
    Paladin_GM = 22,
    Ranger_GM = 23,
    SK_GM = 24,
    Druid_GM = 25,
    Monk_GM = 26,
    Bard_GM = 27,
    Rogue_GM = 28,
    Shaman_GM = 29,
    Necromancer_GM = 30,
    Wizard_GM = 31,
    Magician_GM = 32,
    Enchanter_GM = 33,
    Beastlord_GM = 34,
    Berserker_GM = 35,

    Banker = 40,
    Merchant = 41,
    Discord_Merchant = 50,
    Adventure_Recruiter = 60,
    Adventure_Merchant = 61,
    Tribute_Master = 63,
    Guild_Tribute_Master = 64,
    Guild_Banker = 66,
    Norraths_Keeper_Merchant = 67,
    Dark_Reign_Merchant = 68,
    Fellowship_Master = 69,
    Alt_Currency_Merchant = 70,
    Mercenary_Liason = 71
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
