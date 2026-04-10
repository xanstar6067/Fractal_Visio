Shader "FractalVisio/MandelbrotPerturbation"
{
    Properties
    {
        _CenterDeltaHigh ("Center Delta High", Vector) = (0, 0, 0, 0)
        _CenterDeltaLow ("Center Delta Low", Vector) = (0, 0, 0, 0)
        _Scale ("Scale", Float) = 3
        _Aspect ("Aspect", Float) = 1
        _Iterations ("Iterations", Float) = 128
        _OrbitLength ("Orbit Length", Int) = 128
        _PaletteTex ("Palette", 2D) = "white" {}
        _ReferenceOrbitTexHigh ("Reference Orbit High", 2D) = "black" {}
        _ReferenceOrbitTexLow ("Reference Orbit Low", 2D) = "black" {}
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
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_PaletteTex);
            SAMPLER(sampler_PaletteTex);
            TEXTURE2D(_ReferenceOrbitTexHigh);
            SAMPLER(sampler_ReferenceOrbitTexHigh);
            TEXTURE2D(_ReferenceOrbitTexLow);
            SAMPLER(sampler_ReferenceOrbitTexLow);

            CBUFFER_START(UnityPerMaterial)
            float4 _CenterDeltaHigh;
            float4 _CenterDeltaLow;
            float _Scale;
            float _Aspect;
            float _Iterations;
            int _OrbitLength;
            CBUFFER_END

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                output.positionCS = float4(output.uv * 2.0 - 1.0, 0.0, 1.0);
                output.uv.y = 1.0 - output.uv.y;
                return output;
            }

            float2 LoadOrbit(int index)
            {
                uint width;
                uint height;
                _ReferenceOrbitTexHigh.GetDimensions(width, height);
                int x = index % (int)width;
                int y = index / (int)width;
                float2 uv = (float2(x, y) + 0.5) / float2(width, height);
                float2 orbitHigh = SAMPLE_TEXTURE2D_LOD(_ReferenceOrbitTexHigh, sampler_ReferenceOrbitTexHigh, uv, 0).xy;
                float2 orbitLow = SAMPLE_TEXTURE2D_LOD(_ReferenceOrbitTexLow, sampler_ReferenceOrbitTexLow, uv, 0).xy;
                return orbitHigh + orbitLow;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                p.x *= _Aspect;

                float2 dc = (_CenterDeltaHigh.xy + _CenterDeltaLow.xy) + p * (_Scale * 0.5);
                float2 delta = 0.0;

                int maxIterations = max(1, (int)_Iterations);
                int orbitLength = max(1, _OrbitLength);
                int loopCount = min(maxIterations, orbitLength);

                int iteration = 0;
                [loop]
                for (int i = 0; i < 4096; i++)
                {
                    if (i >= loopCount)
                    {
                        break;
                    }

                    float2 zr = LoadOrbit(i);
                    float2 deltaSq = float2(delta.x * delta.x - delta.y * delta.y, 2.0 * delta.x * delta.y);
                    float2 twoZDelta = float2(
                        2.0 * (zr.x * delta.x - zr.y * delta.y),
                        2.0 * (zr.x * delta.y + zr.y * delta.x));
                    delta = twoZDelta + deltaSq + dc;

                    float2 z = zr + delta;
                    if (dot(z, z) > 4.0)
                    {
                        iteration = i;
                        break;
                    }

                    iteration = i + 1;
                }

                if (iteration >= maxIterations)
                {
                    return half4(0, 0, 0, 1);
                }

                float t = iteration / max(1.0, _Iterations);
                return SAMPLE_TEXTURE2D(_PaletteTex, sampler_PaletteTex, float2(t, 0.5));
            }
            ENDHLSL
        }
    }
}
