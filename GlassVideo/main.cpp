#include <windows.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <sstream>
#include <fstream>
#include <vector>
#include <queue>
#include <mutex>
#include <string>

#include "Logger.h"
#include "Rendering\VideoWindow.h"
#include "Capture\SessionCapture.h"
#include "Consumers\ConsumerManager.h"
#include "SlotManager.h"
#include "PipeManager.h"

#define GLASSVIDEO_VERSION  "20260314"
#define GLASSVIDEO_LOG_PATH "glassvideo.log"

static VideoWindow       g_window;
SlotManager              g_slotManager;
PipeManager              g_pipeManager("GlassVideo", "GlassVideo_Cmd", "GlassVideo_Notify");
std::queue<std::string>  g_commandQueue;
std::mutex               g_commandMutex;
ConsumerManager          g_consumerManager;

bool g_debugNextFrame = false;

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
// SaveScreenshot
//
// Copies the SRV for the given slot to a CPU-accessible staging texture and writes
// a BMP file named "screenshot_slot<n>.bmp" next to the executable.
// The title bar is cropped from the top of the image using GetWindowRect/GetClientRect.
//
// slotId:  The slot to screenshot
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
static void SaveScreenshot(unsigned int slotId)
{
    Logger::Instance().Write("SaveScreenshot: slotId=%u", slotId);

    std::lock_guard<std::mutex> lock(g_slotManager.GetMutex());

    const std::multimap<SlotID, std::unique_ptr<SlotInfo>>& slots = g_slotManager.GetSlots();

    SlotInfo* slot = nullptr;
    for (auto& pair : slots)
    {
        if (pair.second->slotId == slotId)
        {
            slot = pair.second.get();
            break;
        }
    }

    if (slot == nullptr)
    {
        Logger::Instance().Write("SaveScreenshot: slotId=%u not found.", slotId);
        return;
    }

    ID3D11ShaderResourceView* srv = slot->capture.GetShaderResourceView();
    if (srv == nullptr)
    {
        Logger::Instance().Write("SaveScreenshot: slotId=%u has no SRV.", slotId);
        return;
    }

    // Get the underlying texture from the SRV.
    ID3D11Resource* resource = nullptr;
    srv->GetResource(&resource);
    if (resource == nullptr)
    {
        Logger::Instance().Write("SaveScreenshot: GetResource returned null.");
        return;
    }

    ID3D11Texture2D* srcTexture = nullptr;
    HRESULT hr = resource->QueryInterface(__uuidof(ID3D11Texture2D), (void**)&srcTexture);
    resource->Release();

    if (FAILED(hr) || srcTexture == nullptr)
    {
        Logger::Instance().Write("SaveScreenshot: QueryInterface for Texture2D failed: 0x%08X", hr);
        return;
    }

    D3D11_TEXTURE2D_DESC desc = {};
    srcTexture->GetDesc(&desc);

    Logger::Instance().Write("SaveScreenshot: texture size=%dx%d format=%u",
        desc.Width, desc.Height, desc.Format);

    // Create a staging texture for CPU readback.
    D3D11_TEXTURE2D_DESC stagingDesc = desc;
    stagingDesc.Usage = D3D11_USAGE_STAGING;
    stagingDesc.BindFlags = 0;
    stagingDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    stagingDesc.MiscFlags = 0;

    ID3D11Device* device = g_window.GetRenderer().GetDevice();
    ID3D11DeviceContext* context = g_window.GetRenderer().GetContext();

    ID3D11Texture2D* stagingTexture = nullptr;
    hr = device->CreateTexture2D(&stagingDesc, nullptr, &stagingTexture);
    if (FAILED(hr))
    {
        Logger::Instance().Write("SaveScreenshot: CreateTexture2D staging failed: 0x%08X", hr);
        srcTexture->Release();
        return;
    }

    context->CopyResource(stagingTexture, srcTexture);
    srcTexture->Release();

    D3D11_MAPPED_SUBRESOURCE mapped = {};
    hr = context->Map(stagingTexture, 0, D3D11_MAP_READ, 0, &mapped);
    if (FAILED(hr))
    {
        Logger::Instance().Write("SaveScreenshot: Map failed: 0x%08X", hr);
        stagingTexture->Release();
        return;
    }

    // Calculate title bar crop using client origin mapped to screen coordinates.
    int titleBarHeight = 0;
    if (slot->hwnd != NULL)
    {
        RECT clientRect = {};
        if (GetClientRect(slot->hwnd, &clientRect))
        {
            POINT topLeft = { clientRect.left, clientRect.top };
            MapWindowPoints(slot->hwnd, NULL, &topLeft, 1);
            RECT windowRect = {};
            GetWindowRect(slot->hwnd, &windowRect);
            titleBarHeight = topLeft.y - windowRect.top;
            if (titleBarHeight < 0)
            {
                titleBarHeight = 0;
            }
        }
    }

    Logger::Instance().Write("SaveScreenshot: titleBarHeight=%d", titleBarHeight);

    int width = (int)desc.Width;
    int height = (int)desc.Height;
    int rowBytes = width * 4;
    int croppedHeight = height - titleBarHeight;

    if (croppedHeight <= 0)
    {
        Logger::Instance().Write("SaveScreenshot: cropped height is zero or negative, aborting.");
        context->Unmap(stagingTexture, 0);
        stagingTexture->Release();
        return;
    }

    int imageSize = rowBytes * croppedHeight;

    char filename[64] = {};
    snprintf(filename, sizeof(filename), "screenshot_slot%u.bmp", slotId);

    BITMAPFILEHEADER fileHeader = {};
    fileHeader.bfType = 0x4D42;
    fileHeader.bfSize = sizeof(BITMAPFILEHEADER) + sizeof(BITMAPINFOHEADER) + imageSize;
    fileHeader.bfOffBits = sizeof(BITMAPFILEHEADER) + sizeof(BITMAPINFOHEADER);

    BITMAPINFOHEADER infoHeader = {};
    infoHeader.biSize = sizeof(BITMAPINFOHEADER);
    infoHeader.biWidth = width;
    infoHeader.biHeight = -croppedHeight;
    infoHeader.biPlanes = 1;
    infoHeader.biBitCount = 32;
    infoHeader.biCompression = BI_RGB;
    infoHeader.biSizeImage = imageSize;

    std::ofstream file(filename, std::ios::binary);
    if (!file.is_open())
    {
        Logger::Instance().Write("SaveScreenshot: failed to open file '%s'.", filename);
        context->Unmap(stagingTexture, 0);
        stagingTexture->Release();
        return;
    }

    file.write((const char*)&fileHeader, sizeof(fileHeader));
    file.write((const char*)&infoHeader, sizeof(infoHeader));

    // Skip title bar rows, write remaining rows.
    const uint8_t* src = (const uint8_t*)mapped.pData;
    src += (size_t)titleBarHeight * mapped.RowPitch;
    for (int row = 0; row < croppedHeight; row++)
    {
        file.write((const char*)src, rowBytes);
        src += mapped.RowPitch;
    }

    file.close();
    context->Unmap(stagingTexture, 0);
    stagingTexture->Release();

    Logger::Instance().Write("SaveScreenshot: saved '%s' (%dx%d, cropped %d rows).",
        filename, width, croppedHeight, titleBarHeight);
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// OnCommand
//
// Called by PipeManager on its reader thread when a command arrives from Glass.exe.
// Dispatches the following commands:
//   slot_define   <slotId> <x> <y> <width> <height>
//   slot_assign   <slotId> <sessionName> <hwnd>
//   slot_remove   <slotId>
//   unassign      <sessionName>
//   clear_all
//   region_source <name> <x> <y> <width> <height>
//   region_dest   <name> <x> <y> <width> <height>
// 
// command:  The command to parse and dispatch.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
static void OnCommand(const std::string& command)
{
    Logger::Instance().Write("OnCommand: %s", command.c_str());

    std::vector<std::string> args = SplitArgs(command);
    if (args.empty())
    {
        Logger::Instance().Write("OnCommand: empty command, ignoring.");
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
        g_window.Render();
    }
    else if (cmd == "clear_all")
    {
        Logger::Instance().Write("OnCommand: clear_all.");
        g_slotManager.Clear();
        g_slotManager.ClearRegions();

        // Clear both swap chain buffers to black before slots are gone.
        // FLIP_SEQUENTIAL has 2 buffers — must Present twice to guarantee
        // both buffers are black before next launch.
        g_window.GetRenderer().Clear(0.0f, 0.0f, 0.0f);
        g_window.GetRenderer().Present();
        g_window.GetRenderer().Clear(0.0f, 0.0f, 0.0f);
        g_window.GetRenderer().Present();

        Logger::Instance().Write("OnCommand: clear_all complete.");
    }
    else if (cmd == "region_source")
    {
        // region_source <name> <x> <y> <width> <height>
        if (args.size() < 6)
        {
            Logger::Instance().Write("OnCommand: region_source missing arguments.");
            return;
        }
        int x, y, w, h;
        if ((!ParseInt(args[2], x)) ||
            (!ParseInt(args[3], y)) ||
            (!ParseInt(args[4], w)) ||
            (!ParseInt(args[5], h)))
        {
            Logger::Instance().Write("OnCommand: region_source invalid arguments.");
            return;
        }
        Logger::Instance().Write("OnCommand: region_source name=%s x=%d y=%d w=%d h=%d",
            args[1].c_str(), x, y, w, h);
        g_slotManager.DefineSource(args[1], x, y, w, h);
    }
    else if (cmd == "region_dest")
    {
        // region_dest <name> <x> <y> <width> <height>
        if (args.size() < 6)
        {
            Logger::Instance().Write("OnCommand: region_dest missing arguments.");
            return;
        }
        int x, y, w, h;
        if ((!ParseInt(args[2], x)) ||
            (!ParseInt(args[3], y)) ||
            (!ParseInt(args[4], w)) ||
            (!ParseInt(args[5], h)))
        {
            Logger::Instance().Write("OnCommand: region_dest invalid arguments.");
            return;
        }
        Logger::Instance().Write("OnCommand: region_dest name=%s x=%d y=%d w=%d h=%d",
            args[1].c_str(), x, y, w, h);
        g_slotManager.DefineDestination(args[1], x, y, w, h);
    }
    else if (cmd == "consumer_load")
    {
        // consumer_load <path>
        if (args.size() < 2)
        {
            Logger::Instance().Write("OnCommand: consumer_load missing path argument.");
            return;
        }
        Logger::Instance().Write("OnCommand: consumer_load path=%s", args[1].c_str());
        g_consumerManager.Load(args[1]);
        }
    else if (cmd == "consumer_unload")
    {
        // consumer_unload <path>
        if (args.size() < 2)
        {
            Logger::Instance().Write("OnCommand: consumer_unload missing path argument.");
            return;
        }
        Logger::Instance().Write("OnCommand: consumer_unload path=%s", args[1].c_str());
        g_consumerManager.Unload(args[1]);
    }

    else if (cmd == "screenshot")
    {
        // screenshot <slotId>
        if (args.size() < 2)
        {
            Logger::Instance().Write("OnCommand: screenshot missing slotId argument.");
            return;
        }
        unsigned int slotId;
        if (!ParseUInt(args[1], slotId))
        {
            Logger::Instance().Write("OnCommand: screenshot invalid slotId.");
            return;
        }
        Logger::Instance().Write("OnCommand: screenshot slotId=%u", slotId);
        SaveScreenshot(slotId);
    }
    else if (cmd == "debugnextframe")
    {
        Logger::Instance().Write("OnCommand: debugnextframe requested.");
        g_debugNextFrame = true;
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
    g_consumerManager.UnloadAll();
    g_slotManager.Clear();
    g_pipeManager.Stop();
    Logger::Instance().Write("GlassVideo shutdown complete.");

    return (int)msg.wParam;
}