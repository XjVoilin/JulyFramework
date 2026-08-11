// UI/HollowMask
// Alpha-based 镂空遮罩，替代 Mask(Stencil) + InvertedStencil 组合。
//
// 原方案问题：Stencil Buffer 二值化裁切，叠加 ASTC/ETC2 压缩后
// 真机边缘出现严重锯齿甚至块状缺失。
//
// 本 shader 在 fragment 中采样 _MainTex alpha + smoothstep + fwidth
// 做自适应抗锯齿，完全绕过 Stencil Buffer。
//
// 用法：
//   1. BlackFill Image.sprite 设为遮罩形状纹理（鹅头等），
//      UGUI 自动绑定到 _MainTex，无需手动设置纹理
//   2. Material 上设 _UVScale = childSize / parentSize
//   3. overlay 颜色来自 Image.color（vertex color），不使用纹理 RGB
//   4. 纹理不透明处 = 镂空洞口，与 Mask + InvertedStencil 语义一致
Shader "UI/HollowMask"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Softness ("Edge Softness", Range(0.001, 0.15)) = 0.02
        _UVScale ("UV Scale (Child/Parent)", Vector) = (1, 1, 0, 0)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 0
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

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
            "CanUseSpriteAtlas" = "True"
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
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _Softness;
            float4 _UVScale;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);

                #ifdef UNITY_COLORSPACE_GAMMA
                OUT.color = v.color;
                #else
                half4 c = clamp(v.color, 0, 1);
                OUT.color = half4(GammaToLinearSpace(c.rgb), c.a);
                #endif
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 maskUV = (IN.texcoord - 0.5) * _UVScale.xy + 0.5;

                half inBounds = step(0, maskUV.x) * step(maskUV.x, 1)
                              * step(0, maskUV.y) * step(maskUV.y, 1);

                half texAlpha = tex2D(_MainTex, maskUV).a;
                half fw = max(fwidth(texAlpha) * 0.5, _Softness);
                texAlpha = smoothstep(0.5 - fw, 0.5 + fw, texAlpha);

                half overlayAlpha = 1.0 - texAlpha * inBounds;

                half4 color = IN.color;
                color.a *= overlayAlpha;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
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
