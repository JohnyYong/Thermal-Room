Shader "Custom/FireHeatVolume" {
    Properties
    {
        _FireHeatTex("Fire Heat", 3D) = "" {}
        _Density("Density", Float) = 1

        // Bottom of the mapped range: room ambient, not absolute zero.
        _MinTemperature("Min Temperature (C)", Float) = 20

        // Top of the mapped range. Must sit above ThermalSimulation's
        // Flame Gas Temp (1000 by default) or every flame pins to one colour.
        _MaxTemperature("Max Temperature (C)", Float) = 1200

        // Ramp shape between min and max. 1 = linear.
        _HeatGamma("Heat Gamma", Range(0.25, 3)) = 0.75

        [Header(Palette)]
        // Thermal ramp, coolest -> hottest. Each band covers a fifth of
        // the range. The old ramp went through pure yellow (1,1,0); these
        // keep the middle of the range orange instead.
        _Pal0("0.0  Coolest", Color) = (0, 0, 0, 1)
        _Pal1("0.2  Warm", Color) = (1, 1, 1, 1)
        _Pal2("0.4  Hot", Color) = (1, 0.72, 0.25, 1)
        _Pal3("0.6  Hotter", Color) = (1, 0.45, 0.05, 1)
        _Pal4("0.8  Very hot", Color) = (1, 0.15, 0, 1)
        _Pal5("1.0  Hottest", Color) = (0.55, 0.06, 0.06, 1)

        [Header(Performance)]
        _StepWorld("Step Length (m)", Range(0.03, 0.5)) = 0.15
        _MaxSteps("Max Steps", Range(8, 256)) = 40
        // The thermal view deliberately sees up to a metre past surfaces.
        _DepthSlack("Depth Slack (m)", Float) = 1
    }

    SubShader {
        Tags { "RenderPipeline" = "UniversalPipeline"
               "Queue" = "Transparent+1"
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

            sampler3D _FireHeatTex;

            float _Density;
            float _MinTemperature;
            float _MaxTemperature;
            float _HeatGamma;

            float4 _Pal0, _Pal1, _Pal2, _Pal3, _Pal4, _Pal5;

            // Five equal bands between the six palette colours.
            float3 ThermalPalette(float heat)
            {
                float t = saturate(heat) * 5.0;

                if (t < 1.0) return lerp(_Pal0.rgb, _Pal1.rgb, t);
                if (t < 2.0) return lerp(_Pal1.rgb, _Pal2.rgb, t - 1.0);
                if (t < 3.0) return lerp(_Pal2.rgb, _Pal3.rgb, t - 2.0);
                if (t < 4.0) return lerp(_Pal3.rgb, _Pal4.rgb, t - 3.0);
                return lerp(_Pal4.rgb, _Pal5.rgb, t - 4.0);
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                MarchSetup m;
                if (!SetupMarch(i, m))
                    discard;

                float3 p = m.startPos;
                float  maxHeat = 0;
                float  range = max(_MaxTemperature - _MinTemperature, 1e-3);

                [loop]
                for (int s = 0; s < m.steps; s++)
                {
                    float temp = tex3Dlod(_FireHeatTex, float4(p, 0)).r;

                    // saturate before pow: pow of a negative is NaN.
                    float t = saturate((temp - _MinTemperature) / range);
                    maxHeat = max(maxHeat, pow(t, _HeatGamma));

                    p += m.rayDir * m.stepSize;
                }

                // NaN backstop: transparent rather than a black hole.
                if (!(maxHeat > 0.0)) maxHeat = 0.0;
                maxHeat = saturate(maxHeat);

                float3 color = ThermalPalette(maxHeat);
                float  alpha = pow(maxHeat, 0.7) * _Density;

                return float4(color, alpha);
            }

            ENDHLSL
        }
    }
}
