#pragma once
#include <map>
#include <vector>
#include <set>
#include <string>
#include <mutex>
#include <thread>
#include <atomic>
#include <queue>
#include <condition_variable>
#include <functional>
#include <windows.h>

typedef unsigned int CommandID;
typedef unsigned int GroupID;
typedef unsigned int StepID;
typedef unsigned int CharacterID;
typedef std::set<std::string> MemberSet;

// Special target group IDs reserved by the Glass protocol.
// Values >= 4 are relay group IDs defined by the database.
enum class SpecialTarget : GroupID
{
    None = 0,
    Self = 1,
    All = 2,
    Others = 3
};

// Describes the type of action a command performs.
enum class CommandActionType
{
    Keystroke,       // press
    KeystrokeHold,   // hold
    KeystrokeRelease, // release
    Text
};

// A single step within a command.
struct CommandStep
{
    StepID              sequence;
    CommandActionType   actionType;
    unsigned int        delayMs;
    std::string         value;
};

// A command definition — maps a command ID to an ordered map of steps.
struct CommandDefinition
{
    CommandID                        id;
    std::map<StepID, CommandStep>    steps;
};

// Tracks repeat state for a running auto-repeat binding.
struct RepeatState
{
    CommandID         commandId;
    GroupID           groupId;
    unsigned int      intervalMs;
    std::thread       thread;
    std::atomic<bool> running;

    RepeatState() : commandId(0), groupId(0), intervalMs(0), running(false) {}
};

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager
//
// Owns group membership, command definitions, round-robin state, auto-repeat
// threads, and the execution queue for the ISXGlass keyboard system.
//
// All pISInterface->ExecuteCommand calls are routed through a single worker
// thread to prevent concurrent access to the IS interface.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
class KeyManager
{
public:
    // Starts the execution worker thread.
    void Start();

    // Stops the execution worker thread and all repeat threads cleanly.
    void Shutdown();

    // Clears state in preparation for a new profile.
    void Reset();

    // Stores pending group membership from a bulk relay_group message.
    void LoadRelayGroup(GroupID groupId, const std::vector<CharacterID>& characterIds);

    // Resolves pending group memberships for the given character ID.
    void ResolvePending(CharacterID characterId, const std::string& sessionName);

    // Registers a group ID.
    void DefineGroup(GroupID groupId);

    // Adds a session to a group.
    void AddToGroup(GroupID groupId, const std::string& sessionName);

    // Removes a session from a group (for example due to ATG window)
    void RemoveFromGroup(GroupID groupId, const std::string& sessionName);

    // Removes a session from all groups (for example, due to session disconnect)
    void RemoveFromAllGroups(const std::string& sessionName);

    // Declares a command ID, clearing any existing steps.
    void DeclareCommand(CommandID commandId);

    // Adds or replaces a single step in an existing command definition.
    void AddCommandStep(CommandID commandId, StepID sequence, CommandActionType actionType, unsigned int delayMs, const std::string& value);

    // Executes a command on the given target group via the execution queue.
    void ExecuteCommand(CommandID commandId, GroupID groupId, bool roundRobin);

    // Starts auto-repeat for a command on a group at the given interval.
    void StartRepeat(CommandID commandId, GroupID groupId, unsigned int intervalMs, bool roundRobin);

    // Stops auto-repeat for a command on a group.
    void StopRepeat(CommandID commandId, GroupID groupId);

private:
    std::string SubstituteVariables(const std::string& value);

    // Returns the next session name in round-robin order for the given group.
    std::string RoundRobinNext(GroupID groupId);

    // Enqueues all steps of a command for execution on the given session.
    void EnqueueExecution(const CommandDefinition& cmd, const SessionEntry* session);

    // Worker thread entry point — drains the execution queue.
    void ExecutionWorker();

    // Returns a randomized human-like delay scaled around the given base value in milliseconds.
    unsigned int HumanDelay(unsigned int baseMs);

    std::mutex                                           _mutex;
    std::map<GroupID, MemberSet>                         _presentMembers;
    std::map<GroupID, std::set<CharacterID>>             _registeredMembers;
    std::map<GroupID, MemberSet::iterator>               _roundRobinIterators;
    std::map<CommandID, CommandDefinition>               _commands;
    std::map<std::pair<CommandID, GroupID>, RepeatState> _repeats;
   

    // Execution queue
    std::queue<std::function<void()>>   _execQueue;
    std::mutex                          _execMutex;
    std::condition_variable             _execCV;
    std::atomic<bool>                   _execRunning;
    std::thread                         _execThread;
};

extern KeyManager g_KeyManager;