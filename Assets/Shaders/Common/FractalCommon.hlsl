#ifndef FRACTALVISIO_FRACTAL_COMMON_INCLUDED
#define FRACTALVISIO_FRACTAL_COMMON_INCLUDED

// Everything every fractal shader shares: the fullscreen triangle, the screen-to-plane mapping,
// the palette lookup and the common uniforms. A fractal shader includes this and writes only its
// own iteration.
//
// A fractal with extra uniforms declares them before the include:
//   #define FRACTAL_EXTRA_UNIFORMS float2 _JuliaC; float _Power;
// and sets them from IFractalDefinition.BindMaterial.

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

#define FRACTAL_INTERIOR_COLOR half4(0.012, 0.02, 0.047, 1.0)

half4 FractalEscapeColor(int iteration)
{
    float palettePosition = frac(iteration * 0.021);
    return SAMPLE_TEXTURE2D(_PaletteTex, sampler_PaletteTex, float2(palettePosition, 0.5));
}

#endif // FRACTALVISIO_FRACTAL_COMMON_INCLUDED
