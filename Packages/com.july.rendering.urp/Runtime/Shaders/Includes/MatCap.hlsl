#ifndef JULY_URP_MATCAP_INCLUDED
#define JULY_URP_MATCAP_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// normalWS is normalized. Material UV scale and offset are applied by the caller.
float2 JulyMatCapUV(float3 normalWS)
{
    float2 viewXY = UNITY_MATRIX_V._m01_m11 * normalWS.y;
    viewXY = UNITY_MATRIX_V._m00_m10 * normalWS.x + viewXY;
    viewXY = UNITY_MATRIX_V._m02_m12 * normalWS.z + viewXY;
    return viewXY * 0.5 + 0.5;
}

// Both inputs are in [0, 1]. The caller supplies the lighting model's light amount.
float JulyMatCapLightAttenuation(float lightAmount, float darkRetention)
{
    // Interpolate from the fully lit endpoint to preserve the original evaluation order.
    return (1.0 - darkRetention) * (lightAmount - 1.0) + 1.0;
}

#endif
