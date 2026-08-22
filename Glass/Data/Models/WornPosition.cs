using Glass.Core.Logging;

namespace Glass.Data.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// WornPosition
//
// Worn equipment positions within the Carried storage system, with each member
// set to the raw wire value from the item serialization header.  None is the
// reserved no-position value.
///////////////////////////////////////////////////////////////////////////////////////////////
public enum WornPosition : uint
{
    Charm = 0,
    LeftEar = 1,
    Head = 2,
    Face = 3,
    RightEar = 4,
    Neck = 5,
    Shoulders = 6,
    Arms = 7,
    Back = 8,
    LeftWrist = 9,
    RightWrist = 10,
    Range = 11,
    Hands = 12,
    Primary = 13,
    Secondary = 14,
    LeftRing = 15,
    RightRing = 16,
    Chest = 17,
    Legs = 18,
    Feet = 19,
    Waist = 20,
    PowerSource = 21,
    Ammo = 22,
    None = 0xFFFF
}

///////////////////////////////////////////////////////////////////////////////////////////////
// WornPositionExtensions
//
// Display names and membership tests for WornPosition values.
///////////////////////////////////////////////////////////////////////////////////////////////
public static class WornPositionExtensions
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Names
    //
    // Printable names for the worn positions, keyed by worn position.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private static readonly Dictionary<WornPosition, string> Names = new Dictionary<WornPosition, string>()
    {
        { WornPosition.Charm,       "Charm" },
        { WornPosition.LeftEar,     "Left Ear" },
        { WornPosition.Head,        "Head" },
        { WornPosition.Face,        "Face" },
        { WornPosition.RightEar,    "Right Ear" },
        { WornPosition.Neck,        "Neck" },
        { WornPosition.Shoulders,   "Shoulders" },
        { WornPosition.Arms,        "Arms" },
        { WornPosition.Back,        "Back" },
        { WornPosition.LeftWrist,   "Left Wrist" },
        { WornPosition.RightWrist,  "Right Wrist" },
        { WornPosition.Range,       "Range" },
        { WornPosition.Hands,       "Hands" },
        { WornPosition.Primary,     "Primary" },
        { WornPosition.Secondary,   "Secondary" },
        { WornPosition.LeftRing,    "Left Ring" },
        { WornPosition.RightRing,   "Right Ring" },
        { WornPosition.Chest,       "Chest" },
        { WornPosition.Legs,        "Legs" },
        { WornPosition.Feet,        "Feet" },
        { WornPosition.Waist,       "Waist" },
        { WornPosition.PowerSource, "Power Source" },
        { WornPosition.Ammo,        "Ammo" }
    };

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // IsWorn
    //
    // Tests whether this value is a named worn position.  None and out-of-range
    // wire values are not worn.
    //
    // position:  The value to test
    //
    // Returns true when the value is a named worn position, false otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static bool IsWorn(this WornPosition position)
    {
        return Names.ContainsKey(position);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // DisplayName
    //
    // Returns the printable name of this worn position.
    //
    // position:  The value to describe
    //
    // Returns the printable name, or "<unknown>" for a value with no name.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static string DisplayName(this WornPosition position)
    {
        string name;

        if (Names.TryGetValue(position, out string? knownName))
        {
            name = knownName;
        }
        else
        {
            DebugLog.Write(LogChannel.Fields, "DisplayName: no name for worn position " +
                (uint)position, LogLevel.Warn);
            name = "<unknown>";
        }

        return name;
    }
}