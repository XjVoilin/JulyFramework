Shader "Hidden/JulyGF/MatCap Reference Preview"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        [Normal] _NormalMap("Normal Map", 2D) = "bump" {}
        _NormalScale("Normal Scale", Float) = 0
        _Matcap("MatCap", 2D) = "black" {}
        _MatcapScale("MatCap Strength", Range(0, 5)) = 0
        _AlphaMap("Alpha Mask", 2D) = "white" {}
        [HideInInspector] _Transparent("Transparent", Float) = 0
        [HideInInspector] _SrcBlend("Source Blend", Float) = 1
        [HideInInspector] _DstBlend("Destination Blend", Float) = 0
        [HideInInspector] _ZWrite("Depth Write", Float) = 1
        [HideInInspector] _Cull("Cull Mode", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.july.rendering.urp/Runtime/Shaders/Includes/MatCap.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_Matcap); SAMPLER(sampler_Matcap);
            TEXTURE2D(_AlphaMap); SAMPLER(sampler_AlphaMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _NormalMap_ST;
                float4 _Matcap_ST;
                float4 _AlphaMap_ST;
                half4 _BaseColor;
                half _NormalScale;
                half _MatcapScale;
                half _Transparent;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half3 tangentWS : TEXCOORD2;
                half3 bitangentWS : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                VertexNormalInputs basis = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.normalWS = basis.normalWS;
                output.tangentWS = basis.tangentWS;
                output.bitangentWS = basis.bitangentWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                if (_NormalScale > 0)
                {
                    half4 packedNormal = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, TRANSFORM_TEX(input.uv, _NormalMap));
                    half3 normalTS = UnpackNormalScale(packedNormal, _NormalScale);
                    normalWS = normalize(normalTS.x * input.tangentWS + normalTS.y * input.bitangentWS + normalTS.z * normalWS);
                }
                float2 matcapUV = JulyMatCapUV(normalWS);
                half4 matcap = SAMPLE_TEXTURE2D(_Matcap, sampler_Matcap, TRANSFORM_TEX(matcapUV, _Matcap));
                half3 reflection = matcap.rgb * _MatcapScale;
                Light sun = GetMainLight();
                half facingLight = saturate(dot(normalWS, sun.direction));
                half3 lighting = 0.55h + sun.color * facingLight;
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(input.uv, _BaseMap)) * _BaseColor;
                half alpha = 1;
                if (_Transparent > 0)
                {
                    half mask = SAMPLE_TEXTURE2D(_AlphaMap, sampler_AlphaMap, TRANSFORM_TEX(input.uv, _AlphaMap)).a;
                    alpha = saturate(albedo.a * mask + dot(reflection, half3(0.21h, 0.72h, 0.07h)));
                }
                half reflectionAttenuation = JulyMatCapLightAttenuation(facingLight, 0.4);
                return half4(albedo.rgb * lighting + reflection * reflectionAttenuation, alpha);
            }
            ENDHLSL
        }
    }
}
