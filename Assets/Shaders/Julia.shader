Shader "FractalVisio/Julia"
{
    Properties
    {
        _Center ("Center", Vector) = (0, 0, 0, 0)
        _Scale ("Scale", Float) = 3.2
        _Aspect ("Aspect", Float) = 1
        _Rotation ("Rotation", Float) = 0
        _Iterations ("Iterations", Int) = 128
        _JuliaC ("C", Vector) = (-0.8, 0.156, 0, 0)
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

            // The constant C, set by JuliaDefinition.BindMaterial.
            #define FRACTAL_EXTRA_UNIFORMS float4 _JuliaC;
            #include "Common/FractalCommon.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                // The pixel is where the orbit starts; C is the same everywhere.
                float2 z = FractalPlanePoint(input.uv);
                float2 c = _JuliaC.xy;

                // Matches MandelbrotSamplerD.Bailout, which the Julia samplers share.
                const float bailout = 65536.0;

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
