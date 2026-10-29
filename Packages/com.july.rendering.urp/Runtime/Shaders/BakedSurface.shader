Shader "July/URP/Baked Surface"
{
    Properties
    {
        [MainTexture] _BaseMap ("Baked Image", 2D) = "white" {}
        [MainColor] _BaseColor ("Color", Color) = (1, 1, 1, 1)
        [ToggleOff(_RECEIVE_SHADOWS_OFF)] _ReceiveShadows ("Receive Shadows", Float) = 1
        _ShadowStrength ("Received Shadow Strength", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" "UniversalMaterialType"="Unlit" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
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
            float4 screenPosition : TEXCOORD2;
        };
        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.screenPosition = ComputeScreenPos(output.positionCS);
            return output;
        }
        ENDHLSL
        Pass
        {
            Name "BakedSurface"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Back ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _RECEIVE_SHADOWS_OFF
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            half4 Frag(Varyings input) : SV_Target
            {
                half3 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                #if !defined(_RECEIVE_SHADOWS_OFF)
                    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                        float4 shadowCoord = input.screenPosition;
                    #else
                        float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                    #endif
                    Light mainLight = GetMainLight(shadowCoord, input.positionWS, half4(1, 1, 1, 1));
                    color *= lerp(1.0h, mainLight.shadowAttenuation, _ShadowStrength);
                #endif
                return half4(color, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Back ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment DepthFrag
            half DepthFrag(Varyings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }
    }
}
