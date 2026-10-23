Shader "Custom/FireVolume" {
    Properties
    {
        _FlameTex("Flame", 3D) = "" {}
        _SmokeTex("Smoke", 3D) = "" {}

        // Overall multiplier, kept so existing materials keep their value.
        _Density("Density", Float) = 1

        // Smoke extinction per metre at full smoke density.
        _SmokeExtinction("Smoke Extinction (1/m)", Float) = 8

        // Flame values below this are treated as empty air, so faint traces
        // don't glow into a halo around the fire.
        _FlameThreshold("Flame Threshold", Range(0, 0.9)) = 0.15

        // Contrast above the threshold: dim tongue edges, bright core.
        _FlameSharpness("Flame Sharpness", Range(0.5, 4)) = 1.6

        // Light emitted per metre of flame.
        _Emission("Emission (per m)", Float) = 3

        // How much a metre of flame hides what's behind it. Keep low.
        _FlameOpacity("Flame Opacity (per m)", Float) = 2

        [Header(Colour)]
        // The flame ramp, faint edge -> hottest core. Tune these freely.
        _ColorEdge("Edge (faint)", Color) = (0.50, 0.05, 0.00, 1)
        _ColorBody("Body", Color) = (1.00, 0.28, 0.02, 1)
        _ColorCore("Core", Color) = (1.00, 0.48, 0.08, 1)
        _ColorHot("Hottest", Color) = (1.00, 0.68, 0.30, 1)

        // Where the core and hottest colours start, 0..1 of flame intensity.
        // Raise them to keep more of the flame orange.
        _CoreStart("Core Starts At", Range(0, 1)) = 0.55
        _HotStart("Hottest Starts At", Range(0, 1)) = 0.85

        [Header(Performance)]
        // Metres per ray step. Around the fine sim cell size is enough;
        // smaller costs more without adding detail.
        _StepWorld("Step Length (m)", Range(0.03, 0.5)) = 0.12
        _MaxSteps("Max Steps", Range(8, 256)) = 48
        _DepthSlack("Depth Slack (m)", Float) = 0.02
    }

    SubShader {
        Tags { "RenderPipeline" = "UniversalPipeline"
               "Queue" = "Transparent"
               "RenderType" = "Transparent" }

        // Premultiplied: colour is emitted light, alpha is how much of the
        // background the flame and smoke block.
        Blend One OneMinusSrcAlpha
        Cull Front ZWrite Off ZTest Always

        Pass {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "VolumeCommon.hlsl"

            sampler3D _FlameTex;
            sampler3D _SmokeTex;

            float _Density;
            float _SmokeExtinction;
            float _FlameThreshold;
            float _FlameSharpness;
            float _Emission;
            float _FlameOpacity;

            float4 _ColorEdge;
            float4 _ColorBody;
            float4 _ColorCore;
            float4 _ColorHot;
            float  _CoreStart;
            float  _HotStart;

            // Edge -> body -> core -> hottest, all from the material.
            float3 FlameColor(float t)
            {
                float coreStart = saturate(_CoreStart);
                float hotStart  = max(saturate(_HotStart), coreStart + 0.01);

                float3 c = lerp(_ColorEdge.rgb, _ColorBody.rgb, saturate(t / max(coreStart, 0.01)));
                c = lerp(c, _ColorCore.rgb, saturate((t - coreStart) / max(hotStart - coreStart, 0.01)));
                c = lerp(c, _ColorHot.rgb,  saturate((t - hotStart) / max(1.0 - hotStart, 0.01)));
                return c;
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                MarchSetup m;
                if (!SetupMarch(i, m))
                    discard;

                float3 p = m.startPos;
                float3 accumColor = 0;
                float  transmittance = 1.0;

                [loop]
                for (int s = 0; s < m.steps; s++)
                {
                    // tex3Dlod: no derivatives inside a variable-length loop,
                    // and cheaper on mobile.
                    float flame = tex3Dlod(_FlameTex, float4(p, 0)).r;
                    float smoke = tex3Dlod(_SmokeTex, float4(p, 0)).r;

                    transmittance *= exp(-smoke * _SmokeExtinction * m.stepWorld);

                    float f = saturate((flame - _FlameThreshold) /
                                       max(1.0 - _FlameThreshold, 1e-3));
                    f = pow(f, _FlameSharpness);

                    if (f > 0.0)
                    {
                        accumColor += FlameColor(f) * f * _Emission * _Density *
                                      m.stepWorld * transmittance;

                        transmittance *= exp(-f * _FlameOpacity * m.stepWorld);
                    }

                    if (transmittance < 0.01)
                        break;

                    p += m.rayDir * m.stepSize;
                }

                return float4(accumColor, 1.0 - transmittance);
            }

            ENDHLSL
        }
    }
}
