namespace Glass.Data.Models;

public class KeyPage
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Device { get; set; } = string.Empty;

    public List<KeyBinding> KeyBindings { get; set; } = new();
}

public class KeyBinding
{
    public int Id { get; set; }
    public int KeyPageId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string CommandType { get; set; } = string.Empty;

    /// <summary>
    /// If set, this binding sends to a relay group rather than a specific character.
    /// </summary>
    public int? RelayGroupId { get; set; }
    public RelayGroup? RelayGroup { get; set; }

    /// <summary>
    /// If true, cycles through relay group members one at a time rather than broadcasting.
    /// </summary>
    public bool RoundRobin { get; set; }

    /// <summary>
    /// Optional command parameters, serialized as JSON or a simple string depending on CommandType.
    /// </summary>
    public string? Params { get; set; }
}