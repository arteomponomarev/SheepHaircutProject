Shader "ShearAndGrow/SoftCharacter"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _ShadeColor ("Shade Tint", Color) = (0.62, 0.52, 0.5, 1)
        _RimColor ("Rim Color", Color) = (1, 0.88, 0.72, 1)
        _RimStrength ("Rim Strength", Range(0,2)) = 0.35
        _Wrap ("Light Wrap", Range(0,1)) = 0.45
        _Smoothness ("Specular Smoothness", Range(0,1)) = 0.2
        _SpecStrength ("Specular Strength", Range(0,2)) = 0.1
        _FuzzScale ("Fuzz Noise Scale", Float) = 40
        _FuzzStrength ("Fuzz Strength (0 = smooth)", Range(0,1)) = 0
        _CurlScale ("Curl Scale", Float) = 30
        _VertexAO ("Vertex Color AO", Range(0,1)) = 1
        _Metallic ("Metal-ish (trimmer comb)", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _ShadeColor;
            half4 _RimColor;
            half _RimStrength;
            half _Wrap;
            half _Smoothness;
            half _SpecStrength;
            float _FuzzScale;
            half _FuzzStrength;
            float _CurlScale;
            half _VertexAO;
            half _Metallic;
        CBUFFER_END

        float SoftHash13(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }
        float SoftNoise(float3 p)
        {
            float3 i = floor(p); float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(SoftHash13(i), SoftHash13(i + float3(1,0,0)), f.x),
                             lerp(SoftHash13(i + float3(0,1,0)), SoftHash13(i + float3(1,1,0)), f.x), f.y),
                        lerp(lerp(SoftHash13(i + float3(0,0,1)), SoftHash13(i + float3(1,0,1)), f.x),
                             lerp(SoftHash13(i + float3(0,1,1)), SoftHash13(i + float3(1,1,1)), f.x), f.y), f.z);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                half4 color : TEXCOORD3;
                float fog : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS.xyz;
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                // SafeNormalize: an interpolated normal can collapse to ~0 (pole/degenerate verts) -> no NaN pixels.
                float3 n = SafeNormalize(i.normalWS);
                half3 albedo = _BaseColor.rgb;
                half ao = lerp(1.0, i.color.r, _VertexAO);
                if (_FuzzStrength > 0.001)
                {
                    // curly micro-relief for wool clumps & undercoat (object space so it sticks to deforming chunks)
                    float c1 = SoftNoise(i.positionOS * _CurlScale);
                    float c2 = SoftNoise(i.positionOS * _FuzzScale + 17.3);
                    float3 grad = float3(SoftNoise(i.positionOS * _CurlScale + float3(0.37,0,0)) - c1,
                                         SoftNoise(i.positionOS * _CurlScale + float3(0,0.37,0)) - c1,
                                         SoftNoise(i.positionOS * _CurlScale + float3(0,0,0.37)) - c1);
                    // doNormalize=false: grad can be (0,0,0); normalising it would produce NaN.
                    n = SafeNormalize(n - TransformObjectToWorldDir(grad, false) * 2.2 * _FuzzStrength);
                    albedo *= lerp(1.0, 0.82 + 0.18 * c2 + 0.08 * c1, _FuzzStrength);
                    ao *= lerp(1.0, 0.75 + 0.25 * smoothstep(0.2, 0.8, c1), _FuzzStrength);
                }
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light l = GetMainLight(shadowCoord);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half ndl = dot(n, l.direction);
                half wrap = saturate((ndl + _Wrap) / (1.0 + _Wrap));
                half atten = l.shadowAttenuation * l.distanceAttenuation;
                half lightAmt = wrap * lerp(0.3, 1.0, atten);
                half3 shade = lerp(_ShadeColor.rgb, 1.0.xxx, lightAmt);
                half3 diffuse = albedo * l.color * lightAmt;
                half3 ambient = albedo * SampleSH(n) * ao;
                half3 h = SafeNormalize(l.direction + v);
                half specPow = exp2(10.0 * _Smoothness + 1.0);
                half spec = pow(saturate(dot(n, h)), specPow) * _SpecStrength * atten;
                half3 specCol = lerp(1.0.xxx, albedo, _Metallic) * l.color;
                half rim = pow(1.0 - saturate(dot(n, v)), 3.0) * _RimStrength;
                half3 col = diffuse * shade * ao + ambient + specCol * spec + _RimColor.rgb * rim * (0.4 + 0.6 * atten);
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex vertS
            #pragma fragment fragS
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vertS(A v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 ld = normalize(_LightPosition - posWS);
            #else
                float3 ld = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, ld));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return cs;
            }
            half4 fragS() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vertD
            #pragma fragment fragD
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vertD(A v) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(v); return TransformObjectToHClip(v.positionOS.xyz); }
            half4 fragD(float4 pos : SV_POSITION) : SV_Target { return pos.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vertDN
            #pragma fragment fragDN
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            V vertDN(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 fragDN(V i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
