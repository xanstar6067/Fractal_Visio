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

#endif // FRACTALVISIO_FRACTAL_COMMON_INCLUDED
