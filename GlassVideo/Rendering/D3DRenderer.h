#pragma once
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include "QuadRenderer.h"

// Owns the D3D11 device, swap chain, and render target for a single VideoWindow.
// Responsible for presenting frames to the window.
// Capture and compositing will be added on top of this once rendering is confirmed working.

class D3DRenderer
{
public:
    D3DRenderer();
    ~D3DRenderer();

    bool Initialize(HWND hwnd, int width, int height);
    void Shutdown();

    void Resize(int width, int height);
    void Clear(float r, float g, float b);
    void Present();
    void Render(ID3D11ShaderResourceView* srv);

    ID3D11Device* GetDevice() const;
    ID3D11DeviceContext* GetContext() const;

    ID3D11RenderTargetView* GetRenderTargetView() const;
    QuadRenderer& GetQuadRenderer();

    ID3D11ShaderResourceView* GetBlackSRV() const;

private:

    bool CreateRenderTarget();
    void ReleaseRenderTarget();

    ID3D11Device* _device;
    ID3D11DeviceContext* _context;
    IDXGISwapChain* _swapChain;
    ID3D11RenderTargetView* _renderTargetView;
    QuadRenderer            _quadRenderer;
    ID3D11ShaderResourceView* _blackSRV;

    int _width;
    int _height;
};