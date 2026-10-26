Shader "Custom/SmokeVolumeStandalone"
{
    // The shader SmokeSim draws with (its smokeMaterial). Same property names
    // as before, so the material and SmokeSim.BindRenderTextures need no
    // changes. Rebuilt on VolumeCommon.hlsl for Quest, with sub-voxel flame
    // detail to hide the sim grid.
    Properties
    {
        _SmokeTex   ("Smoke",           3D)    = "" {}
        _FireTex    ("Fire",            3D)    = "" {}

        _Density         ("Smoke Density",     Float) = 3
        _SmokeColorLow   ("Smoke Color Thin",  Color) = (0.15, 0.15, 0.15, 1)
        _SmokeColorHigh  ("Smoke Color Thick", Color) = (0.4,  0.4,  0.4,  1)

        _FireBrightness  ("Fire Brightness",   Float) = 14
        _FireColorLow    ("Fire Color (Cool)", Color) = (1.0, 0.15, 0.0, 1)
        _FireColorMid    ("Fire Color (Mid)",  Color) = (1.0, 0.65, 0.1, 1)
        _FireColorHigh   ("Fire Color (Hot)",  Color) = (1.0, 1.0,  0.8, 1)

        // Soot inside the reaction zone is incandescent -- emitting, not
        // blocking. 1 = a fully luminous cell is completely transparent.
        _SootIncandescence ("Soot Incandescence", Range(0,1)) = 0.85

        // Flame light scattered by nearby soot.
        _FireScatter ("Fire In-Scatter", Range(0,4)) = 1.2

        [Header(Flame Detail)]
        // Bends the flame sideways before sampling it, metres. This is what
        // hides the voxel grid: it curves the straight, blocky columns into
        // wavering tongues. About one sim cell (0.2 m). 0 turns it off.
        _DetailWarp("Warp Amount (m)", Range(0, 0.5)) = 0.18
        // Size of the warp's swirls, metres.
        _DetailWarpScale("Warp Size (m)", Range(0.05, 1)) = 0.3
        // Fine animated breakup of the flame edges. 0 turns it off.
        _DetailStrength("Detail Strength", Range(0, 1.5)) = 0.8
        // Size of the finest features, metres.
        _DetailScale("Detail Size (m)", Range(0.02, 0.5)) = 0.08
        // How fast the detail rises, metres per second.
        _DetailSpeed("Detail Rise Speed (m/s)", Range(0, 5)) = 1.5
        // >1 stretches the detail into vertical streaks (tongues, not blobs).
        _DetailStretch("Detail Vertical Stretch", Range(1, 4)) = 2.0
        // 1 = detail only eats into faint edges; 0 = breaks up everything.
        _DetailCoreProtect("Protect Core", Range(0, 1)) = 0.4

        [Header(Performance)]
        // Replaces the old fixed Ray Steps (128). Steps now follow how far
        // the ray actually travels through the box.
        _StepWorld("Step Length (m)", Range(0.03, 0.5)) = 0.1
        _MaxSteps("Max Steps", Range(8, 256)) = 64
        _DepthSlack("Depth Slack (m)", Float) = 0.02
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }

        // Premultiplied alpha: RGB is already transmittance-weighted.
        Blend One OneMinusSrcAlpha
        Cull Front
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "VolumeCommon.hlsl"

            sampler3D _SmokeTex;
            sampler3D _FireTex;

            float  _Density;
            float4 _SmokeColorLow;
            float4 _SmokeColorHigh;

            float  _FireBrightness;
            float4 _FireColorLow;
            float4 _FireColorMid;
            float4 _FireColorHigh;

            float _SootIncandescence;
            float _FireScatter;

            float _DetailWarp;
            float _DetailWarpScale;
            float _DetailStrength;
            float _DetailScale;
            float _DetailSpeed;
            float _DetailStretch;
            float _DetailCoreProtect;

            float3 FireColor(float f)
            {
                if (f < 0.5)
                    return lerp(_FireColorLow.rgb, _FireColorMid.rgb, f * 2.0);
                return lerp(_FireColorMid.rgb, _FireColorHigh.rgb, (f - 0.5) * 2.0);
            }

            // Hash without sin(): sin-based hashes band on mobile GPUs.
            float Hash13(float3 p3)
            {
                p3 = frac(p3 * 0.1031);
                p3 += dot(p3, p3.zyx + 31.32);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ValueNoise3(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash13(i);
                float n100 = Hash13(i + float3(1, 0, 0));
                float n010 = Hash13(i + float3(0, 1, 0));
                float n110 = Hash13(i + float3(1, 1, 0));
                float n001 = Hash13(i + float3(0, 0, 1));
                float n101 = Hash13(i + float3(1, 0, 1));
                float n011 = Hash13(i + float3(0, 1, 1));
                float n111 = Hash13(i + float3(1, 1, 1));

                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            // Sideways (xz) displacement for the fire lookup, world metres.
            float3 FireWarp(float3 worldPos, float t)
            {
                float3 q = worldPos / max(_DetailWarpScale, 1e-3);
                q.y = q.y * 0.5 - t * _DetailSpeed / max(_DetailWarpScale, 1e-3);

                float wx = ValueNoise3(q) - 0.5;
                float wz = ValueNoise3(q + float3(31.7, 11.3, 57.9)) - 0.5;
                return float3(wx, 0.0, wz) * (2.0 * _DetailWarp);
            }

            // Two octaves of rising, stretched noise, roughly -0.5..0.5.
            float FireDetail(float3 worldPos, float t)
            {
                float3 q = worldPos / max(_DetailScale, 1e-3);
                q.y = q.y / _DetailStretch - t * _DetailSpeed / max(_DetailScale, 1e-3);

                float n = ValueNoise3(q) * 0.65
                        + ValueNoise3(q * 2.03 + float3(17.1, 5.3, 41.7)) * 0.35;
                return n - 0.5;
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                // Occlusion is resolved ONCE here (scene depth -> max ray
                // length), instead of a world->view transform every step.
                MarchSetup m;
                if (!SetupMarch(i, m))
                    discard;

                float3 pos = m.startPos;

                float3 wp    = mul(unity_ObjectToWorld, float4(m.startPos - 0.5, 1)).xyz;
                float3 wStep = mul((float3x3)unity_ObjectToWorld, m.rayDir * m.stepSize);
                float  t     = fmod(_Time.y, 1000.0);

                // Step length as a fraction of the box, matching the units
                // the material's brightness and density were tuned in.
                float stepUvw = m.stepSize;

                float3 accum = 0;
                float  transmittance = 1.0;

                [loop]
                for (int s = 0; s < m.steps; s++)
                {
                    float4 uvw    = float4(pos, 0);
                    float  smokeD = tex3Dlod(_SmokeTex, uvw).r;
                    float  fireD  = tex3Dlod(_FireTex,  uvw).r;

                    // --- flame detail: only near flame, so empty air is free ---
                    if (fireD > 0.002)
                    {
                        if (_DetailWarp > 0.0)
                        {
                            float3 offUvw = mul((float3x3)unity_WorldToObject, FireWarp(wp, t));
                            fireD = tex3Dlod(_FireTex, float4(pos + offUvw, 0)).r;
                        }

                        if (_DetailStrength > 0.0 && fireD > 0.001)
                        {
                            float edge = lerp(1.0, saturate(1.0 - fireD), _DetailCoreProtect);
                            fireD = saturate(fireD + FireDetail(wp, t) * _DetailStrength * edge);
                        }
                    }

                    // --- emission: passes through only what is in front ---
                    if (fireD > 0.001)
                    {
                        float edgeBoost = smoothstep(0.0, 0.15, fireD);

                        // stepUvw, not metres: _FireBrightness and _Density
                        // were tuned against the old shader, whose step was
                        // a fraction of the BOX (0..1). The box is room-sized,
                        // so a step in metres is several times larger and
                        // blew everything out to saturated yellow.
                        float3 fc = FireColor(fireD) * fireD
                                  * _FireBrightness * stepUvw * edgeBoost;

                        accum += fc * transmittance;
                    }

                    // --- absorption: affects everything behind ---
                    if (smokeD > 0.005)
                    {
                        float3 sc = lerp(_SmokeColorLow.rgb, _SmokeColorHigh.rgb,
                                         saturate(smokeD));

                        float sa = saturate(smokeD * stepUvw * _Density * 4.0);

                        // Glowing soot in the reaction zone does not occlude.
                        sa *= saturate(1.0 - fireD * _SootIncandescence);

                        // In-scattering: soot next to a flame is lit by it.
                        if (fireD > 0.001)
                            sc += FireColor(fireD) * fireD * _FireScatter;

                        accum += sc * sa * transmittance;
                        transmittance *= (1.0 - sa);

                        if (transmittance < 0.002) break;
                    }

                    pos += m.rayDir * m.stepSize;
                    wp  += wStep;
                }

                return float4(accum, 1.0 - transmittance);
            }
            ENDHLSL
        }
    }
}
