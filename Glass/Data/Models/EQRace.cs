using Glass.Core.Logging;
using System.Collections.Generic;

namespace Glass.Data.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// EQRace
//
// EQRaces, with each member set to the raw wire value for the race.
///////////////////////////////////////////////////////////////////////////////////////////////
public enum EQRace
{
    Human = 1,
    Barbarian = 2,
    Erudite = 3,
    WoodElf = 4,
    HighElf = 5,
    DarkElf = 6,
    HalfElf = 7,
    Dwarf = 8,
    Troll = 9,
    Ogre = 10,
    Halfling = 11,
    Gnome = 12,
    Iksar = 128,
    VahShir = 130,
    Froglok = 330,
    Drakkin = 522
}

public static class EQEQRaceExtensions
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // EQRaceNames
    //
    // Printable names for the races, keyed by race.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static readonly Dictionary<EQRace, string> EQRaceNames = new Dictionary<EQRace, string>()
    {
        { EQRace.Human,     "Human" },
        { EQRace.Barbarian, "Barbarian" },
        { EQRace.Erudite,   "Erudite" },
        { EQRace.WoodElf,   "Wood Elf" },
        { EQRace.HighElf,   "High Elf" },
        { EQRace.DarkElf,   "Dark Elf" },
        { EQRace.HalfElf,   "Half Elf" },
        { EQRace.Dwarf,     "Dwarf" },
        { EQRace.Troll,     "Troll" },
        { EQRace.Ogre,      "Ogre" },
        { EQRace.Halfling,  "Halfling" },
        { EQRace.Gnome,     "Gnome" },
        { EQRace.Iksar,     "Iksar" },
        { EQRace.VahShir,   "Vah Shir" },
        { EQRace.Froglok,   "Froglok" },
        { EQRace.Drakkin,   "Drakkin" }
    };

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ToDisplayString
    //
    // Looks up the printable name of a race.  Returns a descriptive placeholder for unknown
    // values rather than throwing.
    //
    // race:  The race to query.
    //
    // Returns the race name, or "Unknown(n)" when the value is not in the table.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static string ToDisplayString(this EQRace race)
    {
        if (EQRaceNames.TryGetValue(race, out string? name))
        {
            return name;
        }

        DebugLog.Write(LogChannel.Fields, "EQRaceExtensions.ToDisplayString: race " +
            (uint)race + " not in table, returning 'Unknown'.", LogLevel.Warn);
        return "Unknown(" + (uint)race + ")";
    }
}
