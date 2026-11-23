Shader "JulyGF/URP/MatCap Glass"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Glass Tint and Opacity", Color) = (1, 1, 1, 0.2)
        _AlphaMap("Opacity Mask (R × A)", 2D) = "white" {}
        [Normal] _NormalMap("Normal Map", 2D) = "bump" {}
        _NormalScale("Normal Scale", Range(0, 2)) = 0
        _Matcap("MatCap", 2D) = "black" {}
        _MatcapScale("MatCap Strength", Range(0, 5)) = 1
        _MatcapTint("MatCap Tint", Color) = (1, 1, 1, 1)
        _MatcapDarkRetention("Dark Reflection Retention", Range(0, 1)) = 1
        _ReflectionOpacity("Reflection Opacity", Range(0, 1)) = 1
        _RimStrength("Rim Strength", Range(0, 1)) = 0
        _RimWidth("Rim Width", Range(0.01, 1)) = 0.25
        _RimColor("Rim Color", Color) = (1, 1, 1, 1)
        _RimOpacity("Rim Opacity", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "MatCapGlass"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Includes/MatCap.hlsl"
            #include "Includes/Glass.hlsl"
            #include "Includes/Rim.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_AlphaMap); SAMPLER(sampler_AlphaMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_Matcap); SAMPLER(sampler_Matcap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _AlphaMap_ST;
                float4 _NormalMap_ST;
                float4 _Matcap_ST;
                half4 _BaseColor;
                half4 _MatcapTint;
                half4 _RimColor;
                half _NormalScale;
                half _MatcapScale;
                half _MatcapDarkRetention;
                half _ReflectionOpacity;
                half _RimStrength;
                half _RimWidth;
                half _RimOpacity;
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
                float3 positionWS : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.uv = input.uv;
                VertexNormalInputs basis = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.normalWS = basis.normalWS;
                output.tangentWS = basis.tangentWS;
                output.bitangentWS = basis.bitangentWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 geometryNormalWS = normalize(input.normalWS);
                half3 normalWS = geometryNormalWS;
                if (_NormalScale > 0)
                {
                    half4 packed = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, TRANSFORM_TEX(input.uv, _NormalMap));
                    half3 normalTS = UnpackNormalScale(packed, _NormalScale);
                    normalWS = normalize(normalTS.x * input.tangentWS + normalTS.y * input.bitangentWS + normalTS.z * normalWS);
                }

                Light sun = GetMainLight();
                half facingLight = saturate(dot(normalWS, sun.direction));
                half3 lighting = 0.55h + sun.color * facingLight;
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(input.uv, _BaseMap)) * _BaseColor;

                float2 matcapUV = TRANSFORM_TEX(JulyMatCapUV(normalWS), _Matcap);
                half3 reflection = SAMPLE_TEXTURE2D(_Matcap, sampler_Matcap, matcapUV).rgb;
                reflection *= _MatcapTint.rgb * _MatcapScale;
                reflection *= JulyMatCapLightAttenuation(facingLight, _MatcapDarkRetention);

                half2 opacityMask = SAMPLE_TEXTURE2D(_AlphaMap, sampler_AlphaMap, TRANSFORM_TEX(input.uv, _AlphaMap)).ra;
                half baseOpacity = albedo.a * opacityMask.x * opacityMask.y;
                half opacity = JulyGlassOpacity(baseOpacity, reflection, _ReflectionOpacity);
                half3 color = albedo.rgb * lighting + reflection;
                if (_RimStrength > 0)
                {
                    half3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                    half rim = JulyRimMask(geometryNormalWS, viewDirectionWS, _RimWidth) * _RimStrength;
                    color += rim * _RimColor.rgb;
                    opacity = saturate(opacity + rim * _RimOpacity);
                }
                return half4(color, opacity);
            }
            ENDHLSL
        }
    }
}
