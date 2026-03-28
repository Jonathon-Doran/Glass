#include "ISXGlass.h"
#include "KeyManager.h"
#include "Logger.h"
#include <random>

KeyManager g_KeyManager;


//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager::Start
//
// Starts the execution worker thread.
// Called once during ISXGlass initialization.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void KeyManager::Start()
{
    _execRunning = true;
    _execThread = std::thread(&KeyManager::ExecutionWorker, this);
    Logger::Instance().Write("KeyManager::Start: execution worker thread started.");
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager::LoadRelayGroup
//
// Stores pending group membership from a bulk relay_group message.
// Defines the group, stores all character IDs as pending, and resolves
// any characters that are already connected.
//
// groupId:      The group to define
// characterIds: The Glass character IDs that belong to this group
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void KeyManager::LoadRelayGroup(GroupID groupId, const std::vector<CharacterID>& characterIds)
{
    Logger::Instance().Write("KeyManager::LoadRelayGroup: groupId=%u count=%zu", groupId, characterIds.size());

    std::vector<std::pair<CharacterID, std::string>> toResolve;

    {
        std::lock_guard<std::mutex> lock(_mutex);

        if (_presentMembers.find(groupId) == _presentMembers.end())
        {
            _presentMembers[groupId] = MemberSet();
        }

        _registeredMembers[groupId] = std::set<CharacterID>(characterIds.begin(), characterIds.end());

        for (CharacterID characterId : characterIds)
        {
            SessionEntry* entry = g_SessionManager.FindSessionForCharacter(characterId);
            if (entry != nullptr)
            {
                toResolve.push_back({ characterId, entry->sessionName });
            }
        }
    }

    for (const auto& pair : toResolve)
    {
        ResolvePending(pair.first, pair.second);
    }
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager::ResolvePending
//
// Resolves pending group memberships for the given character.
// Called when a session connects. If the character has no pending memberships, returns silently.
//
// characterId:  The Glass character ID that just connected
// sessionName:  The session name to add to any pending groups
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void KeyManager::ResolvePending(CharacterID characterId, const std::string& sessionName)
{
    std::lock_guard<std::mutex> lock(_mutex);

    for (std::pair<const GroupID, std::set<CharacterID>>& pair : _registeredMembers)
    {
        if (pair.second.find(characterId) != pair.second.end())
        {
            GroupID groupId = pair.first;
            _presentMembers[groupId].insert(sessionName);
            _roundRobinIterators[groupId] = _presentMembers[groupId].begin();
            Logger::Instance().Write("KeyManager::ResolvePending: characterId=%u resolved to session=%s in groupId=%u",
                characterId, sessionName.c_str(), groupId);
        }
    }

    // Add to the All group.
    GroupID allGroupId = (GroupID)SpecialTarget::All;
    _presentMembers[allGroupId].insert(sessionName);
    _roundRobinIterators[allGroupId] = _presentMembers[allGroupId].begin();
    Logger::Instance().Write("KeyManager::ResolvePending: session=%s added to All group.", sessionName.c_str());

    // Mirror All into Others. Others uses the same membership as All,
    // but excludes the active session at execution time.
    GroupID othersGroupId = (GroupID)SpecialTarget::Others;
    _presentMembers[othersGroupId] = _presentMembers[allGroupId];
    _roundRobinIterators[othersGroupId] = _presentMembers[othersGroupId].begin();
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager::DefineGroup
//
// Registers a group ID. Creates an empty member set if the group does not already exist.
//
// groupId:  The group to register
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void KeyManager::DefineGroup(GroupID groupId)
{
    std::lock_guard<std::mutex> lock(_mutex);

    if (_presentMembers.find(groupId) == _presentMembers.end())
    {
        _presentMembers[groupId] = MemberSet();
        Logger::Instance().Write("KeyManager::DefineGroup: groupId=%u", groupId);
    }
    else
    {
        Logger::Instance().Write("KeyManager::DefineGroup: groupId=%u already exists, skipping.", groupId);
    }
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager::AddToGroup
//
// Adds a session to a group. Creates the group if it does not exist.
// Resets the round-robin iterator for the group.
//
// groupId:      The group to add to
// sessionName:  The session to add
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void KeyManager::AddToGroup(GroupID groupId, const std::string& sessionName)
{
    std::lock_guard<std::mutex> lock(_mutex);

    _presentMembers[groupId].insert(sessionName);
    _roundRobinIterators[groupId] = _presentMembers[groupId].begin();

    Logger::Instance().Write("KeyManager::AddToGroup: groupId=%u session=%s", groupId, sessionName.c_str());
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager::RemoveFromGroup
//
// Removes a session from a group. Resets the round-robin iterator for the group.
//
// groupId:      The group to remove from
// sessionName:  The session to remove
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void KeyManager::RemoveFromGroup(GroupID groupId, const std::string& sessionName)
{
    std::lock_guard<std::mutex> lock(_mutex);

    auto groupIt = _presentMembers.find(groupId);
    if (groupIt == _presentMembers.end())
    {
        Logger::Instance().Write("KeyManager::RemoveFromGroup: groupId=%u not found.", groupId);
        return;
    }

    groupIt->second.erase(sessionName);
    _roundRobinIterators[groupId] = groupIt->second.begin();

    Logger::Instance().Write("KeyManager::RemoveFromGroup: groupId=%u session=%s removed.", groupId, sessionName.c_str());
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager::RemoveFromAllGroups
//
// Removes a session from all present member groups, including the All group.
// Resets the round-robin iterator for any affected group.
// Called when a session disconnects.
//
// sessionName:  The session name to remove
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void KeyManager::RemoveFromAllGroups(const std::string& sessionName)
{
    std::lock_guard<std::mutex> lock(_mutex);

    for (std::pair<const GroupID, MemberSet>& pair : _presentMembers)
    {
        if (pair.second.erase(sessionName) > 0)
        {
            _roundRobinIterators[pair.first] = pair.second.begin();
            Logger::Instance().Write("KeyManager::RemoveFromAllGroups: session=%s removed from groupId=%u.",
                sessionName.c_str(), pair.first);
        }
    }
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager::DeclareCommand
//
// Registers a command ID, clearing any existing steps.
// Called when Glass sends cmd_define before the cmd_step messages.
//
// commandId:  The command ID to declare
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void KeyManager::DeclareCommand(CommandID commandId)
{
    std::lock_guard<std::mutex> lock(_mutex);

    CommandDefinition& def = _commands[commandId];
    def.id = commandId;
    def.steps.clear();

    Logger::Instance().Write("KeyManager::DeclareCommand: commandId=%u", commandId);
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager::AddCommandStep
//
// Adds a single step to an existing command definition.
// If the command has not been declared, logs a warning and returns.
//
// commandId:   The command to add the step to
// sequence:    The step sequence number — steps execute in ascending sequence order
// actionType:  Keystroke or Text
// delayMs:     Milliseconds to wait after executing this step
// value:       The keystroke string or text string to execute
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void KeyManager::AddCommandStep(CommandID commandId, StepID sequence, CommandActionType actionType, unsigned int delayMs, const std::string& value)
{
    std::lock_guard<std::mutex> lock(_mutex);

    auto it = _commands.find(commandId);
    if (it == _commands.end())
    {
        Logger::Instance().Write("KeyManager::AddCommandStep: commandId=%u not declared, ignoring step sequence=%u.", commandId, sequence);
        return;
    }

    CommandStep step;
    step.sequence = sequence;
    step.actionType = actionType;
    step.delayMs = delayMs;
    step.value = value;

    it->second.steps[sequence] = step;

    Logger::Instance().Write("KeyManager::AddCommandStep: commandId=%u sequence=%u type=%d delayMs=%u value=%s",
        commandId, sequence, (int)actionType, delayMs, value.c_str());
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager::RoundRobinNext
//
// Returns the next session name in round-robin order for the given group.
// Advances the iterator, wrapping around. Returns empty string if the group
// is empty or not found. Caller must hold _mutex.
//
// groupId:  The group to advance
// 
// Returns:  The session name for the next member in round-robin order.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
std::string KeyManager::RoundRobinNext(GroupID groupId)
{
    auto groupIt = _presentMembers.find(groupId);
    if ((groupIt == _presentMembers.end()) || groupIt->second.empty())
    {
        return std::string();
    }

    auto& members = groupIt->second;
    auto& it = _roundRobinIterators[groupId];

    if ((it == members.end()) || (it == members.begin() && members.size() == 0))
    {
        it = members.begin();
    }

    std::string session = *it;
    ++it;
    if (it == members.end())
    {
        it = members.begin();
    }

    return session;
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager::ExecuteCommand
//
// Executes a command on the target specified by groupId.
// Self is handled directly via the active session.
// All other targets (All, Others, relay groups) use _presentMembers for execution,
// with round-robin or broadcast depending on the roundRobin flag.
//
// commandId:   The command to execute
// groupId:     The target group ID or special target value
// roundRobin:  If true, execute on one session in round-robin order; otherwise broadcast
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void KeyManager::ExecuteCommand(CommandID commandId, GroupID groupId, bool roundRobin)
{
    std::lock_guard<std::mutex> lock(_mutex);

    auto cmdIt = _commands.find(commandId);
    if (cmdIt == _commands.end())
    {
        Logger::Instance().Write("KeyManager::ExecuteCommand: commandId=%u not found.", commandId);
        return;
    }

    Logger::Instance().Write("KeyManager::ExecuteCommand: commandId=%u groupId=%u roundRobin=%d", commandId, groupId, (int)roundRobin);

    if (groupId == (GroupID)SpecialTarget::None)
    {
        Logger::Instance().Write("KeyManager::ExecuteCommand: target=None, skipping.");
        return;
    }

    if (groupId == (GroupID)SpecialTarget::Self)
    {
        SessionEntry* activeSession = g_SessionManager.GetActiveSession();
        if (!activeSession)
        {
            Logger::Instance().Write("KeyManager::ExecuteCommand: target=Self but no active session.");
            return;
        }
        Logger::Instance().Write("KeyManager::ExecuteCommand: target=Self session='%s'.", activeSession->sessionName.c_str());
        EnqueueExecution(cmdIt->second, activeSession);
        return;
    }

    // For Others, exclude the active session from execution.
    SessionEntry* excludeSession = nullptr;
    if (groupId == (GroupID)SpecialTarget::Others)
    {
        excludeSession = g_SessionManager.GetActiveSession();
    }

    auto groupIt = _presentMembers.find((GroupID) SpecialTarget::All);
    if ((groupIt == _presentMembers.end()) || groupIt->second.empty())
    {
        Logger::Instance().Write("KeyManager::ExecuteCommand: groupId=%u not found or empty.", groupId);
        return;
    }

    if (roundRobin)
    {
        std::string sessionName = RoundRobinNext(groupId);
        if (sessionName.empty())
        {
            Logger::Instance().Write("KeyManager::ExecuteCommand: groupId=%u round-robin returned empty.", groupId);
            return;
        }
        SessionEntry* session = g_SessionManager.FindSession(sessionName);
        if ((session != nullptr) && (session != excludeSession))
        {
            Logger::Instance().Write("KeyManager::ExecuteCommand: commandId=%u groupId=%u round-robin session='%s'.",
                commandId, groupId, sessionName.c_str());
            EnqueueExecution(cmdIt->second, session);
        }
        else
        {
            Logger::Instance().Write("KeyManager::ExecuteCommand: groupId=%u round-robin session excluded or not found.", groupId);
        }
    }
    else
    {
        Logger::Instance().Write("KeyManager::ExecuteCommand: commandId=%u groupId=%u members=%zu.",
            commandId, groupId, groupIt->second.size());
        for (const std::string& memberName : groupIt->second)
        {
            SessionEntry* session = g_SessionManager.FindSession(memberName);
            if ((session != nullptr) && (session != excludeSession))
            {
                EnqueueExecution(cmdIt->second, session);
            }
        }
    }
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager::StartRepeat
//
// Starts auto-repeat for a command on a group at the given interval.
// If already running, logs and returns.
//
// commandId:   The command to repeat
// groupId:     The target group
// intervalMs:  The repeat interval in milliseconds
// roundRobin:  If true, execute on one session in round-robin order; otherwise broadcast
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void KeyManager::StartRepeat(CommandID commandId, GroupID groupId, unsigned int intervalMs, bool roundRobin)
{
    std::lock_guard<std::mutex> lock(_mutex);

    auto key = std::make_pair(commandId, groupId);
    auto it = _repeats.find(key);
    if ((it != _repeats.end()) && it->second.running)
    {
        Logger::Instance().Write("KeyManager::StartRepeat: commandId=%u groupId=%u already running.", commandId, groupId);
        return;
    }

    Logger::Instance().Write("KeyManager::StartRepeat: commandId=%u groupId=%u intervalMs=%u roundRobin=%d",
        commandId, groupId, intervalMs, (int)roundRobin);


    RepeatState& state = _repeats[key];
    state.commandId = commandId;
    state.groupId = groupId;
    state.intervalMs = intervalMs;
    state.running = true;

    state.thread = std::thread([this, commandId, groupId, intervalMs, roundRobin, &state]()
        {
            Logger::Instance().Write("KeyManager: repeat thread started. commandId=%u groupId=%u", commandId, groupId);
            while (state.running)
            {
                ExecuteCommand(commandId, groupId, roundRobin);
                std::this_thread::sleep_for(std::chrono::milliseconds(HumanDelay(intervalMs)));
            }
            Logger::Instance().Write("KeyManager: repeat thread stopped. commandId=%u groupId=%u", commandId, groupId);
        });
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager::StopRepeat
//
// Stops auto-repeat for a command on a group and joins the thread.
//
// commandId:  The command to stop
// groupId:    The target group
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void KeyManager::StopRepeat(CommandID commandId, GroupID groupId)
{
    std::unique_lock<std::mutex> lock(_mutex);

    auto key = std::make_pair(commandId, groupId);
    auto it = _repeats.find(key);
    if ((it == _repeats.end()) || !it->second.running)
    {
        Logger::Instance().Write("KeyManager::StopRepeat: commandId=%u groupId=%u not running.", commandId, groupId);
        return;
    }

    Logger::Instance().Write("KeyManager::StopRepeat: commandId=%u groupId=%u stopping.", commandId, groupId);

    it->second.running = false;
    lock.unlock();
    it->second.thread.join();

    std::lock_guard<std::mutex> relock(_mutex);
    _repeats.erase(key);

    Logger::Instance().Write("KeyManager::StopRepeat: commandId=%u groupId=%u stopped.", commandId, groupId);
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager::Reset
//
// Stops all running repeat threads and clears all group, command, round-robin,
// and execution queue state. The execution worker thread continues running.
// Called when a new profile is launched to ensure stale state does not carry over.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void KeyManager::Reset()
{
    Logger::Instance().Write("KeyManager::Reset: stopping all repeat threads.");

    std::unique_lock<std::mutex> lock(_mutex);

    for (auto& pair : _repeats)
    {
        if (pair.second.running)
        {
            pair.second.running = false;
        }
    }

    lock.unlock();

    for (auto& pair : _repeats)
    {
        if (pair.second.thread.joinable())
        {
            pair.second.thread.join();
        }
    }

    {
        std::lock_guard<std::mutex> execLock(_execMutex);
        while (!_execQueue.empty())
        {
            _execQueue.pop();
        }
        Logger::Instance().Write("KeyManager::Reset: execution queue cleared.");
    }

    std::lock_guard<std::mutex> relock(_mutex);
    _repeats.clear();
    _presentMembers.clear();
    _commands.clear();
    _roundRobinIterators.clear();

    Logger::Instance().Write("KeyManager::Reset: complete.");
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager::ExecutionWorker
//
// Worker thread that drains the execution queue.
// Blocks on the condition variable when the queue is empty.
// Exits cleanly when _execRunning is false and the queue is empty.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void KeyManager::ExecutionWorker()
{
    Logger::Instance().Write("KeyManager::ExecutionWorker: started.");

    while (true)
    {
        std::function<void()> task;

        {
            std::unique_lock<std::mutex> lock(_execMutex);
            _execCV.wait(lock, [this]()
                {
                    return (!_execQueue.empty() || !_execRunning);
                });

            if (!_execRunning && _execQueue.empty())
            {
                Logger::Instance().Write("KeyManager::ExecutionWorker: exiting.");
                return;
            }

            task = std::move(_execQueue.front());
            _execQueue.pop();
        }

        task();
    }
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager::HumanDelay
//
// Returns a randomized human-like delay in milliseconds scaled around the given base value.
// Distribution is non-linear, simulating natural human timing variation:
//   60% chance: 40-80% of base  (fast)
//   30% chance: 80-140% of base (brief pause)
//   10% chance: 140-300% of base (occasional hesitation)
//
// baseMs:  The center point of the delay distribution in milliseconds
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
unsigned int KeyManager::HumanDelay(unsigned int baseMs)
{
    if (baseMs == 0)
    {
        return 0;
    }

    static thread_local std::mt19937 rng(std::random_device{}());

    std::uniform_int_distribution<int> rollDist(0, 99);
    int roll = rollDist(rng);

    unsigned int pct;
    if (roll < 60)
    {
        std::uniform_int_distribution<unsigned int> rangeDist(40, 80);
        pct = rangeDist(rng);
    }
    else if (roll < 90)
    {
        std::uniform_int_distribution<unsigned int> rangeDist(80, 140);
        pct = rangeDist(rng);
    }
    else
    {
        std::uniform_int_distribution<unsigned int> rangeDist(140, 300);
        pct = rangeDist(rng);
    }

    return (baseMs * pct) / 100;
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager::EnqueueExecution
//
// Enqueues all steps of a command for execution on the given session.
// Keystroke steps are sent as a single relay press command.
// Text steps are sent one character at a time with randomized inter-character delays.
// A step delay is applied after all characters of a step are sent, if delayMs is non-zero.
//
// cmd:         The command definition containing the steps to execute
// sessionName: The session to execute on
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void KeyManager::EnqueueExecution(const CommandDefinition& cmd, const SessionEntry* session)
{
    if (cmd.steps.empty())
    {
        Logger::Instance().Write("KeyManager::EnqueueExecution: commandId=%u has no steps, skipping session=%s.",
            cmd.id, session->sessionName.c_str());
        return;
    }

    Logger::Instance().Write("KeyManager::EnqueueExecution: commandId=%u steps=%zu session=%s.",
        cmd.id, cmd.steps.size(), session->sessionName.c_str());


    for (const auto& pair : cmd.steps)
    {
        const CommandStep step = pair.second;
        const CommandID cmdId = cmd.id;

        if (step.actionType == CommandActionType::Keystroke)
        {
            std::lock_guard<std::mutex> lock(_execMutex);
            _execQueue.push([this, step, session, cmdId]()
                {
                    char command[512] = {};
                    snprintf(command, sizeof(command), "relay %s \"press %s\"",
                        session -> sessionName.c_str(), step.value.c_str());
                    Logger::Instance().Write("KeyManager::ExecutionWorker: commandId=%u sequence=%u keystroke: %s",
                        cmdId, step.sequence, command);
                    pISInterface->ExecuteCommand(command);

                    if (step.delayMs > 0)
                    {
                        std::this_thread::sleep_for(std::chrono::milliseconds(HumanDelay(step.delayMs)));
                    }
                });
        }
        else
        {
            // Text — one task per character with randomized delay
            std::string substituted = SubstituteVariables(step.value);
            for (char ch : substituted)
            {
                std::lock_guard<std::mutex> lock(_execMutex);
                const char character = ch;
                unsigned int delay = HumanDelay(100);

                _execQueue.push([this, character, session, cmdId, step, delay]()
                    {
                        char command[512] = {};
                        if (character == ' ')
                        {
                            snprintf(command, sizeof(command), "relay %s \"press Space\"",
                                session->sessionName.c_str());
                        }
                        else
                        {
                            snprintf(command, sizeof(command), "relay %s \"press %c\"",
                                session->sessionName.c_str(), character);
                        }
                        Logger::Instance().Write("KeyManager::ExecutionWorker: '%s'", command);
                        pISInterface->ExecuteCommand(command);

                        std::this_thread::sleep_for(std::chrono::milliseconds(delay));
                    });
            }

            {
                std::lock_guard<std::mutex> lock(_execMutex);
                _execQueue.push([this, session, cmdId, step]()
                    {
                        char command[512] = {};
                        snprintf(command, sizeof(command), "relay %s \"press Enter\"", session->sessionName.c_str());
                        pISInterface->ExecuteCommand(command);
                    });
            }

            if (step.delayMs > 0)
            {
                std::lock_guard<std::mutex> lock(_execMutex);
                unsigned int delayMs = step.delayMs;
                _execQueue.push([delayMs]()
                    {
                        std::this_thread::sleep_for(std::chrono::milliseconds(delayMs));
                    });
            }
        }
    }

    _execCV.notify_one();
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager::SubstituteVariables
//
// Substitutes template variables in a text step value.
// Variables are enclosed in braces e.g. {shortname}.
// Currently supported variables:
//   {shortname} — the first 4 characters of the active session's character name
//
// value:  The text string containing variables to substitute
//
// Returns:  The text string with all known variables replaced
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
std::string KeyManager::SubstituteVariables(const std::string& value)
{
    std::string result = value;

    size_t pos = result.find('{');
    if (pos == std::string::npos)
    {
        return result;
    }



    size_t start = 0;
    while ((start = result.find('{', start)) != std::string::npos)
    {
        size_t end = result.find('}', start);
        if (end == std::string::npos)
        {
            break;
        }

        std::string variable = result.substr(start + 1, end - start - 1);
        std::string replacement;

        if (variable == "shortname")
        {
            SessionEntry* active = g_SessionManager.GetActiveSession();
            if (active != nullptr)
            {
                replacement = active->characterName.substr(0, 4);
            }
            else
            {
                Logger::Instance().Write("KeyManager::SubstituteVariables: {shortname} requested but no active session.");
                replacement = "";
            }
        }
        else
        {
            Logger::Instance().Write("KeyManager::SubstituteVariables: unknown variable '{%s}', leaving as-is.", variable.c_str());
            start = end + 1;
            continue;
        }

        result.replace(start, end - start + 1, replacement);
        start += replacement.length();
    }

    Logger::Instance().Write("KeyManager::SubstituteVariables: '%s' -> '%s'", value.c_str(), result.c_str());
    return result;
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyManager::Shutdown
//
// Stops all running repeat threads and the execution worker thread cleanly.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void KeyManager::Shutdown()
{
    Logger::Instance().Write("KeyManager::Shutdown: stopping all repeat threads.");

    std::unique_lock<std::mutex> lock(_mutex);

    for (auto& pair : _repeats)
    {
        if (pair.second.running)
        {
            pair.second.running = false;
        }
    }

    lock.unlock();

    for (auto& pair : _repeats)
    {
        if (pair.second.thread.joinable())
        {
            pair.second.thread.join();
        }
    }

    {
        std::lock_guard<std::mutex> relock(_mutex);
        _repeats.clear();
    }

    // Stop execution worker thread
    _execRunning = false;
    _execCV.notify_all();

    if (_execThread.joinable())
    {
        _execThread.join();
    }

    Logger::Instance().Write("KeyManager::Shutdown: complete.");
}