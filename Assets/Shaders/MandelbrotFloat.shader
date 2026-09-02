Shader "FractalVisio/MandelbrotFloat"
{
    Properties
    {
        _Center ("Center", Vector) = (-0.5, 0, 0, 0)
        _Scale ("Scale", Float) = 3
        _Aspect ("Aspect", Float) = 1
        _Rotation ("Rotation", Float) = 0
        _Iterations ("Iterations", Int) = 128
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_PaletteTex);
            SAMPLER(sampler_PaletteTex);

            CBUFFER_START(UnityPerMaterial)
            float4 _Center;
            float _Scale;
            float _Aspect;
            float _Rotation;
            int _Iterations;
            CBUFFER_END

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 offset = input.uv - 0.5;
                float2 d = float2(offset.x * _Aspect, offset.y);
                float sinR, cosR;
                sincos(_Rotation, sinR, cosR);
                d = float2(d.x * cosR - d.y * sinR, d.x * sinR + d.y * cosR);
                float2 c = _Center.xy + d * _Scale;

                // Main cardioid and period-2 bulb: very cheap on mobile GPUs.
                float cardioidX = c.x - 0.25;
                float y2 = c.y * c.y;
                float q = cardioidX * cardioidX + y2;
                if (q * (q + cardioidX) <= 0.25 * y2 || dot(c + float2(1.0, 0.0), c + float2(1.0, 0.0)) <= 0.0625)
                {
                    return half4(0.012, 0.02, 0.047, 1.0);
                }

                float2 z = 0.0;
                int maxIterations = clamp(_Iterations, 1, 2048);
                int iteration = 0;
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
                    if (dot(z, z) > 4.0)
                    {
                        escaped = true;
                        break;
                    }
                }

                if (!escaped)
                {
                    return half4(0.012, 0.02, 0.047, 1.0);
                }

                float palettePosition = frac(iteration * 0.021);
                return SAMPLE_TEXTURE2D(_PaletteTex, sampler_PaletteTex, float2(palettePosition, 0.5));
            }
            ENDHLSL
        }
    }
}
