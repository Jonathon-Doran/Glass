using Glass.Core.Logging;
using System.Collections.Generic;

namespace Glass.Data.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// Skills
//
// Static skill id to skill name reference table. 
///////////////////////////////////////////////////////////////////////////////////////////////
public static class Skills
{
    private static readonly Dictionary<uint, string> Names = new Dictionary<uint, string>()
    {
        { 0x0, "1H Blunt" },
        { 0x1, "1H Slashing" },
        { 0x2, "2H Blunt" },
        { 0x3, "2H Slashing" },
        { 0x4, "Abjuration" },
        { 0x5, "Alteration" },
        { 0x6, "Apply Poison" },
        { 0x7, "Archery" },
        { 0x8, "Backstab" },
        { 0x9, "Bind Wound" },
        { 0xA, "Bash" },
        { 0xB, "Block" },
        { 0xC, "Brass Instruments" },
        { 0xD, "Channeling" },
        { 0xE, "Conjuration" },
        { 0xF, "Defense" },
        { 0x10, "Disarm" },
        { 0x11, "Disarm Traps" },
        { 0x12, "Divination" },
        { 0x13, "Dodge" },
        { 0x14, "Double Attack" },
        { 0x15, "Dragon Punch" },
        { 0x16, "Dual Wield" },
        { 0x17, "Eagle Strike" },
        { 0x18, "Evocation" },
        { 0x19, "Feign Death" },
        { 0x1A, "Flying Kick" },
        { 0x1B, "Forage" },
        { 0x1C, "Hand to Hand" },
        { 0x1D, "Hide" },
        { 0x1E, "Kick" },
        { 0x1F, "Meditate" },
        { 0x20, "Mend" },
        { 0x21, "Offense" },
        { 0x22, "Parry" },
        { 0x23, "Pick Lock" },
        { 0x24, "Piercing" },
        { 0x25, "Riposte" },
        { 0x26, "Round Kick" },
        { 0x27, "Safe Fall" },
        { 0x28, "Sense Heading" },
        { 0x29, "Singing" },
        { 0x2A, "Sneak" },
        { 0x2B, "Specialize Abjuration" },
        { 0x2C, "Specialize Alteration" },
        { 0x2D, "Specialize Conjuration" },
        { 0x2E, "Specialize Divination" },
        { 0x2F, "Specialize Evocation" },
        { 0x30, "Pick Pocket" },
        { 0x31, "Stringed Instruments" },
        { 0x32, "Swimming" },
        { 0x33, "Throwing" },
        { 0x34, "Tiger Claw" },
        { 0x35, "Tracking" },
        { 0x36, "Wind Instruments" },
        { 0x37, "Fishing" },
        { 0x38, "Make Poison" },
        { 0x39, "Tinkering" },
        { 0x3A, "Research" },
        { 0x3B, "Alchemy" },
        { 0x3C, "Baking" },
        { 0x3D, "Tailoring" },
        { 0x3E, "Sense Traps" },
        { 0x3F, "Blacksmithing" },
        { 0x40, "Fletching" },
        { 0x41, "Brewing" },
        { 0x42, "Alcohol Tolerance" },
        { 0x43, "Begging" },
        { 0x44, "Jewelry" },
        { 0x45, "Pottery" },
        { 0x46, "Percussion Instruments" },
        { 0x47, "Intimidation" },
        { 0x48, "Berserking" },
        { 0x49, "Taunt" },
        { 0xFFFFFFFF, "None" }
    };

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetSkillName
    //
    // Looks up a skill name by its id. Returns a descriptive placeholder for unknown values
    // rather than throwing.
    //
    // skill:    The skill id to query.
    //
    // Returns the skill name, or "Unknown(0x..)" when the id is not in the table.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static string GetSkillName(uint skill)
    {
        if (Names.TryGetValue(skill, out string? name))
        {
            return name;
        }

        DebugLog.Write(LogChannel.General, "Skills.GetSkillName: skill=0x" + skill.ToString("X2")
            + " not in table, returning 'Unknown'.", LogLevel.Warn);
        return "Unknown(0x" + skill.ToString("X2") + ")";
    }
}