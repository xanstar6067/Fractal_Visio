#ifndef FRACTALVISIO_FRACTAL_COMMON_INCLUDED
#define FRACTALVISIO_FRACTAL_COMMON_INCLUDED

// Everything every fractal shader shares: the fullscreen triangle, the screen-to-plane mapping,
// the colouring and the common uniforms. A fractal shader includes this and writes only its own
// iteration.
//
// A fractal with extra uniforms declares them before the include:
//   #define FRACTAL_EXTRA_UNIFORMS float2 _JuliaC; float _Power;
// and sets them from IFractalDefinition.BindMaterial.
//
// The colouring here must stay in step with Rendering/Coloring/EscapeColorMapper.cs. The backend
// switches under the viewer mid-zoom, so a palette that shifts at the handoff reads as a glitch.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#ifndef FRACTAL_EXTRA_UNIFORMS
#define FRACTAL_EXTRA_UNIFORMS
#endif

TEXTURE2D(_PaletteTex);
SAMPLER(sampler_PaletteTex);

CBUFFER_START(UnityPerMaterial)
float4 _Center;
float _Scale;
float _Aspect;
float _Rotation;
int _Iterations;
float _ColorCycle;
float _ColorOffset;
float _ColorSmooth;
float _ColorLogarithmic;
float4 _InteriorColor;
// Relief (FRACTAL_RELIEF), from Core/Coloring/ReliefLight.cs: sun direction and slope,
// highlight direction and exponent, ambient / 1 over flat light / highlight strength.
float4 _ReliefLight;
float4 _ReliefHalf;
float4 _ReliefTone;
FRACTAL_EXTRA_UNIFORMS
CBUFFER_END

struct Attributes
{
    uint vertexID : SV_VertexID;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
};

Varyings Vert(Attributes input)
{
    Varyings output;
    output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
    output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
    return output;
}

// Screen point to point on the complex plane. This must stay identical to the CPU mapping in
// FractalCpuKernels.Normalize, or the two backends disagree at the fp32 -> fp64 handoff and the
// image jumps when the renderer switches.
float2 FractalPlanePoint(float2 uv)
{
    float2 offset = uv - 0.5;
    float2 d = float2(offset.x * _Aspect, offset.y);
    float sinR, cosR;
    sincos(_Rotation, sinR, cosR);
    d = float2(d.x * cosR - d.y * sinR, d.x * sinR + d.y * cosR);
    return _Center.xy + d * _Scale;
}

int FractalMaxIterations()
{
    return clamp(_Iterations, 1, 2048);
}

#define FRACTAL_INTERIOR_COLOR half4(_InteriorColor.rgb, 1.0)

// Continuous escape count for a power-2 map. Mirrors Core/Rendering/IEscapeSampler.cs
// (EscapeMath.Smooth): 1 at the moment of escape, 2 one iteration later, which is what makes the
// value continuous across the iteration boundary instead of stepping.
float FractalSmoothCount(int iteration, float squaredModulus, float bailout)
{
    if (squaredModulus <= 1.0 || bailout <= 1.0)
    {
        return iteration + 1.0;
    }

    float ratio = log(squaredModulus) / log(bailout);
    if (ratio <= 0.0)
    {
        return iteration + 1.0;
    }

    return iteration + 1.0 - log2(ratio);
}

// The same for a map of degree `power` (z^p + c). Mirrors EscapeMath.Smooth(..., power): the
// logarithm is to base p, or a cubic map shows a step at every iteration boundary.
float FractalSmoothCountPower(int iteration, float squaredModulus, float bailout, float power)
{
    if (squaredModulus <= 1.0 || bailout <= 1.0 || power <= 1.0)
    {
        return iteration + 1.0;
    }

    float ratio = log(squaredModulus) / log(bailout);
    if (ratio <= 0.0)
    {
        return iteration + 1.0;
    }

    return iteration + 1.0 - log(ratio) / log(power);
}

// Escape count onto the palette. `_ColorSmooth` drops the fraction rather than the sampler doing
// it, so the switch stays a recolour on both backends.
half4 FractalEscapeColor(float escapeCount)
{
    float count = lerp(floor(escapeCount), escapeCount, saturate(_ColorSmooth));
    float cycle = max(_ColorCycle, 1.0);
    float linearPosition = count / cycle;
    float logPosition = log(1.0 + max(count, 0.0)) / log(1.0 + cycle);
    float normalized = lerp(linearPosition, logPosition, saturate(_ColorLogarithmic));
    return SAMPLE_TEXTURE2D(_PaletteTex, sampler_PaletteTex, float2(frac(normalized + _ColorOffset), 0.5));
}

// ---- Relief -------------------------------------------------------------------------------------
// A shader that supports it declares
//   #pragma multi_compile_local __ FRACTAL_RELIEF
// carries the orbit's derivative under #if defined(FRACTAL_RELIEF) and returns
// FractalReliefColor(count, FractalSlope...(z, derivative)). See Core/Coloring/ReliefLight.cs.

// Slope from a complex derivative: grad log|z| points along z conj(dz). Mirrors
// EscapeMath.HolomorphicSlope.
float2 FractalSlope(float2 z, float2 dz)
{
    return float2(z.x * dz.x + z.y * dz.y, z.y * dz.x - z.x * dz.y);
}

// Slope from a real Jacobian j = (dx/dcx, dx/dcy, dy/dcx, dy/dcy): J^T z. Mirrors
// EscapeMath.JacobianSlope.
float2 FractalSlopeJacobian(float2 z, float4 j)
{
    return float2(j.x * z.x + j.z * z.y, j.y * z.x + j.w * z.y);
}

// One Jacobian step of the z^2 family, from z before the step. Mirrors
// EscapeMath.QuadraticJacobianStep: rows scaled by a and b, plus `identity` on the diagonal.
float4 FractalJacobianStep(float2 z, float a, float b, float identity, float4 j)
{
    float x2 = 2.0 * z.x;
    float y2 = 2.0 * z.y;
    return float4(
        a * (x2 * j.x - y2 * j.z) + identity,
        a * (x2 * j.y - y2 * j.w),
        b * (y2 * j.x + x2 * j.z),
        b * (y2 * j.y + x2 * j.w) + identity);
}

// The palette colour lit as a relief. Must stay in step with EscapeColorMapper.Shade - the CPU
// path lights its buffer with the same formula, and the backend switches mid-zoom.
half4 FractalReliefColor(float escapeCount, float2 planeSlope)
{
    half4 baseColor = FractalEscapeColor(escapeCount);
    float3 color = baseColor.rgb;

    // Plane slope to screen slope: the view's rotation turned back (ReliefLight.PackScreenSlope).
    float sinR, cosR;
    sincos(_Rotation, sinR, cosR);
    float2 slope = float2(planeSlope.x * cosR + planeSlope.y * sinR, -planeSlope.x * sinR + planeSlope.y * cosR);
    float largest = max(abs(slope.x), abs(slope.y));
    slope = largest > 0.0 ? slope / largest : float2(0.0, 0.0);
    float slopeLength = length(slope);
    slope = slopeLength > 0.0 ? slope / slopeLength : float2(0.0, 0.0);

    float2 n = slope * _ReliefLight.w;
    float inverseLength = rsqrt(dot(n, n) + 1.0);

    float diffuse = max(0.0, (dot(n, _ReliefLight.xy) + _ReliefLight.z) * inverseLength);
    float illumination = (_ReliefTone.x + (1.0 - _ReliefTone.x) * diffuse) * _ReliefTone.y;
    color = illumination <= 1.0 ? color * illumination : lerp(color, 1.0, (illumination - 1.0) / illumination);

    if (_ReliefTone.z > 0.0)
    {
        float facing = max(0.0, (dot(n, _ReliefHalf.xy) + _ReliefHalf.z) * inverseLength);
        color = lerp(color, 1.0, saturate(_ReliefTone.z * pow(facing, _ReliefHalf.w)));
    }

    return half4(color, 1.0);
}

#endif // FRACTALVISIO_FRACTAL_COMMON_INCLUDED
