#pragma once
#include <windows.h>
#include <d3d11.h>
#include <string>
#include <vector>

// Renders a texture to the current render target via a screen-space quad.
// Owns the vertex shader, pixel shader, and sampler state.
// The quad covers the entire render target in NDC space.

struct QuadConstants
{
    float topCropUV;
    float padding[3];
};

class QuadRenderer
{
public:
    QuadRenderer();
    ~QuadRenderer();

    bool Initialize(ID3D11Device* device);
    void Shutdown();

    void Render(ID3D11DeviceContext* context, ID3D11ShaderResourceView* srv, float topCropUV = 0.0f);

private:
    bool LoadShader(const std::string& path, std::vector<char>& bytecode);

    ID3D11VertexShader* _vertexShader;
    ID3D11PixelShader* _pixelShader;
    ID3D11SamplerState* _samplerState;
    ID3D11Buffer* _constantBuffer;
};