Shader "FractalVisio/FrameComposite"
{
    // Draws one already-rendered fractal buffer into the display, placed by an affine uv map
    // instead of by rewriting its pixels. Two passes so the compositor can lay a wide, coarse
    // frame down first and then blend the sharp one over it wherever that one has coverage.
    Properties
    {
        _MainTex ("Frame", 2D) = "black" {}
        _FallbackColor ("Uncovered colour", Color) = (0.012, 0.02, 0.047, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        float4 _MainTex_TexelSize;

        // Display uv -> frame uv, rows 0 and 1 of an affine 3x3. See Core/View/FramePlacement.cs.
        // Two vectors, not a matrix: a dot product has no row/column convention to get wrong.
        float4 _FrameUvRow0;
        float4 _FrameUvRow1;
        float4 _FallbackColor;

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

        float2 FrameUv(float2 displayUv)
        {
            float3 homogeneous = float3(displayUv, 1.0);
            return float2(
                dot(_FrameUvRow0.xyz, homogeneous),
                dot(_FrameUvRow1.xyz, homogeneous));
        }

        // 1 well inside the frame, 0 outside, with a texel-wide ramp between. Without the ramp the
        // boundary of the sharp layer reads as a hard rectangle sliding across the picture during
        // a zoom-out; with it, the coarse layer underneath just fades in.
        float Coverage(float2 uv)
        {
            float2 zero = float2(0.0, 0.0);
            float2 edge = max(_MainTex_TexelSize.xy * 1.5, float2(1e-5, 1e-5));
            float2 lower = smoothstep(zero, edge, uv);
            float2 upper = smoothstep(zero, edge, float2(1.0, 1.0) - uv);
            return lower.x * lower.y * upper.x * upper.y;
        }
        ENDHLSL

        // Pass 0 - base layer. Opaque: whatever it cannot cover becomes the fallback colour.
        Pass
        {
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = FrameUv(input.uv);
                half4 frame = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, saturate(uv));
                half4 color = lerp(_FallbackColor, frame, Coverage(uv));
                color.a = 1.0;
                return color;
            }
            ENDHLSL
        }

        // Pass 1 - sharp layer over whatever pass 0 left. Colour blends, alpha is left alone:
        // blending alpha too would leave the seam band below 1 and the RawImage would show it
        // as a translucent ring.
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha, Zero One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = FrameUv(input.uv);
                half4 frame = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, saturate(uv));
                return half4(frame.rgb, Coverage(uv));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
