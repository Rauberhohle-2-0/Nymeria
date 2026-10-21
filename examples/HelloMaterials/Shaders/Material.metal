#include <metal_stdlib>
using namespace metal;

struct VertexIn {
    float3 position [[attribute(0)]];
    float2 uv [[attribute(1)]];
};

struct VertexOut {
    float4 position [[position]];
    float2 uv;
};

// System.Numerics stores a matrix row-major and transforms row vectors, while
// MSL stores float4x4 column-major and transforms column vectors. Uploading the
// row-major bytes unchanged is correct: MSL reads them as the transpose, so
// `mvp * position` matches `position * mvp` in C#. Do not transpose twice.
struct VertexUniforms {
    float4x4 mvp;
};

struct FragmentUniforms {
    float4 tint;
};

vertex VertexOut vertex_main(VertexIn in [[stage_in]],
                             constant VertexUniforms &uniforms [[buffer(0)]]) {
    VertexOut out;
    out.position = uniforms.mvp * float4(in.position, 1.0);
    out.uv = in.uv;
    return out;
}

// The sampled base-color texture is multiplied by the material tint. A material
// without a texture, or a mesh without UVs, binds a generated white texture so
// the tint is shown directly. Alpha is not blended.
fragment float4 fragment_main(VertexOut in [[stage_in]],
                              constant FragmentUniforms &uniforms [[buffer(0)]],
                              texture2d<float> baseColorTexture [[texture(0)]],
                              sampler baseColorSampler [[sampler(0)]]) {
    float4 texel = baseColorTexture.sample(baseColorSampler, in.uv);
    return float4(texel.rgb * uniforms.tint.rgb, 1.0);
}
