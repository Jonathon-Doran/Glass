#include "ISXGlass.h"
#include "LSVariables.h"
#include "KeyManager.h"
#include "Logger.h"

#include <sstream>

// Parse a string into up to 4 tokens separated by spaces.
// Returns the number of tokens found.
static int ParseTokens(const std::string& input, std::string* tokens, int maxTokens)
{
    int count = 0;
    size_t pos = 0;
    while ((count < maxTokens) && (pos < input.size()))
    {
        size_t next = input.find(' ', pos);
        if (next == std::string::npos)
        {
            tokens[count++] = input.substr(pos);
            break;
        }
        if (count == (maxTokens - 1))
        {
            // Last slot gets the remainder unsplit
            tokens[count++] = input.substr(pos);
            break;
        }
        tokens[count++] = input.substr(pos, next - pos);
        pos = next + 1;
    }
    return count;
}

// ----------------------------------------------------------------------------
// Verb handlers
// ----------------------------------------------------------------------------

static void HandleStatus(const std::string& args)
{
    static char response[4096];

    g_SessionManager.EnumerateSessions();

    const auto& sessions = g_SessionManager.GetSessions();
    Logger::Instance().Write("HandleStatus: session count=%u", (unsigned int)sessions.size());

    snprintf(response, sizeof(response), "Sessions=%u", (unsigned int)sessions.size());
    g_PipeManager.Send(response);

    for (const auto& pair : sessions)
    {
        unsigned int accountId = pair.first;
        const std::string& characterName = pair.second.characterName;
        Logger::Instance().Write("HandleStatus: slot=%u character=%s", accountId, characterName.c_str());
        snprintf(response, sizeof(response), "Session %u Name=is%u Character=%s", accountId, accountId, characterName.c_str());
        g_PipeManager.Send(response);
    }

    // Capture VideoFeed:DumpSources output into a LavishScript variable, then read it back.
    pISInterface->ExecuteCommand("declarevariable Sources index:string");
    pISInterface->ExecuteCommand("LavishScript:Eval[\"VideoFeed:DumpSources\", Sources]");

    LSOBJECT result;
    char buffer[256] = {};

    if (pISInterface->DataParse("Sources.Size", result) && result.Type)
    {
        unsigned int lineCount = result.DWord;
        Logger::Instance().Write("VideoFeed:DumpSources: %u lines", lineCount);

        for (unsigned int i = 1; i <= lineCount; i++)
        {
            char query[64] = {};
            snprintf(query, sizeof(query), "Sources[%u]", i);
            if (pISInterface->DataParse(query, result) && result.Type)
            {
                buffer[0] = 0;
                result.Type->ToText(result.GetObjectData(), buffer, sizeof(buffer));
                Logger::Instance().Write("VideoFeed:DumpSources[%u]: %s", i, buffer);
            }
        }
    }
    else
    {
        Logger::Instance().Write("VideoFeed:DumpSources: failed to read Sources.Size");
    }
}


//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// HandleLaunch
//
// Launches an EverQuest session for the given account.
// Expects: accountId characterName server characterId
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
static void HandleLaunch(const std::string& args)
{
    std::string tokens[4];
    int count = ParseTokens(args, tokens, 4);

    if (count < 4)
    {
        Logger::Instance().Write("HandleLaunch: requires accountId, characterName, server, and characterId.");
        g_PipeManager.Send("launch error requires accountId characterName server characterId");
        return;
    }

    unsigned int accountId = (unsigned int)atoi(tokens[0].c_str());
    if (accountId == 0)
    {
        Logger::Instance().Write("HandleLaunch: invalid accountId: %s", tokens[0].c_str());
        g_PipeManager.Send("launch error invalid accountId");
        return;
    }

    CharacterID characterId = (CharacterID)atoi(tokens[3].c_str());

    Logger::Instance().Write("HandleLaunch: accountId=%u characterName=%s server=%s characterId=%u",
        accountId, tokens[1].c_str(), tokens[2].c_str(), characterId);

    g_SessionManager.Launch(accountId, tokens[1].c_str(), tokens[2].c_str(), characterId);
    g_PipeManager.Send(("launch " + tokens[1]).c_str());
}

// Executes an Inner Space console command received from Glass via the relay verb,
// captures output into a LavishScript variable, and pipes each line back to Glass.
static void HandleExec(const std::string& args)
{
    Logger::Instance().Write("HandleRelay: %s", args.c_str());

  //  char command[1024] = {};
    // Helper to escape quotes for LavishScript
    std::string escaped_args = "";
    for (char c : args) {
        if (c == '\"') escaped_args += "\\\"";
        else escaped_args += c;
    }

    char command[2048] = {}; // Increased size to handle extra escape chars
    snprintf(command, sizeof(command), "LavishScript:Eval[\"%s\", RelayOutput]", escaped_args.c_str());
  //  snprintf(command, sizeof(command), "LavishScript:Eval[\"%s\", RelayOutput]", args.c_str());

    pISInterface->ExecuteCommand("declarevariable RelayOutput index:string");
    pISInterface->ExecuteCommand("declarevariable RelayIterator iterator");
    pISInterface->ExecuteCommand(command);
    pISInterface->ExecuteCommand("RelayOutput:GetIterator[RelayIterator]");

    LSOBJECT result;
    char buffer[4096] = {};

    LSOBJECT valid;
    if (!pISInterface->DataParse("RelayIterator:First", valid) || !valid.Type)
    {
        Logger::Instance().Write("HandleRelay: iterator First failed or empty");
        return;
    }

    bool hasValue = valid.DWord != 0;
    while (hasValue)
    {
        if (pISInterface->DataParse("RelayIterator.Value", result) && result.Type)
        {
            buffer[0] = 0;
            result.Type->ToText(result.GetObjectData(), buffer, sizeof(buffer));
            Logger::Instance().Write("HandleRelay value: %s", buffer);
            g_PipeManager.Send(buffer);
        }

        LSOBJECT next;
        if (!pISInterface->DataParse("RelayIterator:Next", next) || !next.Type)
        {
            break;
        }
        hasValue = next.DWord != 0;
    }
}

static void HandleVar(const std::string& args)
{
    // var declare <name> <type>
    // var set <name> <key> <value>
    // var setat <name> <index> <value>
    // var get <name> <key>
    // var getat <name> <index>
    // var exists <name>
    // var keyexists <name> <key>
    // var indexexists <name> <index>

    std::string tokens[4];
    int count = ParseTokens(args, tokens, 4);

    if (count < 1)
    {
        Logger::Instance().Write("HandleVar: no subverb provided.");
        g_PipeManager.Send("var error no subverb");
        return;
    }

    std::string& subverb = tokens[0];
    std::string& varName = tokens[1];
    std::string& keyOrIndex = tokens[2];
    std::string& value = tokens[3];

    if (subverb == "declare")
    {
        if (count < 3)
        {
            Logger::Instance().Write("HandleVar: declare requires name and type.");
            g_PipeManager.Send("var error declare requires name and type");
            return;
        }
        LSVariables::Declare(varName.c_str(), keyOrIndex.c_str());
        g_PipeManager.Send("var ok");
    }
    else if (subverb == "set")
    {
        if (count < 4)
        {
            Logger::Instance().Write("HandleVar: set requires name, key, and value.");
            g_PipeManager.Send("var error set requires name key and value");
            return;
        }
        LSVariables::SetString(varName.c_str(), keyOrIndex.c_str(), value.c_str());
        g_PipeManager.Send("var ok");
    }
    else if (subverb == "setat")
    {
        if (count < 4)
        {
            Logger::Instance().Write("HandleVar: setat requires name, index, and value.");
            g_PipeManager.Send("var error setat requires name index and value");
            return;
        }
        unsigned int index = (unsigned int)atoi(keyOrIndex.c_str());
        LSVariables::SetStringAt(varName.c_str(), index, value.c_str());
        g_PipeManager.Send("var ok");
    }
    else if (subverb == "get")
    {
        if (count < 3)
        {
            Logger::Instance().Write("HandleVar: get requires name and key.");
            g_PipeManager.Send("var error get requires name and key");
            return;
        }
        char buf[256];
        buf[0] = 0;
        if (LSVariables::GetString(varName.c_str(), keyOrIndex.c_str(), buf, sizeof(buf)))
        {
            char response[512];
            snprintf(response, sizeof(response), "var result %s", buf);
            g_PipeManager.Send(response);
        }
        else
        {
            g_PipeManager.Send("var notfound");
        }
    }
    else if (subverb == "getat")
    {
        if (count < 3)
        {
            Logger::Instance().Write("HandleVar: getat requires name and index.");
            g_PipeManager.Send("var error getat requires name and index");
            return;
        }
        unsigned int index = (unsigned int)atoi(keyOrIndex.c_str());
        char buf[256];
        buf[0] = 0;
        if (LSVariables::GetStringAt(varName.c_str(), index, buf, sizeof(buf)))
        {
            char response[512];
            snprintf(response, sizeof(response), "var result %s", buf);
            g_PipeManager.Send(response);
        }
        else
        {
            g_PipeManager.Send("var notfound");
        }
    }
    else if (subverb == "exists")
    {
        if (count < 2)
        {
            Logger::Instance().Write("HandleVar: exists requires name.");
            g_PipeManager.Send("var error exists requires name");
            return;
        }
        bool exists = LSVariables::Exists(varName.c_str());
        g_PipeManager.Send(exists ? "var exists true" : "var exists false");
    }
    else if (subverb == "keyexists")
    {
        if (count < 3)
        {
            Logger::Instance().Write("HandleVar: keyexists requires name and key.");
            g_PipeManager.Send("var error keyexists requires name and key");
            return;
        }
        bool exists = LSVariables::KeyExists(varName.c_str(), keyOrIndex.c_str());
        g_PipeManager.Send(exists ? "var keyexists true" : "var keyexists false");
    }
    else if (subverb == "indexexists")
    {
        if (count < 3)
        {
            Logger::Instance().Write("HandleVar: indexexists requires name and index.");
            g_PipeManager.Send("var error indexexists requires name and index");
            return;
        }
        unsigned int index = (unsigned int)atoi(keyOrIndex.c_str());
        bool exists = LSVariables::IndexExists(varName.c_str(), index);
        g_PipeManager.Send(exists ? "var indexexists true" : "var indexexists false");
    }
    else
    {
        Logger::Instance().Write("HandleVar: unknown subverb: %s", subverb.c_str());
        g_PipeManager.Send("var error unknown subverb");
    }
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// HandleNewProfile
//
// Called when Glass notifies ISXGlass that a new profile is being launched.
// Resets all KeyManager state so stale group and command definitions are cleared
// before the new profile's state is pushed.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
static void HandleNewProfile(const std::string& args)
{
    Logger::Instance().Write("HandleNewProfile: resetting KeyManager state.");
    g_KeyManager.Reset();
    g_SessionManager.Reset();
    Logger::Instance().Write("HandleNewProfile: reset complete.");
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// HandleRelayGroup
//
// Handles a bulk relay group membership message from Glass.
// Expects: groupId characterId [characterId ...]
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
static void HandleRelayGroup(const std::string& args)
{
    std::istringstream stream(args);
    std::string token;

    if (!std::getline(stream, token, ' '))
    {
        Logger::Instance().Write("HandleRelayGroup: missing groupId.");
        return;
    }

    GroupID groupId = (GroupID)atoi(token.c_str());
    Logger::Instance().Write("HandleRelayGroup: groupId=%u", groupId);

    std::vector<CharacterID> characterIds;
    while (std::getline(stream, token, ' '))
    {
        if (!token.empty())
        {
            CharacterID characterId = (CharacterID)atoi(token.c_str());
            characterIds.push_back(characterId);
            Logger::Instance().Write("HandleRelayGroup: groupId=%u characterId=%u", groupId, characterId);
        }
    }

    g_KeyManager.LoadRelayGroup(groupId, characterIds);
}

static void HandleGroupDefine(const std::string& args)
{
    std::string tokens[1];
    if (ParseTokens(args, tokens, 1) < 1)
    {
        Logger::Instance().Write("HandleGroupDefine: requires groupId.");
        return;
    }
    GroupID groupId = (GroupID)atoi(tokens[0].c_str());
    Logger::Instance().Write("HandleGroupDefine: groupId=%u", groupId);
    g_KeyManager.DefineGroup(groupId);
}

static void HandleGroupAdd(const std::string& args)
{
    std::string tokens[2];
    if (ParseTokens(args, tokens, 2) < 2)
    {
        Logger::Instance().Write("HandleGroupAdd: requires groupId and sessionName.");
        return;
    }
    GroupID groupId = (GroupID)atoi(tokens[0].c_str());
    Logger::Instance().Write("HandleGroupAdd: groupId=%u session=%s", groupId, tokens[1].c_str());
    g_KeyManager.AddToGroup(groupId, tokens[1]);
}

static void HandleGroupRemove(const std::string& args)
{
    std::string tokens[2];
    if (ParseTokens(args, tokens, 2) < 2)
    {
        Logger::Instance().Write("HandleGroupRemove: requires groupId and sessionName.");
        return;
    }
    GroupID groupId = (GroupID)atoi(tokens[0].c_str());
    Logger::Instance().Write("HandleGroupRemove: groupId=%u session=%s", groupId, tokens[1].c_str());
    g_KeyManager.RemoveFromGroup(groupId, tokens[1]);
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// HandleCmdDefine
//
// Declares a command ID, clearing any existing steps.
// Protocol: cmd_define <commandId>
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
static void HandleCmdDefine(const std::string& args)
{
    std::string tokens[1];
    if (ParseTokens(args, tokens, 1) < 1)
    {
        Logger::Instance().Write("HandleCmdDefine: requires commandId.");
        return;
    }
    CommandID commandId = (CommandID)atoi(tokens[0].c_str());
    Logger::Instance().Write("HandleCmdDefine: commandId=%u", commandId);
    g_KeyManager.DeclareCommand(commandId);
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// HandleCmdStep
//
// Adds a single step to an existing command definition.
// Protocol: cmd_step <commandId> <sequence> <type> <delayMs> <value>
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
static void HandleCmdStep(const std::string& args)
{
 

    std::string tokens[5];
    if (ParseTokens(args, tokens, 5) < 5)
    {
        Logger::Instance().Write("HandleCmdStep: requires commandId, sequence, type, delayMs, and value.");
        return;
    }

    CommandID    commandId = (CommandID)atoi(tokens[0].c_str());
    StepID       sequence = (StepID)atoi(tokens[1].c_str());
    unsigned int delayMs = (unsigned int)atoi(tokens[3].c_str());
    std::string& value = tokens[4];

    CommandActionType actionType;
    if (tokens[2] == "key")
    {
        actionType = CommandActionType::Keystroke;
    }
    else if (tokens[2] == "text")
    {
        actionType = CommandActionType::Text;
    }
    else
    {
        Logger::Instance().Write("HandleCmdStep: unknown type: %s", tokens[2].c_str());
        return;
    }

    Logger::Instance().Write("HandleCmdStep: commandId=%u sequence=%u type=%s delayMs=%u value=%s",
        commandId, sequence, tokens[2].c_str(), delayMs, value.c_str());

    g_KeyManager.AddCommandStep(commandId, sequence, actionType, delayMs, value);
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// HandleActivate
//
// Called when Glass notifies ISXGlass that a session has gained focus.
// Protocol: activate <sessionName>
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
static void HandleActivate(const std::string& args)
{
    if (args.empty())
    {
        Logger::Instance().Write("HandleActivate: missing sessionName.");
        return;
    }
    Logger::Instance().Write("HandleActivate: sessionName='%s'.", args.c_str());
    SessionEntry* session = g_SessionManager.FindSession(args);
    g_SessionManager.SetActiveSession(session);
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// HandleCmdRepeatStart
//
// Starts auto-repeat for a command on a group at the given interval.
// Protocol: cmd_repeat_start <commandId> <groupId> <intervalMs> <roundRobin>
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
static void HandleCmdRepeatStart(const std::string& args)
{
    std::string tokens[4];
    if (ParseTokens(args, tokens, 4) < 4)
    {
        Logger::Instance().Write("HandleCmdRepeatStart: requires commandId, groupId, intervalMs, and roundRobin.");
        return;
    }
    CommandID    commandId = (CommandID)atoi(tokens[0].c_str());
    GroupID      groupId = (GroupID)atoi(tokens[1].c_str());
    unsigned int intervalMs = (unsigned int)atoi(tokens[2].c_str());
    bool         roundRobin = (atoi(tokens[3].c_str()) != 0);
    Logger::Instance().Write("HandleCmdRepeatStart: commandId=%u groupId=%u intervalMs=%u roundRobin=%d",
        commandId, groupId, intervalMs, (int)roundRobin);
    g_KeyManager.StartRepeat(commandId, groupId, intervalMs, roundRobin);
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// HandleCmdRepeatStop
//
// Stops auto-repeat for a command on a group.
// Protocol: cmd_repeat_stop <commandId> <groupId>
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
static void HandleCmdRepeatStop(const std::string& args)
{
    std::string tokens[2];
    if (ParseTokens(args, tokens, 2) < 2)
    {
        Logger::Instance().Write("HandleCmdRepeatStop: requires commandId and groupId.");
        return;
    }
    CommandID commandId = (CommandID)atoi(tokens[0].c_str());
    GroupID   groupId = (GroupID)atoi(tokens[1].c_str());
    Logger::Instance().Write("HandleCmdRepeatStop: commandId=%u groupId=%u", commandId, groupId);
    g_KeyManager.StopRepeat(commandId, groupId);
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// HandleCmdExecute
//
// Executes a command on the given target group.
// Protocol: cmd_execute <commandId> <groupId> <roundRobin>
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
static void HandleCmdExecute(const std::string& args)
{
    std::string tokens[2];
    if (ParseTokens(args, tokens, 3) < 2)
    {
        Logger::Instance().Write("HandleCmdExecute: requires commandId and groupId.");
        return;
    }
    CommandID commandId = (CommandID)atoi(tokens[0].c_str());
    GroupID groupId = (GroupID)atoi(tokens[1].c_str());
    bool roundRobin = (atoi(tokens[2].c_str()) != 0);
    Logger::Instance().Write("HandleCmdExecute: commandId=%u groupId=%u roundRobin=%d", commandId, groupId, (int)roundRobin);
    g_KeyManager.ExecuteCommand(commandId, groupId, roundRobin);
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// HandleCommand
//
// Dispatches a single command received from Glass.exe.
// Called from PulseService on the Inner Space pulse thread.
// No blocking operations permitted.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void HandleCommand(const std::string& cmd)
{
    Logger::Instance().Write("HandleCommand: %s", cmd.c_str());

    size_t spacePos = cmd.find(' ');
    std::string verb = (spacePos != std::string::npos) ? cmd.substr(0, spacePos) : cmd;
    std::string args = (spacePos != std::string::npos) ? cmd.substr(spacePos + 1) : "";

    if (verb == "status")
    {
        HandleStatus(args);
    }
    else if (verb == "launch")
    {
        HandleLaunch(args);
    }
    else if (verb == "exec")
    {
        HandleExec(args);
    }
    else if (verb == "var")
    {
        HandleVar(args);
    }
    else if (verb == "group_define")
    {
        HandleGroupDefine(args);
    }
    else if (verb == "group_add")
    {
        HandleGroupAdd(args);
    }
    else if (verb == "group_remove")
    {
        HandleGroupRemove(args);
    }
    else if (verb == "cmd_define")
    {
        HandleCmdDefine(args);
    }
    else if (verb == "cmd_step")
    {
        HandleCmdStep(args);
    }
    else if (verb == "cmd_execute")
    {
        HandleCmdExecute(args);
    }
    else if (verb == "start")
    {
        HandleCmdRepeatStart(args);
    }
    else if (verb == "stop")
    {
        HandleCmdRepeatStop(args);
    }
    else if (verb == "activate")
    {
        HandleActivate(args);
    }
    else if (verb == "new_profile")
    {
        HandleNewProfile(args);
    }
    else if (verb == "relay_group")
    {
        HandleRelayGroup(args);
    }
    else
    {
        Logger::Instance().Write("HandleCommand: unknown verb: %s", verb.c_str());
        g_PipeManager.Send("error unknown command");
    }
}