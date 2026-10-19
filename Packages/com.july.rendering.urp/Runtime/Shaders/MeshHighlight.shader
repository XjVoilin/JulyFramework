Shader "July/URP/Mesh Highlight"
{
    Properties
    {
        _OverlayColor("Surface Tint", Vector) = (1, 0.92156863, 0.01568628, 0.5)
        _OutlineColor("Outline Color", Vector) = (0, 0, 0, 1)
        _OutlineWidth("Outline Width", Float) = 0.2
        _GlowColor("Glow Color", Vector) = (1, 0.96470594, 0.13333334, 1)
        _GlowWidth("Glow Width", Float) = 0.25
        _GlowIntensity("Glow Intensity", Float) = 1.86
        _SeeThroughColor("Occluded Surface Tint", Vector) = (1, 0.8169587, 0, 1)
        _SeeThroughIntensity("Occluded Surface Intensity", Float) = 2.62
        _SeeThroughNoise("Occluded Surface Noise", Range(0, 0.5)) = 0.47
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _OverlayColor, _OutlineColor, _GlowColor, _SeeThroughColor;
            float _OutlineWidth, _GlowWidth, _GlowIntensity, _SeeThroughIntensity, _SeeThroughNoise;
        CBUFFER_END
        UNITY_INSTANCING_BUFFER_START(HighlightDirections)
            UNITY_DEFINE_INSTANCED_PROP(float4, _HighlightDirection)
        UNITY_INSTANCING_BUFFER_END(HighlightDirections)
        struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS : SV_POSITION; };
        Varyings Vert(Attributes input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS);
            return output;
        }
        float2 ProjectedNormal(float3 normalOS)
        {
            float3 normalVS = TransformWorldToViewDir(TransformObjectToWorldNormal(normalOS));
            // 正对相机的法线不会产生 XY 平面的法线扩张。
            float2 direction = normalVS.xy * rsqrt(max(dot(normalVS.xy, normalVS.xy), 1e-12));
            return mul((float2x2)UNITY_MATRIX_P, direction);
        }
        Varyings OutlineVert(Attributes input)
        {
            Varyings output = Vert(input);
            float width = _OutlineWidth / 100.0;
            // 本效果只支持正交相机，constantWidth 的投影系数固定为 2。
            output.positionCS.xy += 2.0 * width *
                (ProjectedNormal(input.normalOS) + UNITY_ACCESS_INSTANCED_PROP(HighlightDirections, _HighlightDirection).xy);
            return output;
        }
        Varyings GlowVert(Attributes input)
        {
            Varyings output = Vert(input);
            float width = _OutlineWidth / 100.0 + _GlowWidth / 100.0 * (1.0 + 0.25 * sin(_Time.w));
            output.positionCS.xy += 2.0 * width *
                (ProjectedNormal(input.normalOS) + UNITY_ACCESS_INSTANCED_PROP(HighlightDirections, _HighlightDirection).xy);
            return output;
        }
        Varyings OverlayVert(Attributes input)
        {
            Varyings output = Vert(input);
            #if UNITY_REVERSED_Z
                output.positionCS.z += 0.00005;
            #else
                output.positionCS.z -= 0.00005;
            #endif
            return output;
        }
        half3 HostColor(half3 color)
        {
            #if defined(UNITY_COLORSPACE_GAMMA)
                return color;
            #else
                return SRGBToLinear(color);
            #endif
        }
        half4 MaskFrag(Varyings input) : SV_Target { return 0; }
        half4 OverlayFrag(Varyings input) : SV_Target
        {
            float pulse = 0.5 + abs(0.5 - frac(fmod(_Time.y, 1000.0)));
            return half4(HostColor(_OverlayColor.rgb), _OverlayColor.a * pulse);
        }
        half4 GlowFrag(Varyings input) : SV_Target
        {
            return half4(HostColor(_GlowColor.rgb), _GlowIntensity * 0.5);
        }
        half4 OutlineFrag(Varyings input) : SV_Target { return _OutlineColor; }
        half4 SeeThroughFrag(Varyings input) : SV_Target
        {
            float noise = _SeeThroughNoise;
            half3 tint = _SeeThroughColor.rgb + noise * 0.1 * frac(frac(_Time.w) * input.positionCS.y);
            float alpha = _SeeThroughIntensity * (1.0 - 2.0 * noise + 2.0 * noise * frac(input.positionCS.y * 0.5));
            return half4(HostColor(tint), alpha * _SeeThroughColor.a);
        }
        ENDHLSL
        Pass
        {
            Name "ClearExpandedStencil"
            Cull Back ZTest Always ZWrite Off ColorMask 0
            // 在此渲染器中预留模板位 1，并保留其他无关模板位。
            Stencil { Ref 2 ReadMask 2 WriteMask 2 Comp Always Pass Zero }
            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma vertex OutlineVert
            #pragma fragment MaskFrag
            ENDHLSL
        }
        Pass
        {
            Name "SilhouetteMask"
            Cull Back ZTest Always ZWrite Off ColorMask 0
            Stencil { Ref 2 ReadMask 2 WriteMask 2 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex OverlayVert
            #pragma fragment MaskFrag
            ENDHLSL
        }
        Pass
        {
            Name "SurfaceOverlay"
            Cull Back ZTest LEqual ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex OverlayVert
            #pragma fragment OverlayFrag
            ENDHLSL
        }
        Pass
        {
            Name "OuterGlow"
            Cull Back ZTest Always ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            Stencil { Ref 2 ReadMask 2 WriteMask 2 Comp NotEqual Pass Keep }
            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma vertex GlowVert
            #pragma fragment GlowFrag
            ENDHLSL
        }
        Pass
        {
            Name "BlackOutline"
            Cull Back ZTest LEqual ZWrite On Blend SrcAlpha OneMinusSrcAlpha
            Stencil { Ref 2 ReadMask 2 WriteMask 2 Comp NotEqual Pass Replace }
            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            ENDHLSL
        }
        Pass
        {
            Name "ClearSeeThroughStencil"
            Cull Back ZTest Always ZWrite Off ColorMask 0
            Stencil { Ref 2 ReadMask 2 WriteMask 2 Comp Always Pass Zero }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment MaskFrag
            ENDHLSL
        }
        Pass
        {
            Name "VisibleSurfaceMask"
            Cull Back ZTest LEqual ZWrite Off ColorMask 0
            Stencil { Ref 2 ReadMask 2 WriteMask 2 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment MaskFrag
            ENDHLSL
        }
        Pass
        {
            Name "OccludedSurface"
            Cull Back ZTest Greater ZWrite On Blend SrcAlpha OneMinusSrcAlpha
            Stencil { Ref 2 ReadMask 2 WriteMask 2 Comp Greater Pass Replace }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment SeeThroughFrag
            ENDHLSL
        }
    }
}
