using Glass.Controls;

namespace Glass.Data.Models;

public class KeyPage
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public KeyboardType Device { get; set; }

    public List<KeyBinding> KeyBindings { get; set; } = new();
}

public enum TriggerOn
{
    Press = 0,
    Release = 1,
    Both = 2
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
    public TriggerOn TriggerOn { get; set; } = TriggerOn.Press;
    public KeyType KeyType { get; set; } = KeyType.Momentary;
    public int RepeatIntervalMs { get; set; } = 1000;

    // Runtime only — not persisted
    public bool IsToggled { get; set; }        // toggle state is stored in the page bindings at runtime
}