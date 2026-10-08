Shader "FractalVisio/Simonobrot"
{
    // z -> z^p |z|^p + c, the plane and its Julia sets (_JuliaC.z). Mirrors SimonobrotStep and
    // SimonobrotSamplerD; the inversion negates c's real part.
    Properties
    {
        _Center ("Center", Vector) = (0, 0, 0, 0)
        _Scale ("Scale", Float) = 3
        _Aspect ("Aspect", Float) = 1
        _Rotation ("Rotation", Float) = 0
        _Iterations ("Iterations", Int) = 128
        _Power ("Power", Float) = 2
        _Inversion ("Inversion", Float) = 0
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

            #define FRACTAL_EXTRA_UNIFORMS float _Power; float _Inversion; float4 _JuliaC;
            #include "Common/FractalCommon.hlsl"

            // z^n for n = 0..8, by repeated multiplication.
            float2 SimonobrotPower(float2 z, int n)
            {
                float2 w = float2(1.0, 0.0);
                [loop]
                for (int k = 0; k < 8; k++)
                {
                    if (k >= n)
                    {
                        break;
                    }

                    w = float2(w.x * z.x - w.y * z.y, w.x * z.y + w.y * z.x);
                }

                return w;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 pixel = FractalPlanePoint(input.uv);
                bool julia = _JuliaC.z > 0.5;
                float inversion = _Inversion > 0.5 ? -1.0 : 1.0;
                float2 z = julia ? pixel : 0.0;
                float2 c = julia ? _JuliaC.xy : pixel;
                c.x *= inversion;

                // Matches MandelbrotSamplerD.Bailout and SimonobrotStep.ClampPower / SmoothDegree.
                const float bailout = 65536.0;
                int power = clamp((int)round(_Power), -8, 8);
                int n = abs(power);
                float degree = power >= 1 ? 2.0 * power : 2.0;

                int maxIterations = FractalMaxIterations();
                int iteration = 0;
                float squared = 0.0;
                bool escaped = false;

#if defined(FRACTAL_RELIEF)
                float4 j = julia ? float4(1.0, 0.0, 0.0, 1.0) : 0.0;
                float2 add = julia ? 0.0 : float2(inversion, 1.0);
#endif

                [loop]
                for (int i = 0; i < 2048; i++)
                {
                    if (i >= maxIterations)
                    {
                        break;
                    }

                    float r2 = dot(z, z);
                    float2 next = 0.0;
                    if (r2 > 0.0)
                    {
                        float radius = sqrt(r2);
                        float2 zp = SimonobrotPower(z, n);
                        float rp = pow(radius, (float)n);
                        if (power < 0)
                        {
                            zp = float2(zp.x, -zp.y) / dot(zp, zp);
                            rp = 1.0 / rp;
                        }

#if defined(FRACTAL_RELIEF)
                        // r^p d(z^p) + z^p grad(r^p): SimonobrotStep.Jacobian.
                        float2 a = power * float2(zp.x * z.x + zp.y * z.y, zp.y * z.x - zp.x * z.y) / r2;
                        float g = power * rp / r2;
                        float4 s = float4(
                            rp * a.x + zp.x * g * z.x,
                            -rp * a.y + zp.x * g * z.y,
                            rp * a.y + zp.y * g * z.x,
                            rp * a.x + zp.y * g * z.y);
                        j = float4(
                            s.x * j.x + s.y * j.z,
                            s.x * j.y + s.y * j.w,
                            s.z * j.x + s.w * j.z,
                            s.z * j.y + s.w * j.w);
#endif
                        next = zp * rp;
                    }
#if defined(FRACTAL_RELIEF)
                    else
                    {
                        j = 0.0;
                    }

                    j.x += add.x;
                    j.w += add.y;
#endif

                    z = next + c;
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

                float count = FractalSmoothCountPower(iteration, squared, bailout, degree);
#if defined(FRACTAL_RELIEF)
                return FractalReliefColor(count, FractalSlopeJacobian(z, j));
#else
                return FractalEscapeColor(count);
#endif
            }
            ENDHLSL
        }
    }
}
