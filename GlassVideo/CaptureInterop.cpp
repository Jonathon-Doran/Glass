#include "CaptureInterop.h"

// Creates a GraphicsCaptureItem for the given window handle.
// Uses IGraphicsCaptureItemInterop to bridge Win32 HWND to the WinRT capture API.
winrt::Windows::Graphics::Capture::GraphicsCaptureItem CreateCaptureItemForWindow(HWND hwnd)
{
    winrt::Windows::Graphics::Capture::GraphicsCaptureItem item = { nullptr };

    winrt::Windows::Foundation::IActivationFactory activationFactory =
        winrt::get_activation_factory<winrt::Windows::Graphics::Capture::GraphicsCaptureItem>();

    winrt::com_ptr<IGraphicsCaptureItemInterop> interopFactory =
        activationFactory.as<IGraphicsCaptureItemInterop>();

    winrt::check_hresult(interopFactory->CreateForWindow(
        hwnd,
        winrt::guid_of<ABI::Windows::Graphics::Capture::IGraphicsCaptureItem>(),
        reinterpret_cast<void**>(winrt::put_abi(item))));

    return item;
}