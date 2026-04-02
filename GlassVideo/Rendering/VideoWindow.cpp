#include "GlassVideo.h"
#include "VideoWindow.h"
#include "Logger.h"

const char* VideoWindow::ClassName = "GlassVideoWindow";

VideoWindow::VideoWindow()
    : _hwnd(nullptr)
    , _instance(nullptr)
{
}

VideoWindow::~VideoWindow()
{
    Destroy();
}

// Creates and shows the video window. Registers the window class on first call.
// Returns false if registration or creation fails.
bool VideoWindow::Create(HINSTANCE instance, const std::string& title, int x, int y, int width, int height)
{
    _instance = instance;
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

    WNDCLASSEXA wc = {};
    wc.cbSize = sizeof(WNDCLASSEXA);
    wc.style = CS_HREDRAW | CS_VREDRAW;
    wc.lpfnWndProc = WndProc;
    wc.hInstance = instance;
    wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
    wc.hbrBackground = (HBRUSH)GetStockObject(BLACK_BRUSH);
    wc.lpszClassName = ClassName;

    if (!RegisterClassExA(&wc))
    {
        DWORD err = GetLastError();
        if (err != ERROR_CLASS_ALREADY_EXISTS)
        {
            Logger::Instance().Write("VideoWindow: RegisterClassEx failed: %d", err);
            return false;
        }
    }

    _hwnd = CreateWindowExA(
        0,
        ClassName,
        title.c_str(),
        WS_OVERLAPPEDWINDOW,
        x, y, width, height,
        nullptr,
        nullptr,
        instance,
        this);

    if (!_hwnd)
    {
        Logger::Instance().Write("VideoWindow: CreateWindowEx failed: %d", GetLastError());
        return false;
    }

    Logger::Instance().Write("VideoWindow: created. hwnd=%p title=%s", _hwnd, title.c_str());

    ShowWindow(_hwnd, SW_SHOW);
    UpdateWindow(_hwnd);

    if (!_renderer.Initialize(_hwnd, width, height))
    {
        Logger::Instance().Write("VideoWindow: D3DRenderer initialization failed.");
        Destroy();
        return false;
    }

    return true;
}

// Destroys the window if it exists.
void VideoWindow::Destroy()
{
    if (_hwnd)
    {
        Logger::Instance().Write("VideoWindow: destroying. hwnd=%p", _hwnd);
        DestroyWindow(_hwnd);
        _hwnd = nullptr;
    }
}

// Returns the window handle.
HWND VideoWindow::GetHwnd() const
{
    return _hwnd;
}

// Returns true if the window has been successfully created.
bool VideoWindow::IsValid() const
{
    return (_hwnd != nullptr);
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// VideoWindow::Render
//
// Renders all active slots into their viewport rectangles, then renders
// region destinations for each slot that has an active capture.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void VideoWindow::Render()
{
    std::lock_guard<std::mutex> lock(g_slotManager.GetMutex());

    ID3D11RenderTargetView* rtv = _renderer.GetRenderTargetView();
    _renderer.GetContext()->OMSetRenderTargets(1, &rtv, nullptr);

    const std::multimap<SlotID, std::unique_ptr<SlotInfo>>& slots = g_slotManager.GetSlots();
    const RegionSourceMap& sources = g_slotManager.GetSources();
    const RegionDestMap& destinations = g_slotManager.GetDestinations();

    for (const std::pair<const SlotID, std::unique_ptr<SlotInfo>>& pair : slots)
    {
        SlotInfo* slot = pair.second.get();

        ID3D11ShaderResourceView* srv = slot->capture.GetShaderResourceView();

        if (!srv)
        {
            srv = _renderer.GetBlackSRV();
            if (!srv)
            {
                continue;
            }
        }

        // Calculate title bar crop for this slot.
        float titleBarUV = 0.0f;
        if (slot->hwnd != NULL)
        {
            RECT clientRect = {};
            if (GetClientRect(slot->hwnd, &clientRect))
            {
                POINT topLeft = { clientRect.left, clientRect.top };
                MapWindowPoints(slot->hwnd, NULL, &topLeft, 1);
                RECT windowRect = {};
                GetWindowRect(slot->hwnd, &windowRect);
                int titleBarHeight = topLeft.y - windowRect.top;
                if ((titleBarHeight > 0) && (slot->capture.GetHeight() > 0))
                {
                    titleBarUV = (float)titleBarHeight / (float)slot->capture.GetHeight();
                }
            }
        }

        // Render full slot.
        D3D11_VIEWPORT vp = {};
        vp.TopLeftX = (float)slot->x;
        vp.TopLeftY = (float)slot->y;
        vp.Width = (float)slot->width;
        vp.Height = (float)slot->height;
        vp.MinDepth = 0.0f;
        vp.MaxDepth = 1.0f;
        _renderer.GetContext()->RSSetViewports(1, &vp);
        _renderer.GetQuadRenderer().Render(_renderer.GetContext(), srv, 0.0f, titleBarUV, 1.0f, 1.0f);

        // Skip region rendering if no active capture.
        if (slot->hwnd == NULL)
        {
            continue;
        }

        ID3D11ShaderResourceView* regionSrv = slot->capture.GetShaderResourceView();
        if (regionSrv == nullptr)
        {
            continue;
        }

        int captureWidth = slot->capture.GetWidth();
        int captureHeight = slot->capture.GetHeight();

        if ((captureWidth <= 0) || (captureHeight <= 0))
        {
            continue;
        }

        for (const std::pair<const std::string, RegionDest>& destPair : destinations)
        {
            const RegionDest& dest = destPair.second;

            const RegionSourceMap::const_iterator sourceIt = sources.find(dest.name);
            if (sourceIt == sources.end())
            {
                continue;
            }

            const RegionSource& source = sourceIt->second;

            // Compute UV rect, offsetting v by title bar crop.
            float u0 = (float)source.x / (float)captureWidth;
            float v0 = titleBarUV + (float)source.y / (float)captureHeight;
            float u1 = (float)(source.x + source.width) / (float)captureWidth;
            float v1 = titleBarUV + (float)(source.y + source.height) / (float)captureHeight;

            // Clamp UVs to valid range.
            u0 = max(0.0f, min(1.0f, u0));
            v0 = max(0.0f, min(1.0f, v0));
            u1 = max(0.0f, min(1.0f, u1));
            v1 = max(0.0f, min(1.0f, v1));

            // Destination viewport is slot origin + dest offset.
            D3D11_VIEWPORT destVp = {};
            destVp.TopLeftX = (float)(slot->x + dest.x);
            destVp.TopLeftY = (float)(slot->y + dest.y);
            destVp.Width = (float)dest.width;
            destVp.Height = (float)dest.height;
            destVp.MinDepth = 0.0f;
            destVp.MaxDepth = 1.0f;
            _renderer.GetContext()->RSSetViewports(1, &destVp);

            _renderer.GetQuadRenderer().Render(_renderer.GetContext(), regionSrv, u0, v0, u1, v1);
        }
    }

    _renderer.Present();
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// VideoWindow::GetRenderer
//
// Returns a reference to the D3D11 renderer owned by this window.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
D3DRenderer& VideoWindow::GetRenderer()
{
    return _renderer;
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// VideoWindow::WndProc
//
// Window procedure.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
LRESULT CALLBACK VideoWindow::WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    if (msg == WM_CREATE)
    {
        CREATESTRUCTA* cs = (CREATESTRUCTA*)lParam;
        SetWindowLongPtrA(hwnd, GWLP_USERDATA, (LONG_PTR)cs->lpCreateParams);
        return 0;
    }

    VideoWindow* self = (VideoWindow*)GetWindowLongPtrA(hwnd, GWLP_USERDATA);

    switch (msg)
    {
        case WM_DESTROY:
        {
            Logger::Instance().Write("VideoWindow: WM_DESTROY. hwnd=%p", hwnd);
            if (self)
            {
                self->_hwnd = nullptr;
            }
            PostQuitMessage(0);
            return 0;
        }

        case WM_SIZE:
        {
            int width = LOWORD(lParam);
            int height = HIWORD(lParam);
            Logger::Instance().Write("VideoWindow: WM_SIZE. width=%d height=%d", width, height);
            if (self && self->_renderer.GetDevice() && (width > 0) && (height > 0))
            {
                self->_renderer.Resize(width, height);
            }
            return 0;
        }

        case WM_PAINT:
        {
            PAINTSTRUCT ps;
            BeginPaint(hwnd, &ps);
            if (self && self->_renderer.GetDevice())
            {
                self->Render();
            }
            EndPaint(hwnd, &ps);
            return 0;
        }

        case WM_ENTERSIZEMOVE:
        {
            Logger::Instance().Write("VideoWindow: WM_ENTERSIZEMOVE.");
            SetTimer(hwnd, 1, 16, nullptr);
            return 0;
        }

        case WM_EXITSIZEMOVE:
        {
            Logger::Instance().Write("VideoWindow: WM_EXITSIZEMOVE.");
            KillTimer(hwnd, 1);
            return 0;
        }

        case WM_TIMER:
        {
            if (self)
            {
                self->Render();
            }
            return 0;
        }

        case WM_LBUTTONDOWN:
        {
            int x = LOWORD(lParam);
            int y = HIWORD(lParam);
            HWND target = g_slotManager.HitTest(x, y);
            if (target)
            {
                Logger::Instance().Write("VideoWindow: WM_LBUTTONDOWN hit slot hwnd=%p, bringing to foreground.", target);
                SetForegroundWindow(target);
            }
            else
            {
                Logger::Instance().Write("VideoWindow:  WM_LBUTTONDOWN could not map mouse (%d,%d) to slot", x, y);
            }
            return 0;
        }
    }

    return DefWindowProcA(hwnd, msg, wParam, lParam);
}