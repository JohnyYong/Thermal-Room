using UnityEngine;

// Standalone volumetric smoke simulation. Emits smoke at a Transform,
// smoke rises, hits ceiling, spreads across the ceiling, cools/settles
// down over time. No dependency on other systems, no pressure solver,
// no shader ping-pong -- just direct compute steps.
//
// Setup: put on a Cube GameObject with SmokeMat material. Set Volume
// Center/Size to cover the room. Assign an Emitter Transform. Play.
[ExecuteAlways]
public class SmokeSim : MonoBehaviour
{
    [Header("Compute")]
    public ComputeShader compute;

    [Header("Sim Volume (world space)")]
    public Vector3 volumeCenter = Vector3.zero;
    public Vector3 volumeSize = new Vector3(10, 5, 10);
    [Tooltip("Voxel size in world units. Smaller = more detail, more cost.")]
    public float cellSize = 0.25f;

    [Header("Room Auto-Detection")]
    [Tooltip("Layers considered walls. Any collider on these layers becomes a solid voxel.")]
    public LayerMask obstacleLayers = ~0;
    public bool ignoreTriggers = true;

    [Header("Emitter")]
    public Transform emitter;
    [Tooltip("World-space radius of the emit ball.")]
    public float emitRadius = 0.6f;
    [Tooltip("Density added per second at the emitter center.")]
    public float emitRate = 2.0f;

    [Header("Physics")]
    [Tooltip("How fast smoke rises. Voxels/sec.")]
    public float riseSpeed = 4.0f;

    [Tooltip("How fast cooled smoke drifts down. Should be << riseSpeed.")]
    public float settleSpeed = 0.5f;

    [Tooltip("How fast smoke spreads horizontally once it's under a ceiling.")]
    public float ceilingSpread = 2.0f;

    [Tooltip("Slow global fade rate per second. Keeps sim bounded.")]
    public float decayRate = 0.01f;

    [Header("Rendering")]
    public Material smokeMaterial;

    // ---- internals ----
    int gridX, gridY, gridZ;
    Vector3 origin;

    RenderTexture smokeA, smokeB;
    RenderTexture obstacle;

    int kEmit, kStep, kClear;
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
        kClear = compute.FindKernel("Clear");

        smokeA = MakeVolume();
        smokeB = MakeVolume();
        obstacle = MakeVolume();

        // Clear both smoke buffers to zero via compute so we know they start empty.
        ClearVolume(smokeA);
        ClearVolume(smokeB);

        BakeObstacleFromScene();

        // Make the render mesh cover the sim bounds exactly.
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

        DispatchEmit();
        DispatchStep();
        BindRenderTextures();
    }

    void DispatchEmit()
    {
        if (emitter == null) return;

        Vector3 emitVoxel = (emitter.position - origin) / cellSize;

        compute.SetFloats("EmitCenter", emitVoxel.x, emitVoxel.y, emitVoxel.z);
        compute.SetFloat("EmitRadius", emitRadius / cellSize);
        compute.SetFloat("EmitRate", emitRate);

        compute.SetTexture(kEmit, "SmokeIn", smokeA);
        compute.SetTexture(kEmit, "SmokeOut", smokeB);
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
        Dispatch(kStep);
        Swap(ref smokeA, ref smokeB);
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

    // Auto-detect the room by asking Physics which voxels overlap real
    // colliders on the obstacleLayers mask. Works for any scene without
    // requiring a hand-authored room shape.
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
        smokeMaterial.SetVector("_VolumeMin", origin);
        smokeMaterial.SetVector("_VolumeSize", volumeSize);
    }

    void Swap<T>(ref T a, ref T b) { T t = a; a = b; b = t; }

    void OnDestroy()
    {
        if (smokeA != null) smokeA.Release();
        if (smokeB != null) smokeB.Release();
        if (obstacle != null) obstacle.Release();
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
        Gizmos.DrawWireCube(volumeCenter, volumeSize);
        if (emitter != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(emitter.position, emitRadius);
        }
    }
}