#pragma once
#include <map>
#include <set>
#include <string>
#include <mutex>
#include <windows.h>


// SessionManager maintains a cache of active Inner Space sessions and their
// associated character names, PIDs, and HWNDs. Character names are persisted
// in the ISXGlassChars LavishScript collection so they survive extension reloads.

#define ISXGLASS_CHARS_VAR "ISXGlassChars"

typedef unsigned int SessionID;

// Tracks all known information about an active Inner Space session.
struct SessionEntry
{
    std::string    sessionName;
    SessionID      sessionId;
    std::string    characterName;
    DWORD          pid = 0;
    HWND           hwnd = NULL;
};



class SessionManager
{
public:
    // Declares ISXGlassChars if needed, builds the performance core mask,
    // and populates the session cache by enumerating active Inner Space sessions.
    void Initialize();

    // Launches EverQuest in the given slot if not already active.
    // Stores the character name in the cache and in ISXGlassChars.
    void Launch(SessionID sessionId, const char* characterName, const char* server);

    // Returns the character name for the given account ID, or empty string
    // if not known.
    std::string GetCharacterName(SessionID sessionId);

    // Re-enumerates active Inner Space sessions and refreshes the session cache.
    void EnumerateSessions(bool sendNotify=false);

    void SendSessionConnected(const std::string& sessionName, DWORD pid, HWND hwnd);

    // Returns a copy of the full session cache.
    const std::map<SessionID, SessionEntry>& GetSessions() const;

    // Returns true if a session named is<accountId> is currently active.
    bool IsSessionActive(SessionID sessionId);

private:
    // Parses the account ID from a session name e.g. "is9" -> 9.
    // Returns 0 if the name does not match the expected format.
    unsigned int ParseSessionId(const char* sessionName);

    // Marks an account ID as expecting a new session to come online.
    void AddPendingLaunch(const std::string& sessionName);

    // Enumerates CPU sets and builds an affinity mask covering only
    // performance cores. Falls back to all cores if topology is uniform.
    void BuildPerformanceCoreMask();

    std::map<SessionID, SessionEntry> _sessions;
};

extern SessionManager g_SessionManager;