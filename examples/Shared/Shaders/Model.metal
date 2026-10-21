#include <metal_stdlib>
using namespace metal;

struct VertexIn {
    float3 position [[attribute(0)]];
    float3 color [[attribute(1)]];
};

struct VertexOut {
    float4 position [[position]];
    float3 color;
};

// System.Numerics stores a matrix row-major and transforms row vectors, while
// MSL stores float4x4 column-major and transforms column vectors. Uploading the
// row-major bytes unchanged is correct: MSL reads them as the transpose, so
// `mvp * position` matches `position * mvp` in C#. Do not transpose twice.
struct Uniforms {
    float4x4 mvp;
};

vertex VertexOut vertex_main(VertexIn in [[stage_in]],
                             constant Uniforms &uniforms [[buffer(0)]]) {
    VertexOut out;
    out.position = uniforms.mvp * float4(in.position, 1.0);
    out.color = in.color;
    return out;
}

fragment float4 fragment_main(VertexOut in [[stage_in]]) {
    return float4(in.color, 1.0);
}
