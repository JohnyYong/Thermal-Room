#ifndef VOLUME_COMMON_INCLUDED
#define VOLUME_COMMON_INCLUDED

// Shared ray-marching setup for FireVolume, SmokeVolume, HeatVolume and
// FireHeatVolume. Put this file in the same folder as those shaders.
//
// Two things here are what make the volumes affordable on Quest:
//
// 1. Scene depth is read ONCE per pixel and turned into a maximum distance
//    along the ray. The old shaders read the depth texture and did a full
//    world->clip transform on EVERY step to test occlusion -- 512 times
//    per pixel, per eye, per volume.
//
// 2. The step count comes from how far the ray actually travels through
//    the box, at a fixed world-space step length, capped by _MaxSteps.
//    The old fixed 512 steps was the same cost whether the ray crossed
//    5 cm or 5 m of volume.
//
// It also carries the single-pass-instanced stereo setup Quest needs:
// without it a custom shader renders into one eye only, or with the
// wrong eye's camera position.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

float _StepWorld;   // metres per step
float _MaxSteps;    // hard cap on steps per pixel
float _DepthSlack;  // metres a sample may sit behind scene geometry and still count

struct appdata
{
    float4 vertex : POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct v2f
{
    float4 pos      : SV_POSITION;
    float3 localPos : TEXCOORD0;
    UNITY_VERTEX_OUTPUT_STEREO
};

v2f vert(appdata v)
{
    // ZERO_INITIALIZE is URP's version. UNITY_INITIALIZE_OUTPUT only
    // exists in the built-in pipeline's UnityCG.cginc.
    v2f o;
    ZERO_INITIALIZE(v2f, o);
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

    o.pos = TransformObjectToHClip(v.vertex.xyz);
    o.localPos = v.vertex.xyz + 0.5;
    return o;
}

float2 RayBoxIntersection(float3 rayOrigin, float3 rayDir, float3 boxMin, float3 boxMax)
{
    // Keep each component away from 0 (with its sign) so 1/0 can't
    // produce Inf and then NaN.
    float3 safeDir;
    safeDir.x = (rayDir.x >= 0.0) ? max(rayDir.x, 1e-6) : min(rayDir.x, -1e-6);
    safeDir.y = (rayDir.y >= 0.0) ? max(rayDir.y, 1e-6) : min(rayDir.y, -1e-6);
    safeDir.z = (rayDir.z >= 0.0) ? max(rayDir.z, 1e-6) : min(rayDir.z, -1e-6);

    float3 invDir = 1.0 / safeDir;

    float3 t0 = (boxMin - rayOrigin) * invDir;
    float3 t1 = (boxMax - rayOrigin) * invDir;

    float3 tmin = min(t0, t1);
    float3 tmax = max(t0, t1);

    return float2(max(max(tmin.x, tmin.y), tmin.z),
                  min(min(tmax.x, tmax.y), tmax.z));
}

// Per-pixel 0..1 offset. Starting each ray a random fraction of a step in
// turns the banding a low step count would show into fine noise.
float VolumeJitter(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

struct MarchSetup
{
    float3 startPos;   // box UVW of the first sample
    float3 rayDir;     // box-UVW direction, normalised
    float  stepSize;   // box-UVW units per step
    float  stepWorld;  // metres per step
    int    steps;
};

// Returns false when there is nothing to march: the ray misses the box,
// or scene geometry sits in front of it.
bool SetupMarch(v2f i, out MarchSetup m)
{
    m = (MarchSetup)0;

    float3 rayOrigin = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz + 0.5;
    float3 rayDir    = normalize(i.localPos - rayOrigin);

    float2 hit = RayBoxIntersection(rayOrigin, rayDir, float3(0, 0, 0), float3(1, 1, 1));
    if (hit.y < 0.0 || hit.x > hit.y)
        return false;

    // Camera inside the box: start at the camera, not behind it.
    float entry = max(hit.x, 0.0);

    // --- Occlusion, once per pixel -----------------------------------
    // Distances along the ray are in box units from the camera. One box
    // unit along this ray covers `worldPerLocal` in world space, so its
    // eye depth is that vector projected on the camera's forward axis.
    float2 screenUV = GetNormalizedScreenSpaceUV(i.pos);
    float  sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams) + _DepthSlack;

    float3 worldPerLocal = mul((float3x3)unity_ObjectToWorld, rayDir);
    float3 camForward    = -UNITY_MATRIX_V[2].xyz;
    float  tScene        = sceneEye / max(dot(worldPerLocal, camForward), 1e-4);

    float exitDist = min(hit.y, tScene);
    float travel   = exitDist - entry;
    if (travel <= 0.0)
        return false;

    // --- Step count from world distance --------------------------------
    float travelWorld = length(worldPerLocal) * travel;
    int   steps = (int)ceil(travelWorld / max(_StepWorld, 0.01));
    steps = clamp(steps, 4, (int)max(_MaxSteps, 4.0));

    m.steps     = steps;
    m.stepSize  = travel / steps;
    m.stepWorld = travelWorld / steps;
    m.rayDir    = rayDir;
    m.startPos  = rayOrigin + rayDir * (entry + VolumeJitter(i.pos.xy) * m.stepSize);

    return true;
}

#endif
