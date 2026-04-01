#include "SessionCapture.h"
#include "CaptureInterop.h"
#include "Logger.h"
#include <roapi.h>

#pragma comment(lib, "runtimeobject.lib")
#pragma comment(lib, "windowsapp.lib")

// Helper: convert ID3D11Device to WinRT IDirect3DDevice.
// Required by WGC frame pool creation.
static winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DDevice
CreateWinRTDevice(ID3D11Device* d3dDevice)
{
    winrt::com_ptr<IDXGIDevice> dxgiDevice;
    winrt::check_hresult(d3dDevice->QueryInterface(
        __uuidof(IDXGIDevice),
        dxgiDevice.put_void()));

    winrt::com_ptr<::IInspectable> inspectable;
    winrt::check_hresult(
        ::CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.get(), inspectable.put()));

    return inspectable.as<winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DDevice>();
}

// Helper: get ID3D11Texture2D from a WinRT IDirect3DSurface.
static winrt::com_ptr<ID3D11Texture2D>
GetD3DTexture(winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DSurface const& surface)
{
    winrt::com_ptr<Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess> access =
        surface.as<Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
    winrt::com_ptr<ID3D11Texture2D> texture;
    winrt::check_hresult(access->GetInterface(__uuidof(ID3D11Texture2D), texture.put_void()));
    return texture;
}

SessionCapture::SessionCapture()
    : _hwnd(nullptr)
    , _device(nullptr)
    , _context(nullptr)
    , _srv(nullptr)
    , _frameAvailable(false)
{
}

SessionCapture::~SessionCapture()
{
    Shutdown();
}

int SessionCapture::GetWidth() const
{
    return _width;
}

int SessionCapture::GetHeight() const
{
    return _height;
}

// Initializes WGC capture for the given window.
// Creates a GraphicsCaptureItem from the HWND, a frame pool, and starts the capture session.
bool SessionCapture::Initialize(ID3D11Device* device, HWND hwnd)
{
    Logger::Instance().Write("SessionCapture: Initialize. hwnd=%p", hwnd);

    _hwnd = hwnd;
    _device = device;
    _shuttingDown = false;

    device->GetImmediateContext(&_context);

    // Wrap the D3D11 device for WinRT.
    try
    {
        _winrtDevice = CreateWinRTDevice(device);
        Logger::Instance().Write("SessionCapture: WinRT device created.");
    }
    catch (winrt::hresult_error const& e)
    {
        Logger::Instance().Write("SessionCapture: CreateWinRTDevice failed: 0x%08X %S",
            e.code().value, e.message().c_str());
        return false;
    }

    // Create a GraphicsCaptureItem from the HWND.
    try
    {
        _item = CreateCaptureItemForWindow(hwnd);
        Logger::Instance().Write("SessionCapture: GraphicsCaptureItem created. size=%dx%d",
            _item.Size().Width, _item.Size().Height);
    }
    catch (winrt::hresult_error const& e)
    {
        Logger::Instance().Write("SessionCapture: CreateCaptureItemForWindow failed: 0x%08X %S",
            e.code().value, e.message().c_str());
        return false;
    }

    _width = _item.Size().Width;
    _height = _item.Size().Height;

    // Create frame pool. BGRA8 matches D3DRenderer swap chain format.
    winrt::Windows::Graphics::SizeInt32 size = _item.Size();
    try
    {
        _framePool = winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool::Create(
            _winrtDevice,
            winrt::Windows::Graphics::DirectX::DirectXPixelFormat::B8G8R8A8UIntNormalized,
            2,
            size);

        Logger::Instance().Write("SessionCapture: frame pool created. buffers=2 size=%dx%d",
            size.Width, size.Height);
    }
    catch (winrt::hresult_error const& e)
    {
        Logger::Instance().Write("SessionCapture: frame pool creation failed: 0x%08X %S",
            e.code().value, e.message().c_str());
        return false;
    }

    // Subscribe to frame arrived event.
    _frameArrivedRevoker = _framePool.FrameArrived(
        winrt::auto_revoke,
        { this, &SessionCapture::OnFrameArrived });

    Logger::Instance().Write("SessionCapture: frame arrived handler registered.");

    // Start the capture session.
    try
    {
        _session = _framePool.CreateCaptureSession(_item);

        // Disable the yellow capture border if supported (Windows 11+).
        try
        {
            _session.IsBorderRequired(false);
            Logger::Instance().Write("SessionCapture: border disabled.");
            _session.IsCursorCaptureEnabled(false);
        }
        catch (...)
        {
            Logger::Instance().Write("SessionCapture: IsBorderRequired not supported, ignoring.");
        }

        _session.StartCapture();
        Logger::Instance().Write("SessionCapture: capture session started.");
    }
    catch (winrt::hresult_error const& e)
    {
        Logger::Instance().Write("SessionCapture: session start failed: 0x%08X %S",
            e.code().value, e.message().c_str());
        return false;
    }

    Logger::Instance().Write("SessionCapture: initialized successfully.");
    return true;
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Shutdown
// 
// Stops the capture session and releases all resources.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void SessionCapture::Shutdown()
{
    if (_shuttingDown.exchange(true))
    {
        Logger::Instance().Write("SessionCapture: Shutdown already in progress, ignoring.");
        return;
    }

    Logger::Instance().Write("SessionCapture: Shutdown. hwnd=%p", _hwnd);

    _frameArrivedRevoker.revoke();

    if (_session)
    {
        _session.Close();
        _session = nullptr;
    }
    if (_framePool)
    {
        _framePool.Close();
        _framePool = nullptr;
    }

    ReleaseFrame();

    if (_context)
    {
        _context->Release();
        _context = nullptr;
    }

    _winrtDevice = nullptr;
    _item = nullptr;
    _device = nullptr;
    _hwnd = nullptr;
    _frameAvailable = false;

    Logger::Instance().Write("SessionCapture: shutdown complete.");
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// OnFrameArrived
// 
// Called by the WGC frame pool when a new frame is available.
// Sets the flag for the render loop to pick up on its next iteration.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void SessionCapture::OnFrameArrived(
    winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool const& sender,
    winrt::Windows::Foundation::IInspectable const& args)
{
    _frameAvailable = true;
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Capture
// 
// Called from the render loop. Acquires the latest frame and updates the SRV.
// Returns true if a valid SRV is available.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
bool SessionCapture::Capture()
{
    if (!_frameAvailable)
    {
        return (_srv != nullptr);
    }
    _frameAvailable = false;

    winrt::Windows::Graphics::Capture::Direct3D11CaptureFrame frame = _framePool.TryGetNextFrame();
    if (!frame)
    {
        Logger::Instance().Write("SessionCapture: TryGetNextFrame returned null.");
        return (_srv != nullptr);
    }

    winrt::Windows::Graphics::SizeInt32 size = frame.ContentSize();

    winrt::com_ptr<ID3D11Texture2D> texture;
    try
    {
        texture = GetD3DTexture(frame.Surface());
    }
    catch (winrt::hresult_error const& e)
    {
        Logger::Instance().Write("SessionCapture: GetD3DTexture failed: 0x%08X %S",
            e.code().value, e.message().c_str());
        return (_srv != nullptr);
    }

    ReleaseFrame();

    HRESULT hr = _device->CreateShaderResourceView(texture.get(), nullptr, &_srv);
    if (FAILED(hr))
    {
        Logger::Instance().Write("SessionCapture: CreateShaderResourceView failed: 0x%08X", hr);
        return false;
    }

    return true;
}

// Returns the current shader resource view, or null if no frame has been captured yet.
ID3D11ShaderResourceView* SessionCapture::GetShaderResourceView() const
{
    return _srv;
}

// Returns true if a valid SRV exists.
bool SessionCapture::IsValid() const
{
    return (_srv != nullptr);
}

// Returns the HWND this capture is associated with.
HWND SessionCapture::GetHwnd() const
{
    return _hwnd;
}

// Releases the current SRV.
void SessionCapture::ReleaseFrame()
{
    if (_srv)
    {
        _srv->Release();
        _srv = nullptr;
    }
}