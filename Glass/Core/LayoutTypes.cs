namespace Glass.Core;

public enum LayoutStrategies
{
    FixedWindowSize,
    SubsetAndSwap,
    SummaryWindows
}

// Represents a physical monitor as enumerated by the OS.
public class EnumeratedMonitor
{
    public string DeviceName { get; set; } = string.Empty;
    public float DpiScale { get; set; }
    public int DeviceIndex { get; set; }
}