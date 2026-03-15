namespace Glass.Data.Models;

/// <summary>
/// A named, portable group of characters. Not tied to any machine.
/// </summary>
public class CharacterSet
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<SlotAssignment> Slots { get; set; } = new();
    public List<WindowLayout> WindowLayouts { get; set; } = new();
}