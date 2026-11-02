#ifndef SHEAR_PROCESS_WOOL_COMMON_INCLUDED
#define SHEAR_PROCESS_WOOL_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_StateMask); SAMPLER(sampler_StateMask);

CBUFFER_START(UnityPerMaterial)
    float4 _StateMask_TexelSize;
    half4 _DirtyColor;
    half4 _DirtSpotColor;
    half4 _WetColor;
    half4 _DryColor;
    float4 _Footprint;        // xy = half size (x,z)
    float _MaskIsClean;       // 1: mask = cleanliness (washing)
    float _MaskIsDry;         // 1: mask = dryness (drying)
    float _CleanBase;         // cleanliness when the mask is not cleanliness
    float _DryBase;           // dryness when the mask is not dryness
    float _DirtyScale, _WetScale, _DryScale;
    float _DirtyLobes, _WetLobes, _DryLobes;
    float _ClumpPull;
    float _WetSag;
    float _WetGloss;
    float _FuzzRim;
    float4 _ToolPos;          // xyz object space, w = active 0..1
    float4 _ToolDir;          // xyz stream direction (object space), w = radius
    float _AirPush;
    float _WaterPush;
CBUFFER_END

struct HeapAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 uv0        : TEXCOORD0;   // footprint uv of the puff centre
    float4 uv1        : TEXCOORD1;   // radius, seed, ao, layer
    float3 uv2        : TEXCOORD2;   // puff centre
    float3 uv3        : TEXCOORD3;   // wet clump centre
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

float HeapHash13(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float HeapNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(HeapHash13(i), HeapHash13(i + float3(1,0,0)), f.x),
                     lerp(HeapHash13(i + float3(0,1,0)), HeapHash13(i + float3(1,1,0)), f.x), f.y),
                lerp(lerp(HeapHash13(i + float3(0,0,1)), HeapHash13(i + float3(1,0,1)), f.x),
                     lerp(HeapHash13(i + float3(0,1,1)), HeapHash13(i + float3(1,1,1)), f.x), f.y), f.z);
}

float2 HeapFootprintUV(float3 posOS)
{
    return float2(posOS.x / _Footprint.x, posOS.z / _Footprint.y) * 0.5 + 0.5;
}

// clean = how washed (0 dirty .. 1 clean), dry = how dried (0 wet .. 1 dry)
void HeapStateAt(float2 uv, out float clean, out float dry)
{
    float m = SAMPLE_TEXTURE2D_LOD(_StateMask, sampler_StateMask, uv, 0).r;
    clean = lerp(_CleanBase, m, _MaskIsClean);
    dry = lerp(_DryBase, m, _MaskIsDry);
}

// Lumpy cauliflower radius multiplier of a puff in direction dir.
float HeapRadial(float3 dir, float seed, float lobeAmt, float flat)
{
    float n1 = HeapNoise(dir * 2.1 + seed * 37.0) - 0.5;
    float n2 = HeapNoise(dir * 4.7 + seed * 53.0) - 0.5;
    float m = 1.0 + lobeAmt * (n1 * 2.0 + n2 * 0.9);
    // puffs resting on the table flatten a little
    return m * lerp(1.0, 0.78, saturate(-dir.y) * flat);
}

// Per-puff shape: matted dirty → heavy, smooth, clumped wet → large, lumpy, separated dry wool.
float3 HeapDeform(HeapAttributes v, out float clean, out float dry, out float wet, out float lobeAmt, out float3 normalOut)
{
    HeapStateAt(v.uv0, clean, dry);
    // dirty wool is dry-ish but matted; wetness comes from washing and leaves with drying
    wet = clean * (1.0 - dry);
    float dirtyW = 1.0 - clean;
    float scale = _DirtyScale * dirtyW + _WetScale * wet + _DryScale * (clean * dry);
    lobeAmt = _DirtyLobes * dirtyW + _WetLobes * wet + _DryLobes * (clean * dry);

    float r = v.uv1.x;
    float seed = v.uv1.y;
    float3 c = v.uv2;
    // wet puffs pull together into heavier lumps and sag; drying releases them again
    c = lerp(c, v.uv3, wet * _ClumpPull);
    c.y -= wet * r * _WetSag;

    float3 dir = v.normalOS;
    float flat = step(c.y, r * 0.8);
    float rad = HeapRadial(dir, seed, lobeAmt, flat) * r * scale;
    float3 p = c + dir * rad;
    // normal of the lumpy surface from two neighbouring directions (lumps receive real shading)
    float3 t1 = normalize(cross(dir, abs(dir.y) < 0.95 ? float3(0, 1, 0) : float3(1, 0, 0)));
    float3 t2 = cross(dir, t1);
    const float e = 0.12;
    float3 d1 = normalize(dir + t1 * e), d2 = normalize(dir + t2 * e);
    float3 p1 = d1 * HeapRadial(d1, seed, lobeAmt, flat) * r * scale;
    float3 p2 = d2 * HeapRadial(d2, seed, lobeAmt, flat) * r * scale;
    normalOut = normalize(cross(p1 - dir * rad, p2 - dir * rad));
    if (dot(normalOut, dir) < 0.0) normalOut = -normalOut;

    // gentle idle breathing of fluffy dry wool
    float t = _Time.y;
    p += dir * sin(t * 1.7 + seed * 40.0) * 0.0018 * clean * dry;

    // tool interaction: air pushes and wobbles puffs, water presses them down slightly
    float3 d = p - _ToolPos.xyz;
    float radius = max(_ToolDir.w, 1e-3);
    float k = saturate(1.0 - length(d.xz) / (radius * 2.2));
    k = k * k * _ToolPos.w;
    float3 away = normalize(float3(d.x, 0.35, d.z) + 1e-4);
    float wobble = sin(t * 21.0 + seed * 61.0) * 0.5 + sin(t * 13.0 + seed * 17.0) * 0.5;
    p += (away * _AirPush + dir * wobble * _AirPush * 0.6) * k * r;
    p.y -= _WaterPush * k * r;
    return p;
}

#endif
