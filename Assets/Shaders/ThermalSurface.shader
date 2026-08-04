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
        Offset -1, -50

        Pass
        {
            Name "ForwardThermal"

            Tags
            {
                "LightMode" = "UniversalForward"
            }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Modern URP 3D texture binding -- the legacy `sampler3D` +
            // `tex3D()` Cg compatibility macros this used to use are not
            // reliably supported for script-assigned RenderTextures across
            // every graphics backend (already known to silently fail under
            // DX12, see FireHeatVolume.shader; Vulkan/XR is the same class
            // of problem). TEXTURE3D/SAMPLER/SAMPLE_TEXTURE3D is the
            // correct SRP-safe way to bind and sample a Texture3D/3D
            // RenderTexture and matches what this file's own DepthNormals
            // pass already does correctly below.
            TEXTURE3D(_SolidTemperatureTex);
            SAMPLER(sampler_SolidTemperatureTex);

            TEXTURE3D(_ObstacleTex);
            SAMPLER(sampler_ObstacleTex);

            float3 _ThermalVolumeMin;
            float3 _ThermalVolumeSize;

            float _ThermalMinTemp;
            float _ThermalMaxTemp;

            float _BlurRadius;
            float _BlurSigma;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // Genuine geometric nudge toward the camera along the
                // surface normal, on top of the ShaderLab Offset above.
                // Unlike Offset (a depth-buffer bias whose effectiveness
                // depends on the platform's depth precision), this actually
                // moves the vertex in world space -- so it can't flicker
                // due to a mobile GPU's lower-precision depth buffer no
                // longer resolving a -1 unit bias reliably.
                float3 worldPos = TransformObjectToWorld(v.vertex.xyz);
                float3 worldNormal = TransformObjectToWorldNormal(v.normal);
                worldPos += worldNormal * 0.003;

                o.pos = TransformWorldToHClip(worldPos);
                o.worldPos = worldPos;

                return o;
            }

            float3 ThermalPalette(float heat)
            {
                if (heat < 0.2)
                {
                    // Black -> White
                    return lerp(
                        float3(0, 0, 0),
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
                                SAMPLE_TEXTURE3D(
                                    _ObstacleTex,
                                    sampler_ObstacleTex,
                                    sampleUV).r;

                            if (obstacle > 0.5)
                            {
                                temp +=
                                    SAMPLE_TEXTURE3D(
                                        _SolidTemperatureTex,
                                        sampler_SolidTemperatureTex,
                                        sampleUV).r
                                    * w;

                                weight += w;
                            }
                        }
                    }
                }

                if (weight < 0.0001)
                {
                    return SAMPLE_TEXTURE3D(
                        _SolidTemperatureTex,
                        sampler_SolidTemperatureTex,
                        uvw).r;
                }

                return temp / weight;
            }

            float4 frag(
                v2f i)
                : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

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

                float alpha =
                    smoothstep(
                        0.2,
                        0.8,
                        heat);

                return float4(
                    colour * (1 + glow),
                    alpha);
            }

            ENDHLSL
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
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vertDepth(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

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
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

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
