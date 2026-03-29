namespace Glass.Data.Models;

public class CommandStep
{
    public int Id { get; set; }
    public int CommandId { get; set; }
    public int Sequence { get; set; }
    public string Type { get; set; } = string.Empty;
    public string PressType { get; set; } = "press";
    public string Value { get; set; } = string.Empty;
    public int DelayMs { get; set; }
}