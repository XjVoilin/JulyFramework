Shader "Hidden/July/URP/Depth Reference"
{
    Properties { _DepthDelta("Controlled depth difference", Float) = 0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite On ZTest Always ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float _DepthDelta;
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(float3 positionOS : POSITION)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(positionOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }
            float4 Frag(Varyings input, out float depth : SV_Depth) : SV_Target
            {
                // Test-only: one half has the rounding difference, the other remains identical.
                float delta = input.positionCS.x >= 32 ? _DepthDelta : 0;
                #if UNITY_REVERSED_Z
                    depth = input.positionCS.z + delta;
                #else
                    depth = input.positionCS.z - delta;
                #endif
                return 0;
            }
            ENDHLSL
        }
    }
}
