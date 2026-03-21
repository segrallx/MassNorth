#ifndef SOLDIER_ANIMATION_INCLUDED
#define SOLDIER_ANIMATION_INCLUDED

// Shared vertex animation logic for soldier instancing.
// Used by both ForwardLit and ShadowCaster passes.

void ApplySoldierAnimation(inout float3 pos, float swingTag, float phase, float animSpeed,
                           float swingSpeed, float swingAngle)
{
    float t = _Time.y * swingSpeed * animSpeed + phase;

    // --- Body bounce & sway (applied to ALL vertices) ---
    // Vertical bounce: abs(sin) gives a "hop" feel
    pos.y += abs(sin(t)) * 0.03;
    // Forward/back lean: ~3 degrees
    float leanAngle = sin(t) * 0.0523; // radians(3)
    float cosLean = cos(leanAngle);
    float sinLean = sin(leanAngle);
    float tmpY = pos.y * cosLean - pos.z * sinLean;
    float tmpZ = pos.y * sinLean + pos.z * cosLean;
    pos.y = tmpY;
    pos.z = tmpZ;
    // Left/right weight shift
    pos.x += sin(t * 2.0) * 0.01;

    // --- Limb swing (only for tagged vertices) ---
    if (swingTag > 0.01)
    {
        // Direction: 0.25 => forward, 0.75 => backward (opposite phase)
        float dir = swingTag < 0.5 ? 1.0 : -1.0;

        // Snappy swing: sign(sin) * pow(abs(sin), 0.7) for staccato feel
        float rawSin = sin(t);
        float snappy = sign(rawSin) * pow(abs(rawSin), 0.7);
        float angle = snappy * radians(swingAngle) * dir;

        // Rotate around X axis, pivot at limb top (shoulder/hip)
        float pivotY = 0.11; // approximate shoulder/hip height for Q-style proportions
        float relY = pos.y - pivotY;
        float cosA = cos(angle);
        float sinA = sin(angle);
        pos.y = pivotY + relY * cosA - pos.z * sinA;
        pos.z = relY * sinA + pos.z * cosA;
    }
}

#endif // SOLDIER_ANIMATION_INCLUDED
