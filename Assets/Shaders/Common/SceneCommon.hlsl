#ifndef FRACTALVISIO_SCENE_COMMON_INCLUDED
#define FRACTALVISIO_SCENE_COMMON_INCLUDED

// Everything a 3D scene's shader shares before its own distance estimate: the uniforms, the
// fullscreen triangle, colour space and the palette. The shader then writes
//
//   float SceneMap(float3 p, out float4 trap, inout float stepScale)
//
// and includes SceneMarch.hlsl, which marches the rays, lights and colours the surface. A scene
// with extra uniforms declares them before this include:
//   #define SCENE_EXTRA_UNIFORMS float4 _JuliaC;
// and sets them from ISceneDefinition.BindMaterial, as _Shape and _SceneIterations are.
//
// Ported from the WPF version's Fractal3DShader (Direct3D 11). Lighting is computed in linear
// colour and written as sRGB, as there: the project renders in gamma space, so what the shader
// returns is what the screen shows.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#ifndef SCENE_EXTRA_UNIFORMS
#define SCENE_EXTRA_UNIFORMS
#endif

// The frame this target is cut from, and the band of it this draw marches. Every other row is
// copied from the previous frame, so a full frame can be built a few bands per screen frame
// without a draw that runs long enough for a phone's driver to call the GPU hung.
TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);
TEXTURE2D(_PaletteTex);
SAMPLER(sampler_PaletteTex);

CBUFFER_START(UnityPerMaterial)
float4 _Frame;          // xy: size of the whole image in pixels
float4 _Window;         // xy: where this target starts in the whole image (pixels, from bottom-left); zw: its size
float4 _Band;           // x, y: first and end row of this target marched now (pixels from the bottom)
float4 _CameraPosition; // xyz: camera; w: field-of-view scale, 1 / tan(fov / 2)
float4 _CameraRight;
float4 _CameraUp;
float4 _CameraForward;
float4 _March;          // x: steps, y: detail (pixel tolerance), z: trace distance, w: bounding radius
float4 _LightDir;       // xyz: towards the light; w: highlight strength
float4 _Look;           // x: colouring source; y: shadow sharpness, 0 = none; z: occlusion on; w: ambient
float4 _Style;          // x: shading style; y: effect strength; z: sky in the ambient light; w: occlusion strength
float4 _ColorMap;       // x: palette sweeps per unit; y: palette offset
float4 _View;           // x: scale of the view against the default one, from the CPU (see ViewScale in WPF)
float4 _Surface;        // material colour, linear
float4 _SkyTop;         // linear
float4 _SkyBottom;      // linear
float4 _LightColor;     // linear
float4 _Shape;          // the scene's own: power, escape radius, ...
float _SceneIterations; // the scene's iteration count, for colouring by iteration
SCENE_EXTRA_UNIFORMS
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

float3 SceneSrgbToLinear(float3 color)
{
    float3 low = color / 12.92;
    float3 high = pow(max((color + 0.055) / 1.055, 1e-8), 2.4);
    return lerp(low, high, step(0.04045, color));
}

float3 SceneLinearToSrgb(float3 color)
{
    color = saturate(color);
    float3 low = color * 12.92;
    float3 high = 1.055 * pow(max(color, 1e-8), 1.0 / 2.4) - 0.055;
    return lerp(low, high, step(0.0031308, color));
}

// The session's palette, a ring of 256 colours: positions wrap round it, as on the plane.
float3 ScenePalette(float value)
{
    float3 srgb = SAMPLE_TEXTURE2D_LOD(_PaletteTex, sampler_PaletteTex, float2(frac(value), 0.5), 0).rgb;
    return SceneSrgbToLinear(srgb);
}

float3 SceneSky(float3 direction)
{
    return lerp(_SkyBottom.rgb, _SkyTop.rgb, saturate(direction.y * 0.5 + 0.5));
}

float SceneMinAxis(float3 v)
{
    float3 a = abs(v);
    return min(a.x, min(a.y, a.z));
}

#endif
