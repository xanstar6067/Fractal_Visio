Shader "FractalVisio/Hybrid"
{
    // A hybrid (Fractals/Hybrid): the folded step of Folded.shader, with the formula changing from
    // one iteration to the next. Each phase of the cycle has the five vectors Folded.shader takes for
    // one formula, as arrays set by HybridShader.Bind; _HybridCycle.x is the cycle's length. The step
    // must stay the same expression as FoldedFormula.Step and StepJacobian.
    Properties
    {
        _Center ("Center", Vector) = (0, 0, 0, 0)
        _Scale ("Scale", Float) = 3
        _Aspect ("Aspect", Float) = 1
        _Rotation ("Rotation", Float) = 0
        _Iterations ("Iterations", Int) = 128
        _HybridCycle ("Cycle length", Vector) = (1, 0, 0, 0)
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

            // 16 = HybridShader.Slots = HybridSequence.MaximumCycle.
            #define FRACTAL_EXTRA_UNIFORMS float4 _HybridPolyA[16]; float4 _HybridPolyC[16]; float4 _HybridTail[16]; float4 _HybridFlags[16]; float4 _HybridForm[16]; float4 _HybridCycle; float4 _JuliaC;
            #include "Common/FractalCommon.hlsl"

            // A and C of phase k at (x, y): .x = A, .y = C.
            float2 HybridPolynomials(int k, float x2, float y2)
            {
                float4 m = float4(x2 * x2, x2 * y2, y2 * y2, x2);
                float4 tail = _HybridTail[k];
                return float2(dot(_HybridPolyA[k], m) + tail.x * y2 + tail.y,
                              dot(_HybridPolyC[k], m) + tail.z * y2 + tail.w);
            }

            // Phase k's z^p with its folds, without the constant.
            float2 HybridStep(int k, float2 z)
            {
                float x2 = z.x * z.x;
                float y2 = z.y * z.y;
                float2 ac = HybridPolynomials(k, x2, y2);
                float4 form = _HybridForm[k];

                float4 values = float4(z.x, z.y, ac.x, ac.y);
                float4 folded = lerp(values, abs(values), _HybridFlags[k]);

                bool odd = form.x > 0.5;
                float p = (odd ? folded.x : 1.0) * folded.z;
                float q = (odd ? folded.y : folded.x * folded.y) * folded.w;
                return form.z > 0.5 ? float2(q, form.y * p) : float2(p, form.y * q);
            }

#if defined(FRACTAL_RELIEF)
            // Phase k's Jacobian (d re/dx, d re/dy, d im/dx, d im/dy), as FoldedFormula.StepJacobian.
            float4 HybridStepJacobian(int k, float2 z)
            {
                float x = z.x;
                float y = z.y;
                float x2 = x * x;
                float y2 = y * y;
                float2 ac = HybridPolynomials(k, x2, y2);
                float4 polyA = _HybridPolyA[k];
                float4 polyC = _HybridPolyC[k];
                float4 tail = _HybridTail[k];
                float4 form = _HybridForm[k];

                float4 values = float4(x, y, ac.x, ac.y);
                float4 signs = lerp(1.0, values < 0.0 ? -1.0 : 1.0, _HybridFlags[k]);
                float4 folded = signs * values;

                float2 gradA = signs.z * float2(
                    4.0 * polyA.x * x * x2 + 2.0 * polyA.y * x * y2 + 2.0 * polyA.w * x,
                    2.0 * polyA.y * x2 * y + 4.0 * polyA.z * y * y2 + 2.0 * tail.x * y);
                float2 gradC = signs.w * float2(
                    4.0 * polyC.x * x * x2 + 2.0 * polyC.y * x * y2 + 2.0 * polyC.w * x,
                    2.0 * polyC.y * x2 * y + 4.0 * polyC.z * y * y2 + 2.0 * tail.z * y);

                float2 gradP;
                float2 gradQ;
                if (form.x > 0.5)
                {
                    gradP = float2(signs.x * folded.z, 0.0) + folded.x * gradA;
                    gradQ = float2(0.0, signs.y * folded.w) + folded.y * gradC;
                }
                else
                {
                    gradP = gradA;
                    gradQ = float2(signs.x * folded.y, folded.x * signs.y) * folded.w + folded.x * folded.y * gradC;
                }

                return form.z > 0.5
                    ? float4(gradQ, form.y * gradP)
                    : float4(gradP, form.y * gradQ);
            }
#endif

            half4 Frag(Varyings input) : SV_Target
            {
                float2 pixel = FractalPlanePoint(input.uv);
                bool julia = _JuliaC.z > 0.5;
                float2 z = julia ? pixel : 0.0;
                float2 c = julia ? _JuliaC.xy : pixel;

                // Matches MandelbrotSamplerD.Bailout - the two backends must agree.
                const float bailout = 65536.0;

                int maxIterations = FractalMaxIterations();
                int cycle = clamp((int)(_HybridCycle.x + 0.5), 1, 16);
                int phase = 0;
                int iteration = 0;
                float squared = 0.0;
                float degree = 2.0;
                bool escaped = false;

#if defined(FRACTAL_RELIEF)
                // With respect to c (J_0 = 0, plus I per step) or to the start (J_0 = I).
                float identity = julia ? 0.0 : 1.0;
                float4 j = julia ? float4(1.0, 0.0, 0.0, 1.0) : 0.0;
#endif

                [loop]
                for (int i = 0; i < 2048; i++)
                {
                    if (i >= maxIterations)
                    {
                        break;
                    }

#if defined(FRACTAL_RELIEF)
                    float4 s = HybridStepJacobian(phase, z);
                    j = float4(
                        s.x * j.x + s.y * j.z + identity,
                        s.x * j.y + s.y * j.w,
                        s.z * j.x + s.w * j.z,
                        s.z * j.y + s.w * j.w + identity);
#endif
                    z = HybridStep(phase, z) + c;

                    // The degree of the step taken: what the smooth count of an escape on it uses.
                    degree = _HybridForm[phase].w;
                    phase = phase + 1 >= cycle ? 0 : phase + 1;
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
