cbuffer QuadConstants : register(b0)
{
    float topCropUV;
    float3 padding;
};

struct VSOutput
{
    float4 position : SV_POSITION;
    float2 texcoord : TEXCOORD0;
};

VSOutput main(uint vertexId : SV_VertexID)
{
    VSOutput output;
    float u = (vertexId & 1) ? 1.0 : 0.0;
    float v = (vertexId & 2) ? 1.0 : 0.0;
    output.texcoord = float2(u, v == 0.0 ? topCropUV : 1.0);
    output.position = float4(float2(u, v) * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    return output;
}