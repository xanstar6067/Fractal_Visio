Shader "FractalVisio/MandelbrotPerturbation"
{
    Properties
    {
        _CenterDeltaHigh ("Center Delta High", Vector) = (0, 0, 0, 0)
        _CenterDeltaLow  ("Center Delta Low",  Vector) = (0, 0, 0, 0)
        _Scale           ("Scale",             Float)  = 3
        _Aspect          ("Aspect",            Float)  = 1
        _Iterations      ("Iterations",        Float)  = 128
        _OrbitLength     ("Orbit Length",      Int)    = 128
        _PaletteTex              ("Palette",              2D) = "white" {}
        _ReferenceOrbitTexHigh   ("Reference Orbit High", 2D) = "black" {}
        _ReferenceOrbitTexLow    ("Reference Orbit Low",  2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_PaletteTex);            SAMPLER(sampler_PaletteTex);
            TEXTURE2D(_ReferenceOrbitTexHigh); SAMPLER(sampler_ReferenceOrbitTexHigh);
            TEXTURE2D(_ReferenceOrbitTexLow);  SAMPLER(sampler_ReferenceOrbitTexLow);

            CBUFFER_START(UnityPerMaterial)
            float4 _CenterDeltaHigh;
            float4 _CenterDeltaLow;
            float  _Scale;
            float  _Aspect;
            float  _Iterations;
            int    _OrbitLength;
            CBUFFER_END

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                // Стандартный fullscreen triangle: vertexID 0,1,2
                o.uv          = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                o.positionCS  = float4(o.uv * 2.0 - 1.0, 0.0, 1.0);
                // НЕ флипаем Y здесь — CPU-ядро считает Y снизу вверх (0=низ),
                // RenderTexture тоже хранит Y снизу вверх на большинстве платформ.
                // Флип при необходимости делается ниже через sign(_ProjectionParams.x).
                return o;
            }

            // Loads the reference orbit sample. The residual improves the
            // texture upload round-trip, but the recurrence below runs in float.
            float2 LoadOrbit(int index)
            {
                uint w, h;
                _ReferenceOrbitTexHigh.GetDimensions(w, h);
                int   col = index % (int)w;
                int   row = index / (int)w;
                float2 uv = (float2(col, row) + 0.5) / float2(w, h);

                float2 hi = SAMPLE_TEXTURE2D_LOD(_ReferenceOrbitTexHigh,
                                                  sampler_ReferenceOrbitTexHigh, uv, 0).rg;
                float2 lo = SAMPLE_TEXTURE2D_LOD(_ReferenceOrbitTexLow,
                                                  sampler_ReferenceOrbitTexLow,  uv, 0).rg;
                return hi + lo;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // uv в [0,1]. Переводим в [-1,1] по обеим осям.
                float2 uv = input.uv;

                // Учитываем знак проекционной матрицы (OpenGL vs D3D convention).
                // _ProjectionParams.x == -1 означает, что UV уже флипнуты системой.
                if (_ProjectionParams.x < 0.0)
                    uv.y = 1.0 - uv.y;

                // p соответствует (nx - 0.5) * 2  в CPU-ядре.
                float2 p = uv - 0.5;   // [-0.5, 0.5]

                // Смещение пикселя в пространстве фрактала (точно как в CPU):
                //   cx = center.x + (nx-0.5)*2 * halfScale * aspect
                //   cy = center.y + (ny-0.5)*2 * halfScale
                float halfScale = _Scale * 0.5;
                float2 pixelOffset = float2(p.x * 2.0 * halfScale * _Aspect,
                                            p.y * 2.0 * halfScale);

                // dc = смещение от референсной точки до текущего пикселя.
                // CenterDelta = viewCenter - referenceCenter  (вычислено в decimal на CPU).
                float2 centerDelta = _CenterDeltaHigh.xy + _CenterDeltaLow.xy;
                float2 dc = centerDelta + pixelOffset;

                // ── Perturbation iteration ────────────────────────────────────
                // delta_0 = 0
                // delta_{n+1} = 2*Z_n*delta_n + delta_n^2 + dc
                // escape: |Z_{n+1} + delta_{n+1}|^2 > 4

                float2 delta = float2(0.0, 0.0);
                int maxIter   = max(1, (int)_Iterations);
                int orbitLen  = max(2, _OrbitLength);
                int loopCount = min(maxIter, orbitLen - 1);

                // FIX: используем отдельный флаг вместо перезаписи iteration,
                // чтобы корректно отличить "убежало на шаге i" от "не убежало".
                bool escaped  = false;
                int  escapeAt = 0;

                [loop]
                for (int i = 0; i < loopCount; i++)
                {
                    float2 zRef = LoadOrbit(i);

                    // delta^2
                    float2 dSq = float2(delta.x * delta.x - delta.y * delta.y,
                                        2.0 * delta.x * delta.y);
                    // 2 * Z_ref * delta
                    float2 twoZd = float2(2.0 * (zRef.x * delta.x - zRef.y * delta.y),
                                          2.0 * (zRef.x * delta.y + zRef.y * delta.x));

                    delta = twoZd + dSq + dc;

                    float2 zNextRef = LoadOrbit(i + 1);
                    float2 z = zNextRef + delta;
                    if (dot(z, z) > 4.0)
                    {
                        escaped  = true;
                        escapeAt = i + 1; // количество итераций до выхода
                        break;
                    }
                }

                // Точки внутри множества — чёрные.
                if (!escaped)
                    return half4(0.0, 0.0, 0.0, 1.0);

                float t = saturate((float)escapeAt / max(1.0, (float)maxIter));
                return SAMPLE_TEXTURE2D(_PaletteTex, sampler_PaletteTex, float2(t, 0.5));
            }
            ENDHLSL
        }
    }
}
