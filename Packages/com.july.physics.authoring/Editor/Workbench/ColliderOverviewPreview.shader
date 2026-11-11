Shader "Hidden/July/ColliderOverviewPreview"
{
    Properties
    {
        _Color ("Color", Color) = (.8, .8, .8, 1)
        _ZTest ("Depth test", Float) = 4
        _Shading ("Shading", Float) = 1
        _ZWrite ("Depth write", Float) = 1
        _ColorMask ("Color mask", Float) = 15
        _OffsetFactor ("Offset factor", Float) = 0
        _OffsetUnits ("Offset units", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Cull Off
            Offset [_OffsetFactor], [_OffsetUnits]
            ZTest [_ZTest]
            ZWrite [_ZWrite]
            ColorMask [_ColorMask]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color;
            float _Shading;
            struct Input { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct Output { float4 position : SV_POSITION; float3 normal : TEXCOORD0; };
            Output vert(Input value)
            {
                Output output;
                output.position = UnityObjectToClipPos(value.vertex);
                output.normal = UnityObjectToWorldNormal(value.normal);
                return output;
            }
            float4 frag(Output value) : SV_Target
            {
                float3 normal = value.normal * rsqrt(max(dot(value.normal, value.normal), 1e-12));
                float light = .5 + .5 * abs(dot(normal, normalize(float3(-.4, .7, -.6))));
                return float4(_Color.rgb * lerp(1, light, _Shading), _Color.a);
            }
            ENDHLSL
        }
    }
}
