Shader "July/URP/Mesh Highlight"
{
    Properties
    {
        [MeshHighlightColor] _OverlayColor("Visible surface tint (A = max alpha, sRGB)", Vector) = (1,0.92156863,0.01568628,0.5)
        _OverlayPulseMinAlpha("Surface pulse minimum alpha", Range(0,1)) = 0.25
        [MeshHighlightColor] _OutlineColor("Outline color", Vector) = (0,0,0,1)
        _OutlineWidth("Outline width", Float) = 0.2
        [MeshHighlightColor] _GlowColor("Outer glow color (sRGB)", Vector) = (1,0.96470594,0.13333334,1)
        _GlowWidth("Outer glow width", Float) = 0.25
        _GlowIntensity("Outer glow intensity", Float) = 1.86
        [MeshHighlightColor] _SeeThroughColor("Occluded surface tint (sRGB)", Vector) = (1,0.8169587,0,1)
        _SeeThroughIntensity("Occluded surface intensity", Float) = 2.62
        _SeeThroughNoise("Occluded scan contrast", Range(0,0.5)) = 0.47
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        CBUFFER_START(UnityPerMaterial)
        half4 _OverlayColor, _OutlineColor, _GlowColor, _SeeThroughColor;
        float _OutlineWidth, _GlowWidth, _GlowIntensity, _SeeThroughIntensity, _SeeThroughNoise;
        float _OverlayPulseMinAlpha;
        CBUFFER_END
        struct Attributes { float3 positionOS : POSITION; };
        struct Varyings { float4 positionCS : SV_POSITION; };
        Varyings MaskVert(Attributes input)
        {
            Varyings output;
            // Match ordinary URP surface vertex programs: world xyz, then constant-w projection.
            float3 positionWS = TransformObjectToWorld(input.positionOS);
            output.positionCS = TransformWorldToHClip(positionWS);
            return output;
        }
        half4 MaskFrag(Varyings input) : SV_Target { return 1; }
        ENDHLSL
        Pass
        {
            Name "CompleteSilhouette"
            Cull Back ZTest Always ZWrite Off ColorMask R
            HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment MaskFrag
            ENDHLSL
        }
        Pass
        {
            Name "VisibleSilhouette"
            Cull Back ZTest LEqual ZWrite Off ColorMask G
            // Favor the same surface across independently compiled depth calculations.
            // Units only: no slope bias that could grow on grazing-angle surfaces.
            Offset 0, -1
            HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment MaskFrag
            ENDHLSL
        }
        Pass
        {
            Name "LocalComposite"
            Cull Off ZTest Always ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex CompositeVert
            #pragma fragment CompositeFrag
            TEXTURE2D(_HighlightMask); SAMPLER(sampler_HighlightMask);
            // Keep silhouette/depth classification point sampled; only animate glow with linear coverage.
            SAMPLER(sampler_HighlightLinearClamp);
            float4 _HighlightMaskSize;
            // Screen-space support of projected normal expansion plus the four diagonal offsets.
            float4 _HighlightWidthFactors;
            half3 HostColor(half3 color)
            {
                #if defined(UNITY_COLORSPACE_GAMMA)
                    return color;
                #else
                    return SRGBToLinear(color);
                #endif
            }
            Varyings CompositeVert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                return output;
            }
            half2 ReadMask(float2 uv)
            {
                // Clamping would extend a silhouette touching the image edge outside the image.
                if (any(uv < 0) || any(uv > 1)) return 0;
                return SAMPLE_TEXTURE2D_LOD(_HighlightMask, sampler_HighlightMask, uv, 0).rg;
            }
            half ReadGlowMask(float2 uv)
            {
                if (any(uv < 0) || any(uv > 1)) return 0;
                return SAMPLE_TEXTURE2D_LOD(_HighlightMask, sampler_HighlightLinearClamp, uv, 0).r;
            }
            static const float2 Directions[8] = {
                float2(1,0),float2(-1,0),float2(0,1),float2(0,-1),
                float2(.7071,.7071),float2(-.7071,.7071),float2(.7071,-.7071),float2(-.7071,-.7071)
            };
            half4 CompositeFrag(Varyings input) : SV_Target
            {
                float2 uv = input.positionCS.xy * _HighlightMaskSize.xy;
                half2 mask = ReadMask(uv);
                if (mask.r > .5h)
                {
                    if (mask.g > .5h)
                    {
                        float pulse = abs(1.0 - 2.0 * frac(fmod(_Time.y, 1000.0)));
                        float minAlpha = min(_OverlayPulseMinAlpha, _OverlayColor.a);
                        float alpha = lerp(minAlpha, _OverlayColor.a, pulse);
                        return half4(HostColor(_OverlayColor.rgb), alpha);
                    }
                    half3 tint = _SeeThroughColor.rgb + _SeeThroughNoise * .1 * frac(frac(_Time.w) * input.positionCS.y);
                    float scan = _SeeThroughIntensity * (1 - 2 * _SeeThroughNoise + 2 * _SeeThroughNoise * frac(input.positionCS.y * .5));
                    return half4(HostColor(tint), _SeeThroughColor.a * scan);
                }
                half visibleNear = 0, glow = 0;
                float glowWidth = _OutlineWidth + _GlowWidth * (1 + .25 * sin(_Time.w));
                [unroll] for (int i = 0; i < 8; i++)
                {
                    float factor = _HighlightWidthFactors.x + (i < 4 ? _HighlightWidthFactors.y : _HighlightWidthFactors.z);
                    visibleNear = max(visibleNear, ReadMask(uv + Directions[i] * (_OutlineWidth * factor) * _HighlightMaskSize.xy).g);
                    glow = max(glow, ReadGlowMask(uv + Directions[i] * (glowWidth * factor) * _HighlightMaskSize.xy));
                    glow = max(glow, ReadGlowMask(uv + Directions[i] * (glowWidth * factor * .5) * _HighlightMaskSize.xy));
                }
                if (visibleNear > .5h) return _OutlineColor;
                return half4(HostColor(_GlowColor.rgb), saturate(_GlowIntensity * .5) * _GlowColor.a * glow);
            }
            ENDHLSL
        }
    }
}
