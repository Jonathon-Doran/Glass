#include <windows.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <sstream>
#include <vector>
#include <queue>
#include <mutex>
#include <string>

#include "Logger.h"
#include "VideoWindow.h"
#include "SessionCapture.h"
#include "SlotManager.h"
#include "PipeManager.h"

#define GLASSVIDEO_VERSION  "20260314"
#define GLASSVIDEO_LOG_PATH "glassvideo.log"

static VideoWindow       g_window;
SlotManager              g_slotManager;
static PipeManager       g_pipeManager("GlassVideo", "GlassVideo_Cmd", "GlassVideo_Notify");
std::queue<std::string>  g_commandQueue;
std::mutex               g_commandMutex;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ParseInt
//
// Parses an integer from a string. Returns false if the string is not a valid integer.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
static bool ParseInt(const std::string& s, int& out)
{
    try
    {
        out = std::stoi(s);
        return true;
    }
    catch (...)
    {
        return false;
    }
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ParseUInt
//
// Parses an unsigned integer from a string. Returns false if invalid.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
static bool ParseUInt(const std::string& s, unsigned int& out)
{
    try
    {
        out = (unsigned int)std::stoul(s);
        return true;
    }
    catch (...)
    {
        return false;
    }
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// SplitArgs
//
// Splits a string into tokens by spaces.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
static std::vector<std::string> SplitArgs(const std::string& s)
{
    std::vector<std::string> args;
    std::istringstream stream(s);
    std::string token;
    while (stream >> token)
    {
        args.push_back(token);
    }
    return args;
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// OnCommand
//
// Called by PipeManager on its reader thread when a command arrives from Glass.exe.
// Dispatches slot_define, slot_assign, slot_remove, unassign, and clear_slots commands.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
static void OnCommand(const std::string& command)
{
    Logger::Instance().Write("OnCommand: %s", command.c_str());

    std::vector<std::string> args = SplitArgs(command);
    if (args.empty())
    {
        return;
    }

    const std::string& cmd = args[0];

    if (cmd == "slot_define")
    {
        // slot_define <slotId> <x> <y> <width> <height>
        if (args.size() < 6)
        {
            Logger::Instance().Write("OnCommand: slot_define missing arguments.");
            return;
        }
        unsigned int slotId;
        int x, y, w, h;
        if ((!ParseUInt(args[1], slotId)) ||
            (!ParseInt(args[2], x)) ||
            (!ParseInt(args[3], y)) ||
            (!ParseInt(args[4], w)) ||
            (!ParseInt(args[5], h)))
        {
            Logger::Instance().Write("OnCommand: slot_define invalid arguments.");
            return;
        }
        g_slotManager.Define(slotId, x, y, w, h);
    }
    else if (cmd == "slot_assign")
    {
        // slot_assign <slotId> <sessionName> <hwnd>
        if (args.size() < 4)
        {
            Logger::Instance().Write("OnCommand: slot_assign missing arguments.");
            return;
        }
        unsigned int slotId;
        if (!ParseUInt(args[1], slotId))
        {
            Logger::Instance().Write("OnCommand: slot_assign invalid slotId.");
            return;
        }
        const std::string& sessionName = args[2];
        HWND hwnd = (HWND)(uintptr_t)strtoull(args[3].c_str(), nullptr, 16);

        Logger::Instance().Write("OnCommand: slot_assign slotId=%u sessionName=%s hwnd=%p", slotId, sessionName.c_str(), hwnd);
        if (!IsWindow(hwnd))
        {
            Logger::Instance().Write("OnCommand: slot_assign hwnd=%p is not a valid window, ignoring.", hwnd);
            return;
        }
        Logger::Instance().Write("OnCommand: slot_assign calling Assign.");
        g_slotManager.Assign(g_window.GetRenderer().GetDevice(), slotId, sessionName, hwnd);
    }
    else if (cmd == "slot_remove")
    {
        // slot_remove <slotId>
        if (args.size() < 2)
        {
            Logger::Instance().Write("OnCommand: slot_remove missing arguments.");
            return;
        }
        unsigned int slotId;
        if (!ParseUInt(args[1], slotId))
        {
            Logger::Instance().Write("OnCommand: slot_remove invalid slotId.");
            return;
        }
        g_slotManager.Remove(slotId);
    }
    else if (cmd == "unassign")
    {
        // unassign <sessionName>
        if (args.size() < 2)
        {
            Logger::Instance().Write("OnCommand: unassign missing arguments.");
            return;
        }
        g_slotManager.Unassign(args[1]);
    }
    else if (cmd == "clear_slots")
    {
        g_slotManager.Clear();
    }
    else
    {
        Logger::Instance().Write("OnCommand: unrecognized command: %s", cmd.c_str());
    }
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// wWinMain
//
// Application entry point.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
int APIENTRY wWinMain(
    _In_     HINSTANCE instance,
    _In_opt_ HINSTANCE prevInstance,
    _In_     LPWSTR    cmdLine,
    _In_     int       showCmd)
{
    Logger::Instance().Open(GLASSVIDEO_LOG_PATH);
    Logger::Instance().Write("GlassVideo %s starting.", GLASSVIDEO_VERSION);

    winrt::init_apartment(winrt::apartment_type::single_threaded);
    Logger::Instance().Write("WinRT apartment initialized.");

    if (!g_window.Create(instance, "GlassVideo", 0, 30, 3840, 2070))
    {
        Logger::Instance().Write("Failed to create video window, exiting.");
        return 1;
    }
    Logger::Instance().Write("Video window created. hwnd=%p", g_window.GetHwnd());

    g_pipeManager.Start(OnCommand);
    Logger::Instance().Write("PipeManager started.");

    MSG msg = {};
    while (true)
    {
        if (PeekMessage(&msg, nullptr, 0, 0, PM_REMOVE))
        {
            if (msg.message == WM_QUIT)
            {
                Logger::Instance().Write("WM_QUIT received, exiting loop.");
                break;
            }
            TranslateMessage(&msg);
            DispatchMessage(&msg);
        }
        else
        {
            {
                std::lock_guard<std::mutex> lock(g_commandMutex);
                while (!g_commandQueue.empty())
                {
                    std::string cmd = g_commandQueue.front();
                    g_commandQueue.pop();
                    OnCommand(cmd);
                }
            }
            g_slotManager.CaptureAll();
            g_window.Render();
        }
    }

    Logger::Instance().Write("GlassVideo shutting down.");
    g_slotManager.Clear();
    g_pipeManager.Stop();
    Logger::Instance().Write("GlassVideo shutdown complete.");

    return (int)msg.wParam;
}