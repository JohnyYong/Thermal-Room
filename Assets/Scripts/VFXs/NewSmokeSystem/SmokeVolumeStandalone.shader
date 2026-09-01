Shader "Custom/SmokeVolumeStandalone"
{
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

        // Soot sitting inside the reaction zone is incandescent -- it is
        // emitting, not blocking. Only cooled soot downstream reads as
        // opaque. This is how much flame suppresses smoke opacity in the
        // same cell. 1 = a fully luminous cell is completely transparent.
        _SootIncandescence ("Soot Incandescence", Range(0,1)) = 0.85

        // Flame light scattered by nearby soot. A real smoke plume glows
        // orange near the fire because it is being lit by it, not because
        // it is burning. This also means the flame reads through a thin
        // layer of its own plume instead of vanishing behind it.
        _FireScatter ("Fire In-Scatter", Range(0,4)) = 1.2

        _StepCount       ("Ray Steps",         Range(32, 512)) = 128
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }

        // PREMULTIPLIED ALPHA.
        //
        // This was SrcAlpha OneMinusSrcAlpha, which multiplies the returned
        // RGB by the returned alpha at blend time. Both the fire and smoke
        // terms below are already weighted by transmittance as they are
        // accumulated, so that second multiply applied the alpha twice --
        // and for emissive fire it is wrong in principle regardless. Light
        // emitted by a flame is not attenuated by the flame's own opacity,
        // so thin fire was being dimmed toward nothing and fire behind any
        // smoke was multiplied away entirely.
        Blend One OneMinusSrcAlpha
        Cull Front
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Enables stereo instancing so this shader runs once per vertex/
            // fragment across BOTH eyes on Quest. Without this the shader
            // renders to one eye only, leaving the other blank.
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

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
            float _StepCount;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 localPos : TEXCOORD0;
                float4 screenPos: TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;

                // Required for stereo -- sets up the eye index so subsequent
                // matrix multiplications use the correct per-eye view/proj.
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.localPos = v.vertex.xyz + 0.5;
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            float2 RayBox(float3 ro, float3 rd, float3 bmin, float3 bmax)
            {
                float3 inv = 1.0 / rd;
                float3 t0 = (bmin - ro) * inv;
                float3 t1 = (bmax - ro) * inv;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);
                return float2(max(max(tmin.x, tmin.y), tmin.z),
                              min(min(tmax.x, tmax.y), tmax.z));
            }

            float3 FireColor(float f)
            {
                if (f < 0.5)
                    return lerp(_FireColorLow.rgb, _FireColorMid.rgb, f * 2.0);
                return lerp(_FireColorMid.rgb, _FireColorHigh.rgb, (f - 0.5) * 2.0);
            }

            float4 frag(v2f i) : SV_Target
            {
                // Required in the fragment shader too -- otherwise camera
                // pos and view matrices resolve to whichever eye was last
                // set globally, breaking the second eye.
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 ro = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz + 0.5;
                float3 rd = normalize(i.localPos - ro);

                float2 hit = RayBox(ro, rd, float3(0,0,0), float3(1,1,1));
                if (hit.x > hit.y) discard;

                float entry  = max(hit.x, 0);
                float travel = hit.y - entry;
                int   steps  = (int)_StepCount;
                float step   = travel / steps;

                // Per-eye scene depth sample. SampleSceneDepth already
                // handles the stereo texture array under the hood when
                // the shader is compiled with instancing support.
                float2 sceneUV = i.screenPos.xy / i.screenPos.w;
                float rawSceneDepth = SampleSceneDepth(sceneUV);
                float sceneEyeDepth = LinearEyeDepth(rawSceneDepth, _ZBufferParams);

                float3 pos = ro + rd * entry;

                // Emission accumulated so far, already premultiplied.
                float3 accum = 0;

                // Fraction of light from behind that still reaches the eye.
                // Tracking transmittance directly rather than alpha makes the
                // emission/absorption split explicit: emission is scaled by
                // the transmittance IN FRONT of it, absorption reduces the
                // transmittance for everything behind.
                float transmittance = 1.0;

                [loop]
                for (int s = 0; s < steps; s++)
                {
                    float3 sampleLocal = pos - 0.5;
                    float3 sampleWorld = mul(unity_ObjectToWorld, float4(sampleLocal, 1)).xyz;
                    float sampleEyeDepth = -TransformWorldToView(sampleWorld).z;

                    if (sampleEyeDepth > sceneEyeDepth + 0.02)
                    {
                        pos += rd * step;
                        continue;
                    }

                    float smokeD = tex3D(_SmokeTex, pos).r;
                    float fireD  = tex3D(_FireTex,  pos).r;

                    // --- emission first: this cell's light passes through
                    //     only what is already in front of it ---
                    if (fireD > 0.001)
                    {
                        float edgeBoost = smoothstep(0.0, 0.15, fireD);

                        float3 fc = FireColor(fireD) * fireD
                                  * _FireBrightness * step * edgeBoost;

                        accum += fc * transmittance;
                    }

                    // --- then absorption, which affects everything behind ---
                    if (smokeD > 0.005)
                    {
                        float3 sc = lerp(_SmokeColorLow.rgb,
                                         _SmokeColorHigh.rgb,
                                         saturate(smokeD));

                        float sa = saturate(smokeD * step * _Density * 4.0);

                        // Glowing soot in the reaction zone does not occlude.
                        // Without this the flame is buried under the very
                        // smoke it is producing.
                        sa *= saturate(1.0 - fireD * _SootIncandescence);

                        // In-scattering: soot next to a flame is lit by it.
                        if (fireD > 0.001)
                        {
                            sc += FireColor(fireD) * fireD * _FireScatter;
                        }

                        accum += sc * sa * transmittance;

                        transmittance *= (1.0 - sa);

                        // Early-out only once the remaining contribution is
                        // genuinely negligible. At 0.01 the ray was
                        // terminating inside the plume before reaching the
                        // flame at its centre, so no amount of fire
                        // brightness could ever show up.
                        if (transmittance < 0.002) break;
                    }

                    pos += rd * step;
                }

                // Premultiplied output: RGB is already transmittance-weighted,
                // alpha is coverage only. Do not multiply RGB by alpha here.
                return float4(accum, 1.0 - transmittance);
            }
            ENDHLSL
        }
    }
}
