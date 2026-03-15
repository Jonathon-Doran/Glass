namespace Glass.Core;

public static class DebugLog
{
    public static bool Log_Pipes = true;
    public static bool Log_Video = false;
    public static bool Log_Sessions = true;
    public static bool Log_Input = false;
    public static bool Log_Database = true;

    private static volatile Action<string>? _log;

    public static void Initialize(Action<string> log)
    {
        _log = log;
    }

    // Writes a message unconditionally — for high priority messages that always log.
    public static void Write(string message)
    {
        _log?.Invoke(message);
    }

    // Writes a message only if the specified feature flag is enabled.
    public static void Write(bool flag, string message)
    {
        if (flag)
        {
            _log?.Invoke(message);
        }
    }

    // Sets a feature flag by name. Returns false if the feature name is not recognized.
    public static bool SetFlag(string feature, bool enabled)
    {
        switch (feature.ToLower())
        {
            case "pipes": 
                Log_Pipes = enabled; 
                return true;
            case "video": 
                Log_Video = enabled; 
                return true;
            case "sessions": 
                Log_Sessions = enabled; 
                return true;
            case "input": 
                Log_Input = enabled; 
                return true;
            case "database":
                Log_Database = enabled;
                return true;
            default: 
                return false;
        }
    }
}