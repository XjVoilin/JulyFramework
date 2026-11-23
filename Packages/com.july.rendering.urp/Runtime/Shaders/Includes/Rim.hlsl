#ifndef JULY_URP_RIM_INCLUDED
#define JULY_URP_RIM_INCLUDED

// Use normalized vectors toward the visible side and the camera. Width is in (0, 1].
half JulyRimMask(half3 normalWS, half3 viewDirectionWS, half width)
{
    half facing = saturate(dot(normalWS, viewDirectionWS));
    return 1.0h - smoothstep(0.0h, width, facing);
}

#endif
