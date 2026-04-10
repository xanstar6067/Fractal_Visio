Shader "FractalVisio/MandelbrotFloat"
{
    Properties
    {
        _Center ("Center", Vector) = (0, 0, 0, 0)
        _Scale ("Scale", Float) = 3
        _Iterations ("Iterations", Float) = 128
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
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_PaletteTex);
            SAMPLER(sampler_PaletteTex);

            CBUFFER_START(UnityPerMaterial)
            float4 _Center;
            float _Scale;
            float _Iterations;
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

                output.uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                output.positionCS = float4(output.uv * 2.0 - 1.0, 0.0, 1.0);
                output.uv.y = 1.0 - output.uv.y;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float aspect = _ScreenParams.x / _ScreenParams.y;
                p.x *= aspect;

                float2 c = float2(_Center.x, _Center.y) + p * (_Scale * 0.5);
                float2 z = 0.0;

                int maxIterations = max(1, (int)_Iterations);
                int iteration = 0;
                [loop]
                for (int i = 0; i < 4096; i++)
                {
                    if (i >= maxIterations)
                    {
                        break;
                    }

                    float x = z.x * z.x - z.y * z.y + c.x;
                    float y = 2.0 * z.x * z.y + c.y;
                    z = float2(x, y);

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
