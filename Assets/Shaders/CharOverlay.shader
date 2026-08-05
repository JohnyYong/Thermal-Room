Shader "Custom/CharOverlay"
{
    Properties
    {
        _CharColor("Char Color", Color) = (0.03, 0.025, 0.02, 1)
        _NoiseScale("Noise Scale", Float) = 1.2
        _NoiseDetail("Noise Detail", Range(1,4)) = 3
        _EdgeSoftness("Edge Softness", Range(0.01, 0.5)) = 0.12
        _Burn("Burn (fallback, used outside the thermal volume)", Range(0,1)) = 0
        _MaxCharAlpha("Max Char Opacity", Range(0,1)) = 1

        // Set from C# (CharOverlayController.BuildOverlayClone) to the
        // source mesh's OBJECT-SPACE bounds. Declared here (not just in the
        // CBUFFER) so the SRP Batcher packs/validates this material
        // correctly -- omitting a CBUFFER field from Properties is what
        // caused the values to arrive as garbage on some GPUs/drivers.
        _MeshBoundsCenter("Mesh Bounds Center (internal, set by script)", Vector) = (0,0,0,0)
        _MeshBoundsSize("Mesh Bounds Size (internal, set by script)", Vector) = (1,1,1,0)
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
                float3 _MeshBoundsCenter;
                float3 _MeshBoundsSize;
            CBUFFER_END

            // Bound globally every FixedUpdate by ThermalSimulation. Kept
            // declared here (harmless if unused) in case you want to layer
            // fine local detail back in later, but the main mask below no
            // longer reads from it -- see note in frag().
            TEXTURE3D(_CharTex);
            float3 _ThermalVolumeMin;
            float3 _ThermalVolumeSize;

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

            // Remaps raw object-space coords into a fixed, mesh-agnostic
            // range (~-0.5..0.5 across the object) so _NoiseScale means
            // "cells across the object" no matter how the mesh's own units
            // are baked, and no matter what scale the surrounding SCENE is
            // built at (world position / Transform scale never enter this
            // at all, unlike sampling from positionWS). max(...,0.0001)
            // guards a zero-extent axis from producing a divide-by-zero.
            float3 NormalizeObjectSpace(float3 posOS)
            {
                float3 safeSize = max(_MeshBoundsSize, 0.0001);
                return (posOS - _MeshBoundsCenter) / safeSize;
            }

            float CharPattern(float3 posOS)
            {
                float3 normalizedPos = NormalizeObjectSpace(posOS);

                float3 p = normalizedPos * _NoiseScale;
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

            // Looks up the real char value at a world position by mapping it
            // into the thermal volume's 0-1 UVW space. Returns 0 (untouched)
            // for anything outside the volume's bounds -- e.g. an object
            // that isn't inside the simulated region at all.
            float SampleCharAt(float3 worldPos)
            {
                float3 uvw = (worldPos - _ThermalVolumeMin) / max(_ThermalVolumeSize, 0.0001);

                if (any(uvw < 0.0) || any(uvw > 1.0))
                    return 0.0;

                return SAMPLE_TEXTURE3D(_CharTex, sampler_LinearClamp, uvw).r;
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
                float3 positionOS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float localBurn = saturate(_Burn);

                float pattern = CharPattern(IN.positionOS);

                float charMask = 1.0 - smoothstep(
                    localBurn - _EdgeSoftness,
                    localBurn + _EdgeSoftness,
                    pattern);

                float alpha = charMask * _MaxCharAlpha;

                return half4(_CharColor.rgb, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
