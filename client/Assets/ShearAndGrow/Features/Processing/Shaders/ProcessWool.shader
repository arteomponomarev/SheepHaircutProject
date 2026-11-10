Shader "ShearAndGrow/ProcessWool"
{
    Properties
    {
        [NoScaleOffset] _StateMask ("State Mask (R8, CPU authority mirror)", 2D) = "black" {}
        _DirtyColor ("Dirty Color", Color) = (0.86, 0.78, 0.62, 1)
        _DirtSpotColor ("Dirt Spot Color", Color) = (0.52, 0.42, 0.30, 1)
        _WetColor ("Wet Color", Color) = (0.72, 0.72, 0.70, 1)
        _DryColor ("Dry Color", Color) = (1.0, 0.985, 0.955, 1)
        _Footprint ("Footprint half size (x,z)", Vector) = (0.3, 0.24, 0, 0)
        _MaskIsClean ("Mask is cleanliness", Float) = 1
        _MaskIsDry ("Mask is dryness", Float) = 0
        _CleanBase ("Cleanliness (when not masked)", Range(0,1)) = 1
        _DryBase ("Dryness (when not masked)", Range(0,1)) = 0
        [Header(Shape)]
        _DirtyScale ("Dirty scale", Float) = 0.97
        _WetScale ("Wet scale", Float) = 0.84
        _DryScale ("Dry scale", Float) = 1.12
        _DirtyLobes ("Dirty lumpiness", Float) = 0.16
        _WetLobes ("Wet lumpiness", Float) = 0.05
        _DryLobes ("Dry lumpiness", Float) = 0.34
        _ClumpPull ("Wet clumping", Range(0,1)) = 0.28
        _WetSag ("Wet sag", Range(0,1)) = 0.22
        [Header(Surface)]
        _WetGloss ("Wet gloss", Range(0,2)) = 0.8
        _FuzzRim ("Dry fuzzy rim", Range(0,1)) = 0.5
        [Header(Tool)]
        _ToolPos ("Tool pos OS (w=active)", Vector) = (0, 10, 0, 0)
        _ToolDir ("Tool dir OS (w=radius)", Vector) = (0, -1, 0, 0.06)
        _AirPush ("Air push", Float) = 0
        _WaterPush ("Water press", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "ProcessWoolCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float3 dirOS      : TEXCOORD3;
                float4 data       : TEXCOORD4; // x seed, y ao, z lumpiness, w layer
                float  fogFactor  : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(HeapAttributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float clean, dry, wet, lobes;
                float3 nOS;
                float3 p = HeapDeform(v, clean, dry, wet, lobes, nOS);
                o.positionOS = p;
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(nOS);
                o.dirOS = v.normalOS;
                o.data = float4(v.uv1.y, v.uv1.z, lobes, v.uv1.w);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // local state per pixel → soft, exact brush edges independent of puff size
                float clean, dry;
                HeapStateAt(HeapFootprintUV(i.positionOS), clean, dry);
                float wet = clean * (1.0 - dry);
                float dirt = 1.0 - clean;
                float seed = i.data.x;
                float lobes = i.data.z;

                float3 nBase = SafeNormalize(i.normalWS);
                // cotton relief: strong on dry fluffy wool, almost none on slick wet wool
                float3 q = i.dirOS * 6.0 + seed * 61.7;
                float f0 = HeapNoise(q);
                float3 g = float3(HeapNoise(q + float3(0.31, 0, 0)), HeapNoise(q + float3(0, 0.31, 0)), HeapNoise(q + float3(0, 0, 0.31))) - f0;
                float relief = 0.05 + lobes * 0.35;
                float3 n = SafeNormalize(nBase - TransformObjectToWorldDir(g, false) * relief * 3.0);

                // ---- albedo
                half3 dryClean = _DryColor.rgb;
                half3 wetClean = _WetColor.rgb;
                half3 baseCol = lerp(wetClean, dryClean, dry);
                baseCol = lerp(baseCol, _DirtyColor.rgb, saturate(dirt * 1.1));
                // dirt spots and dust shrink and vanish as the wool gets cleaner
                float spotsN = HeapNoise(i.positionOS * 26.0) * 0.65 + HeapNoise(i.positionOS * 71.0) * 0.35;
                // fully dirty: ~45% covered by soft dark smudges; they shrink to nothing as cleanliness rises
                float spotEdge = 1.0 - dirt * 0.45;
                float spots = smoothstep(spotEdge - 0.06, spotEdge + 0.06, spotsN) * saturate(dirt * 1.4);
                float dust = smoothstep(0.80, 0.93, HeapNoise(i.positionOS * 140.0)) * dirt;
                half3 albedo = lerp(baseCol, _DirtSpotColor.rgb, spots * 0.62);
                albedo = lerp(albedo, _DirtSpotColor.rgb * 0.75, dust * 0.55);
                albedo *= 0.95 + 0.05 * f0;
                half ao = i.data.y;

                // ---- lighting (soft cartoon wool, matches the sheep coat)
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float3 v = SafeNormalize(GetWorldSpaceViewDir(i.positionWS));
                half wrapK = lerp(0.55, 0.8, dry);
                half ndl = dot(n, mainLight.direction);
                half wrap = saturate((ndl + wrapK) / (1.0 + wrapK));
                wrap = wrap * wrap * (3.0 - 2.0 * wrap);
                half att = lerp(0.55, 1.0, mainLight.shadowAttenuation) * mainLight.distanceAttenuation;
                half3 lit = albedo * mainLight.color * wrap * att;
                half3 scatter = albedo * half3(1.0, 0.82, 0.66) * (1.0 - wrap) * 0.3 * (1.0 - wet * 0.6);
                half3 amb = albedo * SampleSH(n) * lerp(0.6, 1.0, ao) * lerp(1.25, 1.05, dry * clean);
                half3 col = (lit + scatter) * ao + amb;

                // wet: soft sheen broken up by fibres + tiny droplet glints (not a plastic highlight)
                half3 h = SafeNormalize(mainLight.direction + v);
                half breakup = 0.55 + 0.45 * HeapNoise(i.positionOS * 48.0);
                half spec = pow(saturate(dot(n, h)), 28.0) * 0.55 * breakup;
                half glint = smoothstep(0.92, 0.985, HeapNoise(i.positionOS * 95.0 + seed * 3.0));
                col += mainLight.color * (spec + glint * 0.8) * wet * _WetGloss * att;

                // dry: fuzzy translucent rim (soft cotton silhouette)
                half fres = pow(1.0 - saturate(dot(nBase, v)), 2.4);
                col += dryClean * fres * _FuzzRim * dry * clean * ao;
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "ProcessWoolCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 vertShadow(HeapAttributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float clean, dry, wet, lobes;
                float3 nOS;
                float3 p = HeapDeform(v, clean, dry, wet, lobes, nOS);
                float3 posWS = TransformObjectToWorld(p);
                float3 nWS = TransformObjectToWorldNormal(nOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - posWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, lightDir));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return cs;
            }

            half4 fragShadow() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing
            #include "ProcessWoolCommon.hlsl"

            float4 vertDepth(HeapAttributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float clean, dry, wet, lobes;
                float3 nOS;
                float3 p = HeapDeform(v, clean, dry, wet, lobes, nOS);
                return TransformObjectToHClip(p);
            }
            half4 fragDepth() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
