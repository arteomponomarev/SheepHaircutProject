Shader "Hidden/ShearAndGrow/WoolMaskStamp"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _Brush;
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 disc : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.disc = input.uv * 2.0 - 1.0;
                float2 maskUv = _Brush.xy + output.disc * _Brush.z;
                output.positionCS = float4(maskUv * 2.0 - 1.0, 0.0, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                    output.positionCS.y = -output.positionCS.y;
                #endif
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                clip(1.0 - dot(input.disc, input.disc));
                return half4(0, 0, 0, 0);
            }
            ENDHLSL
        }
    }
}
