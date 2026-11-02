#ifndef SHEAR_WOOL_SHELL_COMMON_INCLUDED
#define SHEAR_WOOL_SHELL_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_Mask); SAMPLER(sampler_Mask);

CBUFFER_START(UnityPerMaterial)
    float4 _Mask_TexelSize;
    half4 _WoolRootColor;
    half4 _WoolTipColor;
    half4 _RimColor;
    half4 _SheenColor;
    float _Thickness;
    float _BulkHeight;
    float _CurlScale;
    float _DetailScale;
    float _Droop;
    float _EdgeNoise;
    float _Wrap;
    float _MinDensity;
    float4 _TrimmerPosOS;   // xyz pos in body object space, w = active (0..1)
    float4 _TrimmerDirOS;   // xyz stroke direction in object space, w = radius
    float _TrimmerPush;
    float _PuffWrap;
    float _PuffFuzz;
    float _PuffFuzzScale;
    float _PuffRim;
    half4 _PuffScatterColor;
    float _BulkShade;
    float _PuffAmbient;
CBUFFER_END

struct WoolAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 uv         : TEXCOORD0;
    float2 shell      : TEXCOORD1;
    half4  color      : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

float3 WoolHash33(float3 p)
{
    p = float3(dot(p, float3(127.1, 311.7, 74.7)),
               dot(p, float3(269.5, 183.3, 246.1)),
               dot(p, float3(113.5, 271.9, 124.6)));
    return frac(sin(p) * 43758.5453123);
}

float WoolHash13(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float WoolValueNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(WoolHash13(i + float3(0,0,0)), WoolHash13(i + float3(1,0,0)), f.x),
                     lerp(WoolHash13(i + float3(0,1,0)), WoolHash13(i + float3(1,1,0)), f.x), f.y),
                lerp(lerp(WoolHash13(i + float3(0,0,1)), WoolHash13(i + float3(1,0,1)), f.x),
                     lerp(WoolHash13(i + float3(0,1,1)), WoolHash13(i + float3(1,1,1)), f.x), f.y), f.z);
}

// Cheap 2x2x2 Voronoi: returns F1 distance, cell random and direction to the feature point.
void WoolVoronoi(float3 p, out float f1, out float cellRand, out float3 toFeature)
{
    float3 basePos = floor(p - 0.5);
    f1 = 8.0;
    cellRand = 0.0;
    toFeature = float3(0, 1, 0);
    [unroll] for (int z = 0; z < 2; z++)
    [unroll] for (int y = 0; y < 2; y++)
    [unroll] for (int x = 0; x < 2; x++)
    {
        float3 cell = basePos + float3(x, y, z);
        float3 h = WoolHash33(cell);
        float3 fp = cell + 0.15 + h * 0.7;
        float3 d = p - fp;
        float dd = dot(d, d);
        if (dd < f1)
        {
            f1 = dd;
            cellRand = h.x;
            toFeature = d;
        }
    }
    f1 = sqrt(f1);
}

// Extrudes a shell vertex. Wool thickness is real geometry: shell t moves along the normal.
float3 WoolExtrude(float3 posOS, float3 normalOS, float t, float heightMul, out float pushAmount)
{
    // Negative shell height marks baked coiled locks. Keep their real rounded geometry.
    if (t < 0.0) { pushAmount = 0.0; return posOS; }
    float h = t * _Thickness * _BulkHeight * (0.35 + 0.65 * heightMul);
    float3 p = posOS + normalOS * h;
    // gentle gravity droop of the outer curls
    p.y -= t * t * _Droop * _Thickness;
    // comb push: curls around the trimmer head bend away from it (cut edge ripple / spring)
    float3 toV = p - _TrimmerPosOS.xyz;
    float dist = length(toV);
    float radius = max(_TrimmerDirOS.w, 1e-3);
    float k = saturate(1.0 - dist / (radius * 2.2));
    k = k * k * _TrimmerPosOS.w;
    float3 pushDir = normalize(toV + normalOS * 0.05 + _TrimmerDirOS.xyz * 0.6);
    p += pushDir * k * t * _TrimmerPush * _Thickness;
    pushAmount = k;
    return p;
}

// Returns visibility of this shell fragment; clip() if <= 0. Also outputs curl height & shading normal bits.
float WoolCoverage(float3 baseOS, float2 uv, float t, out float curlH, out float cellRand, out float3 curlDir, out float maskValue)
{
    if (t < 0.0)
    {
        curlH = 1.0;
        cellRand = WoolValueNoise(baseOS * _DetailScale);
        curlDir = float3(0, 1, 0);
        maskValue = SAMPLE_TEXTURE2D_LOD(_Mask, sampler_Mask, uv, 0).r;
        return maskValue - 0.5;
    }
    float3 p = baseOS * _CurlScale;
    float f1;
    WoolVoronoi(p, f1, cellRand, curlDir);
    // dome-shaped curls of varying size; secondary curly detail
    float dome = saturate(1.0 - f1 * f1 * 1.15);
    float detail = WoolValueNoise(baseOS * _DetailScale);
    curlH = saturate(dome * (0.72 + 0.28 * cellRand) + (detail - 0.5) * 0.28);

    float m = SAMPLE_TEXTURE2D_LOD(_Mask, sampler_Mask, uv, 0).r;
    maskValue = m;
    // fuzzy but crisp shear edge: noise only perturbs the threshold, it does not smear the strip
    float edgeN = WoolValueNoise(baseOS * (_DetailScale * 1.7)) - 0.5;
    float visible = m + edgeN * _EdgeNoise - 0.5;
    // curls right at the edge are a bit shorter and lifted (freshly cut look)
    float edgeFactor = saturate((m - 0.5) * 3.0);
    float strandTop = lerp(_MinDensity, 1.0, curlH) * lerp(0.72, 1.0, edgeFactor);
    float strand = strandTop - t;
    return min(visible, strand);
}

#endif
