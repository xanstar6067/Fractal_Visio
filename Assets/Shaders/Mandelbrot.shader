Shader "FractalVisio/Mandelbrot"
{
    Properties
    {
        _Center ("Center", Vector) = (-0.5, 0, 0, 0)
        _Scale ("Scale", Float) = 3
        _Aspect ("Aspect", Float) = 1
        _Rotation ("Rotation", Float) = 0
        _Iterations ("Iterations", Int) = 128
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
            #include "Common/FractalCommon.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                float2 c = FractalPlanePoint(input.uv);

                // Main cardioid and period-2 bulb: very cheap on mobile GPUs.
                float cardioidX = c.x - 0.25;
                float y2 = c.y * c.y;
                float q = cardioidX * cardioidX + y2;
                if (q * (q + cardioidX) <= 0.25 * y2 || dot(c + float2(1.0, 0.0), c + float2(1.0, 0.0)) <= 0.0625)
                {
                    return FRACTAL_INTERIOR_COLOR;
                }

                // Bailout well above the 4 that decides membership: the smooth escape count only
                // approximates anything once the orbit is clear of the set. Matches
                // MandelbrotSamplerD.Bailout - the two backends must agree.
                const float bailout = 65536.0;

                float2 z = 0.0;
                int maxIterations = FractalMaxIterations();
                int iteration = 0;
                float squared = 0.0;
                bool escaped = false;

                [loop]
                for (int i = 0; i < 2048; i++)
                {
                    if (i >= maxIterations)
                    {
                        break;
                    }

                    z = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + c;
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

                return FractalEscapeColor(FractalSmoothCount(iteration, squared, bailout));
            }
            ENDHLSL
        }
    }
}
