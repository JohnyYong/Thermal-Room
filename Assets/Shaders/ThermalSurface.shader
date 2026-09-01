Shader "Custom/ThermalSurface"
{
    Properties
    {
        _MaxTemperature(
            "Max Temperature",
            Float) = 500

        _BlurRadius(
            "Blur Radius",
            Range(0.1, 5.0)) = 1.75

        _BlurSigma(
            "Blur Sigma",
            Range(0.1, 5.0)) = 2.0

        [Header(Cold Visibility)]
        _MinAlpha(
            "Min Alpha (cold floor)",
            Range(0.0, 1.0)) = 0.6

        _ColdColour(
            "Cold Colour",
            Color) = (0.05, 0.05, 0.15, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler3D _SolidTemperatureTex;
            sampler3D _ObstacleTex;

            float3 _ThermalVolumeMin;
            float3 _ThermalVolumeSize;

            float _ThermalMinTemp;
            float _ThermalMaxTemp;

            float _BlurRadius;
            float _BlurSigma;

            float _MinAlpha;
            float4 _ColdColour;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;

                o.pos =
                    UnityObjectToClipPos(
                        v.vertex);

                o.worldPos =
                    mul(
                        unity_ObjectToWorld,
                        v.vertex).xyz;

                return o;
            }

            // Cold end now returns _ColdColour instead of pure black. Pure
            // black in a THERMAL palette reads as "no data" (transparent,
            // showing whatever's underneath) rather than as a valid cold
            // temperature reading -- which is exactly what was leaking the
            // furniture's raw dark base texture through. A dark blue/grey
            // reads unambiguously as "this is cold", not "this is a hole".
            float3 ThermalPalette(float heat)
            {
                if (heat < 0.2)
                {
                    // Cold colour -> White
                    return lerp(
                        _ColdColour.rgb,
                        float3(1, 1, 1),
                        heat / 0.2);
                }

                if (heat < 0.4)
                {
                    // White -> Yellow
                    return lerp(
                        float3(1, 1, 1),
                        float3(1, 1, 0),
                        (heat - 0.2) / 0.2);
                }

                if (heat < 0.6)
                {
                    // Yellow -> Orange
                    return lerp(
                        float3(1, 1, 0),
                        float3(1, 0.5, 0),
                        (heat - 0.4) / 0.2);
                }

                if (heat < 0.8)
                {
                    // Orange -> Red
                    return lerp(
                        float3(1, 0.5, 0),
                        float3(1, 0, 0),
                        (heat - 0.6) / 0.2);
                }

                // Red -> Dark Red
                return lerp(
                    float3(1, 0, 0),
                    float3(0.35, 0, 0),
                    (heat - 0.8) / 0.2);
            }

            float SampleBlurredTemperature(
                float3 uvw)
            {
                float3 voxel =
                    float3(
                        1.0 / 400.0,
                        1.0 / 200.0,
                        1.0 / 400.0);

                float temp = 0;
                float weight = 0;

                const int kernelSize = 3;

                for (int z = -kernelSize; z <= kernelSize; z++)
                {
                    for (int y = -kernelSize; y <= kernelSize; y++)
                    {
                        for (int x = -kernelSize; x <= kernelSize; x++)
                        {
                            float dist =
                                length(
                                    float3(
                                        x,
                                        y,
                                        z));

                            float w =
                                exp(
                                    -(dist * dist) /
                                    (2.0 *
                                        _BlurSigma *
                                        _BlurSigma));

                            float3 sampleUV =
                                uvw +
                                float3(
                                    x,
                                    y,
                                    z) *
                                voxel *
                                _BlurRadius;

                            sampleUV =
                                saturate(
                                    sampleUV);

                            float obstacle =
                                tex3D(
                                    _ObstacleTex,
                                    sampleUV).r;

                            if (obstacle > 0.5)
                            {
                                temp +=
                                    tex3D(
                                        _SolidTemperatureTex,
                                        sampleUV).r
                                    * w;

                                weight += w;
                            }
                        }
                    }
                }

                if (weight < 0.0001)
                {
                    return tex3D(
                        _SolidTemperatureTex,
                        uvw).r;
                }

                return temp / weight;
            }

            fixed4 frag(
                v2f i)
                : SV_Target
            {
                float3 uvw =
                    (i.worldPos -
                     _ThermalVolumeMin) /
                     _ThermalVolumeSize;

                uvw =
                    saturate(
                        uvw);

                float temp =
                    SampleBlurredTemperature(
                        uvw);

                float heat =
                    saturate(
                        (temp -
                         _ThermalMinTemp) /
                        (_ThermalMaxTemp -
                         _ThermalMinTemp));

                heat =
                    smoothstep(
                        0.05,
                        0.95,
                        heat);

                float3 colour =
                    ThermalPalette(
                        heat);

                float glow =
                    heat *
                    heat *
                    2.0;

                // Was: alpha = smoothstep(0.2, 0.8, heat), which hits 0 at
                // low heat -- fully transparent, letting whatever's behind
                // this pass (the object's own raw dark base texture, from
                // thermalGreyscaleMaterial) show through unmodified. That's
                // what was rendering as solid black holes in the middle of
                // visible fire: those objects hadn't heated up yet, so the
                // thermal overlay vanished and exposed their real dark
                // colour underneath. Flooring alpha at _MinAlpha keeps the
                // thermal tint always dominant, so "cold" reads as a
                // visible cold colour instead of "no overlay at all".
                float alpha =
                    max(
                        smoothstep(
                            0.2,
                            0.8,
                            heat),
                        _MinAlpha);

                return float4(
                    colour * (1 + glow),
                    alpha);
            }

            ENDCG
        }

        Pass
        {
            Name "DepthNormals"

            Tags
            {
                "LightMode" = "DepthNormals"
            }

            ZWrite On
            Cull Back

            HLSLPROGRAM

            #pragma vertex vertDepth
            #pragma fragment fragDepth

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings vertDepth(Attributes input)
            {
                Varyings output;

                VertexPositionInputs pos =
                    GetVertexPositionInputs(
                        input.positionOS.xyz);

                output.positionHCS =
                    pos.positionCS;

                output.normalWS =
                    TransformObjectToWorldNormal(
                        input.normalOS);

                return output;
            }

            float4 fragDepth(Varyings input)
                : SV_Target
            {
                float3 normal =
                    normalize(input.normalWS);

                return float4(
                    normal * 0.5 + 0.5,
                    1.0);
            }

            ENDHLSL
        }
    }
}
