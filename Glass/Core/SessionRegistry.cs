using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Glass.Core;

// Tracks active EverQuest sessions and their associated process and window information.
public class SessionRegistry
{
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // Represents a single active EverQuest session.
    public class SessionEntry
    {
        public string SessionName { get; set; } = string.Empty;
        public string CharacterName { get; set; } = string.Empty;
        public uint Pid { get; set; }
        public IntPtr Hwnd { get; set; }
    }

    private readonly Dictionary<string, SessionEntry> _sessions = new();
    private readonly object _lock = new();

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnSessionConnected
    //
    // Adds or updates a session when ISXGlass reports it has connected.
    // The HWND is not yet known at this point.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void OnSessionConnected(string sessionName, string characterName, uint pid)
    {
        DebugLog.Write(DebugLog.Log_Sessions, $"SessionRegistry.OnSessionConnected: session={sessionName} character={characterName} pid={pid}");
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionName, out var entry))
            {
                entry = new SessionEntry { SessionName = sessionName };
                _sessions[sessionName] = entry;
            }
            entry.CharacterName = characterName;
            entry.Pid = pid;
            entry.Hwnd = IntPtr.Zero;
        }

        // Attempt HWND lookup immediately — it may already exist.
        TryFindHwnd(sessionName);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnSessionDisconnected
    //
    // Removes a session from the registry when it disconnects.
    //
    // sessionName:  The session that disconnected
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void OnSessionDisconnected(string sessionName)
    {
        DebugLog.Write(DebugLog.Log_Sessions, $"SessionRegistry.OnSessionDisconnected: session={sessionName}");
        _sessions.Remove(sessionName);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // TryFindHwnd
    //
    // Finds the main window for a session by enumerating top-level windows
    // and matching on PID.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void TryFindHwnd(string sessionName)
    {
        SessionEntry? entry;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionName, out entry))
            {
                DebugLog.Write(DebugLog.Log_Sessions, $"SessionRegistry.TryFindHwnd: session={sessionName} not found.");
                return;
            }
            if (entry.Pid == 0)
            {
                DebugLog.Write(DebugLog.Log_Sessions, $"SessionRegistry.TryFindHwnd: session={sessionName} has no PID yet.");
                return;
            }
        }

        uint targetPid = entry.Pid;
        IntPtr found = IntPtr.Zero;

        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == targetPid)
            {
                var className = new System.Text.StringBuilder(256);
                GetClassName(hwnd, className, className.Capacity);
                if (className.ToString() == "_EverQuestwndclass")
                {
                    found = hwnd;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);

        if (found != IntPtr.Zero)
        {
            lock (_lock)
            {
                entry.Hwnd = found;
            }
            DebugLog.Write(DebugLog.Log_Sessions, $"SessionRegistry.TryFindHwnd: session={sessionName} hwnd={found:X}");
        }
        else
        {
            DebugLog.Write(DebugLog.Log_Sessions, $"SessionRegistry.TryFindHwnd: session={sessionName} no visible window found for pid={targetPid}.");
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetSessions
    //
    // Returns a snapshot of all current sessions.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<SessionEntry> GetSessions()
    {
        lock (_lock)
        {
            return _sessions.Values.ToList();
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetSession
    //
    // Returns the entry for a specific session, or null if not found.
    //
    // sessionName:  The session to query
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public SessionEntry? GetSession(string sessionName)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionName, out var entry) ? entry : null;
        }
    }
}