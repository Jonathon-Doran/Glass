namespace Glass.Data.Models;

public class RelayGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public List<Character> Characters { get; set; } = new();
}
