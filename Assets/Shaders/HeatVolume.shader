Shader "Custom/HeatVolume" {
    Properties
    {
        _TemperatureTex("Temperature", 3D) = "" {}
        _Density("Density", Float) = 1
        _MaxTemperature("Max Temperature", Float) = 500

        [Header(Performance)]
        _StepWorld("Step Length (m)", Range(0.03, 0.5)) = 0.15
        _MaxSteps("Max Steps", Range(8, 256)) = 40
        // The thermal view deliberately sees up to a metre past surfaces.
        _DepthSlack("Depth Slack (m)", Float) = 1
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

            sampler3D _TemperatureTex;

            float _Density;
            float _MaxTemperature;

            float3 ThermalPalette(float heat)
            {
                if (heat < 0.2)
                    return lerp(float3(0, 0, 0), float3(0.2, 0, 0.4), heat / 0.2);

                if (heat < 0.4)
                    return lerp(float3(0.2, 0, 0.4), float3(0.8, 0, 0), (heat - 0.2) / 0.2);

                if (heat < 0.6)
                    return lerp(float3(0.8, 0, 0), float3(1, 0.4, 0), (heat - 0.4) / 0.2);

                if (heat < 0.8)
                    return lerp(float3(1, 0.4, 0), float3(1, 1, 0), (heat - 0.6) / 0.2);

                return lerp(float3(1, 1, 0), float3(1, 1, 1), (heat - 0.8) / 0.2);
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                MarchSetup m;
                if (!SetupMarch(i, m))
                    discard;

                float3 p = m.startPos;
                float  maxHeat = 0;

                [loop]
                for (int s = 0; s < m.steps; s++)
                {
                    float temp = tex3Dlod(_TemperatureTex, float4(p, 0)).r;

                    // saturate before pow: pow of a negative is NaN.
                    float heat = pow(saturate(temp / max(_MaxTemperature, 1e-3)), 0.5);
                    maxHeat = max(maxHeat, heat);

                    p += m.rayDir * m.stepSize;
                }

                if (!(maxHeat > 0.0)) maxHeat = 0.0;   // NaN backstop

                float3 color = ThermalPalette(maxHeat);
                float  alpha = pow(maxHeat, 0.7) * _Density;

                return float4(color, alpha);
            }

            ENDHLSL
        }
    }
}
