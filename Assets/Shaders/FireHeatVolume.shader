Shader "Custom/FireHeatVolume" {
    Properties
    {
        _FireHeatTex("Fire Heat", 3D) = "" {}
        _Density("Density", Float) = 1

        // Bottom of the mapped range. Anything at or below this reads as
        // the coldest palette colour, so it should sit at room ambient
        // rather than absolute zero.
        _MinTemperature("Min Temperature (C)", Float) = 20

        // Top of the mapped range. MUST sit above ThermalSimulation's
        // Flame Gas Temp C (1000 by default) or every flame pins to the
        // darkest colour and the whole core renders as one flat blob --
        // at the old value of 500 anything hotter than 500C was
        // indistinguishable from anything hotter than 3000C.
        _MaxTemperature("Max Temperature (C)", Float) = 1200

        // Shapes the ramp between min and max. 1 = linear. Below 1 pushes
        // detail into the cool end (the old hard-coded pow 0.5), above 1
        // into the hot end. 0.75 keeps warm objects readable without
        // throwing away the whole top of the range.
        _HeatGamma("Heat Gamma", Range(0.25, 3)) = 0.75
    }

    SubShader {
        Tags { "RenderPipeline" =
                   "UniversalPipeline" "Queue" =
                       "Transparent+1" "RenderType" = "Transparent" }

        Blend SrcAlpha OneMinusSrcAlpha Cull Front ZWrite Off ZTest Always

            Pass {
            Name "Forward"

                Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM

#pragma vertex vert
#pragma fragment frag

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

                sampler3D _FireHeatTex;

            float _Density;
            float _MinTemperature;
            float _MaxTemperature;
            float _HeatGamma;

            float2 RayBoxIntersection(
                float3 rayOrigin,
                float3 rayDir,
                float3 boxMin,
                float3 boxMax)
            {
                // Guard against zero (or near-zero) direction components.
                // 1.0/0 = Inf, and (0)*Inf = NaN, which poisons the whole
                // ray -> ThermalPalette(NaN) -> solid black. The Editor's
                // shader compiler tolerated this; the DX12 build does not.
                // Clamp each component away from 0 while preserving its sign.
                float3 safeDir;
                safeDir.x = (rayDir.x >= 0.0)
                    ? max(rayDir.x, 1e-6) : min(rayDir.x, -1e-6);
                safeDir.y = (rayDir.y >= 0.0)
                    ? max(rayDir.y, 1e-6) : min(rayDir.y, -1e-6);
                safeDir.z = (rayDir.z >= 0.0)
                    ? max(rayDir.z, 1e-6) : min(rayDir.z, -1e-6);

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

            float3 ThermalPalette(float heat) {
                if (heat < 0.2) {
                    // Black -> White
                    return lerp(float3(0, 0, 0), float3(1, 1, 1), heat / 0.2);
                }

                if (heat < 0.4) {
                    // White -> Yellow
                    return lerp(float3(1, 1, 1), float3(1, 1, 0),
                                (heat - 0.2) / 0.2);
                }

                if (heat < 0.6) {
                    // Yellow -> Orange
                    return lerp(float3(1, 1, 0), float3(1, 0.5, 0),
                                (heat - 0.4) / 0.2);
                }

                if (heat < 0.8) {
                    // Orange -> Red
                    return lerp(float3(1, 0.5, 0), float3(1, 0, 0),
                                (heat - 0.6) / 0.2);
                }

                // Red -> Dark Red.
                //
                // The endpoint was (0.35, 0, 0). Against the saturated
                // yellow and orange of the surrounding bands that reads as
                // black, not as dark red -- simultaneous contrast does the
                // rest. Lifting it to 0.55 with a touch of green and blue
                // keeps it unmistakably RED while still being clearly the
                // darkest, hottest end of the ramp.
                return lerp(float3(1, 0, 0), float3(0.55, 0.06, 0.06),
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

                // farHit behind the camera => box entirely behind us.
                if (hit.y < 0.0)
                {
                    discard;
                }

                // Ray misses the box entirely.
                if (hit.x > hit.y)
                {
                    discard;
                }

                // When the camera is INSIDE the box, nearHit (hit.x) is
                // negative -- entry is behind us. Start marching from the
                // camera itself (0) in that case instead of from a bogus
                // behind-camera entry point. This is what was degenerating
                // in the build when the drone flew inside the heat volume
                // and producing the black region.
                float entryDistance =
                    max(hit.x, 0.0);

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

                    // A raw depth of ~0 (reversed-Z far plane / sky / an
                    // unwritten depth pixel) means "nothing solid here" --
                    // do NOT treat that as an occluder, or downward rays over
                    // the floor can break on step 0 and render pure black.
                    // Only apply occlusion when we have a real depth value.
                    if (rawSceneDepth > 0.0)
                    {
                        float sceneDepth =
                            LinearEyeDepth(rawSceneDepth, _ZBufferParams);

                        float sampleEyeDepth =
                            -TransformWorldToView(sampleWorld).z;

                        if (sampleEyeDepth > sceneDepth + 1.0)
                        {
                            break;
                        }
                    }

                    float temp = tex3D(_FireHeatTex, samplePos).r;

                    // Normalise across the ACTUAL mapped band rather than
                    // from absolute zero. The old form was
                    // pow(temp / _MaxTemperature, 0.5), which put 20C
                    // ambient at 0.2 (pure white on this ramp) and drove
                    // everything above _MaxTemperature to a single value.
                    //
                    // saturate() before pow() matters: pow() of a negative
                    // base is undefined and returns NaN on some targets,
                    // and a NaN here propagates through max() into the
                    // palette. The existing !(maxHeat > 0) backstop below
                    // catches it, but only after the fact.
                    float t = saturate(
                        (temp - _MinTemperature) /
                        max(_MaxTemperature - _MinTemperature, 1e-3));

                    float heat = pow(t, _HeatGamma);

                    maxHeat = max(maxHeat, heat);

                    samplePos += rayDir * stepSize;
                }

                // Final NaN backstop: if anything upstream still produced a
                // NaN, `!(maxHeat > 0)` is true for NaN, so we zero it. That
                // yields alpha 0 (transparent) instead of a solid black blob,
                // so a stray bad pixel shows the greyscale surface behind it
                // rather than punching a black hole in the thermal view.
                if (!(maxHeat > 0.0))
                {
                    maxHeat = 0.0;
                }

                maxHeat = saturate(maxHeat);

                float3 color = ThermalPalette(maxHeat);

                float alpha = pow(maxHeat, 0.7) * _Density;

                return float4(color, alpha);
            }

            ENDHLSL
        }
    }
}