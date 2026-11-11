Shader "UI/Gradient"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _ColorA ("Start Color", Color) = (1,1,1,1)
        _ColorB ("End Color", Color) = (0,0,0,1)
        _CurvePower ("Curve Power", Range(0.1, 8.0)) = 1.0
        [GradientDirection]
        _Direction ("Direction", Float) = 0

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
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _ColorA;
            fixed4 _ColorB;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            half _CurvePower;
            half _Direction;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            half GetGradientFactor(half2 uv)
            {
                if (_Direction < 0.5h)
                    return uv.x;
                if (_Direction < 1.5h)
                    return 1.0h - uv.x;
                if (_Direction < 2.5h)
                    return uv.y;
                if (_Direction < 3.5h)
                    return 1.0h - uv.y;
                if (_Direction < 4.5h)
                    return (uv.x + uv.y) * 0.5h;
                if (_Direction < 5.5h)
                    return 1.0h - (uv.x + uv.y) * 0.5h;
                if (_Direction < 6.5h)
                    return (uv.x + 1.0h - uv.y) * 0.5h;

                return (1.0h - uv.x + uv.y) * 0.5h;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 textureColor = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                half gradientFactor = saturate(GetGradientFactor(IN.texcoord));
                half curvedGradientFactor = pow(gradientFactor, _CurvePower);
                fixed4 gradientColor = lerp(_ColorA, _ColorB, curvedGradientFactor);
                fixed4 color = textureColor * gradientColor * IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001h);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
