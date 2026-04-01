#include "D3DRenderer.h"
#include "Logger.h"
#include <d3d11.h>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")

D3DRenderer::D3DRenderer()
    : _device(nullptr)
    , _context(nullptr)
    , _swapChain(nullptr)
    , _renderTargetView(nullptr)
    , _blackSRV(nullptr)
    , _width(0)
    , _height(0)
{
}

D3DRenderer::~D3DRenderer()
{
    Shutdown();
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Initialize
// 
// Initializes the D3D11 device, swap chain, and render target for the given window.
// Returns false if any step fails.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

bool D3DRenderer::Initialize(HWND hwnd, int width, int height)
{
    _width = width;
    _height = height;

    DXGI_SWAP_CHAIN_DESC sd = {};
    sd.BufferCount = 2;
    sd.BufferDesc.Width = width;
    sd.BufferDesc.Height = height;
    sd.BufferDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    sd.BufferDesc.RefreshRate.Numerator = 60;
    sd.BufferDesc.RefreshRate.Denominator = 1;
    sd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    sd.OutputWindow = hwnd;
    sd.SampleDesc.Count = 1;
    sd.SampleDesc.Quality = 0;
    sd.Windowed = TRUE;
    sd.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;

    D3D_FEATURE_LEVEL featureLevel;
    UINT flags = 0;

#ifdef _DEBUG
    flags |= D3D11_CREATE_DEVICE_DEBUG;
#endif

    HRESULT hr = D3D11CreateDeviceAndSwapChain(
        nullptr,
        D3D_DRIVER_TYPE_HARDWARE,
        nullptr,
        flags,
        nullptr,
        0,
        D3D11_SDK_VERSION,
        &sd,
        &_swapChain,
        &_device,
        &featureLevel,
        &_context);

    if (FAILED(hr))
    {
        Logger::Instance().Write("D3DRenderer: D3D11CreateDeviceAndSwapChain failed: 0x%08X", hr);
        return false;
    }

    Logger::Instance().Write("D3DRenderer: device created. featureLevel=0x%X", featureLevel);

    if (!CreateRenderTarget())
    {
        return false;
    }

    if (!_quadRenderer.Initialize(_device))
    {
        Logger::Instance().Write("D3DRenderer: QuadRenderer initialization failed.");
        return false;
    }

    Logger::Instance().Write("D3DRenderer: QuadRenderer initialized.");

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Create a 1x1 black texture for clearing empty slots.
    ////////////////////////////////////////////////////////////////////////////////////////////////////
    D3D11_TEXTURE2D_DESC td = {};
    td.Width = 1;
    td.Height = 1;
    td.MipLevels = 1;
    td.ArraySize = 1;
    td.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    td.SampleDesc.Count = 1;
    td.Usage = D3D11_USAGE_IMMUTABLE;
    td.BindFlags = D3D11_BIND_SHADER_RESOURCE;

    uint32_t blackPixel = 0xFF000000;
    D3D11_SUBRESOURCE_DATA initData = {};
    initData.pSysMem = &blackPixel;
    initData.SysMemPitch = sizeof(uint32_t);

    ID3D11Texture2D* blackTex = nullptr;
    hr = _device->CreateTexture2D(&td, &initData, &blackTex);
    if (FAILED(hr))
    {
        Logger::Instance().Write("D3DRenderer: CreateTexture2D for black SRV failed: 0x%08X", hr);
        return false;
    }

    hr = _device->CreateShaderResourceView(blackTex, nullptr, &_blackSRV);
    blackTex->Release();
    if (FAILED(hr))
    {
        Logger::Instance().Write("D3DRenderer: CreateShaderResourceView for black SRV failed: 0x%08X", hr);
        return false;
    }

    Logger::Instance().Write("D3DRenderer: black SRV created.");


    Logger::Instance().Write("D3DRenderer: initialized. width=%d height=%d", width, height);
    return true;
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Shutdown
// 
// Releases all D3D11 resources.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void D3DRenderer::Shutdown()
{
    Logger::Instance().Write("D3DRenderer: shutting down.");
    _quadRenderer.Shutdown();

    ReleaseRenderTarget();

    if (_swapChain)
    {
        _swapChain->Release();
        _swapChain = nullptr;
    }
    if (_context)
    {
        _context->Release();
        _context = nullptr;
    }
    if (_device)
    {
        _device->Release();
        _device = nullptr;
    }
    if (_blackSRV)
    {
        _blackSRV->Release();
        _blackSRV = nullptr;
    }
    Logger::Instance().Write("D3DRenderer: shutdown complete.");
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Resize
// 
// Recreates the render target after a resize.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void D3DRenderer::Resize(int width, int height)
{
    Logger::Instance().Write("D3DRenderer: resizing to %d x %d.", width, height);

    _width = width;
    _height = height;

    ReleaseRenderTarget();

    HRESULT hr = _swapChain->ResizeBuffers(0, width, height, DXGI_FORMAT_UNKNOWN, 0);
    if (FAILED(hr))
    {
        Logger::Instance().Write("D3DRenderer: ResizeBuffers failed: 0x%08X", hr);
        return;
    }

    CreateRenderTarget();
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Clear
// 
// Clears the render target to the given RGB color.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void D3DRenderer::Clear(float r, float g, float b)
{
    float color[4] = { r, g, b, 1.0f };
    _context->ClearRenderTargetView(_renderTargetView, color);
    _context->OMSetRenderTargets(1, &_renderTargetView, nullptr);

    D3D11_VIEWPORT vp = {};
    vp.Width = (float)_width;
    vp.Height = (float)_height;
    vp.MaxDepth = 1.0f;
    _context->RSSetViewports(1, &vp);
}

// Presents the back buffer to the window.
void D3DRenderer::Present()
{
    HRESULT hr = _swapChain->Present(1, 0);
    if (FAILED(hr))
    {
        Logger::Instance().Write("D3DRenderer: Present failed: 0x%08X", hr);
    }
}

// Returns the D3D11 device.
ID3D11Device* D3DRenderer::GetDevice() const
{
    return _device;
}

////////////////////////////////////////////////////////////////////////////////////////////////////
// GetBlackSRV
//
// Returns a 1x1 black shader resource view for rendering into empty slots.
////////////////////////////////////////////////////////////////////////////////////////////////////
ID3D11ShaderResourceView* D3DRenderer::GetBlackSRV() const
{
    return _blackSRV;
}

// Returns the D3D11 device context.
ID3D11DeviceContext* D3DRenderer::GetContext() const
{
    return _context;
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// CreateRenderTarget
// 
// Creates the render target view from the swap chain back buffer.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

bool D3DRenderer::CreateRenderTarget()
{
    ID3D11Texture2D* backBuffer = nullptr;
    HRESULT hr = _swapChain->GetBuffer(0, __uuidof(ID3D11Texture2D), (void**)&backBuffer);
    if (FAILED(hr))
    {
        Logger::Instance().Write("D3DRenderer: GetBuffer failed: 0x%08X", hr);
        return false;
    }

    hr = _device->CreateRenderTargetView(backBuffer, nullptr, &_renderTargetView);
    backBuffer->Release();

    if (FAILED(hr))
    {
        Logger::Instance().Write("D3DRenderer: CreateRenderTargetView failed: 0x%08X", hr);
        return false;
    }

    Logger::Instance().Write("D3DRenderer: render target created.");
    return true;
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ReleaseRenderTarget
// 
// Releases the render target view.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void D3DRenderer::ReleaseRenderTarget()
{
    if (_renderTargetView)
    {
        _renderTargetView->Release();
        _renderTargetView = nullptr;
    }
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Render
// 
// Clears the render target, renders the given SRV as a quad, and presents.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void D3DRenderer::Render(ID3D11ShaderResourceView* srv)
{
    Clear(0.0f, 0.0f, 0.0f);
    _quadRenderer.Render(_context, srv);
    Present();
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// D3DRenderer::GetRenderTargetView
//
// Returns the current render target view.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
ID3D11RenderTargetView* D3DRenderer::GetRenderTargetView() const
{
    return _renderTargetView;
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// D3DRenderer::GetQuadRenderer
//
// Returns a reference to the quad renderer.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
QuadRenderer& D3DRenderer::GetQuadRenderer()
{
    return _quadRenderer;
}