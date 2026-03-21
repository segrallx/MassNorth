#ifndef TOON_LIGHTING_INCLUDED
#define TOON_LIGHTING_INCLUDED

// 2-step toon lighting: bright / shadow
// Returns a value in [0, 1] where 0 = full shadow, 1 = fully lit
half ToonStep(half halfLambert, half shadowAtten, half shadowStep, half shadowSmooth)
{
    half intensity = halfLambert * shadowAtten;
    return smoothstep(shadowStep - shadowSmooth, shadowStep + shadowSmooth, intensity);
}

// Full toon lighting calculation
// Returns final lit color
half3 CalcToonLighting(half3 baseColor, half3 normalWS, half3 lightDir, half3 lightColor,
                        half shadowAtten, half4 shadowColor, half shadowStep, half shadowSmooth)
{
    half NdotL = dot(normalWS, lightDir);
    half halfLambert = NdotL * 0.5 + 0.5;

    half toon = ToonStep(halfLambert, shadowAtten, shadowStep, shadowSmooth);

    half3 litColor = baseColor * lightColor;
    half3 shadColor = baseColor * shadowColor.rgb;

    return lerp(shadColor, litColor, toon);
}

#endif
