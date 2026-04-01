#pragma once
#include <windows.h>
#include <string>
#include "D3DRenderer.h"

// Owns a Win32 display window and its D3D11 renderer for a single session's video feed.
class VideoWindow
{
public:
    VideoWindow();
    ~VideoWindow();

    bool Create(HINSTANCE instance, const std::string& title, int x, int y, int width, int height);
    void Destroy();

    HWND            GetHwnd() const;
    bool            IsValid() const;
    D3DRenderer& GetRenderer();
    void            Render();

private:
    static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam);

    HWND        _hwnd;
    HINSTANCE   _instance;
    D3DRenderer _renderer;

    static const char* ClassName;
};