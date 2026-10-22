Shader "Custom/FireVolume" {
    Properties
    {
        _FlameTex("Flame", 3D) = "" {}
        _SmokeTex("Smoke", 3D) = "" {}

        // Overall multiplier, kept so existing materials keep their value.
        _Density("Density", Float) = 1

        // Smoke extinction per metre at full smoke density. Smoke between
        // the camera and the flame dims it.
        _SmokeExtinction("Smoke Extinction (1/m)", Float) = 8

        // Flame values below this are treated as empty air. The old shader
        // used smoothstep(0.05, 1) followed by pow(0.5), which turned the
        // faintest trace of flame into a visible glow -- that halo is what
        // filled in the gaps between tongues and rounded every fire off
        // into a dome.
        _FlameThreshold("Flame Threshold", Range(0, 0.9)) = 0.15

        // Contrast above the threshold. >1 keeps the edge of each tongue
        // dim and the core bright, so the shape reads from the brightness
        // falloff rather than from a hard silhouette.
        _FlameSharpness("Flame Sharpness", Range(0.5, 4)) = 1.6

        // Light emitted per metre of flame. Flame is a light source, not a
        // coloured fog: this is what makes it bright, independent of how
        // opaque it is.
        _Emission("Emission (per m)", Float) = 3

        // How much a metre of flame hides what's behind it. Real flame is
        // mostly transparent; keep this low so overlapping tongues add up
        // instead of the front one painting over the rest.
        _FlameOpacity("Flame Opacity (per m)", Float) = 2
    }

    SubShader {
        Tags { "RenderPipeline" = "UniversalPipeline"
               "Queue" = "Transparent"
               "RenderType" = "Transparent" }

        // Premultiplied: colour is emitted light, alpha is how much of the
        // background the flame (and the smoke in it) blocks.
        Blend One OneMinusSrcAlpha
        Cull Front ZWrite Off ZTest Always

        Pass {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            sampler3D _FlameTex;
            sampler3D _SmokeTex;

            float _Density;
            float _SmokeExtinction;
            float _FlameThreshold;
            float _FlameSharpness;
            float _Emission;
            float _FlameOpacity;

            float2 RayBoxIntersection(
                float3 rayOrigin,
                float3 rayDir,
                float3 boxMin,
                float3 boxMax)
            {
                // Clamp each direction component away from 0 (keeping its
                // sign) so 1/0 can't produce Inf and then NaN.
                float3 safeDir;
                safeDir.x = (rayDir.x >= 0.0) ? max(rayDir.x, 1e-6) : min(rayDir.x, -1e-6);
                safeDir.y = (rayDir.y >= 0.0) ? max(rayDir.y, 1e-6) : min(rayDir.y, -1e-6);
                safeDir.z = (rayDir.z >= 0.0) ? max(rayDir.z, 1e-6) : min(rayDir.z, -1e-6);

                float3 invDir = 1.0 / safeDir;

                float3 t0 = (boxMin - rayOrigin) * invDir;
                float3 t1 = (boxMax - rayOrigin) * invDir;

                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);

                float nearHit = max(max(tmin.x, tmin.y), tmin.z);
                float farHit  = min(min(tmax.x, tmax.y), tmax.z);

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

            // t = shaped flame intensity, 0..1. Deep red at the faint edge,
            // orange through the body, yellow-white only in the core.
            float3 FlameColor(float t)
            {
                float3 c = lerp(float3(0.55, 0.04, 0.00), float3(1.00, 0.35, 0.02), saturate(t / 0.4));
                c = lerp(c, float3(1.00, 0.72, 0.22), saturate((t - 0.4) / 0.35));
                c = lerp(c, float3(1.00, 0.95, 0.80), saturate((t - 0.75) / 0.25));
                return c;
            }

            float4 frag(v2f i) : SV_Target {
                float3 rayOrigin =
                    mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz + 0.5;

                float3 rayDir = normalize(i.localPos - rayOrigin);

                float2 hit = RayBoxIntersection(rayOrigin, rayDir,
                                                float3(0, 0, 0), float3(1, 1, 1));

                if (hit.y < 0.0 || hit.x > hit.y)
                {
                    discard;
                }

                // Camera inside the box: start at the camera, not behind it.
                float entryDistance  = max(hit.x, 0.0);
                float exitDistance   = hit.y;
                float travelDistance = exitDistance - entryDistance;

                float3 samplePos = rayOrigin + rayDir * entryDistance;

                float stepSize = travelDistance / 512.0;

                // World length of one step. Emission and opacity are per
                // metre, so the look no longer depends on how big the box
                // is or how far the ray travels through it.
                float stepWorld =
                    length(mul((float3x3)unity_ObjectToWorld, rayDir * stepSize));

                float3 accumColor   = 0;
                float  transmittance = 1.0;

                [loop]
                for (int step = 0; step < 512; step++)
                {
                    if (step * stepSize >= travelDistance)
                        break;

                    float3 sampleLocal = samplePos - 0.5;
                    float3 sampleWorld =
                        mul(unity_ObjectToWorld, float4(sampleLocal, 1)).xyz;

                    float4 clipPos  = TransformWorldToHClip(sampleWorld);
                    float2 sampleUV = clipPos.xy / clipPos.w;
                    sampleUV = sampleUV * 0.5 + 0.5;
                    sampleUV.y = 1.0 - sampleUV.y;
                    sampleUV = saturate(sampleUV);

                    float rawSceneDepth = SampleSceneDepth(sampleUV);
                    float sceneDepth = LinearEyeDepth(rawSceneDepth, _ZBufferParams);
                    float sampleEyeDepth = -TransformWorldToView(sampleWorld).z;

                    // Depth only increases along the ray, so once a sample
                    // is behind geometry every later one is too.
                    if (sampleEyeDepth > sceneDepth + 0.02)
                        break;

                    float flame = tex3D(_FlameTex, samplePos).r;
                    float smoke = tex3D(_SmokeTex, samplePos).r;

                    // Smoke in front dims everything behind it.
                    transmittance *= exp(-smoke * _SmokeExtinction * stepWorld);

                    // Threshold, then contrast. Below the threshold is air.
                    float f = saturate((flame - _FlameThreshold) /
                                       max(1.0 - _FlameThreshold, 1e-3));
                    f = pow(f, _FlameSharpness);

                    if (f > 0.0)
                    {
                        // Emission: light added, dimmed by whatever is
                        // already in front of this point.
                        accumColor += FlameColor(f) * f * _Emission * _Density *
                                      stepWorld * transmittance;

                        // Absorption: flame blocks a little of what is behind.
                        transmittance *= exp(-f * _FlameOpacity * stepWorld);
                    }

                    if (transmittance < 0.01)
                        break;

                    samplePos += rayDir * stepSize;
                }

                return float4(accumColor, 1.0 - transmittance);
            }

            ENDHLSL
        }
    }
}
