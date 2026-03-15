#include "ISXGlass.h"

#define EXTENSION_VERSION "20260309"

#pragma comment(lib,"isxdk.lib")

ISXPreSetup("ISXGlass", ISXGlass);

void __cdecl PulseService(bool Broadcast, unsigned int MSG, void* lpData);
void __cdecl SystemService(bool Broadcast, unsigned int MSG, void* lpData);

std::queue<std::string> g_CommandQueue;
std::mutex g_QueueMutex;
static void OnCommandReceived(const std::string& command);

// Connects to Inner Space services. Isolated into its own function to allow
// __try/__except without conflicting with C++ object unwinding.
bool ISXGlass::ConnectToInnerSpace(ISInterface* p_ISInterface)
{
    __try
    {
        pISInterface = p_ISInterface;

        Logger::Instance().Write("Calling RegisterExtension");
        RegisterExtension();
        Logger::Instance().Write("RegisterExtension complete");

        Logger::Instance().Write("Connecting Pulse service");
        hPulseService = pISInterface->ConnectService(this, "Pulse", PulseService);
        Logger::Instance().Write("Pulse service handle: %X", hPulseService);

        Logger::Instance().Write("Connecting System service");
        hSystemService = pISInterface->ConnectService(this, "System", SystemService);
        Logger::Instance().Write("System service handle: %X", hSystemService);
    }
    _except(EzCrashFilter(GetExceptionInformation(), "Crash in ConnectToInnerSpace"))
    {
        TerminateProcess(GetCurrentProcess(), 0);
        return false;
    }
    return true;
}

bool ISXGlass::Initialize(ISInterface* p_ISInterface)
{
    Logger::Instance().Open(GLASS_LOG_PATH);
    Logger::Instance().Write("ISXGlass::Initialize");
    p_ISInterface->Printf("ISXGlass loaded successfully");

    if (!ConnectToInnerSpace(p_ISInterface))
    {
        return false;
    }

    g_PipeManager.Start(OnCommandReceived);
    g_SessionManager.Initialize();



    return true;
}

unsigned int ISXGlass::GetVersion()
{
    Logger::Instance().Write("ISXGlass::GetVersion");
    return 0x35;
}

void ISXGlass::Shutdown()
{
   // g_PipeManager.Reset();
    g_PipeManager.Stop();
    pISInterface->Printf("ISXGlass unloading.");
    if (hPulseService)
    {
        pISInterface->DisconnectService(this, hPulseService);
    }
    if (hSystemService)
    {
        pISInterface->DisconnectService(this, hSystemService);
    }
    Logger::Instance().Write("ISXGlass::Shutdown");
}

bool ISXGlass::RequestShutdown()
{
    Logger::Instance().Write("ISXGlass::RequestShutdown");
    return true;
}

void ISXGlass::RegisterExtension()
{
    unsigned int ExtensionSetGUID = pISInterface->GetExtensionSetGUID("ISXGlass");
    if (!ExtensionSetGUID)
    {
        ExtensionSetGUID = pISInterface->CreateExtensionSet("ISXGlass");
        Logger::Instance().Write("ExtensionSetGUID = %d", ExtensionSetGUID);
        if (!ExtensionSetGUID)
        {
            return;
        }
    }
    pISInterface->SetSetting(ExtensionSetGUID, "Filename", ModuleFileName);
    pISInterface->SetSetting(ExtensionSetGUID, "Path", ModulePath);
    pISInterface->SetSetting(ExtensionSetGUID, "Version", EXTENSION_VERSION);
    Logger::Instance().Write("Filename set to %s, path set to %s, version set to %s", ModuleFileName, ModulePath, EXTENSION_VERSION);
}

// Drains the command queue and dispatches each command to HandleCommand.
void __cdecl PulseService(bool Broadcast, unsigned int MSG, void* lpData)
{
    if (MSG == PULSE_PULSE)
    {
        std::lock_guard<std::mutex> lock(g_QueueMutex);
        while (!g_CommandQueue.empty())
        {
            std::string cmd = g_CommandQueue.front();
            g_CommandQueue.pop();
            HandleCommand(cmd);
        }
    }
}

void __cdecl SystemService(bool Broadcast, unsigned int MSG, void* lpData) {}

static LONG EzCrashFilter(_EXCEPTION_POINTERS* pExceptionInfo, const char* szIdentifier, ...)
{
    unsigned int Code = pExceptionInfo->ExceptionRecord->ExceptionCode;
    if (Code == EXCEPTION_BREAKPOINT || Code == EXCEPTION_SINGLE_STEP)
        return EXCEPTION_CONTINUE_SEARCH;
    char szOutput[4096];
    szOutput[0] = 0;
    va_list vaList;
    va_start(vaList, szIdentifier);
    vsprintf_s(szOutput, szIdentifier, vaList);
    Logger::Instance().Write("CRASH: %s", szOutput);
    return EXCEPTION_EXECUTE_HANDLER;
}

// Called by PipeManager on its reader thread when a complete command arrives.
// Must not block — queues the command for processing on the Pulse thread.
static void OnCommandReceived(const std::string& command)
{
    Logger::Instance().Write("PipeManager command received: %s", command.c_str());
    std::lock_guard<std::mutex> lock(g_QueueMutex);
    g_CommandQueue.push(command);
}

ISInterface* pISInterface = 0;
HISXSERVICE hPulseService = 0;
HISXSERVICE hSystemService = 0;
