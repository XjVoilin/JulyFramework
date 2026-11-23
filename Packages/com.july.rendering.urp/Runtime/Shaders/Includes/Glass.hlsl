#ifndef JULY_URP_GLASS_INCLUDED
#define JULY_URP_GLASS_INCLUDED

// The caller supplies masked base opacity and the reflection already used for RGB.
half JulyGlassOpacity(half baseOpacity, half3 reflection, half reflectionOpacity)
{
    half reflectionPeak = max(reflection.r, max(reflection.g, reflection.b));
    return saturate(baseOpacity + saturate(reflectionPeak) * reflectionOpacity);
}

#endif
