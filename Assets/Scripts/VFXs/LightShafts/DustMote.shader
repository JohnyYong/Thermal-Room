// Additive dust mote particle shader for URP on Quest (Single Pass Instanced / Multiview safe).
// Draws a soft round dot procedurally, so no texture is needed.
// Fades motes out right in front of the player's eyes, and fades into the scene fog.
Shader "Custom/VR/DustMote"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 0.97, 0.9, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1
        _Softness ("Dot Softness", Range(0.5, 4)) = 2
        _NearFadeStart ("Near Fade Start (m)", Float) = 0.1
        _NearFadeEnd ("Near Fade End (m)", Float) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                half   fogFactor  : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half  _Intensity;
                half  _Softness;
                float _NearFadeStart;
                float _NearFadeEnd;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                float dist = distance(pos.positionWS, GetCameraPositionWS());
                half nearFade = saturate((dist - _NearFadeStart) / max(_NearFadeEnd - _NearFadeStart, 1e-3));

                OUT.positionCS = pos.positionCS;
                OUT.uv = IN.uv;
                OUT.color = IN.color * _Color;
                OUT.color.a *= nearFade;
                OUT.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 p = IN.uv * 2.0 - 1.0;
                half dot01 = pow(saturate(1.0 - dot(p, p)), _Softness);

                half3 col = IN.color.rgb * (IN.color.a * dot01 * _Intensity);
                col = MixFogColor(col, half3(0, 0, 0), IN.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
