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
    public int? CommandId { get; set; }
    public int Target { get; set; } = -1;
    public int? RelayGroupId { get; set; }
    public bool RoundRobin { get; set; }
    public string? Label { get; set; }
}