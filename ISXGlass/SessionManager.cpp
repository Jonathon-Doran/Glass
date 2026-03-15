#include "LSVariables.h"
#include "ISXGlass.h"
#include "SessionManager.h"
#include "Logger.h"

SessionManager g_SessionManager;

static std::set<std::string>    _pendingLaunches;
static std::mutex               _pendingMutex;
static DWORD_PTR                _performanceCoreMask = 0;

static void OnSessionConnected(int argc, char* argv[], PLSOBJECT pThis);
static void OnSessionRenamed(int argc, char* argv[], PLSOBJECT pThis);
static void OnSessionDisconnected(int argc, char* argv[], PLSOBJECT pThis);


//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Initialize
// 
// Sets processor affinity to performance cores (if they exist).  Enumerates the sessions, stores them in a variable
// on the Innerspace side.  Populates the session cache.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void SessionManager::Initialize()
{
    Logger::Instance().Write("SessionManager::Initialize");
    LSVariables::Declare(ISXGLASS_CHARS_VAR, "collection:string");
    BuildPerformanceCoreMask();


    // Register an event handler to be called when a new session is ready.

    unsigned int sessionRenamedEventId = pISInterface->RegisterEvent("OnSessionRenamed");
    pISInterface->AttachEventTarget(sessionRenamedEventId, OnSessionRenamed);

    // Register an event handler to be called when a session is disconnected
    unsigned int sessionDisconnectedEventId = pISInterface->RegisterEvent("OnSessionDisconnected");
    pISInterface->AttachEventTarget(sessionDisconnectedEventId, OnSessionDisconnected);
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// EnumerateSessions
// 
// Enumerates active Inner Space sessions (from Innerspace "Session" variable) and populates the session cache
// with account IDs and character names from ISXGlassChars.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void SessionManager::EnumerateSessions(bool sendNotify)
{
    SessionEntry entry;
    char query[64];
    char sessionName[256];
    char characterName[256];
    char sessionIdStr[16];
    char key[64];
    LSOBJECT result;

    _sessions.clear();

    unsigned int count = pISInterface->GetSessionCount();
    Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "SessionManager::EnumerateSessions: session count=%u", count);

    for (SessionID id = 1; id <= count; id++)
    {
        snprintf(query, sizeof(query), "Session[%u].Name", id);

        bool parseResult = pISInterface->DataParse(query, result);
        if ((!parseResult) || (result.Type == nullptr))
        {
            Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "SessionManager::EnumerateSessions: session %u DataParse failed.", id);
            continue;
        }

        sessionName[0] = 0;
        result.Type->ToText(result.GetObjectData(), sessionName, sizeof(sessionName));

        SessionID sessionId = ParseSessionId(sessionName);
        if (sessionId == 0)
        {
            Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "SessionManager::EnumerateSessions: session %u name=%s skipped, not an EQ session.", id, sessionName);
            continue;
        }

        characterName[0] = 0;

        snprintf(sessionIdStr, sizeof(sessionIdStr), "%u", sessionId);
        LSVariables::GetString(ISXGLASS_CHARS_VAR, sessionIdStr, characterName, sizeof(characterName));

        Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "SessionManager::EnumerateSessions: ID=%u session=%s character=%s", sessionId, sessionName, characterName);

        entry.characterName = characterName;

        snprintf(query, sizeof(query), "Session[%u].PID", id);
        if (pISInterface->DataParse(query, result) && result.Type)
        {
            char pidBuf[32];
            pidBuf[0] = 0;
            result.Type->ToText(result.GetObjectData(), pidBuf, sizeof(pidBuf));
            entry.pid = (DWORD) atol(pidBuf);
            Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "SessionManager::EnumerateSessions: slot=%u pid=%u", sessionId, entry.pid);
        }

        snprintf(query, sizeof(query), "Session[%u].Window", id);
        if (pISInterface->DataParse(query, result) && result.Type)
        {
            char hwndBuf[32];
            hwndBuf[0] = 0;
            result.Type->ToText(result.GetObjectData(), hwndBuf, sizeof(hwndBuf));
            entry.hwnd = (HWND)(uintptr_t)strtoull(hwndBuf, nullptr, 0);
            Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "SessionManager::EnumerateSessions: slot=%u hwnd=%p", sessionId, entry.hwnd);
        }
        snprintf(sessionName, sizeof(sessionName), "is%d", sessionId);
        entry.sessionName = sessionName;

        _sessions[sessionId] = entry;


        if (sendNotify)
        {
            SendSessionConnected(entry.sessionName, entry.pid, entry.hwnd);
        }
    }

    // Remove stale entries from ISXGlassChars that no longer have active sessions.

    if (LSVariables::FirstKey(ISXGLASS_CHARS_VAR, key, sizeof(key)))
    {
        do
        {
            SessionID sessionId = (unsigned int) atoi(key);
            if (_sessions.find(sessionId) == _sessions.end())
            {
                Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "SessionManager::EnumerateSessions: removing stale entry for slot %u", sessionId);
                LSVariables::Erase(ISXGLASS_CHARS_VAR, key);
            }
        } while (LSVariables::NextKey(ISXGLASS_CHARS_VAR, key, sizeof(key)));
    }
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// IsSessionActive
// 
// Determines if a session named is<accountId> is currently active.
// 
// sessionId:  The ID of the session to check
// 
// Returns:  TRUE if the session is active, FALSE otherwise
// 
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

bool SessionManager::IsSessionActive(SessionID sessionId)
{
    char query[64];
    snprintf(query, sizeof(query), "Session[is%u](exists)", sessionId);
    LSOBJECT result;
    bool parseResult = pISInterface->DataParse(query, result);
    return (parseResult && (result.Type != nullptr));
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// AddPendingLaunch
// 
// Marks a session as expecting to come online.
// Called before IS is asked to launch the process.
// 
// sessionName:   The name of the session (i.e. "is1")
// 
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void SessionManager::AddPendingLaunch(const std::string& sessionName)
{
    std::lock_guard<std::mutex> lock(_pendingMutex);
    Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "SessionManager::AddPendingLaunch: session=%s", sessionName);
    _pendingLaunches.insert(sessionName);
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Launch
// 
// Launches EverQuest in the given slot if not already active.
// Stores the character name in the cache and in ISXGlassChars.
// Performs file virtualization on config files, which is why we need character and server info.
// 
// sessionID:  The ID of the session to launch
// characterName:  The name of the character
// server:  The name of the server
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void SessionManager::Launch(SessionID sessionId, const char* characterName, const char* server)
{
    if (IsSessionActive(sessionId))
    {
        Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "SessionManager::Launch: slot %u already active, skipping.", sessionId);
        return;
    }

    char sessionIdStr[16];
    snprintf(sessionIdStr, sizeof(sessionIdStr), "%u", sessionId);
    LSVariables::SetString(ISXGLASS_CHARS_VAR, sessionIdStr, characterName);

    // Note that the launch command uses prestartup instructions to perform virtualization, so that each character
    // gets its own unique config files.

    char command[1024];
    snprintf(command, sizeof(command),
        "open Everquest EQ%u -slot %u"
        " -prestartup \"FileRedirect eqlsPlayerData.ini eqlsPlayerData-%s-%s.ini;"
        "FileRedirect notes.txt notes-%s-%s.txt;"
        "FileRedirect eqclient.ini eqclient-%s-%s.ini\"",
        sessionId, sessionId,
        characterName, server,
        characterName, server,
        characterName, server);

    Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "SessionManager::Launch: %s", command);

    std::string sessionName = "is" + std::to_string(sessionId);

    AddPendingLaunch(sessionName);
    pISInterface->ExecuteCommand(command);
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// OnSessionConnected -- Callback for session connect
// 
// Called when IS reports a new local session has connected.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

static void OnSessionConnected(int argc, char* argv[], PLSOBJECT pThis)
{
    Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "OnSessionConnected: '%s'", argv[0]);
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// OnSessionRenamed
// 
// Called when IS reports a new local session been renamed.  Occurs later than connect and may be more reliable.
// If the session matches a pending launch, captures PID and HWND,
// applies performance core affinity, and notifies Glass.exe.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

static void OnSessionRenamed(int argc, char* argv[], PLSOBJECT pThis)
{
    if (argc < 1)
    {
        Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "OnSessionRenamed: no arguments.");
        return;
    }

    Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "OnSessionRenamed: '%s'", argv[0]);

    // Parse session name and PID from "is8-15936" format.
    const char* dash = strchr(argv[0], '-');
    if (!dash)
    {
        Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "OnSessionRenamed: could not find '-' in '%s', ignoring.", argv[0]);
        return;
    }

    std::string sessionName(argv[0], dash - argv[0]);
    DWORD pid = (DWORD)atol(dash + 1);
    Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "OnSessionRenamed: sessionName='%s' pid=%u", sessionName.c_str(), pid);

    // Check if this is a pending launch.
    {
        std::lock_guard<std::mutex> lock(_pendingMutex);
        if (_pendingLaunches.find(sessionName) == _pendingLaunches.end())
        {
            Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "OnSessionRenamed: '%s' not in pending launches, ignoring.", sessionName.c_str());
            return;
        }
        _pendingLaunches.erase(sessionName);
    }

    // Set performance core affinity.
    if ((pid != 0) && (_performanceCoreMask != 0))
    {
        HANDLE process = OpenProcess(PROCESS_SET_INFORMATION, FALSE, pid);
        if (process)
        {
            if (!SetProcessAffinityMask(process, _performanceCoreMask))
            {
                Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "OnSessionRenamed: SetProcessAffinityMask failed: %u", GetLastError());
            }
            else
            {
                Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "OnSessionRenamed: affinity set for pid=%u mask=0x%llX", pid, (unsigned long long) _performanceCoreMask);
            }
            CloseHandle(process);
        }
        else
        {
            Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "OnSessionRenamed: OpenProcess failed for pid=%u error=%u", pid, GetLastError());
        }
    }

    // Get HWND via DataParse.
    char hwndQuery[64];
    snprintf(hwndQuery, sizeof(hwndQuery), "Session[%s].Window", sessionName.c_str());
    LSOBJECT hwndResult;
    HWND hwnd = NULL;
    if (pISInterface->DataParse(hwndQuery, hwndResult) && hwndResult.Type)
    {
        char hwndBuf[32];
        hwndBuf[0] = 0;
        hwndResult.Type->ToText(hwndResult.GetObjectData(), hwndBuf, sizeof(hwndBuf));
        Logger::Instance().Write("OnSessionRenamed: hwndBuf='%s'", hwndBuf);
        hwnd = (HWND)(uintptr_t)strtoull(hwndBuf, nullptr, 16);
        Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "OnSessionRenamed: hwnd=%p", hwnd);
    }
    else
    {
        Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "OnSessionRenamed: could not get HWND for session '%s'.", sessionName.c_str());
    }

    // Notify Glass.exe.


    g_SessionManager.SendSessionConnected(sessionName, pid, hwnd);
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// OnSessionDisconnected -- Callback for session disconnect
//
// Fires when a local Inner Space session disconnects.
// Notifies Glass.exe so it can release the capture for that session.
//
// argv[0]:  Session name (e.g. "is7")
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
static void OnSessionDisconnected(int argc, char* argv[], PLSOBJECT pThis)
{
    if (argc < 1)
    {
        Logger::Instance().Write("OnSessionDisconnected: no arguments.");
        return;
    }

    std::string sessionName = argv[0];
    Logger::Instance().Write("OnSessionDisconnected: session='%s'", sessionName.c_str());

    char notify[128];
    snprintf(notify, sizeof(notify), "session_disconnected %s", sessionName.c_str());
    Logger::Instance().Write("OnSessionDisconnected: notifying Glass: %s", notify);
    g_PipeManager.Send(notify);
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ParseSessionId
// 
// Parses the session number from a session name e.g. "is9" -> 9.
// Returns 0 if the name does not match the expected format.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

unsigned int SessionManager::ParseSessionId(const char* sessionName)
{
    if (!sessionName || (sessionName[0] != 'i') || (sessionName[1] != 's'))
    {
        return 0;
    }
    return (unsigned int)atoi(sessionName + 2);
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// GetCharacterName
// 
// Look up the character name in the session cache.
// 
// sessionID:  The session to look up.
// 
// Returns:  The character name or an empty string.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

std::string SessionManager::GetCharacterName(SessionID sessionId)
{
    auto it = _sessions.find(sessionId);
    if (it == _sessions.end())
    {
        return "";
    }
    return it->second.characterName;
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// GetSessions
// 
// Returns a copy of the full session cache.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

const std::map<SessionID, SessionEntry>& SessionManager::GetSessions() const
{
    return _sessions;
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// BuildPerformanceCoreMask
// 
// Enumerates CPU sets and builds an affinity mask containing only performance
// cores (efficiency class 0). If all cores share the same efficiency class,
// returns a mask covering all cores so no restriction is applied.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void SessionManager::BuildPerformanceCoreMask()
{
    ULONG bufferSize = 0;
    GetSystemCpuSetInformation(nullptr, 0, &bufferSize, GetCurrentProcess(), 0);

    std::vector<uint8_t> buffer(bufferSize);
    ULONG count = 0;

    if (!GetSystemCpuSetInformation(
        reinterpret_cast<PSYSTEM_CPU_SET_INFORMATION>(buffer.data()),
        bufferSize,
        &count,
        GetCurrentProcess(),
        0))
    {
        Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "SessionManager::BuildPerformanceCoreMask: GetSystemCpuSetInformation failed: %u", GetLastError());
        _performanceCoreMask = 0;
        return;
    }

    // First pass: find the maximum efficiency class and build the all-cores mask.
    BYTE      maxEfficiencyClass = 0;
    DWORD_PTR allMask = 0;

    PSYSTEM_CPU_SET_INFORMATION entry = reinterpret_cast<PSYSTEM_CPU_SET_INFORMATION>(buffer.data());
    ULONG remaining = bufferSize;

    while (remaining >= sizeof(SYSTEM_CPU_SET_INFORMATION))
    {
        if (entry->Type == CpuSetInformation)
        {
            DWORD_PTR coreBit = (DWORD_PTR)1 << entry->CpuSet.LogicalProcessorIndex;
            allMask |= coreBit;
            if (entry->CpuSet.EfficiencyClass > maxEfficiencyClass)
            {
                maxEfficiencyClass = entry->CpuSet.EfficiencyClass;
            }
        }
        remaining -= entry->Size;
        entry = reinterpret_cast<PSYSTEM_CPU_SET_INFORMATION>(
            reinterpret_cast<uint8_t*>(entry) + entry->Size);
    }

    // If all cores share the same efficiency class, no restriction is needed.
    if (maxEfficiencyClass == 0)
    {
        Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "SessionManager::BuildPerformanceCoreMask: uniform efficiency class, using all cores. mask=0x%llX", (unsigned long long)allMask);
        _performanceCoreMask = allMask;
        return;
    }

    // Second pass: build the performance mask from cores with the highest efficiency class.
    DWORD_PTR performanceMask = 0;
    entry = reinterpret_cast<PSYSTEM_CPU_SET_INFORMATION>(buffer.data());
    remaining = bufferSize;

    while (remaining >= sizeof(SYSTEM_CPU_SET_INFORMATION))
    {
        if (entry->Type == CpuSetInformation)
        {
            if (entry->CpuSet.EfficiencyClass == maxEfficiencyClass)
            {
                performanceMask |= (DWORD_PTR)1 << entry->CpuSet.LogicalProcessorIndex;
            }
        }
        remaining -= entry->Size;
        entry = reinterpret_cast<PSYSTEM_CPU_SET_INFORMATION>(
            reinterpret_cast<uint8_t*>(entry) + entry->Size);
    }

    // Log the core breakdown.
    std::string perfCores;
    std::string effCores;
    for (int i = 0; i < 64; i++)
    {
        DWORD_PTR bit = (DWORD_PTR)1 << i;
        if (!(allMask & bit))
        {
            continue;
        }
        if (performanceMask & bit)
        {
            if (!perfCores.empty()) perfCores += ",";
            perfCores += std::to_string(i);
        }
        else
        {
            if (!effCores.empty()) effCores += ",";
            effCores += std::to_string(i);
        }
    }

    Logger::Instance().WriteIf(Logger::Instance().Log_Sessions, "SessionManager::BuildPerformanceCoreMask: performance cores=[%s] efficiency cores=[%s] mask=0x%llX",
        perfCores.c_str(), effCores.c_str(), (unsigned long long)performanceMask);

    _performanceCoreMask = performanceMask;
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// SendSessionConnected
//
// Formats and sends a session_connected notification to Glass.exe.
// Called from both OnSessionRenamed and the resync path in Launch.
//
// sessionName:  The Inner Space session name (e.g. "is7")
// pid:          The process ID of the session
// hwnd:         The window handle of the EQ client
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void SessionManager::SendSessionConnected(const std::string& sessionName, DWORD pid, HWND hwnd)
{
    char notify[128];
    snprintf(notify, sizeof(notify), "session_connected %s %u %llX",
        sessionName.c_str(),
        pid,
        (unsigned long long)(uintptr_t)hwnd);
    Logger::Instance().Write("SendSessionConnected: %s", notify);
    g_PipeManager.Send(notify);
}