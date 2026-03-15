#pragma once
#include <windows.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.capture.h>

// Creates a GraphicsCaptureItem for the given window handle.
// Required for Windows Graphics Capture API in Win32 applications.
winrt::Windows::Graphics::Capture::GraphicsCaptureItem CreateCaptureItemForWindow(HWND hwnd);