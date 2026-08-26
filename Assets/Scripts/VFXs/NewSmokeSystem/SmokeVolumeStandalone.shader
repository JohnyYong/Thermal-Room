Shader "Custom/SmokeVolumeStandalone"
{
    Properties
    {
        _SmokeTex   ("Smoke",           3D)    = "" {}
        _FireTex    ("Fire",            3D)    = "" {}

        _Density         ("Smoke Density",     Float) = 8
        _SmokeColorLow   ("Smoke Color Thin",  Color) = (0.15, 0.15, 0.15, 1)
        _SmokeColorHigh  ("Smoke Color Thick", Color) = (0.4,  0.4,  0.4,  1)

        _FireBrightness  ("Fire Brightness",   Float) = 6
        _FireColorLow    ("Fire Color (Cool)", Color) = (1.0, 0.15, 0.0, 1)
        _FireColorMid    ("Fire Color (Mid)",  Color) = (1.0, 0.65, 0.1, 1)
        _FireColorHigh   ("Fire Color (Hot)",  Color) = (1.0, 1.0,  0.8, 1)

        _StepCount       ("Ray Steps",         Range(32, 512)) = 128
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Front
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
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

            float _StepCount;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 localPos : TEXCOORD0;
                float4 screenPos: TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
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
                float3 ro = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz + 0.5;
                float3 rd = normalize(i.localPos - ro);

                float2 hit = RayBox(ro, rd, float3(0,0,0), float3(1,1,1));
                if (hit.x > hit.y) discard;

                float entry  = max(hit.x, 0);
                float travel = hit.y - entry;
                int   steps  = (int)_StepCount;
                float step   = travel / steps;

                // Scene depth for per-step occlusion check.
                float2 sceneUV = i.screenPos.xy / i.screenPos.w;
                float rawSceneDepth = SampleSceneDepth(sceneUV);
                float sceneEyeDepth = LinearEyeDepth(rawSceneDepth, _ZBufferParams);

                float3 pos = ro + rd * entry;
                float3 accum = 0;
                float  alpha = 0;

                [loop]
                for (int s = 0; s < steps; s++)
                {
                    // Per-step depth check: convert this sample's world
                    // position to eye depth and skip if it's behind a wall.
                    // This handles all camera positions correctly (inside
                    // the volume, outside, near walls) without upfront math.
                    float3 sampleLocal = pos - 0.5;
                    float3 sampleWorld = mul(unity_ObjectToWorld, float4(sampleLocal, 1)).xyz;
                    float sampleEyeDepth = -TransformWorldToView(sampleWorld).z;

                    // 0.02 tolerance avoids z-fighting artifacts right at
                    // the wall surface.
                    if (sampleEyeDepth > sceneEyeDepth + 0.02)
                    {
                        pos += rd * step;
                        continue;
                    }

                    float smokeD = tex3D(_SmokeTex, pos).r;
                    float fireD  = tex3D(_FireTex,  pos).r;

                    if (fireD > 0.001)
                    {
                        float edgeBoost = smoothstep(0.0, 0.15, fireD);
                        float3 fc = FireColor(fireD) * fireD * _FireBrightness * step * edgeBoost;
                        accum += fc * (1 - alpha);
                    }

                    if (smokeD > 0.005)
                    {
                        float3 sc = lerp(_SmokeColorLow.rgb, _SmokeColorHigh.rgb, saturate(smokeD));
                        float sa = saturate(smokeD * step * _Density * 4.0);
                        accum += sc * sa * (1 - alpha);
                        alpha += sa * (1 - alpha);
                        if (alpha > 0.99) break;
                    }

                    pos += rd * step;
                }

                return float4(accum, alpha);
            }
            ENDHLSL
        }
    }
}