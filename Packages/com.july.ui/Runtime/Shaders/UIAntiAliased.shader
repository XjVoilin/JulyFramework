// Image 材质选择 JulyGF/UI/AntiAliased，无需额外组件或 Canvas 顶点通道。
// 合并屏幕像素内的 2x2 纹理采样和矩形边缘覆盖率，按预乘 Alpha 混合。
// 矩形边缘向内约一个屏幕像素过渡，不扩展网格、不对图片 Alpha 做阈值重建。
// 形状坐标使用独立图片的完整 0~1 UV；不用于图集子区域、Filled 或 Tight Mesh。
Shader "JulyGF/UI/AntiAliased"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _AAStrength ("Anti-aliasing Strength", Range(0.0, 2.0)) = 1.0

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "False"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 mask : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float _AAStrength;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;

                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                    input.color.rgb = UIGammaToLinear(input.color.rgb);
                output.color = input.color * _Color;

                float2 pixelSize = output.vertex.w;
                pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                output.mask = float4(
                    input.vertex.xy * 2 - clampedRect.xy - clampedRect.zw,
                    0.25 / (0.25 * float2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize)));
                return output;
            }

            half4 SamplePremultiplied(float2 uv)
            {
                half4 color = tex2D(_MainTex, saturate(uv)) + _TextureSampleAdd;
                color.rgb *= color.a;
                return color;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                // 四个子像素的中心，随实际屏幕缩放与旋转自动换算到纹理坐标。
                float2 uvDx = ddx(input.texcoord);
                float2 uvDy = ddy(input.texcoord);
                float2 dx = uvDx * (0.25 * _AAStrength);
                float2 dy = uvDy * (0.25 * _AAStrength);
                half4 color = (
                    SamplePremultiplied(input.texcoord - dx - dy) +
                    SamplePremultiplied(input.texcoord + dx - dy) +
                    SamplePremultiplied(input.texcoord - dx + dy) +
                    SamplePremultiplied(input.texcoord + dx + dy)) * 0.25;

                color.rgb *= input.color.rgb * input.color.a;
                color.a *= input.color.a;

                // 在网格硬边以内计算连续覆盖率，纯色图片也能得到边缘过渡。
                // 使用梯度长度保持屏幕像素尺度，不采用按图片尺寸百分比的内缩。
                float2 edgeWidth = max(sqrt(uvDx * uvDx + uvDy * uvDy) * _AAStrength, float2(1e-6, 1e-6));
                float2 edgeDistance = min(input.texcoord, 1.0 - input.texcoord);
                float2 coverage = smoothstep(0.0, edgeWidth, edgeDistance);
                color *= coverage.x * coverage.y;

                #ifdef UNITY_UI_CLIP_RECT
                half2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
                color *= mask.x * mask.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
