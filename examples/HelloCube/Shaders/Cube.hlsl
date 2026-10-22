// HelloCube shaders for SDL3's Direct3D 12 backend.
//
// A Windows build compiles this file to DXIL with Microsoft's DirectX Shader
// Compiler (dxc) and embeds the result; see
// docs/WINDOWS_04_RENDERING_COMPATIBILITY.md. The macOS build uses
// Shaders/Cube.metal instead and runs no shader compiler.
//
// This is the HLSL equivalent of Cube.metal: the same position/color vertex
// layout, the same single model-view-projection uniform, and the same
// interpolated face color. The CPU side is unchanged, so this file must keep
// matching the pipeline in CubeRenderer.
//
// SDL3's documented Direct3D 12 conventions, from the pinned SDL3 3.4.18
// SDL_CreateGPUShader documentation:
//
//   * Non-system-value vertex semantics are all TEXCOORD, indexed by the
//     SDL_GPUVertexAttribute location. Location 0 is TEXCOORD0 and location 1 is
//     TEXCOORD1.
//   * Vertex uniform buffers are register(b[n], space1), in SDL push-slot order.
//     This shader pushes one vertex uniform block at slot 0, so it is b0.
//   * A pixel shader's uniform buffers are register(b[n], space3); this one
//     declares none.
//
// System.Numerics stores a matrix row-major and transforms row vectors, so the
// 64 bytes pushed for mvp are a row-major matrix. A cbuffer matrix is
// column-major by default, and it is declared column_major here explicitly: the
// same bytes are then read as the transpose, so `mul(mvp, position)` matches
// `position * mvp` in C#. Do not transpose twice. This is the same convention
// Cube.metal relies on.

struct VertexInput
{
    float3 position : TEXCOORD0;
    float3 color : TEXCOORD1;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float3 color : TEXCOORD0;
};

cbuffer Uniforms : register(b0, space1)
{
    column_major float4x4 mvp;
};

VertexOutput VSMain(VertexInput input)
{
    VertexOutput output;
    output.position = mul(mvp, float4(input.position, 1.0));
    output.color = input.color;
    return output;
}

float4 PSMain(VertexOutput input) : SV_Target
{
    return float4(input.color, 1.0);
}
