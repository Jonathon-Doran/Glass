using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Glass.Data.Models
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AugmentationType
    //
    // Identifes augmentation types
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public enum AugmentationType : byte
    {
        General_Single_Stat = 1,
        General_Multiple_Stat_2 = 2,
        General_Spell_Effect = 3,
        Weapon_General = 4,
        General_Multiple_Stat_5 = 5,
        Weapon_Base_Damage = 6,
        General_Group = 7,
        General_Raid = 8,
        General_Point = 9,
        Crafted_Common = 10,
        Crafted_Group = 11,
        Crafted_Raid = 12,
        Energeiac_Group = 13,
        Energeiac_Raid = 14,
        Emblem = 15,
        Underfoot_Cultural_Group = 16,
        Underfoot_Cultural_Raid = 17,
        Special_Group = 18,
        Special_Raid = 19,
        Weapon_Ornamentation = 20,
        Armor_Ornamentation = 21,
        Size
    }

    public static class AugmentationTypeExtensions
    {
        public static string ToDisplayString(this AugmentationType augmentationType)
        {
            return augmentationType switch
            {
                AugmentationType.General_Single_Stat => "General: Single Stat",
                AugmentationType.General_Multiple_Stat_2 => "General: Multiple Stat",
                AugmentationType.General_Spell_Effect => "General: Spell Effect",
                AugmentationType.Weapon_General => "Weapon: General",
                AugmentationType.General_Multiple_Stat_5 => "General: Multiple Stat",
                AugmentationType.Weapon_Base_Damage => "Weapon: Base Damage",
                AugmentationType.General_Group => "General: Group",                 // 7
                AugmentationType.General_Raid => "General: Raid",
                AugmentationType.General_Point => "General: Point Augments",
                AugmentationType.Crafted_Common => "Crafted: Common",
                AugmentationType.Crafted_Group => "Crafted: Group",
                AugmentationType.Crafted_Raid => "Crafted: Raid",
                AugmentationType.Energeiac_Group => "Energeiac: Group",
                AugmentationType.Energeiac_Raid => "Energeiac: Raid",
                AugmentationType.Emblem => "Emblem",
                AugmentationType.Underfoot_Cultural_Group => "Crafted: Group",
                AugmentationType.Underfoot_Cultural_Raid => "Crafted: Raid",
                AugmentationType.Special_Group => "Special: Group",
                AugmentationType.Special_Raid => "Special: Raid",
                AugmentationType.Weapon_Ornamentation => "Weapon Ornamentation",
                AugmentationType.Armor_Ornamentation => "Armor Ornamentation",
                _ => "Unknown"
            };
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AugmentationSlot
    //
    // One augmentation slot on an item definition, as decoded from one bag of the augment
    // gate.  Index is the slot's position within the gate and is the value a socketed
    // augment's location refers to.  Slots with type 0 do not exist on the item and are
    // not represented.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public class AugmentationSlot
    {
        public uint Index { get; set; }
        public AugmentationType Type { get; set; }
        public Boolean Visible { get; set; }
        public uint Unknown_4 { get; set; }
    }
}
