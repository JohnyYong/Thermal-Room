using UnityEngine;

[ExecuteAlways]
public class FluidSimulator : MonoBehaviour
{
    [Header("Compute Shader")]
    public ComputeShader fluidCompute;

    [Header("Grid Settings")]
    [Range(16, 128)] public int gridSize = 64;

    [Header("Simulation Tuning")]
    public float buoyancySigma = 1.0f;
    public float buoyancyBeta = 0.1f;
    public float ambientTemperature = 0f;
    public float dissipation = 0.999f;
    public float fuelRadius = 4f;
    public float fuelAmount = 5f;
    public float fuelTemperature = 10f;

    [Range(4, 40)]
    public int pressureIterations = 20;

    // ── Textures ──────────────────────────────────────────────────────────────
    RenderTexture _velocityA, _velocityB;
    RenderTexture _densityA, _densityB;
    RenderTexture _temperature;
    RenderTexture _pressureA, _pressureB;
    RenderTexture _divergence;
    RenderTexture _obstacles;

    // ── Public accessors for the renderer ────────────────────────────────────
    public RenderTexture DensityTexture => _densityA;
    public RenderTexture TemperatureTexture => _temperature;
    public RenderTexture ObstaclesTexture => _obstacles;

    // ── Kernel handles ────────────────────────────────────────────────────────
    int _kInject, _kAdvect, _kBuoyancy;
    int _kDivergence, _kPressure, _kSubtract, _kObstacles, _kCeiling;

    bool _initialized = false;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void OnEnable()
    {
        Initialize();
    }

    void OnDisable()
    {
        ReleaseTextures();
    }

    void Update()
    {
        if (!_initialized || fluidCompute == null) return;
        float dt = Mathf.Min(Time.deltaTime, 0.02f);
        Simulate(dt);
    }

    // ── Init ──────────────────────────────────────────────────────────────────

    void Initialize()
    {
        ReleaseTextures();
        CreateTextures();
        CacheKernels();
        InitObstacles();
        _initialized = true;
        Debug.Log($"[FluidSim] Initialized {gridSize}³ grid.");
    }

    RenderTexture CreateVolumeTex(RenderTextureFormat fmt)
    {
        var rt = new RenderTexture(gridSize, gridSize, 0, fmt)
        {
            dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
            volumeDepth = gridSize,
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        rt.Create();
        return rt;
    }

    void CreateTextures()
    {
        _velocityA = CreateVolumeTex(RenderTextureFormat.ARGBHalf);
        _velocityB = CreateVolumeTex(RenderTextureFormat.ARGBHalf);
        _densityA = CreateVolumeTex(RenderTextureFormat.RHalf);
        _densityB = CreateVolumeTex(RenderTextureFormat.RHalf);
        _temperature = CreateVolumeTex(RenderTextureFormat.RHalf);
        _pressureA = CreateVolumeTex(RenderTextureFormat.RHalf);
        _pressureB = CreateVolumeTex(RenderTextureFormat.RHalf);
        _divergence = CreateVolumeTex(RenderTextureFormat.RHalf);
        _obstacles = CreateVolumeTex(RenderTextureFormat.RHalf);
    }

    void CacheKernels()
    {
        _kInject = fluidCompute.FindKernel("InjectFuel");
        _kAdvect = fluidCompute.FindKernel("Advect");
        _kBuoyancy = fluidCompute.FindKernel("ApplyBuoyancy");
        _kDivergence = fluidCompute.FindKernel("ComputeDivergence");
        _kPressure = fluidCompute.FindKernel("PressureSolve");
        _kSubtract = fluidCompute.FindKernel("SubtractPressureGradient");
        _kObstacles = fluidCompute.FindKernel("ApplyObstacles");
        _kCeiling = fluidCompute.FindKernel("InitCeiling");
    }

    void InitObstacles()
    {
        int groups = gridSize / 8;
        fluidCompute.SetTexture(_kCeiling, "Obstacles", _obstacles);
        fluidCompute.Dispatch(_kCeiling, groups, groups, 1);
    }

    // ── Per-frame simulation ──────────────────────────────────────────────────
    void Simulate(float dt)
    {
        int groups = gridSize / 8;
        SetParams(dt);

        // 1. Inject
        Bind(_kInject, _velocityA, _velocityB, _densityA, _densityB);
        fluidCompute.Dispatch(_kInject, groups, groups, groups);
        Swap(ref _densityA, ref _densityB);
        Swap(ref _velocityA, ref _velocityB);

        // 2. Advect
        Bind(_kAdvect, _velocityA, _velocityB, _densityA, _densityB);
        fluidCompute.Dispatch(_kAdvect, groups, groups, groups);
        Swap(ref _velocityA, ref _velocityB);
        Swap(ref _densityA, ref _densityB);

        // 3. Buoyancy
        Bind(_kBuoyancy, _velocityA, _velocityB, _densityA, _densityB);
        fluidCompute.Dispatch(_kBuoyancy, groups, groups, groups);
        Swap(ref _velocityA, ref _velocityB);

        // 4. Divergence
        Bind(_kDivergence, _velocityA, _velocityB, _densityA, _densityB);
        fluidCompute.Dispatch(_kDivergence, groups, groups, groups);

        // 5. Pressure iterations
        for (int i = 0; i < pressureIterations; i++)
        {
            Bind(_kPressure, _velocityA, _velocityB, _densityA, _densityB);
            fluidCompute.Dispatch(_kPressure, groups, groups, groups);
            Swap(ref _pressureA, ref _pressureB);
        }

        // 6. Subtract gradient
        Bind(_kSubtract, _velocityA, _velocityB, _densityA, _densityB);
        fluidCompute.Dispatch(_kSubtract, groups, groups, groups);
        Swap(ref _velocityA, ref _velocityB);

        // 7. Obstacles
        Bind(_kObstacles, _velocityA, _velocityB, _densityA, _densityB);
        fluidCompute.Dispatch(_kObstacles, groups, groups, groups);
        Swap(ref _velocityA, ref _velocityB);
        Swap(ref _densityA, ref _densityB);
    }

    void SetParams(float dt)
    {
        fluidCompute.SetFloat("dt", dt);
        fluidCompute.SetFloat("buoyancySigma", buoyancySigma);
        fluidCompute.SetFloat("buoyancyBeta", buoyancyBeta);
        fluidCompute.SetFloat("ambientTemperature", ambientTemperature);
        fluidCompute.SetFloat("dissipation", dissipation);
        fluidCompute.SetFloat("fuelRadius", fuelRadius);
        fluidCompute.SetFloat("fuelAmount", fuelAmount);
        fluidCompute.SetFloat("fuelTemperature", fuelTemperature);

        // Source at the bottom-center of the volume so flame has room to rise
        Vector3 voxelPos = new Vector3(gridSize * 0.5f, gridSize * 0.1f, gridSize * 0.5f);
        fluidCompute.SetVector("fuelPosition", voxelPos);
    }

    void Bind(int kernel,
              RenderTexture velIn, RenderTexture velOut,
              RenderTexture denIn, RenderTexture denOut)
    {
        var cs = fluidCompute;
        cs.SetTexture(kernel, "VelocityIn", velIn);
        cs.SetTexture(kernel, "VelocityOut", velOut);
        cs.SetTexture(kernel, "DensityIn", denIn);
        cs.SetTexture(kernel, "DensityOut", denOut);
        cs.SetTexture(kernel, "Temperature", _temperature);
        cs.SetTexture(kernel, "Pressure", _pressureA);
        cs.SetTexture(kernel, "PressureOut", _pressureB);
        cs.SetTexture(kernel, "Divergence", _divergence);
        cs.SetTexture(kernel, "Obstacles", _obstacles);
    }

    void Swap(ref RenderTexture a, ref RenderTexture b)
        => (a, b) = (b, a);

    void ReleaseTextures()
    {
        _velocityA?.Release(); _velocityB?.Release();
        _densityA?.Release(); _densityB?.Release();
        _temperature?.Release();
        _pressureA?.Release(); _pressureB?.Release();
        _divergence?.Release();
        _obstacles?.Release();
        _initialized = false;
    }

    // ── Debug gizmo ───────────────────────────────────────────────────────────
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.4f);
        Gizmos.DrawWireCube(transform.position, Vector3.one * 10f);
    }
}