using UnityEngine;

// Volumetric smoke + fire simulation with support for multiple emitters
// sharing one voxel grid. Fire produces smoke as it burns. Both fields
// are composited in a single raymarch shader pass.
[ExecuteAlways]
public class SmokeSim : MonoBehaviour
{

    [System.Serializable]
    public class Emitter
    {
        [Tooltip("Where smoke/fire is generated. Move to move the source.")]
        public Transform transform;

        [Tooltip("World-space radius of the emit ball.")]
        public float radius = 0.6f;

        [Range(0f, 20f)]
        [Tooltip("Smoke density added per second at this emitter's center.")]
        public float smokeRate = 2.0f;

        [Range(0f, 5f)]
        [Tooltip("Fire density this emitter sets per tick. 0 = smoke only, no flame.")]
        public float fireRate = 1.0f;

        [Tooltip("Uncheck to disable without removing from the list.")]
        public bool enabled = true;
    }

    [Header("Compute")]
    public ComputeShader compute;

    [Header("Sim Volume (world space)")]
    public Vector3 volumeCenter = Vector3.zero;
    public Vector3 volumeSize = new Vector3(10, 5, 10);
    public float cellSize = 0.25f;

    [Header("Room Auto-Detection")]
    public LayerMask obstacleLayers = ~0;
    public bool ignoreTriggers = true;

    [Header("Emitters")]
    [Tooltip("One or more fire/smoke sources. Add as many as you need.")]
    public Emitter[] emitters = new Emitter[] { new Emitter() };

    [Header("Smoke")]
    public float riseSpeed = 4.0f;
    public float settleSpeed = 1.0f;
    public float ceilingSpread = 3.0f;
    public float decayRate = 0.005f;

    [Header("Smoke Noise")]
    public float smokeNoiseScale = 0.08f;
    public float smokeNoiseStrength = 1.0f;
    public float smokeNoiseSpeed = 0.6f;

    [Header("Fire")]
    public float fireRiseSpeed = 6.0f;
    public float fireDecayRate = 2.0f;
    public float fireToSmokeRate = 0.5f;
    public float fireCeilingSpread = 2.0f;

    [Header("Fire Noise")]
    public float fireNoiseScale = 0.15f;
    public float fireNoiseStrength = 1.2f;
    public float fireNoiseSpeed = 2.0f;

    [Header("Rendering")]
    public Material smokeMaterial;

    //Wind Direction and flow
    public enum WindPreset
    {
        None,
        North,          // +Z
        South,          // -Z
        East,           // +X
        West,           // -X
        Up,             // +Y (rare — updraft)
        Down,           // -Y (rare — downdraft)
        NorthEast,
        NorthWest,
        SouthEast,
        SouthWest,
        Custom          // use windDirectionCustom instead
    }

    [Header("Wind & Attractor")]
    [Tooltip("Which direction the wind blows. Pick a compass direction or Custom for a specific vector.")]
    public WindPreset windDirection = WindPreset.None;
    public float windStrength = 0f;
    public Vector3 windDirectionCustom = Vector3.zero;

    public Transform attractorPoint;
    public float attractorRadius = 5f;
    public float attractorStrength = 0f;

    [Header("Real Room Airflow (optional)")]
    [Tooltip("If assigned, SmokeSim samples this ThermalSimulation's actual pressure-projected velocity field, so smoke deflects around furniture and banks off walls/vents the way real airflow would -- instead of only the fake wind/attractor/noise offsets above. Leave unassigned to keep the old behavior exactly as-is.")]
    public ThermalSimulation externalVelocitySource;

    [Range(0f, 1f)]
    [Tooltip("How much the real room airflow (above) blends into smoke/fire motion. 0 = fully ignored (old behavior). 1 = fully driven by real room pressure/velocity, on top of whatever wind/attractor/noise still contribute.")]
    public float externalVelocityInfluence = 0f;

    [Range(0f, 1f)]
    [Tooltip("This IS the live dial -- it animates itself from 0 up to Max Drift Speed over Drift Ramp Duration. While the ramp is running, script writes to this every tick, so manual edits here get overwritten until the ramp finishes. After it finishes, this becomes a normal manually-adjustable slider again.")]
    public float driftSpeed = 0f;

    [Range(0f, 1f)]
    [Tooltip("The ceiling driftSpeed ramps up to. Change this any time -- if the ramp already finished, it takes effect immediately; if it's still running, it changes where the ramp is heading.")]
    public float maxDriftSpeed = 0.3f;

    [Tooltip("Seconds after the sim starts (or after Rebake) before driftSpeed reaches maxDriftSpeed. 0 = instant, matching the old always-on behavior.")]
    public float driftRampDuration = 4f;

    // Time.time at which the ramp should start counting from. Set in
    // Init() and reset in Rebake() so re-baking the room also restarts
    // the drift ease-in, rather than snapping straight to full strength.
    float driftRampStartTime;

    // True once the ramp has reached its end and written the final value.
    // After that, UploadDirectionalParams stops touching driftSpeed at all,
    // so it goes back to being a plain manually-editable Inspector field.
    bool driftRampFinished;

    Vector3 GetWindVector()
    {
        switch (windDirection)
        {
            case WindPreset.North: return new Vector3(0, 0, 1);
            case WindPreset.South: return new Vector3(0, 0, -1);
            case WindPreset.East: return new Vector3(1, 0, 0);
            case WindPreset.West: return new Vector3(-1, 0, 0);
            case WindPreset.Up: return new Vector3(0, 1, 0);
            case WindPreset.Down: return new Vector3(0, -1, 0);
            case WindPreset.NorthEast: return new Vector3(1, 0, 1).normalized;
            case WindPreset.NorthWest: return new Vector3(-1, 0, 1).normalized;
            case WindPreset.SouthEast: return new Vector3(1, 0, -1).normalized;
            case WindPreset.SouthWest: return new Vector3(-1, 0, -1).normalized;
            case WindPreset.Custom: return windDirectionCustom;
            default: return Vector3.zero;
        }
    }

    // ---- internals ----
    int gridX, gridY, gridZ;
    Vector3 origin;

    RenderTexture smokeA, smokeB;
    RenderTexture fireA, fireB;
    RenderTexture obstacle;

    // Bound to the ExternalVelocity sampler whenever no ThermalSimulation
    // is assigned (or externalVelocityInfluence is 0), so the compute
    // shader always has a valid Texture3D<float4> bound -- Unity can throw
    // or warn if a kernel expects a texture and nothing was ever set.
    RenderTexture dummyExternalVelocity;

    int kEmit, kStep, kEmitFire, kStepFire, kClear;
    bool initialized;

    ComputeBuffer emitterBuffer;
    int kCopyVolume = -1;
    ComputeBuffer volumeReadBuffer;
    EmitterGPU[] emitterCPUCache;
    bool loggedOverflowOnce;

    // Must match EmitterData in SmokeSim.compute exactly (field order and
    // size). StructuredBuffer layout is positional; any mismatch corrupts
    // every field silently.
    struct EmitterGPU
    {
        public Vector3 center;
        public float radius;
        public float emitRate;
        public float fireRate;
    }

    const int MaxEmitters = 32;

    void Start()
    {
        if (!Application.isPlaying) return;
        Init();
    }

    void Init()
    {
        origin = volumeCenter - volumeSize * 0.5f;

        gridX = Mathf.Max(4, Mathf.CeilToInt(volumeSize.x / cellSize));
        gridY = Mathf.Max(4, Mathf.CeilToInt(volumeSize.y / cellSize));
        gridZ = Mathf.Max(4, Mathf.CeilToInt(volumeSize.z / cellSize));

        Debug.Log($"[SmokeSim] Grid: {gridX} x {gridY} x {gridZ} = {gridX * gridY * gridZ:N0} voxels");

        kEmit = compute.FindKernel("Emit");
        kStep = compute.FindKernel("Step");
        kEmitFire = compute.FindKernel("EmitFire");
        kStepFire = compute.FindKernel("StepFire");
        kClear = compute.FindKernel("Clear");
        kCopyVolume = compute.FindKernel("CopyVolumeToBuffer");
        volumeReadBuffer = new ComputeBuffer(gridX * gridY * gridZ, sizeof(float));

        smokeA = MakeVolume();
        smokeB = MakeVolume();
        fireA = MakeVolume();
        fireB = MakeVolume();
        obstacle = MakeVolume();

        dummyExternalVelocity = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat);
        dummyExternalVelocity.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        dummyExternalVelocity.volumeDepth = 1;
        dummyExternalVelocity.enableRandomWrite = true;
        dummyExternalVelocity.Create();

        ClearVolume(smokeA);
        ClearVolume(smokeB);
        ClearVolume(fireA);
        ClearVolume(fireB);

        // 6 floats per emitter: center(3) + radius(1) + emitRate(1) + fireRate(1)
        emitterBuffer = new ComputeBuffer(MaxEmitters, sizeof(float) * 6);
        emitterCPUCache = new EmitterGPU[MaxEmitters];

        BakeObstacleFromScene();

        transform.position = volumeCenter;
        transform.localScale = volumeSize;

        driftRampStartTime = Time.time;
        driftSpeed = 0f;
        driftRampFinished = false;

        initialized = true;
    }

    void FixedUpdate()
    {
        if (!Application.isPlaying || !initialized || compute == null) return;

        float dt = Time.fixedDeltaTime;
        compute.SetInts("GridSize", gridX, gridY, gridZ);
        compute.SetFloat("DeltaTime", dt);
        compute.SetFloat("Time_", Time.time);
        compute.SetFloats("Origin", origin.x, origin.y, origin.z);
        compute.SetFloat("CellSize", cellSize);

        UploadDirectionalParams();
        UploadExternalVelocityParams();
        BuildEmitterBuffer();

        DispatchEmitFire();
        DispatchStepFire();
        DispatchEmit();
        DispatchStep();

        BindRenderTextures();
    }

    // Rebuilds the GPU emitter buffer from the Inspector list each tick.
    // Skips disabled/null entries. Uploads the full fixed-size array
    // (~768 bytes) rather than a partial range -- trivial cost, and it
    // means the compute shader only ever reads the first `count` slots.
    void BuildEmitterBuffer()
    {
        int count = 0;
        int totalCandidates = 0;

        if (emitters != null)
        {
            for (int i = 0; i < emitters.Length; i++)
            {
                Emitter e = emitters[i];
                if (e == null || !e.enabled || e.transform == null) continue;

                totalCandidates++;
                if (count >= MaxEmitters) continue;

                emitterCPUCache[count] = new EmitterGPU
                {
                    center = (e.transform.position - origin) / cellSize,
                    radius = e.radius / cellSize,
                    emitRate = e.smokeRate,
                    fireRate = e.fireRate,
                };
                count++;
            }
        }

        if (totalCandidates > MaxEmitters && !loggedOverflowOnce)
        {
            Debug.LogWarning($"[SmokeSim] More than {MaxEmitters} active emitters -- extras ignored.");
            loggedOverflowOnce = true;
        }
        else if (totalCandidates <= MaxEmitters)
        {
            loggedOverflowOnce = false;
        }

        emitterBuffer.SetData(emitterCPUCache);
        compute.SetBuffer(kEmit, "Emitters", emitterBuffer);
        compute.SetBuffer(kEmitFire, "Emitters", emitterBuffer);
        compute.SetInt("EmitterCount", count);
    }

    void DispatchEmit()
    {
        compute.SetFloat("FireToSmokeRate", fireToSmokeRate);

        compute.SetTexture(kEmit, "SmokeIn", smokeA);
        compute.SetTexture(kEmit, "SmokeOut", smokeB);
        compute.SetTexture(kEmit, "FireIn", fireA);
        compute.SetTexture(kEmit, "Obstacle", obstacle);
        Dispatch(kEmit);
        Swap(ref smokeA, ref smokeB);
    }

    void DispatchStep()
    {
        compute.SetFloat("RiseSpeed", riseSpeed);
        compute.SetFloat("SettleSpeed", settleSpeed);
        compute.SetFloat("CeilingSpread", ceilingSpread);
        compute.SetFloat("DecayRate", decayRate);
        compute.SetFloat("SmokeNoiseScale", smokeNoiseScale);
        compute.SetFloat("SmokeNoiseStrength", smokeNoiseStrength);
        compute.SetFloat("SmokeNoiseSpeed", smokeNoiseSpeed);

        compute.SetTexture(kStep, "SmokeIn", smokeA);
        compute.SetTexture(kStep, "SmokeOut", smokeB);
        compute.SetTexture(kStep, "Obstacle", obstacle);
        Dispatch(kStep);
        Swap(ref smokeA, ref smokeB);
    }

    void DispatchEmitFire()
    {
        compute.SetTexture(kEmitFire, "FireIn", fireA);
        compute.SetTexture(kEmitFire, "FireOut", fireB);
        compute.SetTexture(kEmitFire, "Obstacle", obstacle);
        Dispatch(kEmitFire);
        Swap(ref fireA, ref fireB);
    }

    void DispatchStepFire()
    {
        compute.SetFloat("FireRiseSpeed", fireRiseSpeed);
        compute.SetFloat("FireDecayRate", fireDecayRate);
        compute.SetFloat("FireCeilingSpread", fireCeilingSpread);
        compute.SetFloat("FireNoiseScale", fireNoiseScale);
        compute.SetFloat("FireNoiseStrength", fireNoiseStrength);
        compute.SetFloat("FireNoiseSpeed", fireNoiseSpeed);

        compute.SetTexture(kStepFire, "FireIn", fireA);
        compute.SetTexture(kStepFire, "FireOut", fireB);
        compute.SetTexture(kStepFire, "Obstacle", obstacle);
        Dispatch(kStepFire);
        Swap(ref fireA, ref fireB);
    }

    void ClearVolume(RenderTexture rt)
    {
        compute.SetInts("GridSize", gridX, gridY, gridZ);
        compute.SetTexture(kClear, "SmokeOut", rt);
        Dispatch(kClear);
    }

    void Dispatch(int kernel)
    {
        compute.Dispatch(kernel,
            Mathf.CeilToInt(gridX / 8f),
            Mathf.CeilToInt(gridY / 8f),
            Mathf.CeilToInt(gridZ / 8f));
    }

    RenderTexture MakeVolume()
    {
        var rt = new RenderTexture(gridX, gridY, 0, RenderTextureFormat.RFloat);
        rt.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        rt.volumeDepth = gridZ;
        rt.enableRandomWrite = true;
        rt.wrapMode = TextureWrapMode.Clamp;
        rt.Create();
        return rt;
    }

    void BakeObstacleFromScene()
    {
        float radius = cellSize * 0.45f;
        var trig = ignoreTriggers
            ? QueryTriggerInteraction.Ignore
            : QueryTriggerInteraction.Collide;

        int solidCount = 0;
        float[] data = new float[gridX * gridY * gridZ];

        for (int z = 0; z < gridZ; z++)
            for (int y = 0; y < gridY; y++)
                for (int x = 0; x < gridX; x++)
                {
                    Vector3 world = origin + new Vector3(
                        (x + 0.5f) * cellSize,
                        (y + 0.5f) * cellSize,
                        (z + 0.5f) * cellSize);

                    bool solid = Physics.CheckSphere(world, radius, obstacleLayers, trig);

                    int idx = x + gridX * (y + gridY * z);
                    data[idx] = solid ? 1f : 0f;
                    if (solid) solidCount++;
                }

        Color[] pixels = new Color[data.Length];
        for (int i = 0; i < data.Length; i++)
            pixels[i] = new Color(data[i], 0, 0, 0);

        var tex = new Texture3D(gridX, gridY, gridZ, TextureFormat.RFloat, false);
        tex.SetPixels(pixels);
        tex.Apply();
        Graphics.CopyTexture(tex, obstacle);
        if (Application.isPlaying) Destroy(tex); else DestroyImmediate(tex);

        Debug.Log($"[SmokeSim] Baked obstacles: {solidCount}/{data.Length} voxels solid.");
    }

    public void Rebake()
    {
        if (!initialized) return;
        Physics.SyncTransforms();
        BakeObstacleFromScene();
        driftRampStartTime = Time.time;
        driftSpeed = 0f;
        driftRampFinished = false;
    }

    void BindRenderTextures()
    {
        if (smokeMaterial == null) return;
        smokeMaterial.SetTexture("_SmokeTex", smokeA);
        smokeMaterial.SetTexture("_FireTex", fireA);
        smokeMaterial.SetVector("_VolumeMin", origin);
        smokeMaterial.SetVector("_VolumeSize", volumeSize);
    }

    void Swap<T>(ref T a, ref T b) { T t = a; a = b; b = t; }

    void OnDestroy()
    {
        if (smokeA != null) smokeA.Release();
        if (smokeB != null) smokeB.Release();
        if (fireA != null) fireA.Release();
        if (fireB != null) fireB.Release();
        if (obstacle != null) obstacle.Release();
        if (dummyExternalVelocity != null) dummyExternalVelocity.Release();
        if (emitterBuffer != null) emitterBuffer.Release();
        if (volumeReadBuffer != null) volumeReadBuffer.Release();
    }

    void UploadDirectionalParams()
    {
        Vector3 raw = GetWindVector();
        Vector3 wind = raw.sqrMagnitude > 0.001f ? raw.normalized : Vector3.zero;

        compute.SetFloats("WindDirection", wind.x, wind.y, wind.z);
        compute.SetFloat("WindStrength", windStrength);

        // Ease driftSpeed itself up toward maxDriftSpeed over
        // driftRampDuration seconds, so the Inspector slider visibly climbs
        // in Play mode instead of a hidden value doing the ramping. Once
        // the ramp completes, we stop writing to driftSpeed entirely so it
        // goes back to being a normal manually-adjustable field -- without
        // that cutoff, any manual edit here would get silently overwritten
        // every tick forever.
        if (!driftRampFinished)
        {
            if (driftRampDuration <= 0.0001f)
            {
                driftSpeed = maxDriftSpeed;
                driftRampFinished = true;
            }
            else
            {
                float elapsed = Time.time - driftRampStartTime;
                float t = Mathf.Clamp01(elapsed / driftRampDuration);
                // Smoothstep -- eases in and out of the ramp itself so it
                // doesn't feel like a mechanical linear slide.
                float eased = t * t * (3f - 2f * t);
                driftSpeed = maxDriftSpeed * eased;

                if (t >= 1f)
                {
                    driftRampFinished = true;
                }
            }
        }

        compute.SetFloat("DriftSpeed", driftSpeed);

        if (attractorPoint != null && attractorStrength > 0f)
        {
            Vector3 attVoxel = (attractorPoint.position - origin) / cellSize;
            compute.SetFloats("AttractorCenter", attVoxel.x, attVoxel.y, attVoxel.z);
            compute.SetFloat("AttractorRadius", attractorRadius / cellSize);
            compute.SetFloat("AttractorStrength", attractorStrength);
        }
        else
        {
            compute.SetFloats("AttractorCenter", 0, 0, 0);
            compute.SetFloat("AttractorRadius", 1f);
            compute.SetFloat("AttractorStrength", 0f);
        }
    }

    // Binds ThermalSimulation's real, pressure-projected velocity field so
    // Step/StepFire can sample actual room airflow -- obstacle deflection
    // and venting toward openings come for free here, since they're already
    // baked into that velocity field by ThermalSimulation's own pressure
    // solve. Falls back to a 1x1x1 dummy texture and influence=0 whenever
    // no source is assigned, so the shader always has a valid binding.
    void UploadExternalVelocityParams()
    {
        bool sourceReady = externalVelocitySource != null
            && externalVelocitySource.IsInitialized
            && externalVelocityInfluence > 0.0001f;

        if (sourceReady)
        {
            RenderTexture vel = externalVelocitySource.GetVelocityVolume();
            compute.SetTexture(kStep, "ExternalVelocity", vel);
            compute.SetTexture(kStepFire, "ExternalVelocity", vel);

            Vector3 extMin = externalVelocitySource.SimBoundsMin;
            compute.SetFloats("ExternalOrigin", extMin.x, extMin.y, extMin.z);
            compute.SetFloat("ExternalCellSize", externalVelocitySource.CellSizePublic);
            compute.SetInts("ExternalGridSize",
                externalVelocitySource.GridXPublic,
                externalVelocitySource.GridYPublic,
                externalVelocitySource.GridZPublic);
            compute.SetFloat("ExternalVelocityInfluence", externalVelocityInfluence);
        }
        else
        {
            compute.SetTexture(kStep, "ExternalVelocity", dummyExternalVelocity);
            compute.SetTexture(kStepFire, "ExternalVelocity", dummyExternalVelocity);
            compute.SetFloats("ExternalOrigin", 0, 0, 0);
            compute.SetFloat("ExternalCellSize", 1f);
            compute.SetInts("ExternalGridSize", 1, 1, 1);
            compute.SetFloat("ExternalVelocityInfluence", 0f);
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
        Gizmos.DrawWireCube(volumeCenter, volumeSize);

        if (emitters != null)
        {
            foreach (var e in emitters)
            {
                if (e == null || e.transform == null) continue;
                Gizmos.color = e.enabled
                    ? new Color(1f, 0.3f, 0f, 1f)
                    : new Color(0.4f, 0.4f, 0.4f, 0.5f);
                Gizmos.DrawWireSphere(e.transform.position, e.radius);
            }
        }

        if (attractorPoint != null && attractorStrength > 0f)
        {
            Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.8f);
            Gizmos.DrawWireSphere(attractorPoint.position, attractorRadius);
            Gizmos.DrawLine(volumeCenter, attractorPoint.position);
        }

        if (windStrength > 0f)
        {
            Vector3 windDir = GetWindVector();
            if (windDir.sqrMagnitude > 0.001f)
            {
                windDir = windDir.normalized;
                Gizmos.color = new Color(0.5f, 1f, 0.5f, 0.8f);
                Vector3 arrowStart = volumeCenter;
                Vector3 arrowEnd = volumeCenter + windDir * (1f + windStrength * 0.5f);
                Gizmos.DrawLine(arrowStart, arrowEnd);
                Gizmos.DrawWireSphere(arrowEnd, 0.15f);
            }
        }
    }


    //For debug
    // Reads any 3D scalar volume back to the CPU with the full depth.
    // Avoids AsyncGPUReadback's z=0-only bug on Tex3D by copying into a
    // linear ComputeBuffer first, then reading the buffer.
    public void RequestVolumeReadback(RenderTexture volume, System.Action<Unity.Collections.NativeArray<float>> onComplete)
    {
        if (kCopyVolume < 0 || volumeReadBuffer == null)
        {
            Debug.LogWarning("[SmokeSim] Readback called before Init completed.");
            return;
        }

        compute.SetTexture(kCopyVolume, "CopySource", volume);
        compute.SetBuffer(kCopyVolume, "CopyDest", volumeReadBuffer);
        compute.SetInts("CopyDims", gridX, gridY, gridZ);

        compute.Dispatch(kCopyVolume,
            Mathf.CeilToInt(gridX / 8f),
            Mathf.CeilToInt(gridY / 8f),
            Mathf.CeilToInt(gridZ / 4f));

        UnityEngine.Rendering.AsyncGPUReadback.Request(volumeReadBuffer, req =>
        {
            if (req.hasError) { Debug.LogWarning("[SmokeSim] Readback error"); return; }
            onComplete?.Invoke(req.GetData<float>());
        });
    }

    // Expose grid info so debug tools don't need reflection.
    public int GridX => gridX;
    public int GridY => gridY;
    public int GridZ => gridZ;
    public Vector3 Origin => origin;
    public RenderTexture ObstacleVolume => obstacle;
    public RenderTexture SmokeVolume => smokeA;
    public RenderTexture FireVolume => fireA;
}