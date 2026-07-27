Shader "Custom/SmokeVolume" {
    Properties { _SmokeTex("Smoke", 3D) = "" {} _Density("Density", Float) =
                     1 _FireGlow("Fire Glow", Float) = 0.4 }

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

            sampler3D _SmokeTex;
            sampler3D _FlameTex;
            sampler3D _TemperatureTex;

            float _Density;

            float _FireGlow;

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

            float3 BlackbodyPalette(float heat) {
                if (heat < 0.25) {
                    return lerp(float3(0.15, 0.0, 0.0), float3(0.8, 0.0, 0.0),
                                heat / 0.25);
                }

                if (heat < 0.5) {
                    return lerp(float3(0.8, 0.0, 0.0), float3(1.0, 0.35, 0.0),
                                (heat - 0.25) / 0.25);
                }

                if (heat < 0.75) {
                    return lerp(float3(1.0, 0.35, 0.0), float3(1.0, 0.85, 0.0),
                                (heat - 0.5) / 0.25);
                }

                return lerp(float3(1.0, 0.85, 0.0), float3(1.0, 1.0, 1.0),
                            (heat - 0.75) / 0.25);
            }

            float4 frag(v2f i) : SV_Target {
                float3 rayOrigin =
                    mul(unity_WorldToObject,
                        float4(_WorldSpaceCameraPos,1)).xyz
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
                    max(hit.x, 0);

                float exitDistance =
                    hit.y;

                float travelDistance =
                    exitDistance - entryDistance;

                float3 samplePos =
                    rayOrigin +
                    rayDir * entryDistance;

                float stepSize = travelDistance / 512.0;

                float3 accumColor = float3(0, 0, 0);

                float accumAlpha = 0;

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

                    bool occluded = sampleEyeDepth > sceneDepth + 0.02;

                    if (occluded) {
                        samplePos += rayDir * stepSize;
                        continue;
                    }

                    float smoke = tex3D(_SmokeTex, samplePos).r;

                    float flame = tex3D(_FlameTex, samplePos).r;

                    float temp = tex3D(_TemperatureTex, samplePos).r;

                    float density = saturate(smoke);

                    float3 smokeColor = lerp(float3(0.00, 0.00, 0.00),
                                             float3(0.01, 0.01, 0.01), density);

                    float heatGlow = saturate(temp / 1162.34);

                    float glow = heatGlow * density * _FireGlow;

                    float3 glowColor = BlackbodyPalette(heatGlow) * glow;

                    float3 color = smokeColor + glowColor;

                    float alpha = pow(density, 0.7) * 0.15 * _Density;

                    accumColor += color * alpha * (1 - accumAlpha);

                    accumAlpha += alpha * (1 - accumAlpha);

                    if (accumAlpha > 0.99) {
                        break;
                    }

                    samplePos += rayDir * stepSize;
                }

                return float4(accumColor, accumAlpha);
            }

            ENDHLSL
        }
    }
}