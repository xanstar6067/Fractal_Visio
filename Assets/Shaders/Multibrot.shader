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

            // Uniforms of this fractal, folded into the shared constant buffer.
            #define FRACTAL_EXTRA_UNIFORMS float _Power;
            #include "Common/FractalCommon.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                float2 c = FractalPlanePoint(input.uv);

                // Matches MultibrotSamplerD.Bailout and MultibrotDefinition.ClampPower.
                const float bailout = 65536.0;
                int power = clamp((int)round(_Power), 2, 8);

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

                    // z -> z^power + c, by repeated multiplication
                    float2 w = z;
                    [loop]
                    for (int k = 1; k < 8; k++)
                    {
                        if (k >= power)
                        {
                            break;
                        }

                        w = float2(w.x * z.x - w.y * z.y, w.x * z.y + w.y * z.x);
                    }

                    z = w + c;
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

                return FractalEscapeColor(FractalSmoothCountPower(iteration, squared, bailout, power));
            }
            ENDHLSL
        }
    }
}
