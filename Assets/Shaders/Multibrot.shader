Shader "FractalVisio/Multibrot"
{
    Properties
    {
        _Center ("Center", Vector) = (0, 0, 0, 0)
        _Scale ("Scale", Float) = 2.8
        _Aspect ("Aspect", Float) = 1
        _Rotation ("Rotation", Float) = 0
        _Iterations ("Iterations", Int) = 128
        _Power ("Power", Float) = 3
        _JuliaC ("C, Julia", Vector) = (0, 0, 0, 0)
        _PaletteTex ("Palette", 2D) = "white" {}
        _ColorCycle ("Iterations per palette sweep", Float) = 48
        _ColorOffset ("Palette offset", Float) = 0
        _ColorSmooth ("Smooth colouring", Float) = 1
        _ColorLogarithmic ("Logarithmic spread", Float) = 1
        _InteriorColor ("Interior", Color) = (0.012, 0.02, 0.047, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Overlay" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local __ FRACTAL_RELIEF

            // Uniforms of this fractal, folded into the shared constant buffer. _JuliaC.z set makes
            // it the Multijulia: z starts at the pixel and c is _JuliaC.xy.
            #define FRACTAL_EXTRA_UNIFORMS float _Power; float4 _JuliaC;
            #include "Common/FractalCommon.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                float2 pixel = FractalPlanePoint(input.uv);
                bool julia = _JuliaC.z > 0.5;
                float2 c = julia ? _JuliaC.xy : pixel;

                // Matches MultibrotSamplerD.Bailout and MultibrotDefinition.ClampPower.
                const float bailout = 65536.0;
                int power = clamp((int)round(_Power), 2, 8);

                float2 z = julia ? pixel : 0.0;
                int maxIterations = FractalMaxIterations();
                int iteration = 0;
                float squared = 0.0;
                bool escaped = false;

#if defined(FRACTAL_RELIEF)
                // dz/dc, or dz/dz_0 for a Julia set.
                float2 dz = julia ? float2(1.0, 0.0) : 0.0;
                float2 dc = julia ? 0.0 : float2(1.0, 0.0);
#endif

                [loop]
                for (int i = 0; i < 2048; i++)
                {
                    if (i >= maxIterations)
                    {
                        break;
                    }

                    // z -> z^power + c, by repeated multiplication: w = z^(power-1) first, which the
                    // relief's derivative p z^(p-1) dz + 1 needs too.
                    float2 w = float2(1.0, 0.0);
                    [loop]
                    for (int k = 1; k < 8; k++)
                    {
                        if (k >= power)
                        {
                            break;
                        }

                        w = float2(w.x * z.x - w.y * z.y, w.x * z.y + w.y * z.x);
                    }

#if defined(FRACTAL_RELIEF)
                    dz = power * float2(w.x * dz.x - w.y * dz.y, w.x * dz.y + w.y * dz.x) + dc;
#endif
                    z = float2(w.x * z.x - w.y * z.y, w.x * z.y + w.y * z.x) + c;
                    iteration = i + 1;
                    squared = dot(z, z);
                    if (squared > bailout)
                    {
                        escaped = true;
                        break;
                    }
                }

                if (!escaped)
                {
                    return FRACTAL_INTERIOR_COLOR;
                }

#if defined(FRACTAL_RELIEF)
                return FractalReliefColor(FractalSmoothCountPower(iteration, squared, bailout, power), FractalSlope(z, dz));
#else
                return FractalEscapeColor(FractalSmoothCountPower(iteration, squared, bailout, power));
#endif
            }
            ENDHLSL
        }
    }
}
