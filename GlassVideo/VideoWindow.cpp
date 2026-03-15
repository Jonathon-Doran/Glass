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
// Renders the given SRV as a fullscreen quad and presents.
// Pass nullptr to clear to black.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void VideoWindow::Render(ID3D11ShaderResourceView* srv)
{
    if (srv)
    {
        _renderer.Render(srv);
    }
    else
    {
        _renderer.Clear(0.0f, 0.0f, 0.0f);
        _renderer.Present();
    }
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
            self->Render(nullptr);
        }
        EndPaint(hwnd, &ps);
        return 0;
    }
    }

    return DefWindowProcA(hwnd, msg, wParam, lParam);
}