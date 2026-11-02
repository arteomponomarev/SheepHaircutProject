Shader "ShearAndGrow/WoolShell"
{
    Properties
    {
        [NoScaleOffset] _Mask ("Cut Mask (R8, CPU authority mirror)", 2D) = "white" {}
        _WoolRootColor ("Root Color", Color) = (0.78, 0.70, 0.60, 1)
        _WoolTipColor ("Tip Color", Color) = (1.0, 0.96, 0.88, 1)
        _RimColor ("Rim Color", Color) = (1.0, 0.86, 0.68, 1)
        _SheenColor ("Sheen Color", Color) = (1.0, 0.97, 0.9, 1)
        _Thickness ("Thickness", Float) = 0.2
        _BulkHeight ("Support volume height", Range(0.2,1)) = 1
        _CurlScale ("Curl Scale", Float) = 9
        _DetailScale ("Detail Scale", Float) = 34
        _Droop ("Droop", Range(0,1)) = 0.25
        _EdgeNoise ("Edge Noise", Range(0,1)) = 0.35
        _Wrap ("Light Wrap", Range(0,1)) = 0.55
        _MinDensity ("Min Strand Height", Range(0,1)) = 0.42
        _TrimmerPosOS ("Trimmer Pos OS", Vector) = (0,100,0,0)
        _TrimmerDirOS ("Trimmer Dir OS", Vector) = (1,0,0,0.08)
        _TrimmerPush ("Trimmer Push", Float) = 0.35
        [Header(Cartoon Puffs)]
        _PuffWrap ("Puff Light Wrap", Range(0,1)) = 0.75
        _PuffFuzz ("Puff Cotton Relief", Range(0,1)) = 0.12
        _PuffFuzzScale ("Puff Relief Scale", Float) = 5
        _PuffRim ("Puff Fuzzy Rim", Range(0,1)) = 0.45
        _PuffAmbient ("Puff Ambient Boost", Range(0.5,2.5)) = 1.45
        _PuffScatterColor ("Puff Scatter Color", Color) = (1, 0.82, 0.66, 1)
        _BulkShade ("Gap Shade (between puffs)", Range(0.3,1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
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

            #include "WoolShellCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 baseOS     : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float2 uv         : TEXCOORD3;
                float2 shellData  : TEXCOORD4; // x = t, y = height multiplier
                float  fogFactor  : TEXCOORD5;
                float  push       : TEXCOORD6;
                float4 normalSeed : TEXCOORD7; // xyz = normal (mesh space, sticks to tumbling clumps), w = puff seed
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(WoolAttributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float t = v.shell.x;
                float push;
                float3 p = WoolExtrude(v.positionOS.xyz, v.normalOS, t, v.color.r, push);
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.baseOS = v.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.shellData = float2(t, v.color.r);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.push = push;
                o.normalSeed = float4(v.normalOS, v.shell.y);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = i.shellData.x;
                float curlH = 1.0, cellRand = 0.5, maskValue = 1.0;
                float3 curlDir = float3(0, 1, 0);
                float cov = WoolCoverage(i.baseOS, i.uv, t, curlH, cellRand, curlDir, maskValue);
                clip(cov);

                bool bakedCurl = t < 0.0;
                float3 nBase = SafeNormalize(i.normalWS);
                // curlDir is the vector to the Voronoi feature point and can be exactly zero -> never normalise it raw.
                float3 curlWS = SafeNormalize(TransformObjectToWorldDir(curlDir, false));
                // Real locks use actual smooth tube normals; bulk gets only subtle relief.
                float3 n = bakedCurl ? nBase : SafeNormalize(nBase + curlWS * 0.12);
                t = bakedCurl ? i.shellData.y : t;

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));

                if (bakedCurl)
                {
                    // ---- Reference "popcorn" puff: soft cartoon wool ball ----
                    float3 nm = SafeNormalize(i.normalSeed.xyz);
                    float3 q = nm * _PuffFuzzScale + i.normalSeed.w * 61.7;
                    float f0 = WoolValueNoise(q);
                    float3 g = float3(WoolValueNoise(q + float3(0.31, 0, 0)), WoolValueNoise(q + float3(0, 0.31, 0)), WoolValueNoise(q + float3(0, 0, 0.31))) - f0;
                    // soft cotton relief that rotates with the puff
                    // g can be (0,0,0) where the noise is flat: transform WITHOUT normalising, then safe-normalise.
                    float3 nf = SafeNormalize(nBase - TransformObjectToWorldDir(g, false) * _PuffFuzz * 3.0);
                    half aoP = i.shellData.y;
                    half3 alb = lerp(_WoolRootColor.rgb, _WoolTipColor.rgb, smoothstep(0.35, 1.0, aoP));
                    alb *= 0.94 + 0.06 * f0;
                    half ndlP = dot(nf, mainLight.direction);
                    half wrapP = saturate((ndlP + _PuffWrap) / (1.0 + _PuffWrap));
                    wrapP = wrapP * wrapP * (3.0 - 2.0 * wrapP); // soft cartoon falloff
                    half attP = lerp(0.55, 1.0, mainLight.shadowAttenuation) * mainLight.distanceAttenuation;
                    half3 lit = alb * mainLight.color * wrapP * attP;
                    // warm light scattering through the fibres on the shadow side
                    half3 scatter = alb * _PuffScatterColor.rgb * (1.0 - wrapP) * 0.35 * aoP;
                    half3 amb = alb * SampleSH(nf) * lerp(0.6, 1.0, aoP) * _PuffAmbient;
                    half fres = pow(1.0 - saturate(dot(nBase, v)), 2.5);
                    half3 rimP = _WoolTipColor.rgb * fres * _PuffRim * aoP;
                    half3 colP = (lit + scatter) * aoP + amb + rimP;
                    colP = MixFog(colP, i.fogFactor);
                    return half4(colP, 1);
                }

                half3 albedo= lerp(_WoolRootColor.rgb, _WoolTipColor.rgb, saturate(t * 1.2));
                albedo *= (0.92 + 0.08 * cellRand) * _BulkShade;
                // micro AO: deep in the coat and between curls
                half ao = lerp(0.38, 1.0, pow(saturate(t), 0.65)) * lerp(0.72, 1.0, curlH);
                // freshly cut edge: slightly brighter, cleaner fibres
                ao *= lerp(1.08, 1.0, saturate((maskValue - 0.5) * 2.0));

                half ndl = dot(n, mainLight.direction);
                half wrap = saturate((ndl + _Wrap) / (1.0 + _Wrap));
                half atten = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half3 diffuse = albedo * mainLight.color * wrap * lerp(0.35, 1.0, atten);
                half3 ambient = albedo * SampleSH(n) * ao;
                half3 h = SafeNormalize(mainLight.direction + v);
                half sheen = pow(saturate(dot(n, h)), 12.0) * 0.12 * t;
                half rim = pow(1.0 - saturate(dot(nBase, v)), 3.0) * t;
                half3 col = diffuse * ao + ambient + _SheenColor.rgb * sheen * atten + _RimColor.rgb * rim * 0.35;
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

            #include "WoolShellCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct VaryingsS
            {
                float4 positionCS : SV_POSITION;
                float3 baseOS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float t : TEXCOORD2;
            };

            VaryingsS vertShadow(WoolAttributes v)
            {
                VaryingsS o;
                UNITY_SETUP_INSTANCE_ID(v);
                float push;
                float3 p = WoolExtrude(v.positionOS.xyz, v.normalOS, v.shell.x, v.color.r, push);
                float3 posWS = TransformObjectToWorld(p);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
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
                o.positionCS = cs;
                o.baseOS = v.positionOS.xyz;
                o.uv = v.uv;
                o.t = v.shell.x;
                return o;
            }

            half4 fragShadow(VaryingsS i) : SV_Target
            {
                float curlH = 1.0, cellRand = 0.5, maskValue = 1.0; float3 curlDir = float3(0, 1, 0);
                clip(WoolCoverage(i.baseOS, i.uv, i.t, curlH, cellRand, curlDir, maskValue));
                return 0;
            }
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
            #include "WoolShellCommon.hlsl"

            struct VaryingsD
            {
                float4 positionCS : SV_POSITION;
                float3 baseOS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float t : TEXCOORD2;
            };

            VaryingsD vertDepth(WoolAttributes v)
            {
                VaryingsD o;
                UNITY_SETUP_INSTANCE_ID(v);
                float push;
                float3 p = WoolExtrude(v.positionOS.xyz, v.normalOS, v.shell.x, v.color.r, push);
                o.positionCS = TransformObjectToHClip(p);
                o.baseOS = v.positionOS.xyz;
                o.uv = v.uv;
                o.t = v.shell.x;
                return o;
            }

            half4 fragDepth(VaryingsD i) : SV_Target
            {
                float curlH = 1.0, cellRand = 0.5, maskValue = 1.0; float3 curlDir = float3(0, 1, 0);
                clip(WoolCoverage(i.baseOS, i.uv, i.t, curlH, cellRand, curlDir, maskValue));
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vertDN
            #pragma fragment fragDN
            #pragma multi_compile_instancing
            #include "WoolShellCommon.hlsl"

            struct VaryingsDN
            {
                float4 positionCS : SV_POSITION;
                float3 baseOS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float t : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
            };

            VaryingsDN vertDN(WoolAttributes v)
            {
                VaryingsDN o;
                UNITY_SETUP_INSTANCE_ID(v);
                float push;
                float3 p = WoolExtrude(v.positionOS.xyz, v.normalOS, v.shell.x, v.color.r, push);
                o.positionCS = TransformObjectToHClip(p);
                o.baseOS = v.positionOS.xyz;
                o.uv = v.uv;
                o.t = v.shell.x;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 fragDN(VaryingsDN i) : SV_Target
            {
                float curlH = 1.0, cellRand = 0.5, maskValue = 1.0; float3 curlDir = float3(0, 1, 0);
                clip(WoolCoverage(i.baseOS, i.uv, i.t, curlH, cellRand, curlDir, maskValue));
                return half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
