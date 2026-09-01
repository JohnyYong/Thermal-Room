Shader "Custom/FireHeatVolume" {
    Properties {
        _FireHeatTex("Fire Heat", 3D) = "" {}
        _Density("Density", Float) = 1
        _MaxTemperature("Max Temperature", Float) = 500

        [Header(Shell Noise)]
        _NoiseScale("Noise Scale", Float) = 6
        _NoiseStrength("Noise Strength", Float) = 0.03
        _NoiseSpeed("Noise Speed", Float) = 1

        [Header(Thermal Palette Colors)]
        _ColorCold("Cold (heat = 0)", Color) = (0, 0, 0, 1)
        _ColorWhite("White", Color) = (1, 1, 1, 1)
        _ColorYellow("Yellow", Color) = (1, 1, 0, 1)
        _ColorOrange("Orange", Color) = (1, 0.5, 0, 1)
        _ColorRed("Red", Color) = (1, 0, 0, 1)
        _ColorDarkRed("Dark Red (heat = 1)", Color) = (0.35, 0, 0, 1)

        [Header(Thermal Palette Thresholds)]
        _Threshold1("Cold -> White ends at", Range(0.001, 0.999)) = 0.12
        _Threshold2("White -> Yellow ends at", Range(0.001, 0.999)) = 0.28
        _Threshold3("Yellow -> Orange ends at", Range(0.001, 0.999)) = 0.58
        _Threshold4("Orange -> Red ends at", Range(0.001, 0.999)) = 0.85
        // Red -> Dark Red always runs from Threshold4 to 1.0
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
            float _MaxTemperature;

            float _NoiseScale;
            float _NoiseStrength;
            float _NoiseSpeed;

            float4 _ColorCold;
            float4 _ColorWhite;
            float4 _ColorYellow;
            float4 _ColorOrange;
            float4 _ColorRed;
            float4 _ColorDarkRed;

            float _Threshold1;
            float _Threshold2;
            float _Threshold3;
            float _Threshold4;

            // --- Shell-warp noise (ported from SmokeSim.compute) ---
            // Perturbs WHERE we sample the heat volume from, per raymarch
            // step, so the rendered silhouette gets its own independent
            // wobble on top of whatever turbulence the sim data already
            // has. This is a forward lookup (not semi-Lagrangian backward
            // advection), so unlike SmokeSim's Step/StepFire kernels there
            // is no sign inversion to worry about here -- either sign
            // produces an equally valid wobble.
            float Hash3(float3 p)
            {
                return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453);
            }

            float ValueNoise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash3(i + float3(0, 0, 0));
                float n100 = Hash3(i + float3(1, 0, 0));
                float n010 = Hash3(i + float3(0, 1, 0));
                float n110 = Hash3(i + float3(1, 1, 0));
                float n001 = Hash3(i + float3(0, 0, 1));
                float n101 = Hash3(i + float3(1, 0, 1));
                float n011 = Hash3(i + float3(0, 1, 1));
                float n111 = Hash3(i + float3(1, 1, 1));

                float x00 = lerp(n000, n100, f.x);
                float x10 = lerp(n010, n110, f.x);
                float x01 = lerp(n001, n101, f.x);
                float x11 = lerp(n011, n111, f.x);
                float y0  = lerp(x00, x10, f.y);
                float y1  = lerp(x01, x11, f.y);
                return lerp(y0, y1, f.z);
            }

            float FBM3(float3 p)
            {
                float total = 0.0;
                float amp = 0.5;
                float freq = 1.0;
                for (int i = 0; i < 3; i++)
                {
                    total += ValueNoise3(p * freq) * amp;
                    freq *= 2.1;
                    amp  *= 0.5;
                }
                return total;
            }

            float3 ShellWarp(float3 localPos, float time)
            {
                float3 q = localPos * _NoiseScale + float3(
                    time * _NoiseSpeed *  0.3,
                    time * _NoiseSpeed * -0.6,
                    time * _NoiseSpeed *  0.3);

                float nx = FBM3(q + float3( 13.1,  47.3,  91.7)) - 0.5;
                float ny = FBM3(q + float3( 71.9, 101.9,  17.3)) - 0.5;
                float nz = FBM3(q + float3( 29.4, -33.7,  55.9)) - 0.5;

                return float3(nx, ny, nz);
            }

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

            // Bands now read their endpoint colors and thresholds straight
            // from material properties, so the whole ramp is tunable from
            // the Inspector without touching this file. Thresholds are
            // clamped/ordered defensively in case someone drags Threshold2
            // below Threshold1 etc. in the Inspector -- avoids a
            // divide-by-(near)zero or an inverted band if that happens.
            float3 ThermalPalette(float heat) {
                float t1 = _Threshold1;
                float t2 = max(_Threshold2, t1 + 0.001);
                float t3 = max(_Threshold3, t2 + 0.001);
                float t4 = max(_Threshold4, t3 + 0.001);

                if (heat < t1) {
                    return lerp(_ColorCold.rgb, _ColorWhite.rgb, heat / t1);
                }

                if (heat < t2) {
                    return lerp(_ColorWhite.rgb, _ColorYellow.rgb,
                                (heat - t1) / (t2 - t1));
                }

                if (heat < t3) {
                    return lerp(_ColorYellow.rgb, _ColorOrange.rgb,
                                (heat - t2) / (t3 - t2));
                }

                if (heat < t4) {
                    return lerp(_ColorOrange.rgb, _ColorRed.rgb,
                                (heat - t3) / (t4 - t3));
                }

                return lerp(_ColorRed.rgb, _ColorDarkRed.rgb,
                            (heat - t4) / (1.0 - t4));
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

                    // Shell warp: displace the LOOKUP position (not the
                    // underlying data) so the rendered boundary of the heat
                    // volume gets its own independent wobble every frame,
                    // on top of whatever turbulence is already baked into
                    // _FireHeatTex from the compute sim. Small strength --
                    // this is in unit-box (0..1) space, so 0.03 is already
                    // a few percent of the whole volume's extent.
                    float3 warpedSamplePos = samplePos;
                    if (_NoiseStrength > 0.0001)
                    {
                        float3 warp = ShellWarp(sampleLocal, _Time.y);
                        warpedSamplePos = saturate(samplePos + warp * _NoiseStrength);
                    }

                    float temp = tex3D(_FireHeatTex, warpedSamplePos).r;

                    float heat = saturate(pow(temp / _MaxTemperature, 0.5));

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
