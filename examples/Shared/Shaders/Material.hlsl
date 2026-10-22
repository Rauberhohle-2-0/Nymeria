// Shared material shader for SDL3's Direct3D 12 backend.
//
// A Windows build compiles this file to DXIL with Microsoft's DirectX Shader
// Compiler (dxc) and embeds the result in every example that owns the material
// shader; see docs/WINDOWS_04_RENDERING_COMPATIBILITY.md and
// examples/Shared/MaterialShaders.targets. The macOS build uses
// Shaders/Material.metal instead and runs no shader compiler.
//
// This is the HLSL equivalent of Material.metal: the same position/normal/UV
// vertex layout, the same vertex and fragment uniform layouts, the same texture
// and sampler bindings, the same world-space lighting equation, and the same
// opacity, mask, and blend behavior. The CPU side is unchanged, so this file must
// keep matching MaterialRenderer and MaterialGpuGeometry.
//
// SDL3's documented Direct3D 12 conventions, from the pinned SDL3 3.4.18
// SDL_CreateGPUShader documentation:
//
//   * Non-system-value vertex semantics are all TEXCOORD, indexed by the
//     SDL_GPUVertexAttribute location. Locations 0, 1, and 2 are TEXCOORD0,
//     TEXCOORD1, and TEXCOORD2.
//   * Vertex uniform buffers are register(b[n], space1), in SDL push-slot order.
//   * A pixel shader's sampled textures are register(t[n], space2), its samplers
//     are register(s[n], space2) in the same order, and its uniform buffers are
//     register(b[n], space3).
//
// So the vertex matrix block is b0 in space1, the fragment block is b0 in space3,
// and the single texture/sampler pair is t0/s0 in space2. That matches the one
// vertex uniform block, one fragment uniform block, and one fragment sampler that
// MaterialRenderer creates its shaders with.
//
// System.Numerics stores a matrix row-major and transforms row vectors, so the
// pushed mvp and normalMatrix are row-major. A cbuffer matrix is column-major by
// default, and both are declared column_major here explicitly: the same bytes are
// then read as the transpose, so `mul(m, v)` matches `v * m` in C#. Do not
// transpose twice. This is the same convention Material.metal relies on.

struct VertexInput
{
    float3 position : TEXCOORD0;
    float3 normal : TEXCOORD1;
    float2 uv : TEXCOORD2;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float3 worldNormal : TEXCOORD0;
    float2 uv : TEXCOORD1;
};

cbuffer VertexUniforms : register(b0, space1)
{
    column_major float4x4 mvp;
    column_major float4x4 normalMatrix;
};

// `tint.a` is the material alpha. `alphaParams.x` is the mask cutoff and
// `alphaParams.y` is the material transparency mode: 0 opaque, 1 mask, 2 blend.
cbuffer FragmentUniforms : register(b0, space3)
{
    float4 ambientColor;
    float4 lightDirectionToLight;
    float4 lightColor;
    float4 tint;
    float4 alphaParams;
};

Texture2D baseColorTexture : register(t0, space2);
SamplerState baseColorSampler : register(s0, space2);

VertexOutput VSMain(VertexInput input)
{
    VertexOutput output;
    output.position = mul(mvp, float4(input.position, 1.0));
    output.worldNormal = mul(normalMatrix, float4(input.normal, 0.0)).xyz;
    output.uv = input.uv;
    return output;
}

// Lighting is evaluated in world space. `ambientColor` and `lightColor` already
// include their CPU-side intensity. The normal is normalized after interpolation
// and guarded against a zero-length result so a degenerate mesh cannot produce
// NaN.
//
// Effective alpha is the sampled texture alpha multiplied by the material alpha.
// Opaque mode ignores it and writes 1.0, mask mode discards fragments below the
// cutoff and otherwise writes 1.0, and blend mode outputs the effective alpha for
// straight source-over compositing configured on the pipeline.
float4 PSMain(VertexOutput input) : SV_Target
{
    float4 texel = baseColorTexture.Sample(baseColorSampler, input.uv);
    float3 baseColor = texel.rgb * tint.rgb;
    float alpha = texel.a * tint.a;

    float normalLengthSquared = dot(input.worldNormal, input.worldNormal);
    float3 normal = normalLengthSquared > 1.0e-12
        ? input.worldNormal * rsqrt(normalLengthSquared)
        : float3(0.0, 0.0, 0.0);

    float diffuse = max(dot(normal, lightDirectionToLight.xyz), 0.0);
    float3 lighting = ambientColor.rgb + (lightColor.rgb * diffuse);
    float3 color = baseColor * lighting;

    float mode = alphaParams.y;

    if (mode >= 1.5)
    {
        return float4(color, alpha);
    }

    if (mode >= 0.5 && alpha < alphaParams.x)
    {
        discard;
    }

    return float4(color, 1.0);
}
