#include <metal_stdlib>
using namespace metal;

struct VertexIn {
    float3 position [[attribute(0)]];
    float3 normal [[attribute(1)]];
    float2 uv [[attribute(2)]];
};

struct VertexOut {
    float4 position [[position]];
    float3 worldNormal;
    float2 uv;
};

// System.Numerics stores a matrix row-major and transforms row vectors, while
// MSL stores float4x4 column-major and transforms column vectors. Uploading the
// row-major bytes unchanged is correct: MSL reads them as the transpose, so
// `mvp * position` matches `position * mvp` in C#. The normal matrix is the
// inverse transpose of the model matrix and follows the same convention.
struct VertexUniforms {
    float4x4 mvp;
    float4x4 normalMatrix;
};

struct FragmentUniforms {
    float4 ambientColor;
    float4 lightDirectionToLight;
    float4 lightColor;
    float4 tint;
};

vertex VertexOut vertex_main(VertexIn in [[stage_in]],
                             constant VertexUniforms &uniforms [[buffer(0)]]) {
    VertexOut out;
    out.position = uniforms.mvp * float4(in.position, 1.0);
    out.worldNormal = (uniforms.normalMatrix * float4(in.normal, 0.0)).xyz;
    out.uv = in.uv;
    return out;
}

// Lighting is evaluated in world space. `ambientColor` and `lightColor` already
// include their CPU-side intensity. The normal is normalized after interpolation
// and guarded against a zero-length result so a degenerate mesh cannot produce
// NaN. Alpha is not blended; the existing material behavior writes 1.0.
fragment float4 fragment_main(VertexOut in [[stage_in]],
                              constant FragmentUniforms &uniforms [[buffer(0)]],
                              texture2d<float> baseColorTexture [[texture(0)]],
                              sampler baseColorSampler [[sampler(0)]]) {
    float4 texel = baseColorTexture.sample(baseColorSampler, in.uv);
    float3 baseColor = texel.rgb * uniforms.tint.rgb;

    float normalLengthSquared = dot(in.worldNormal, in.worldNormal);
    float3 normal = normalLengthSquared > 1.0e-12
        ? in.worldNormal * rsqrt(normalLengthSquared)
        : float3(0.0);

    float diffuse = max(dot(normal, uniforms.lightDirectionToLight.xyz), 0.0);
    float3 lighting =
        uniforms.ambientColor.rgb + (uniforms.lightColor.rgb * diffuse);

    return float4(baseColor * lighting, 1.0);
}
