Shader "Custom/HeatVolume" {
    Properties { _TemperatureTex("Temperature", 3D) =
                     "" {} _Density("Density", Float) =
                         1 _MaxTemperature("Max Temperature", Float) = 500 }

    SubShader {
        Tags { "RenderPipeline" =
                   "UniversalPipeline" "Queue" =
                       "Transparent" "RenderType" = "Transparent" }

        Blend SrcAlpha OneMinusSrcAlpha Cull Front ZWrite Off ZTest Always

            Pass {
            Name "Forward"

                Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM

#pragma vertex vert
#pragma fragment frag

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

                sampler3D _TemperatureTex;

            float _Density;
            float _MaxTemperature;

            float2 RayBoxIntersection(
                float3 rayOrigin,
                float3 rayDir,
                float3 boxMin,
                float3 boxMax)
            {
                float3 invDir = 1.0 / rayDir;

                float3 t0 = (boxMin - rayOrigin) * invDir;
                float3 t1 = (boxMax - rayOrigin) * invDir;

                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);

                float nearHit =
                    max(max(tmin.x, tmin.y), tmin.z);

                float farHit =
                    min(min(tmax.x, tmax.y), tmax.z);

                return float2(nearHit, farHit);
            }

            struct appdata {
                float4 vertex : POSITION;
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float3 localPos : TEXCOORD0;
                float3 worldPos : TEXCOORD1;

                float4 screenPos : TEXCOORD2;
            };

            v2f vert(appdata v) {
                v2f o;

                o.pos = TransformObjectToHClip(v.vertex.xyz);

                o.localPos = v.vertex.xyz + 0.5;

                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                o.screenPos = ComputeScreenPos(o.pos);

                return o;
            }

            float3 ThermalPalette(float heat) {
                if (heat < 0.2) {
                    return lerp(float3(0, 0, 0), float3(0.2, 0, 0.4),
                                heat / 0.2);
                }

                if (heat < 0.4) {
                    return lerp(float3(0.2, 0, 0.4), float3(0.8, 0, 0),
                                (heat - 0.2) / 0.2);
                }

                if (heat < 0.6) {
                    return lerp(float3(0.8, 0, 0), float3(1, 0.4, 0),
                                (heat - 0.4) / 0.2);
                }

                if (heat < 0.8) {
                    return lerp(float3(1, 0.4, 0), float3(1, 1, 0),
                                (heat - 0.6) / 0.2);
                }

                return lerp(float3(1, 1, 0), float3(1, 1, 1),
                            (heat - 0.8) / 0.2);
            }

            float4 frag(v2f i) : SV_Target {
                float3 rayOrigin =
                    mul(unity_WorldToObject,
                        float4(_WorldSpaceCameraPos, 1)).xyz
                    + 0.5;

                float3 rayDir =
                    normalize(i.localPos - rayOrigin);

                float2 hit =
                    RayBoxIntersection(
                        rayOrigin,
                        rayDir,
                        float3(0,0,0),
                        float3(1,1,1));

                if (hit.x > hit.y)
                {
                    discard;
                }

                float entryDistance =
                    max(hit.x, 0.0001);

                float exitDistance =
                    hit.y;

                float travelDistance =
                    exitDistance - entryDistance;

                float3 samplePos =
                    rayOrigin +
                    rayDir * entryDistance;

                float stepSize =
                    travelDistance / 512.0;

                float maxHeat = 0;

                [loop]
                for (int step = 0; step < 512; step++) {
                    float marchedDistance =
                        step * stepSize;

                    if (marchedDistance >= travelDistance)
                    {
                        break;
                    }

                    float3 sampleLocal = samplePos - 0.5;

                    float3 sampleWorld =
                        mul(unity_ObjectToWorld, float4(sampleLocal, 1)).xyz;

                    float4 clipPos = TransformWorldToHClip(sampleWorld);

                    float2 sampleUV = clipPos.xy / clipPos.w;

                    sampleUV = sampleUV * 0.5 + 0.5;

                    sampleUV.y = 1.0 - sampleUV.y;

                    sampleUV = saturate(sampleUV);

                    float rawSceneDepth = SampleSceneDepth(sampleUV);

                    float sceneDepth =
                        LinearEyeDepth(rawSceneDepth, _ZBufferParams);

                    float sampleEyeDepth = -TransformWorldToView(sampleWorld).z;

                    if (sampleEyeDepth > sceneDepth + 1.0)
                    {
                        break;
                    }

                    float temp = tex3D(_TemperatureTex, samplePos).r;

                    float heat = saturate(pow(temp / _MaxTemperature, 0.5));

                    maxHeat = max(maxHeat, heat);

                    samplePos += rayDir * stepSize;

                    if (any(samplePos < 0) || any(samplePos > 1)) {
                        break;
                    }
                }

                float3 color = ThermalPalette(maxHeat);

                float alpha = pow(maxHeat, 0.7) * _Density;

                return float4(color, alpha);
            }

            ENDHLSL
        }
    }
}