// HelloTriangle shaders for SDL3's Direct3D 12 backend.
//
// A Windows build compiles this file to DXIL with Microsoft's DirectX Shader
// Compiler (dxc) and embeds the result; see
// docs/WINDOWS_03_HLSL_DXIL_SHADERS.md. The macOS build uses Shaders/Triangle.metal
// instead and runs no shader compiler.
//
// SDL3 gives every D3D12 vertex input the semantic TEXCOORD with the
// SDL_GPUVertexAttribute location as the semantic index, so location 0 is
// TEXCOORD0 and location 1 is TEXCOORD1. This matches the pipeline built in
// TriangleRenderer.
//
// The entry points are VSMain and PSMain, compiled as vs_6_0 and ps_6_0.

struct VertexInput
{
    float3 position : TEXCOORD0;
    float3 color : TEXCOORD1;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float4 color : TEXCOORD0;
};

VertexOutput VSMain(VertexInput input)
{
    VertexOutput output;
    output.position = float4(input.position, 1.0);
    output.color = float4(input.color, 1.0);
    return output;
}

float4 PSMain(VertexOutput input) : SV_Target
{
    return input.color;
}
