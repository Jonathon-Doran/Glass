namespace Glass.Data.Models;

/// <summary>
/// A named window arrangement for a CharacterSet on a specific Machine.
/// Multiple layouts per machine are supported but one is typical.
/// The MonitorFingerprint is used to detect when the layout is stale
/// (e.g. after moving to a new machine or replacing a monitor).
/// </summary>
public class WindowLayout
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CharacterSetId { get; set; }
    public int MachineId { get; set; }

    /// <summary>
    /// Snapshot of monitor configuration at the time this layout was created or last reflowed.
    /// Compared against the machine's current CachedMonitors to detect staleness.
    /// </summary>
    public string MonitorFingerprint { get; set; } = string.Empty;

    public List<CharacterPlacement> Placements { get; set; } = new();
}

/// <summary>
/// The position and size of a character's game window within a WindowLayout.
/// </summary>
public class CharacterPlacement
{
    public int Id { get; set; }
    public int WindowLayoutId { get; set; }
    public int CharacterId { get; set; }

    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public Character? Character { get; set; }
}
