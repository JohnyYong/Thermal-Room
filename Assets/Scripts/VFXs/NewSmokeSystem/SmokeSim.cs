using UnityEngine;

// Volumetric smoke + fire. Fire is a hot, fast-rising field generated at
// the emitter that also produces smoke as it burns. Smoke is the standard
// long-lived field that rises, spreads, and settles. Both share the same
// voxel grid and are composited in a single raymarch shader pass.
[ExecuteAlways]
public class SmokeSim : MonoBehaviour
{
    [Header("Compute")]
    public ComputeShader compute;

    [Header("Sim Volume (world space)")]
    public Vector3 volumeCenter = Vector3.zero;
    public Vector3 volumeSize = new Vector3(10, 5, 10);
    public float cellSize = 0.25f;

    [Header("Room Auto-Detection")]
    public LayerMask obstacleLayers = ~0;
    public bool ignoreTriggers = true;

    [Header("Emitter")]
    public Transform emitter;
    public float emitRadius = 0.6f;

    [Header("Smoke")]
    [Tooltip("Density added per second at the emitter center (base source).")]
    public float emitRate = 2.0f;
    public float riseSpeed = 4.0f;
    public float settleSpeed = 1.0f;
    public float ceilingSpread = 3.0f;
    public float decayRate = 0.005f;

    [Header("Smoke Noise")]
    public float smokeNoiseScale = 0.1f;
    public float smokeNoiseStrength = 1.5f;
    public float smokeNoiseSpeed = 0.8f;

    [Header("Fire")]
    [Tooltip("Fire density set at source each tick (weighted by ball falloff, max'd with existing).")]
    public float fireEmitRate = 1.0f;
    [Tooltip("How fast fire rises. Voxels/sec.")]
    public float fireRiseSpeed = 10.0f;
    [Tooltip("Fire per-second decay. High = short flame; low = flame reaches ceiling.")]
    public float fireDecayRate = 4.0f;
    [Tooltip("Smoke produced per unit of burning fire per second.")]
    public float fireToSmokeRate = 0.5f;
    [Tooltip("How fast fire spreads sideways once it hits a ceiling (ceiling rollover). 0 = disabled.")]
    public float fireCeilingSpread = 2.0f;

    [Header("Fire Noise")]
    [Tooltip("Scale of the noise pattern. Higher = smaller swirls, more chaos.")]
    public float fireNoiseScale = 0.15f;
    [Tooltip("How far turbulence can offset the sample, in voxels. Higher = wilder motion.")]
    public float fireNoiseStrength = 2.5f;
    [Tooltip("Speed at which the noise pattern flows over time.")]
    public float fireNoiseSpeed = 1.5f;

    [Header("Rendering")]
    public Material smokeMaterial;

    // ---- internals ----
    int gridX, gridY, gridZ;
    Vector3 origin;

    RenderTexture smokeA, smokeB;
    RenderTexture fireA, fireB;
    RenderTexture obstacle;

    int kEmit, kStep, kEmitFire, kStepFire, kClear;
    bool initialized;

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

        smokeA = MakeVolume();
        smokeB = MakeVolume();
        fireA = MakeVolume();
        fireB = MakeVolume();
        obstacle = MakeVolume();

        ClearVolume(smokeA);
        ClearVolume(smokeB);
        ClearVolume(fireA);
        ClearVolume(fireB);

        BakeObstacleFromScene();

        transform.position = volumeCenter;
        transform.localScale = volumeSize;

        initialized = true;
    }

    void FixedUpdate()
    {
        if (!Application.isPlaying || !initialized || compute == null) return;

        float dt = Time.fixedDeltaTime;
        compute.SetInts("GridSize", gridX, gridY, gridZ);
        compute.SetFloat("DeltaTime", dt);
        compute.SetFloat("Time_", Time.time);

        SetEmitCommon();

        DispatchEmitFire();
        DispatchStepFire();
        DispatchEmit();
        DispatchStep();

        BindRenderTextures();
    }

    void SetEmitCommon()
    {
        if (emitter == null) return;

        Vector3 emitVoxel = (emitter.position - origin) / cellSize;
        compute.SetFloats("EmitCenter", emitVoxel.x, emitVoxel.y, emitVoxel.z);
        compute.SetFloat("EmitRadius", emitRadius / cellSize);
    }

    void DispatchEmit()
    {
        if (emitter == null) return;

        compute.SetFloat("EmitRate", emitRate);
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

        compute.SetTexture(kStep, "SmokeIn", smokeA);
        compute.SetTexture(kStep, "SmokeOut", smokeB);
        compute.SetTexture(kStep, "Obstacle", obstacle);
        compute.SetFloat("SmokeNoiseScale", smokeNoiseScale);
        compute.SetFloat("SmokeNoiseStrength", smokeNoiseStrength);
        compute.SetFloat("SmokeNoiseSpeed", smokeNoiseSpeed);
        Dispatch(kStep);
        Swap(ref smokeA, ref smokeB);
    }

    void DispatchEmitFire()
    {
        if (emitter == null) return;

        compute.SetFloat("FireEmitRate", fireEmitRate);

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

        compute.SetTexture(kStepFire, "FireIn", fireA);
        compute.SetTexture(kStepFire, "FireOut", fireB);
        compute.SetTexture(kStepFire, "Obstacle", obstacle);

        compute.SetFloat("FireNoiseScale", fireNoiseScale);
        compute.SetFloat("FireNoiseStrength", fireNoiseStrength);
        compute.SetFloat("FireNoiseSpeed", fireNoiseSpeed);

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
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
        Gizmos.DrawWireCube(volumeCenter, volumeSize);
        if (emitter != null)
        {
            Gizmos.color = new Color(1f, 0.3f, 0f, 1f);
            Gizmos.DrawWireSphere(emitter.position, emitRadius);
        }
    }
}