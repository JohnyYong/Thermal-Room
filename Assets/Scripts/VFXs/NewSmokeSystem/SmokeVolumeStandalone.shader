Shader "Custom/SmokeVolumeStandalone"
{
    Properties
    {
        _SmokeTex   ("Smoke",             3D)    = "" {}
        _Density    ("Density",           Float) = 8
        _ColorLow   ("Color (Thin)",      Color) = (0.15, 0.15, 0.15, 1)
        _ColorHigh  ("Color (Thick)",     Color) = (0.4,  0.4,  0.4,  1)
        _StepCount  ("Ray Steps",         Range(32, 512)) = 128
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

            sampler3D _SmokeTex;
            float  _Density;
            float4 _ColorLow;
            float4 _ColorHigh;
            float  _StepCount;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 localPos : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.localPos = v.vertex.xyz + 0.5;
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

            float4 frag(v2f i) : SV_Target
            {
                float3 ro = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos,1)).xyz + 0.5;
                float3 rd = normalize(i.localPos - ro);

                float2 hit = RayBox(ro, rd, float3(0,0,0), float3(1,1,1));
                if (hit.x > hit.y) discard;

                float entry = max(hit.x, 0);
                float travel = hit.y - entry;
                int   steps = (int)_StepCount;
                float step  = travel / steps;

                float3 pos = ro + rd * entry;
                float3 accum = 0;
                float  alpha = 0;

                [loop]
                for (int s = 0; s < steps; s++)
                {
                    float d = tex3D(_SmokeTex, pos).r;
                    if (d > 0.005)
                    {
                        float3 c = lerp(_ColorLow.rgb, _ColorHigh.rgb, saturate(d));
                        float a = saturate(d * step * _Density * 4.0);
                        accum += c * a * (1 - alpha);
                        alpha += a * (1 - alpha);
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