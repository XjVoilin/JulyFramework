Shader "Hidden/July/Self Shadow UV Bake"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
        struct V { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; };
        V Vert(A i) {
            V o; o.world=TransformObjectToWorld(i.positionOS.xyz); o.positionCS=float4(i.uv*2-1,0,1);
            #if UNITY_UV_STARTS_AT_TOP
                o.positionCS.y=-o.positionCS.y;
            #endif
            return o;
        }
        half4 Frag(V i):SV_Target { half s=MainLightRealtimeShadow(TransformWorldToShadowCoord(i.world));return half4(s,s,s,1); }
        ENDHLSL
        Pass
        {
            Cull Off ZWrite Off ZTest Always Blend One One BlendOp Min
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            ENDHLSL
        }
        Pass
        {
            Cull Off ZWrite Off ZTest Always Blend One One BlendOp Max
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            ENDHLSL
        }
    }
}
