Shader "Custom/FireSmokeVolume" {
    // Fire and smoke in ONE ray march.
    //
    // FireVolume and SmokeVolume used to march the same box, over the same
    // pixels, reading the same smoke texture -- twice. Doing it once halves
    // the cost of the normal view, and composites correctly: smoke in front
    // of a flame now hides it, and flame in front of smoke shows over it,
    // which two separately blended passes could only approximate.
    Properties
    {
        _FlameTex("Flame", 3D) = "" {}
        _SmokeTex("Smoke", 3D) = "" {}
        _TemperatureTex("Temperature", 3D) = "" {}

        [Header(Flame)]
        // Flame values below this are treated as empty air, so faint traces
        // don't glow into a halo around the fire.
        _FlameThreshold("Flame Threshold", Range(0, 0.9)) = 0.15
        // Contrast above the threshold: dim tongue edges, bright core.
        _FlameSharpness("Flame Sharpness", Range(0.5, 4)) = 1.6
        // Light emitted per metre of flame.
        _Emission("Emission (per m)", Float) = 3
        // How much a metre of flame hides what's behind it. Keep low.
        _FlameOpacity("Flame Opacity (per m)", Float) = 2

        [Header(Flame Colour)]
        _ColorEdge("Edge (faint)", Color) = (0.50, 0.05, 0.00, 1)
        _ColorBody("Body", Color) = (1.00, 0.28, 0.02, 1)
        _ColorCore("Core", Color) = (1.00, 0.48, 0.08, 1)
        _ColorHot("Hottest", Color) = (1.00, 0.68, 0.30, 1)
        _CoreStart("Core Starts At", Range(0, 1)) = 0.55
        _HotStart("Hottest Starts At", Range(0, 1)) = 0.85

        [Header(Smoke)]
        _SmokeColorLow("Smoke Color (Thin)", Color) = (0.12, 0.12, 0.12, 1)
        _SmokeColorHigh("Smoke Color (Thick)", Color) = (0.35, 0.35, 0.35, 1)
        // Opacity per metre at full smoke density.
        _SmokeOpacity("Smoke Opacity (per m)", Float) = 30
        // Overall smoke multiplier (was _Density on the smoke material).
        _SmokeDensity("Smoke Density", Float) = 1
        // How much nearby flame lights the smoke from within.
        _FireGlow("Fire Glow", Float) = 0.15

        [Header(Performance)]
        _StepWorld("Step Length (m)", Range(0.03, 0.5)) = 0.12
        _MaxSteps("Max Steps", Range(8, 256)) = 48
        _DepthSlack("Depth Slack (m)", Float) = 0.02
    }

    SubShader {
        Tags { "RenderPipeline" = "UniversalPipeline"
               "Queue" = "Transparent"
               "RenderType" = "Transparent" }

        // Premultiplied: colour is light reaching the eye, alpha is how much
        // of the background the fire and smoke block.
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
            sampler3D _TemperatureTex;

            float  _FlameThreshold;
            float  _FlameSharpness;
            float  _Emission;
            float  _FlameOpacity;

            float4 _ColorEdge;
            float4 _ColorBody;
            float4 _ColorCore;
            float4 _ColorHot;
            float  _CoreStart;
            float  _HotStart;

            float4 _SmokeColorLow;
            float4 _SmokeColorHigh;
            float  _SmokeOpacity;
            float  _SmokeDensity;
            float  _FireGlow;

            float3 FlameColor(float t)
            {
                float coreStart = saturate(_CoreStart);
                float hotStart  = max(saturate(_HotStart), coreStart + 0.01);

                float3 c = lerp(_ColorEdge.rgb, _ColorBody.rgb, saturate(t / max(coreStart, 0.01)));
                c = lerp(c, _ColorCore.rgb, saturate((t - coreStart) / max(hotStart - coreStart, 0.01)));
                c = lerp(c, _ColorHot.rgb,  saturate((t - hotStart) / max(1.0 - hotStart, 0.01)));
                return c;
            }

            float3 BlackbodyPalette(float heat)
            {
                if (heat < 0.25)
                    return lerp(float3(0.15, 0.0, 0.0), float3(0.8, 0.0, 0.0), heat / 0.25);

                if (heat < 0.5)
                    return lerp(float3(0.8, 0.0, 0.0), float3(1.0, 0.35, 0.0), (heat - 0.25) / 0.25);

                if (heat < 0.75)
                    return lerp(float3(1.0, 0.35, 0.0), float3(1.0, 0.85, 0.0), (heat - 0.5) / 0.25);

                return lerp(float3(1.0, 0.85, 0.0), float3(1.0, 1.0, 1.0), (heat - 0.75) / 0.25);
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                MarchSetup m;
                if (!SetupMarch(i, m))
                    discard;

                float3 p = m.startPos;
                float3 accumColor    = 0;
                float  transmittance = 1.0;

                [loop]
                for (int s = 0; s < m.steps; s++)
                {
                    float4 uvw   = float4(p, 0);
                    float  smoke = tex3Dlod(_SmokeTex, uvw).r;
                    float  flame = tex3Dlod(_FlameTex, uvw).r;

                    // --- smoke: absorbs, and glows where flame lights it ---
                    if (smoke > 0.001)
                    {
                        float density = saturate(smoke);

                        float alpha = 1.0 - exp(-pow(density, 0.6) * _SmokeOpacity *
                                                _SmokeDensity * m.stepWorld);

                        float3 color = lerp(_SmokeColorLow.rgb, _SmokeColorHigh.rgb, density);

                        // Only spend the temperature read where it can show.
                        if (flame > 0.001 && _FireGlow > 0.0)
                        {
                            float temp = tex3Dlod(_TemperatureTex, uvw).r;
                            color += BlackbodyPalette(saturate(temp / 1162.34)) *
                                     flame * density * _FireGlow;
                        }

                        accumColor    += color * alpha * transmittance;
                        transmittance *= 1.0 - alpha;
                    }

                    // --- flame: emits light, blocks a little ---
                    float f = saturate((flame - _FlameThreshold) /
                                       max(1.0 - _FlameThreshold, 1e-3));

                    if (f > 0.0)
                    {
                        f = pow(f, _FlameSharpness);

                        accumColor    += FlameColor(f) * f * _Emission * m.stepWorld * transmittance;
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
