Shader "Custom/SmokeVolume" {
    Properties {
        _SmokeTex("Smoke", 3D) = "" {}
        _Density("Density", Float) = 1
        _FireGlow("Fire Glow", Float) = 0.15
        _SmokeColorLow("Smoke Color (Thin)", Color) = (0.12, 0.12, 0.12, 1)
        _SmokeColorHigh("Smoke Color (Thick)", Color) = (0.35, 0.35, 0.35, 1)

        // Opacity per metre at full smoke density (before _Density).
        // The old shader added a fixed alpha per step over 512 steps, so how
        // opaque smoke looked depended on the size of the box. This is the
        // same look expressed per metre: ~30 matches the old shader in a
        // ~6 m box. Lower it for thinner smoke.
        _SmokeOpacity("Smoke Opacity (per m)", Float) = 30

        [Header(Performance)]
        _StepWorld("Step Length (m)", Range(0.03, 0.5)) = 0.12
        _MaxSteps("Max Steps", Range(8, 256)) = 48
        _DepthSlack("Depth Slack (m)", Float) = 0.02
    }

    SubShader {
        Tags { "RenderPipeline" = "UniversalPipeline"
               "Queue" = "Transparent"
               "RenderType" = "Transparent" }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Front ZWrite Off ZTest Always

        Pass {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "VolumeCommon.hlsl"

            sampler3D _SmokeTex;
            sampler3D _FlameTex;
            sampler3D _TemperatureTex;

            float  _Density;
            float  _FireGlow;
            float  _SmokeOpacity;
            float4 _SmokeColorLow;
            float4 _SmokeColorHigh;

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
                float3 accumColor = 0;
                float  accumAlpha = 0;

                [loop]
                for (int s = 0; s < m.steps; s++)
                {
                    float smoke = tex3Dlod(_SmokeTex, float4(p, 0)).r;

                    if (smoke > 0.001)
                    {
                        float flame = tex3Dlod(_FlameTex, float4(p, 0)).r;
                        float temp  = tex3Dlod(_TemperatureTex, float4(p, 0)).r;

                        float density = saturate(smoke);

                        float3 smokeColor =
                            lerp(_SmokeColorLow.rgb, _SmokeColorHigh.rgb, density);

                        // Only real flame lights the smoke up (backlit look).
                        float  heatGlow  = saturate(temp / 1162.34);
                        float  glow      = flame * density * _FireGlow;
                        float3 color     = smokeColor + BlackbodyPalette(heatGlow) * glow;

                        float alpha = 1.0 - exp(-pow(density, 0.6) * _SmokeOpacity *
                                                _Density * m.stepWorld);

                        accumColor += color * alpha * (1 - accumAlpha);
                        accumAlpha += alpha * (1 - accumAlpha);

                        if (accumAlpha > 0.99)
                            break;
                    }

                    p += m.rayDir * m.stepSize;
                }

                return float4(accumColor, accumAlpha);
            }

            ENDHLSL
        }
    }
}
