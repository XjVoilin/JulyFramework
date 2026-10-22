Shader "Hidden/July/MeshAuthoringPreview"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
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
                float light = .4 + .6 * saturate(dot(normalize(value.normal), normalize(float3(-.4, .7, -.6))));
                return float4(float3(.78, .78, .78) * light, 1);
            }
            ENDHLSL
        }
    }
}
