Shader "FractalVisio/Folded"
{
    // Every member of the folded polynomial family (Fractals/Folded/FoldedFormula.cs), on its
    // parameter plane or as a Julia set: the formula arrives as uniforms, set by FoldedShader.Bind.
    // The step here must stay the same expression as FoldedFormula.Step and StepJacobian.
    Properties
    {
        _Center ("Center", Vector) = (0, 0, 0, 0)
        _Scale ("Scale", Float) = 3
        _Aspect ("Aspect", Float) = 1
        _Rotation ("Rotation", Float) = 0
        _Iterations ("Iterations", Int) = 128
        _FoldPolyA ("A over x4, x2y2, y4, x2", Vector) = (0, 0, 0, 1)
        _FoldPolyC ("C over x4, x2y2, y4, x2", Vector) = (0, 0, 0, 0)
        _FoldTail ("A y2, A 1, C y2, C 1", Vector) = (-1, 0, 0, 1)
        _FoldFlags ("Folds of x, y, A, C", Vector) = (0, 0, 0, 0)
        _FoldForm ("Odd, imaginary factor, swap, degree", Vector) = (0, 2, 0, 2)
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

            #define FRACTAL_EXTRA_UNIFORMS float4 _FoldPolyA; float4 _FoldPolyC; float4 _FoldTail; float4 _FoldFlags; float4 _FoldForm; float4 _JuliaC;
            #include "Common/FractalCommon.hlsl"

            // A and C at (x, y): .x = A, .y = C.
            float2 FoldedPolynomials(float x2, float y2)
            {
                float4 m = float4(x2 * x2, x2 * y2, y2 * y2, x2);
                return float2(dot(_FoldPolyA, m) + _FoldTail.x * y2 + _FoldTail.y,
                              dot(_FoldPolyC, m) + _FoldTail.z * y2 + _FoldTail.w);
            }

            // z^p with the folds, without the constant.
            float2 FoldedStep(float2 z)
            {
                float x2 = z.x * z.x;
                float y2 = z.y * z.y;
                float2 ac = FoldedPolynomials(x2, y2);

                // Fold factor by factor: |v| where the flag is set.
                float4 values = float4(z.x, z.y, ac.x, ac.y);
                float4 folded = lerp(values, abs(values), _FoldFlags);

                bool odd = _FoldForm.x > 0.5;
                float p = (odd ? folded.x : 1.0) * folded.z;
                float q = (odd ? folded.y : folded.x * folded.y) * folded.w;
                return _FoldForm.z > 0.5 ? float2(q, _FoldForm.y * p) : float2(p, _FoldForm.y * q);
            }

#if defined(FRACTAL_RELIEF)
            // The step's Jacobian (d re/dx, d re/dy, d im/dx, d im/dy), as FoldedFormula.StepJacobian.
            float4 FoldedStepJacobian(float2 z)
            {
                float x = z.x;
                float y = z.y;
                float x2 = x * x;
                float y2 = y * y;
                float2 ac = FoldedPolynomials(x2, y2);

                float4 values = float4(x, y, ac.x, ac.y);
                float4 signs = lerp(1.0, values < 0.0 ? -1.0 : 1.0, _FoldFlags);
                float4 folded = signs * values;

                float2 gradA = signs.z * float2(
                    4.0 * _FoldPolyA.x * x * x2 + 2.0 * _FoldPolyA.y * x * y2 + 2.0 * _FoldPolyA.w * x,
                    2.0 * _FoldPolyA.y * x2 * y + 4.0 * _FoldPolyA.z * y * y2 + 2.0 * _FoldTail.x * y);
                float2 gradC = signs.w * float2(
                    4.0 * _FoldPolyC.x * x * x2 + 2.0 * _FoldPolyC.y * x * y2 + 2.0 * _FoldPolyC.w * x,
                    2.0 * _FoldPolyC.y * x2 * y + 4.0 * _FoldPolyC.z * y * y2 + 2.0 * _FoldTail.z * y);

                float2 gradP;
                float2 gradQ;
                if (_FoldForm.x > 0.5)
                {
                    gradP = float2(signs.x * folded.z, 0.0) + folded.x * gradA;
                    gradQ = float2(0.0, signs.y * folded.w) + folded.y * gradC;
                }
                else
                {
                    gradP = gradA;
                    gradQ = float2(signs.x * folded.y, folded.x * signs.y) * folded.w + folded.x * folded.y * gradC;
                }

                return _FoldForm.z > 0.5
                    ? float4(gradQ, _FoldForm.y * gradP)
                    : float4(gradP, _FoldForm.y * gradQ);
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
                int iteration = 0;
                float squared = 0.0;
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
                    float4 s = FoldedStepJacobian(z);
                    j = float4(
                        s.x * j.x + s.y * j.z + identity,
                        s.x * j.y + s.y * j.w,
                        s.z * j.x + s.w * j.z,
                        s.z * j.y + s.w * j.w + identity);
#endif
                    z = FoldedStep(z) + c;
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

                float count = FractalSmoothCountPower(iteration, squared, bailout, _FoldForm.w);
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
