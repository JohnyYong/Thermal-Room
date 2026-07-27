Shader "Custom/CharOverlay"
{
    Properties
    {
        _CharColor("Char Color", Color) = (0.03, 0.025, 0.02, 1)
        _NoiseScale("Noise Scale", Float) = 1.2
        _NoiseDetail("Noise Detail", Range(1,4)) = 3
        _EdgeSoftness("Edge Softness", Range(0.01, 0.5)) = 0.12
        _Burn("Burn", Range(0,1)) = 0
        _MaxCharAlpha("Max Char Opacity", Range(0,1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "CharOverlay"
            Tags { "LightMode" = "UniversalForward" }

            // Transparent blend, and offset toward camera so it sits ON the
            // surface without z-fighting the original mesh underneath.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CharColor;
                float  _NoiseScale;
                float  _NoiseDetail;
                float  _EdgeSoftness;
                float  _Burn;
                float  _MaxCharAlpha;
            CBUFFER_END

            // ---- noise (same as CharSurface) --------------------------------

            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash13(i + float3(0,0,0));
                float n100 = Hash13(i + float3(1,0,0));
                float n010 = Hash13(i + float3(0,1,0));
                float n110 = Hash13(i + float3(1,1,0));
                float n001 = Hash13(i + float3(0,0,1));
                float n101 = Hash13(i + float3(1,0,1));
                float n011 = Hash13(i + float3(0,1,1));
                float n111 = Hash13(i + float3(1,1,1));

                float x00 = lerp(n000, n100, f.x);
                float x10 = lerp(n010, n110, f.x);
                float x01 = lerp(n001, n101, f.x);
                float x11 = lerp(n011, n111, f.x);

                float y0 = lerp(x00, x10, f.y);
                float y1 = lerp(x01, x11, f.y);

                return lerp(y0, y1, f.z);
            }

            float CharPattern(float3 worldPos)
            {
                float3 p = worldPos * _NoiseScale;
                float sum = 0, amp = 0.5, total = 0;
                int octaves = (int)_NoiseDetail;

                [unroll(4)]
                for (int i = 0; i < octaves; i++)
                {
                    sum += ValueNoise3D(p) * amp;
                    total += amp;
                    p *= 2.0;
                    amp *= 0.5;
                }
                return sum / max(total, 0.0001);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float pattern = CharPattern(IN.positionWS);

                // Burn sweeps the threshold through the noise -> patches grow
                float charMask = 1.0 - smoothstep(
                    _Burn - _EdgeSoftness,
                    _Burn + _EdgeSoftness,
                    pattern);

                // Alpha IS the char mask -- clean areas are fully transparent,
                // so the original material shows through untouched.
                float alpha = charMask * _MaxCharAlpha;

                return half4(_CharColor.rgb, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
