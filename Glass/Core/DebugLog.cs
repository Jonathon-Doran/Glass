using System;
using System.IO;
using System.Reflection;

namespace Glass.Core;

public static class DebugLog
{
    public static bool Log_Pipes = true;
    public static bool Log_Video = false;
    public static bool Log_Sessions = true;
    public static bool Log_Input = false;
    public static bool Log_Database = true;
    public static bool Log_Network = true;

    private static volatile Action<string>? _log;
    private static StreamWriter? _fileWriter;
    private static readonly object _fileLock = new object();
    public static bool SuppressUI = false;

    // Initializes the debug log, wiring up the console action and opening the log file.
    // The log file is written alongside the executable and cleared on each startup.
    // Parameters:
    //   log - action that writes a message to the UI console
    public static void Initialize(Action<string> log)
    {
        _log = log;

        string exeDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        string logPath = Path.Combine(exeDirectory, "glass.log");

        lock (_fileLock)
        {
            _fileWriter = new StreamWriter(logPath, append: false);
            _fileWriter.AutoFlush = true;
        }

        Write("DebugLog: log file opened at " + logPath);
    }

    // Shuts down the debug log, flushing and closing the log file.
    public static void Shutdown()
    {
        Write("DebugLog: shutting down, closing log file.");

        _log = null;

        lock (_fileLock)
        {
            if (_fileWriter != null)
            {
                _fileWriter.Flush();
                _fileWriter.Close();
                _fileWriter = null;
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Write
    //
    // Writes a message unconditionally — for high priority messages that always log.
    // Writes to the log file always.  Writes to the UI unless SuppressUI is set.
    //
    // message:  The message to write
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static void Write(string message)
    {
        if (!SuppressUI)
        {
            _log?.Invoke(message);
        }

        lock (_fileLock)
        {
            if (_fileWriter != null)
            {
                _fileWriter.WriteLine(message);
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Write
    //
    // Writes a message only if the specified feature flag is enabled.
    // Writes to the log file always.  Writes to the UI unless SuppressUI is set.
    //
    // flag:     Feature flag; message is suppressed if false
    // message:  The message to write
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static void Write(bool flag, string message)
    {
        if (flag)
        {
            Write(message);
        }
    }

    // Sets a feature flag by name. Returns false if the feature name is not recognized.
    // Parameters:
    //   feature - name of the feature flag (case-insensitive)
    //   enabled - the value to set
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
            case "network":
                Log_Network = enabled;
                return true;
            default:
                return false;
        }
    }
}