Shader "FractalVisio/Mandelbulb"
{
    Properties
    {
        _MainTex ("Previous frame", 2D) = "black" {}
        _PaletteTex ("Palette", 2D) = "white" {}
        _Shape ("Power, escape radius", Vector) = (8, 4, 0, 0)
        _SceneIterations ("Iterations", Float) = 9
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
            #include "Common/SceneCommon.hlsl"

            // Least step scale near the poles. The product of the corrections over every iteration
            // soon gets tiny, and then a ray spends all its steps without reaching the surface.
            #define BULB_STEP_FLOOR 0.125

            // The bulb's distance estimate - the same as MandelbulbDefinition.Distance on the CPU,
            // which finds the point under the finger with it.
            float SceneMap(float3 p, out float4 trap, inout float stepScale)
            {
                float power = _Shape.x;
                float bailout = _Shape.y;
                int iterations = (int)_SceneIterations;
                float trapRadius = 1e20;
                float trapAxis = 1e20;
                float trapIndex = 0.0;
                stepScale = 1.0;

                float3 z = p;
                float dr = 1.0;
                float r = length(z);
                [loop]
                for (int i = 0; i < iterations; i++)
                {
                    trapIndex = (float)i;
                    r = length(z);

                    // The radius itself, not its square: the WPF version coloured the bulb by the
                    // square root of the least radius, and the root below gives back exactly that.
                    trapRadius = min(trapRadius, r);
                    trapAxis = min(trapAxis, SceneMinAxis(z));
                    if (r > bailout)
                    {
                        break;
                    }

                    float invR = 1.0 / max(r, 1e-12);
                    float theta = acos(clamp(z.z * invR, -1.0, 1.0));
                    float phi = atan2(z.y, z.x);

                    // Along a parallel the power stretches by sin(n theta) / sin(theta), near a pole
                    // almost n - more than along the radius, which is all the estimate counts. There
                    // it runs high, and the ray steps short.
                    float sinTheta0 = abs(sin(theta));
                    float stretch = sinTheta0 > 1e-4 ? abs(sin(theta * power)) / sinTheta0 : abs(power);
                    stepScale /= max(stretch, 1.0);
                    dr = pow(r, power - 1.0) * power * dr + 1.0;

                    float zr = pow(r, power);
                    theta *= power;
                    phi *= power;
                    float sinTheta = sin(theta);
                    z = zr * float3(sinTheta * cos(phi), sinTheta * sin(phi), cos(theta)) + p;
                }

                r = length(z);
                trap = float4(sqrt(trapRadius), trapAxis, trapIndex, r);
                stepScale = max(stepScale, BULB_STEP_FLOOR);
                return 0.5 * log(max(r, 1.000001)) * r / max(dr, 1e-9);
            }

            #include "Common/SceneMarch.hlsl"
            ENDHLSL
        }
    }
}
