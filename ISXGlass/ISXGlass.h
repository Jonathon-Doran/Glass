#pragma once

#define GLASS_LOG_PATH "C:\\Users\\jhd0044\\source\\repos\\Glass\\ISXGlass\\isxglass.log"

#include <windows.h>
#include <vector>
#include <map>
#include <string>
#include <queue>
#include <mutex>
using namespace std;
#pragma warning(push, 0)
#pragma warning(disable: 4226)
__pragma(warning(disable: 6387 6308 6386 6011 28182 26444 26115 6258 26495 6255))
#include "ISXDK/Threading.h"
#include "ISXDK/WinThreading.h"
#include "ISXDK/Index.h"
#include "LavishScript/LavishScript.h"
#include "ISXDK/ISXDK.h"
#include "ISXDK/ISInterface.h"
#include "ISXDK/ISXInterface.h"
#pragma warning(pop)
#include "PipeManager.h"
#include "SessionManager.h"
#include "Logger.h"

class ISXGlass : public ISXInterface
{
public:
    bool Initialize(ISInterface* pISInterface) override;
    void Shutdown() override;
    unsigned int GetVersion() override;
    bool RequestShutdown() override;
    void RegisterExtension();
private:
    bool ConnectToInnerSpace(ISInterface* p_ISInterface);
};

LONG EzCrashFilter(_EXCEPTION_POINTERS* pExceptionInfo, const char* szIdentifier, ...);
void HandleCommand(const std::string& cmd);

extern ISXGlass* pExtension;
extern ISInterface* pISInterface;
extern HISXSERVICE hPulseService;
extern HISXSERVICE hSystemService;