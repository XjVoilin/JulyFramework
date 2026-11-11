// UI 圆角矩形软裁切 Shader。
// 使用片元距离场生成平滑边缘，可以在矩形、小圆角、大圆角和最大圆角之间连续变化。
Shader "UI/RoundedRect"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Roundness ("Roundness", Range(0.0, 1.0)) = 1.0
        _Inset ("Shape Inset", Range(0.0, 0.49)) = 0.0
        _EdgeSoftness ("Edge Softness", Range(0.5, 2.0)) = 1.0

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
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _Roundness;
            float _Inset;
            float _EdgeSoftness;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            float2 GetRenderedSize(float2 uv)
            {
                float uPerPixel = length(float2(ddx(uv.x), ddy(uv.x)));
                float vPerPixel = length(float2(ddx(uv.y), ddy(uv.y)));
                return 1.0 / max(float2(uPerPixel, vPerPixel), float2(0.0001, 0.0001));
            }

            float GetRoundedRectDistance(float2 uv)
            {
                float2 renderedSize = GetRenderedSize(uv);
                float shorterSide = min(renderedSize.x, renderedSize.y);
                float inset = _Inset * shorterSide;
                float2 halfSize = max(renderedSize * 0.5 - inset, float2(0.0001, 0.0001));
                float cornerRadius = saturate(_Roundness) * min(halfSize.x, halfSize.y);

                float2 position = abs(uv - 0.5) * renderedSize;
                float2 cornerDistance = position - (halfSize - cornerRadius);
                return length(max(cornerDistance, 0.0))
                       + min(max(cornerDistance.x, cornerDistance.y), 0.0)
                       - cornerRadius;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                float shapeDistance = GetRoundedRectDistance(IN.texcoord);
                float edgeWidth = max(fwidth(shapeDistance) * _EdgeSoftness, 0.0001);
                float shapeAlpha = 1.0 - smoothstep(-edgeWidth, edgeWidth, shapeDistance);
                color.a *= shapeAlpha;

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
