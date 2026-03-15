#pragma once

#include <windows.h>
#include <d3d11.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <Windows.Graphics.Capture.Interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>

// Captures a single window using the Windows Graphics Capture API.
// Delivers frames as an ID3D11ShaderResourceView for use by D3DRenderer.
class SessionCapture
{
public:
    SessionCapture();
    ~SessionCapture();

    bool Initialize(ID3D11Device* device, HWND hwnd);
    void Shutdown();

    // Call once per frame. Returns true if a valid frame is available.
    bool Capture();

    ID3D11ShaderResourceView* GetShaderResourceView() const;
    bool IsValid() const;
    HWND GetHwnd() const;
    int GetWidth() const;
    int GetHeight() const;

private:
    void OnFrameArrived(
        winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool const& sender,
        winrt::Windows::Foundation::IInspectable const& args);

    void ReleaseFrame();

    HWND                        _hwnd;
    ID3D11Device* _device;
    ID3D11DeviceContext* _context;
    ID3D11ShaderResourceView* _srv;

    winrt::Windows::Graphics::Capture::GraphicsCaptureItem         _item{ nullptr };
    winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool  _framePool{ nullptr };
    winrt::Windows::Graphics::Capture::GraphicsCaptureSession      _session{ nullptr };
    winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DDevice _winrtDevice{ nullptr };

    winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool::FrameArrived_revoker _frameArrivedRevoker;

    bool _frameAvailable;
    int _width;
    int _height;
};