Shader "FractalVisio/UiBackdropBlur"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _BlurDirection ("Blur direction in texels", Vector) = (1, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;
            float4 _BlurDirection;

            static const float BlurWeights[5] =
            {
                0.2270270, 0.1945946, 0.1216216, 0.0540541, 0.0162162
            };

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // Nine-tap gaussian along one axis. The caller runs it twice - horizontally, then
            // vertically - over an already downscaled copy, which is what makes it cheap enough to
            // redo every frame a panel is open and keep the glass live while the fractal renders.
            half4 Frag(Varyings input) : SV_Target
            {
                float2 texelStep = _BlurDirection.xy * _MainTex_TexelSize.xy;
                half4 sum = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * BlurWeights[0];

                [unroll]
                for (int i = 1; i < 5; i++)
                {
                    float2 offset = texelStep * i;
                    sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + offset) * BlurWeights[i];
                    sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - offset) * BlurWeights[i];
                }

                sum.a = 1.0;
                return sum;
            }
            ENDHLSL
        }
    }
}
