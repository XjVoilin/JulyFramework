Shader "JulyGF/UI/TextMeshPro Double Outline"
{
    Properties
    {
        [Header(Face)]
        _FaceColor("Face Color", Color) = (0, 0, 0, 1)

        [Header(Inner Outline)]
        _InnerOutlineColor("Inner Outline Color", Color) = (1, 1, 1, 1)
        _InnerOutlineWidth("Inner Outline Width", Range(0, 0.4)) = 0.36

        [Header(Outer Outline)]
        _OuterOutlineColor("Outer Outline Color", Color) = (0.72, 0.04, 0.08, 1)
        _OuterOutlineWidth("Outer Outline Width", Range(0, 0.4)) = 0.18

        // TMP only reads its standard properties when sizing glyph quads.
        // Reserve space for both rings at their maximum supported widths.
        // These values size the mesh; they do not control the visible outline.
        [HideInInspector] _OutlineWidth("TMP Padding Reservation", Float) = 1
        // UpdateShaderRatios reads this even when no outline keyword is enabled.
        [HideInInspector] _OutlineSoftness("TMP Outline Softness", Float) = 0
        [HideInInspector] _FaceDilate("Face Dilate", Float) = 0
        [HideInInspector] _WeightNormal("Weight Normal", Float) = 0
        [HideInInspector] _WeightBold("Weight Bold", Float) = 0.5
        [HideInInspector] _ScaleRatioA("Scale Ratio A", Float) = 1

        [HideInInspector] _MainTex("Font Atlas", 2D) = "white" {}
        [HideInInspector] _TextureWidth("Texture Width", Float) = 512
        [HideInInspector] _TextureHeight("Texture Height", Float) = 512
        [HideInInspector] _GradientScale("Gradient Scale", Float) = 5
        [HideInInspector] _ScaleX("Scale X", Float) = 1
        [HideInInspector] _ScaleY("Scale Y", Float) = 1
        [HideInInspector] _PerspectiveFilter("Perspective Correction", Range(0, 1)) = 0.875
        [HideInInspector] _Sharpness("Sharpness", Float) = 0
        [HideInInspector] _VertexOffsetX("Vertex Offset X", Float) = 0
        [HideInInspector] _VertexOffsetY("Vertex Offset Y", Float) = 0

        [HideInInspector] _ClipRect("Clip Rect", Vector) = (-32767, -32767, 32767, 32767)
        [HideInInspector] _MaskSoftnessX("Mask Softness X", Float) = 0
        [HideInInspector] _MaskSoftnessY("Mask Softness Y", Float) = 0
        [HideInInspector] _StencilComp("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask("Stencil Read Mask", Float) = 255
        [HideInInspector] _CullMode("Cull Mode", Float) = 0
        [HideInInspector] _ColorMask("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull [_CullMode]
        ZWrite Off
        Lighting Off
        Fog { Mode Off }
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex VertShader
            #pragma fragment PixShader
            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            // Declare only the TMP inputs used here. The package must not depend
            // on Essential Resources being imported into a project's Assets.
            sampler2D _MainTex;
            fixed4 _FaceColor;
            float _FaceDilate;
            float _WeightNormal;
            float _WeightBold;
            float _VertexOffsetX;
            float _VertexOffsetY;
            float4 _ClipRect;
            float _MaskSoftnessX;
            float _MaskSoftnessY;
            float _GradientScale;
            float _ScaleX;
            float _ScaleY;
            float _PerspectiveFilter;
            float _Sharpness;

            fixed4 _InnerOutlineColor;
            fixed4 _OuterOutlineColor;
            float _InnerOutlineWidth;
            float _OuterOutlineWidth;
            int _UIVertexColorAlwaysGammaSpace;

            struct vertex_t
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                float2 texcoord0 : TEXCOORD0;
                float2 texcoord1 : TEXCOORD1;
            };

            struct pixel_t
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
                float4 vertex : SV_POSITION;
                fixed4 faceColor : COLOR;
                float2 atlasUV : TEXCOORD0;
                float4 thresholds : TEXCOORD1;
                float4 mask : TEXCOORD2;
                half opacity : TEXCOORD3;
            };

            pixel_t VertShader(vertex_t input)
            {
                pixel_t output;
                UNITY_INITIALIZE_OUTPUT(pixel_t, output);
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float4 vert = input.vertex;
                vert.xy += float2(_VertexOffsetX, _VertexOffsetY);
                float4 position = UnityObjectToClipPos(vert);

                float2 pixelSize = position.w;
                pixelSize /= float2(_ScaleX, _ScaleY)
                    * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float scale = rsqrt(dot(pixelSize, pixelSize));
                scale *= abs(input.texcoord1.y) * _GradientScale * (_Sharpness + 1);
                if (UNITY_MATRIX_P[3][3] == 0)
                {
                    scale = lerp(abs(scale) * (1 - _PerspectiveFilter), scale,
                        abs(dot(UnityObjectToWorldNormal(input.normal.xyz),
                            normalize(WorldSpaceViewDir(vert)))));
                }

                // Match TMP's unoutlined face. Do not use _ScaleRatioA here:
                // TMP changes that ratio based on its padding reservation.
                float distanceRatio = (_GradientScale - 1) / _GradientScale;
                float bold = step(input.texcoord1.y, 0);
                float weight = lerp(_WeightNormal, _WeightBold, bold) / 4.0;
                weight = (weight + _FaceDilate) * distanceRatio * 0.5;
                float faceBias = (0.5 - weight) * scale - 0.5;
                float innerWidth = _InnerOutlineWidth * distanceRatio * 0.5 * scale;
                float outerWidth = _OuterOutlineWidth * distanceRatio * 0.5 * scale;

                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                    input.color.rgb = UIGammaToLinear(input.color.rgb);

                fixed4 faceColor = fixed4(input.color.rgb, 1) * _FaceColor;
                faceColor.rgb *= faceColor.a;

                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                output.vertex = position;
                output.faceColor = faceColor;
                output.atlasUV = input.texcoord0;
                output.thresholds = float4(scale, faceBias,
                    faceBias - innerWidth, faceBias - innerWidth - outerWidth);
                output.mask = float4(vert.xy * 2 - clampedRect.xy - clampedRect.zw,
                    0.25 / (0.25 * float2(_MaskSoftnessX, _MaskSoftnessY) + pixelSize.xy));
                output.opacity = input.color.a;
                return output;
            }

            fixed4 PixShader(pixel_t input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float distance = tex2D(_MainTex, input.atlasUV).a * input.thresholds.x;
                half faceCoverage = saturate(distance - input.thresholds.y);
                half innerCoverage = saturate(distance - input.thresholds.z);
                half outerCoverage = saturate(distance - input.thresholds.w);

                fixed4 innerColor = _InnerOutlineColor;
                innerColor.rgb *= innerColor.a;
                fixed4 outerColor = _OuterOutlineColor;
                outerColor.rgb *= outerColor.a;

                // Disjoint coverage bands keep the original face intact, even
                // across its antialiased edge. Zero width gives zero ring coverage.
                fixed4 color = input.faceColor * faceCoverage
                    + innerColor * (innerCoverage - faceCoverage)
                    + outerColor * (outerCoverage - innerCoverage);
                color *= input.opacity;

                #if UNITY_UI_CLIP_RECT
                half2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy))
                    * input.mask.zw);
                color *= mask.x * mask.y;
                #endif

                #if UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
