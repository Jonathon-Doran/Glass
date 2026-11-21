using System;
using System.IO;
using System.Reflection;

namespace Glass.Core.Logging;

///////////////////////////////////////////////////////////////////////////////////////////////
// GlassDebugLogHandler
//
// Writes log messages to the glass.log file in the Logs folder under the
// solution directory.  Manages its own StreamWriter and lock.  The file is created if missing,
// then appended to or overwritten on construction as requested, and flushed on every write
///////////////////////////////////////////////////////////////////////////////////////////////
public class GlassDebugLogHandler : IHandleLogMessages
{
    private StreamWriter? _writer;
    private readonly object _fileLock = new object();

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GlassDebugLogHandler (constructor)
    //
    // Opens the named log file in the Logs folder for writing.  Creates the Logs directory if
    // it does not exist.  When append is true, writes are added to the end of any existing file;
    // otherwise any existing file is overwritten.
    //
    // basename:  The base name of the file.  Should be placed in the Logs directory.
    // append:    True to keep the file's existing contents, false to overwrite them.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public GlassDebugLogHandler(string basename, bool append = false)
    {
        string solutionDirectory = FindSolutionDirectory();
        string logsDirectory = Path.Combine(solutionDirectory, "Logs");
        Directory.CreateDirectory(logsDirectory);

        string logPath = Path.Combine(logsDirectory, basename);

        _writer = new StreamWriter(logPath, append);
        _writer.AutoFlush = true;

        DebugLog.Write(LogChannel.General, "GlassDebugLogHandler: opened '" + logPath + "', "
            + (append ? "appending" : "overwriting") + ".", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // FindSolutionDirectory
    //
    // Walks up from the executable's directory until a .sln file is found.
    // Throws InvalidOperationException if the solution directory cannot be
    // located.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private string FindSolutionDirectory()
    {
        string? directory = AppContext.BaseDirectory;

        while (directory != null)
        {
            string logsPath = Path.Combine(directory, "Logs");

            if (Directory.Exists(logsPath))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException(
            "GlassDebugLogHandler: could not locate Logs directory from "
            + AppContext.BaseDirectory);
    }


    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Write
    //
    // Writes a message to the glass.log file.  Thread-safe via internal lock.
    //
    // message:  The message text to write (already timestamped by DebugLog)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Write(string message)
    {
        lock (_fileLock)
        {
            if (_writer != null)
            {
                _writer.WriteLine(message);
                _writer.Flush();
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Shutdown
    //
    // Flushes and closes the glass.log file.  No further writes will succeed
    // after this call.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Shutdown()
    {
        lock (_fileLock)
        {
            if (_writer != null)
            {
                _writer.Flush();
                _writer.Close();
                _writer = null;
            }
        }
    }
}