Texture2D sourceTexture : register(t0);
SamplerState sourceSampler : register(s0);

struct PSInput
{
    float4 position : SV_POSITION;
    float2 texcoord : TEXCOORD0;
};

float4 main(PSInput input) : SV_TARGET
{
    return sourceTexture.Sample(sourceSampler, input.texcoord);
}