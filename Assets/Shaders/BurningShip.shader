Shader "FractalVisio/BurningShip"
{
    Properties
    {
        _Center ("Center", Vector) = (-0.4, -0.5, 0, 0)
        _Scale ("Scale", Float) = 3
        _Aspect ("Aspect", Float) = 1
        _Rotation ("Rotation", Float) = 0
        _Iterations ("Iterations", Int) = 128
        _Bailout ("Bailout", Float) = 4
        _PaletteTex ("Palette", 2D) = "white" {}
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

            // Uniforms of this fractal, folded into the shared constant buffer.
            #define FRACTAL_EXTRA_UNIFORMS float _Bailout;
            #include "Common/FractalCommon.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                float2 c = FractalPlanePoint(input.uv);

                float2 z = 0.0;
                int maxIterations = FractalMaxIterations();
                float bailout = max(_Bailout, 4.0);
                int iteration = 0;
                bool escaped = false;

                [loop]
                for (int i = 0; i < 2048; i++)
                {
                    if (i >= maxIterations)
                    {
                        break;
                    }

                    // z -> (|Re z| + i|Im z|)^2 + c
                    z = float2(z.x * z.x - z.y * z.y, 2.0 * abs(z.x * z.y)) + c;
                    iteration = i + 1;
                    if (dot(z, z) > bailout)
                    {
                        escaped = true;
                        break;
                    }
                }

                if (!escaped)
                {
                    return FRACTAL_INTERIOR_COLOR;
                }

                return FractalEscapeColor(iteration);
            }
            ENDHLSL
        }
    }
}
