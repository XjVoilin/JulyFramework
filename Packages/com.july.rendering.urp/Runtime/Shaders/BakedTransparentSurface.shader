Shader "July/URP/Baked Transparent Surface"
{
    Properties
    {
        [MainTexture] _BaseMap ("Baked Image", 2D) = "white" {}
        [MainColor] _BaseColor ("Color", Color) = (1, 1, 1, 1)
        [ToggleOff(_RECEIVE_SHADOWS_OFF)] _ReceiveShadows ("Receive Shadows", Float) = 1
        _ShadowStrength ("Received Shadow Strength", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "UniversalMaterialType"="Unlit" }
        Pass
        {
            Name "BakedTransparentSurface"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            Cull Off ZWrite Off ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _RECEIVE_SHADOWS_OFF
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            // Transparent receivers sample the shadow atlas at their own world position.
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _ShadowStrength;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                #if !defined(_RECEIVE_SHADOWS_OFF)
                    Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS), input.positionWS, half4(1, 1, 1, 1));
                    color.rgb *= lerp(1.0h, mainLight.shadowAttenuation, _ShadowStrength);
                #endif
                return color;
            }
            ENDHLSL
        }
    }
}
