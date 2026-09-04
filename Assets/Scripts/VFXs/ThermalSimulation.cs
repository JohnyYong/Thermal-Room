using ScenarioEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public class ThermalSimulation : MonoBehaviour
{
    #region Parameters and Variables
    private static ThermalSimulation _instance;
    public static ThermalSimulation Instance
    {
        get { return _instance; }
    }

    public struct BurningVoxel
    {
        public uint x;
        public uint y;
        public uint z;
        public uint pad;
    }

    public ComputeShader simulation;
    public Material thermalSurfaceMaterial;
    public Transform thermalVolume;

    public float cellSize = 0.25f;

    int gridX;
    int gridY;
    int gridZ;

    public Transform fireSource;

    const int MaxFireSources = 32;
    readonly List<IMobileFireSource> _fireSources = new();
    ComputeBuffer fireSourceBuffer;
    FireSourceGPU[] _fireSourceCPUBuffer;
    bool _loggedFireSourceOverflow;

    struct FireSourceGPU
    {
        public Vector3 position;
        public float heatRadius;
        public float heatPower;
        public float radiationRange;
        public float radiationPower;
    }

    public void RegisterFireSource(IMobileFireSource src)
    {
        if (!_fireSources.Contains(src)) _fireSources.Add(src);
    }

    public void UnregisterFireSource(IMobileFireSource src)
    {
        _fireSources.Remove(src);
    }

    // 1x1x1 zero volume bound to FineFireIn/FineSmokeIn whenever the fine
    // SmokeSim fields are unavailable. FlameHeatStep samples those textures
    // unconditionally, so leaving them unbound produces a "Property ... is
    // not set" warning every tick and undefined reads. With FineGridSize
    // already forced to 1,1,1 by UploadFineCoupling in that case, the box
    // average clamps to this single zero texel -- fire/smoke read 0,
    // emissivity is 0, and the imager correctly sees ambient.
    RenderTexture fineFallback;

    RenderTexture temperatureA;
    RenderTexture temperatureB;

    RenderTexture velocityA;
    RenderTexture velocityB;

    RenderTexture divergence;

    RenderTexture pressureA;
    RenderTexture pressureB;
    RenderTexture obstacleVolume;
    RenderTexture materialVolume;

    RenderTexture fuelA;
    RenderTexture fuelB;

    RenderTexture burningA;
    RenderTexture burningB;

    RenderTexture suppressantA;
    RenderTexture suppressantB;

    RenderTexture solidTemperatureA;
    RenderTexture solidTemperatureB;

    RenderTexture curlA;

    RenderTexture smokeA;
    RenderTexture smokeB;

    RenderTexture flameA;
    RenderTexture flameB;

    RenderTexture flameHeatA;
    RenderTexture flameHeatB;

    RenderTexture charVolume;

    int copyVolumeKernel;
    ComputeBuffer volumeReadBuffer;

    float[] obstacleData;
    float[] materialData;
    float[] fuelData;
    float[] burningData;

    int charAccumKernel;
    int heatKernel;
    int buoyancyKernel;
    int advectTemperatureKernel;
    int advectVelocityKernel;
    int divergenceKernel;
    int pressureKernel;
    int projectKernel;
    int solidHeatKernel;
    int solidConductionKernel;
    int solidToAirKernel;
    int radiationKernel;
    int combustionKernel;
    int combustionToAirKernel;
    int computeCurlKernel;
    int advectSmokeKernel;
    int smokeGenerationKernel;
    int vorticityKernel;
    int flameGenerationKernel;
    int advectFlameKernel;
    int flameHeatKernel;
    int injectFineKernel;
    int runtimeIgniteKernel;
    int extinguishKernel;
    int suppressantDecayKernel;

    ComputeBuffer runtimeVoxelBuffer;


    public float firePower = 0;
    public float diffusionRate = 1.0f;
    // Superseded by buoyancyScale below; kept so existing scenes still
    // deserialize. BuoyancyStep no longer reads it.
    public float buoyancyStrength = 0.01f;

    [Header("Buoyancy (physical)")]
    [Tooltip("Multiplier on true buoyant acceleration g*(T/Tamb - 1). 1.0 is " +
             "physically correct. The old linear buoyancyStrength produced " +
             "roughly a fourteenth of this, which is why the fire looked fluid.")]
    public float buoyancyScale = 1.0f;

    [Tooltip("Cap on buoyant acceleration in m/s^2. Real plumes stop " +
             "accelerating because they entrain cool air; this stands in for " +
             "the part of that the grid cannot resolve. ~30 is reasonable.")]
    public float maxBuoyantAccel = 30f;

    [Tooltip("Baroclinic vorticity production: (grad rho x grad p)/rho^2. " +
             "This is what actually rolls the plume edge into the vortices " +
             "that shed as puffs. Vorticity confinement can only amplify " +
             "vorticity that already exists -- this creates it.")]
    public float baroclinicStrength = 1.0f;

    [Header("Fine Grid Coupling (SmokeSim)")]
    [Tooltip("The SmokeSim that owns the visible fire and smoke. This sim " +
             "reads its fields to decide where heat is, so the thermal image " +
             "and the visible flame are two readouts of one state.")]
    public SmokeSim smokeSim;

    [Tooltip("Measured gas temperature of a fully luminous flame cell, deg C.")]
    public float flameGasTemp = 1000f;

    [Tooltip("Temperature fresh combustion products carry, deg C.")]
    public float smokeGasTemp = 300f;

    [Tooltip("How fast gas temperature relaxes toward the values above, 1/s. " +
             "Relaxing rather than assigning is what lets heat persist after " +
             "the flame has moved on -- which is what a thermal imager shows.")]
    public float fineCouplingRate = 8f;
    public float smokeBuoyancyStrength = 0.02f;
    public float smokeBuoyancyCutoff = 40f;   // °C above ambient -- smoke keeps its own lift above this, loses it below
    public float smokeSettleStrength = 0.15f; // gentle sink applied once smoke has cooled past the cutoff

    public float coolingRate = 0.0f;

    public float solidHeatTransferRate = 0.1f;  // unused
    public float solidConductionRate = 0.05f;   // unused
    public float solidDiffusionRate = 0.05f;
    public float solidToAirRate = 1.0f;

    public float airToSolidRate = 0.01f;
    public float solidCoolingRate = 0.001f;

    public float turbulenceStrength = 0.01f;
    public float vorticityStrength = 0.05f;

    public float radiationRange = 15f;   // unused
    public float radiationPower = 100f;  // unused
    public float burningRadiationRange = 15f;
    public float burningRadiationPower = 20f;

    public float burnRate = 1.0f;
    public float burnHeat = 1300f;
    public float flameAirHeat = 100f;

    public float smokeGenerationRate = 1.0f;
    public float smokeDecayRate = 0.01f;
    public float smokeDiffusionRate = 1f;

    public int pressureIterations = 50;

    public Material volumeMaterial;
    public Material smokeVolumeMaterial;
    public Material fireVolumeMaterial;
    public Material fireHeatVolumeMaterial;
    public Material thermalGreyscaleMaterial;

    Collider[] obstacleColliders;

    public Renderer[] thermalSurfaceRenderers;

    [Header("Char")]
    public float charRate = 0.15f;       // 0-1 over ~7s at full heat
    public float charStartTemp = 150f;
    public float charFullTemp = 400f;
    public bool useTimeForBurn = false;

    // Cached once in Start so we don't call FindObjectsOfType twice.
    ThermalMaterial[] _thermalObjects;

    public float CellSizePublic => cellSize;
    public Vector3 SimBoundsMin => GetSimulationBounds().min;
    public RenderTexture GetBurningVolume() => burningA;
    public RenderTexture GetObstacleVolume() => obstacleVolume;

    public RenderTexture GetFireHeatVolume() => flameHeatA;
    public RenderTexture GetFuelVolume() => fuelA;
    public bool IsInitialized { get; private set; }

    public bool infiniteFuel = false;

    // DEBUG//
    public bool igniteEverything;
    // DEBUG//

    // When false, FixedUpdate skips the entire fire/thermal compute step
    // (all ~70 dispatches per physics tick). Driven by DroneCamera so the
    // sim only runs while the thermal view is actually being used. The
    // GameObject/component stays enabled the whole time, so the singleton,
    // the thermal clones, and RegisterRuntimeBurningObject all keep working
    // regardless of this flag -- only the per-tick simulation work pauses.
    // Set true if you ever want fire to keep spreading in the background.
    public bool simulationActive = true;

    [Header("Room Shape & Openings")]
    public Collider volumeCollider; //The room shape

    [Tooltip("How fast openings vent heat and smoke to ambient")]
    public float openingVentRate = 1.0f;

    RenderTexture openingVolume;
    float[] openingData;
    Collider[] openingColliders;

    public RenderTexture GetOpeningVolume() => openingVolume;
    #endregion


    public void SetSimulationActive(bool active)
    {
        simulationActive = active;
    }

    private void Awake()
    {
        _instance = this;
    }

    //Initialisation of most data
    void Start()
    {
        CalculateGridSize();


        simulation.SetInt("GridX", gridX);
        simulation.SetInt("GridY", gridY);
        simulation.SetInt("GridZ", gridZ);

        heatKernel = simulation.FindKernel("HeatStep");

        buoyancyKernel = simulation.FindKernel("BuoyancyStep");

        advectTemperatureKernel =
            simulation.FindKernel("AdvectTemperatureStep");

        advectVelocityKernel = simulation.FindKernel("AdvectVelocityStep");

        divergenceKernel = simulation.FindKernel("ComputeDivergence");

        pressureKernel = simulation.FindKernel("SolvePressure");

        projectKernel = simulation.FindKernel("ProjectVelocity");

        solidHeatKernel = simulation.FindKernel("SolidHeatTransferStep");

        solidConductionKernel = simulation.FindKernel("SolidConductionStep");

        solidToAirKernel = simulation.FindKernel("SolidToAirStep");

        radiationKernel = simulation.FindKernel("RadiationStep");

        combustionKernel = simulation.FindKernel("CombustionStep");

        combustionToAirKernel = simulation.FindKernel("CombustionToAirStep");

        advectSmokeKernel = simulation.FindKernel("AdvectSmokeStep");

        smokeGenerationKernel = simulation.FindKernel("SmokeGenerationStep");

        computeCurlKernel = simulation.FindKernel("ComputeCurl");

        vorticityKernel = simulation.FindKernel("VorticityConfinementStep");

        flameGenerationKernel = simulation.FindKernel("FlameGenerationStep");

        advectFlameKernel = simulation.FindKernel("AdvectFlameStep");

        flameHeatKernel = simulation.FindKernel("FlameHeatStep");

        injectFineKernel = simulation.FindKernel("InjectFineFieldsStep");

        runtimeIgniteKernel = simulation.FindKernel("RuntimeIgnite");

        extinguishKernel = simulation.FindKernel("ExtinguishStep");

        suppressantDecayKernel = simulation.FindKernel("SuppressantDecayStep");

        charAccumKernel = simulation.FindKernel("CharAccumStep");
        charVolume = CreateVolume();

        copyVolumeKernel = simulation.FindKernel("CopyVolumeToBuffer");
        volumeReadBuffer = new ComputeBuffer(gridX * gridY * gridZ, sizeof(float));

        fineFallback = new RenderTexture(1, 1, 0, RenderTextureFormat.RFloat);
        fineFallback.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        fineFallback.volumeDepth = 1;
        fineFallback.enableRandomWrite = true;
        fineFallback.wrapMode = TextureWrapMode.Clamp;
        fineFallback.filterMode = FilterMode.Point;
        fineFallback.Create();
        // Fresh RTs are undefined, not zero. Clear once so the fallback
        // genuinely reads 0 rather than whatever was in that memory.
        {
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = fineFallback;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = prev;
        }

        fireSourceBuffer = new ComputeBuffer(MaxFireSources, sizeof(float) * 7);
        _fireSourceCPUBuffer = new FireSourceGPU[MaxFireSources];

        runtimeVoxelBuffer = new ComputeBuffer(100000, sizeof(uint) * 4);

        temperatureA = CreateVolume();
        temperatureB = CreateVolume();
        velocityA = CreateVectorVolume();
        velocityB = CreateVectorVolume();
        divergence = CreateVolume();
        pressureA = CreateVolume();
        pressureB = CreateVolume();
        obstacleVolume = CreateVolume();
        materialVolume = CreateVolume();
        openingVolume = CreateVolume();

        solidTemperatureA = CreateVolume();
        solidTemperatureB = CreateVolume();

        fuelA = CreateVolume();
        fuelB = CreateVolume();

        burningA = CreateVolume();
        burningB = CreateVolume();

        suppressantA = CreateVolume();
        suppressantB = CreateVolume();

        curlA = CreateVectorVolume();

        smokeA = CreateVolume();
        smokeB = CreateVolume();

        flameA = CreateVolume();
        flameB = CreateVolume();

        flameHeatA = CreateVolume();
        flameHeatB = CreateVolume();

        obstacleData = new float[gridX * gridY * gridZ];

        materialData = new float[gridX * gridY * gridZ];

        fuelData = new float[gridX * gridY * gridZ];

        burningData = new float[gridX * gridY * gridZ];

        openingData = new float[gridX * gridY * gridZ];

        // Cached once in Start so we don't call FindObjectsOfType twice.
        _thermalObjects = GameObject.FindObjectsByType<ThermalMaterial>(FindObjectsSortMode.None);

        FindObstacleColliders();
        FindOpenings();
        BuildObstacleVolume();
        InitializeSolidTemperature();
        CreateThermalClones();

        IsInitialized = true;

        ResetChar();
    }

    void CalculateGridSize()
    {
        Bounds bounds = GetSimulationBounds();

        gridX = Mathf.Max(4, Mathf.CeilToInt(bounds.size.x / cellSize));

        gridY = Mathf.Max(4, Mathf.CeilToInt(bounds.size.y / cellSize));

        gridZ = Mathf.Max(4, Mathf.CeilToInt(bounds.size.z / cellSize));

        Debug.Log($"Thermal Grid: {gridX} x {gridY} x {gridZ}");
    }

    RenderTexture CreateVolume()
    {
        RenderTexture rt =
            new RenderTexture(gridX, gridY, 0, RenderTextureFormat.RFloat);

        rt.dimension = TextureDimension.Tex3D;

        rt.volumeDepth = gridZ;

        rt.enableRandomWrite = true;

        rt.wrapMode = TextureWrapMode.Clamp;

        rt.Create();

        return rt;
    }

    // Uploads every active heat source (the legacy single `fireSource`
    // Transform, if assigned, plus any registered IMobileFireSource) into
    // one buffer that both HeatStep and RadiationStep read from, sampling
    // each source's CURRENT world position fresh this frame. Called once
    // per FixedUpdate before those two dispatches.
    void BuildFireSourceBuffer()
    {
        int count = 0;

        if (fireSource != null)
        {
            _fireSourceCPUBuffer[count++] = new FireSourceGPU
            {
                position = WorldToVoxel(fireSource.position),
                heatRadius = 8f,
                heatPower = firePower,
                radiationRange = radiationRange,
                radiationPower = radiationPower
            };
        }

        for (int i = 0; i < _fireSources.Count && count < MaxFireSources; i++)
        {
            IMobileFireSource src = _fireSources[i];
            if (src == null) continue; // defensive: destroyed without unregistering

            _fireSourceCPUBuffer[count++] = new FireSourceGPU
            {
                position = WorldToVoxel(src.WorldPosition),
                heatRadius = src.HeatRadius,
                heatPower = src.HeatPower,
                radiationRange = src.RadiationRange,
                radiationPower = src.RadiationPower
            };
        }

        if (count >= MaxFireSources && _fireSources.Count + (fireSource != null ? 1 : 0) > MaxFireSources)
        {
            if (!_loggedFireSourceOverflow)
            {
                Debug.LogWarning($"[ThermalSimulation] More than {MaxFireSources} active fire sources -- extras are being ignored until some are removed.");
                _loggedFireSourceOverflow = true;
            }
        }
        else
        {
            _loggedFireSourceOverflow = false;
        }

        // Upload the whole fixed-size array every frame (a few hundred
        // bytes -- trivial) rather than a partial range, so we don't have
        // to special-case count == 0. Unused slots are simply never read,
        // since the kernels loop exactly `count` times.
        fireSourceBuffer.SetData(_fireSourceCPUBuffer);

        simulation.SetBuffer(heatKernel, "FireSources", fireSourceBuffer);
        simulation.SetBuffer(radiationKernel, "FireSources", fireSourceBuffer);
        simulation.SetInt("FireSourceCount", count);
    }

    void FixedUpdate()
    {
        // Char globals bound before the gate so char stays visible in normal
        // view even while the sim is paused.

        //Debug.Log($"[ThermalSimulation] Created {thermalSurfaceRenderers.Length} thermal clones. " +
        //   $"Total '_Thermal' objects in scene: {GameObject.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Count(r => r.name.EndsWith("_Thermal"))}");

        Bounds bounds = GetSimulationBounds();

        Shader.SetGlobalTexture("_CharTex", charVolume);

        Shader.SetGlobalVector("_ThermalVolumeMin", bounds.min);

        Shader.SetGlobalVector("_ThermalVolumeSize", bounds.size);

        // Char accumulation only depends on which voxels are currently
        // burning (BurningIn) and where obstacles are -- it doesn't need
        // the heat/fluid pipeline below, so it's dispatched here, ahead of
        // the simulationActive gate. This is what actually makes char keep
        // building while the player isn't in thermal view; previously this
        // dispatch lived below the `return` and never ran until the sim
        // was active, so charVolume stayed all zeros until then.
        simulation.SetTexture(charAccumKernel, "BurningIn", burningA);

        simulation.SetTexture(charAccumKernel, "Obstacle", obstacleVolume);

        simulation.SetTexture(charAccumKernel, "CharVolume", charVolume);

        simulation.SetFloat("CharRate", charRate);

        simulation.SetFloat("CharStartTemp", charStartTemp);

        simulation.SetFloat("CharFullTemp", charFullTemp);
        simulation.SetInt("UseTimeForBurn", useTimeForBurn ? 1 : 0);
        simulation.SetFloat("DeltaTime", Time.fixedDeltaTime);

        simulation.Dispatch(charAccumKernel, Mathf.CeilToInt(gridX / 8f),
                            Mathf.CeilToInt(gridY / 8f),
                            Mathf.CeilToInt(gridZ / 8f));

        // Paused while not in thermal view (set by DroneCamera). Skips all
        // fire/thermal compute dispatches for this tick -- the main FPS win
        // when the thermal system isn't being used.
        if (!simulationActive)
            return;

        // Cell size must be uploaded before anything that advects: velocity
        // is stored in m/s and converted to voxels at point of use.
        simulation.SetFloat("CellSize", cellSize);

        UploadFineCoupling();

        // -----------------------------------------------------------------
        // Read SmokeSim's fields in FIRST, before any of the thermal steps.
        // SmokeSim owns where the fire is; this sim owns how hot things are.
        // Injecting here means buoyancy, radiation and conduction all act on
        // the fire the player can actually see.
        // -----------------------------------------------------------------
        if (HasFineFields())
        {
            simulation.SetTexture(injectFineKernel, "TemperatureIn", temperatureA);
            simulation.SetTexture(injectFineKernel, "TemperatureOut", temperatureB);
            simulation.SetTexture(injectFineKernel, "SmokeOut", smokeB);
            simulation.SetTexture(injectFineKernel, "Obstacle", obstacleVolume);
            simulation.SetTexture(injectFineKernel, "FineFireIn", smokeSim.FireVolume);
            simulation.SetTexture(injectFineKernel, "FineSmokeIn", smokeSim.SmokeVolume);

            simulation.SetFloat("DeltaTime", Time.fixedDeltaTime);

            simulation.Dispatch(injectFineKernel, Mathf.CeilToInt(gridX / 8f),
                                Mathf.CeilToInt(gridY / 8f),
                                Mathf.CeilToInt(gridZ / 8f));

            RenderTexture injTemp = temperatureA;
            temperatureA = temperatureB;
            temperatureB = injTemp;

            RenderTexture injSmoke = smokeA;
            smokeA = smokeB;
            smokeB = injSmoke;
        }

        simulation.SetTexture(heatKernel, "TemperatureIn", temperatureA);

        simulation.SetTexture(heatKernel, "TemperatureOut", temperatureB);

        simulation.SetFloat("DeltaTime", Time.fixedDeltaTime);

        simulation.SetFloat("AmbientTemp", 20);

        simulation.SetFloat("CoolingRate", coolingRate);

        simulation.SetFloat("DiffusionRate", diffusionRate);

        // Rebuilds and uploads FireSources once per tick -- covers both
        // HeatStep (here) and RadiationStep (later this same tick) with
        // every source's current live position.
        BuildFireSourceBuffer();

        simulation.SetTexture(heatKernel, "Obstacle", obstacleVolume);

        simulation.SetTexture(heatKernel, "OpeningVolume", openingVolume);

        simulation.SetFloat("OpeningVentRate", openingVentRate);

        simulation.Dispatch(heatKernel, Mathf.CeilToInt(gridX / 8f),
                            Mathf.CeilToInt(gridY / 8f),
                            Mathf.CeilToInt(gridZ / 8f));

        RenderTexture temp = temperatureA;
        temperatureA = temperatureB;
        temperatureB = temp;

        simulation.SetTexture(buoyancyKernel, "TemperatureIn", temperatureA);

        simulation.SetTexture(buoyancyKernel, "SmokeIn", smokeA);

        simulation.SetTexture(buoyancyKernel, "VelocityIn", velocityA);

        simulation.SetTexture(buoyancyKernel, "VelocityOut", velocityB);

        simulation.SetFloat("BuoyancyStrength", buoyancyStrength);

        simulation.SetFloat("BuoyancyScale", buoyancyScale);

        simulation.SetFloat("MaxBuoyantAccel", maxBuoyantAccel);

        simulation.SetFloat("SmokeBuoyancyStrength", smokeBuoyancyStrength);

        simulation.SetFloat("SmokeBuoyancyCutoff", smokeBuoyancyCutoff);

        simulation.SetFloat("SmokeSettleStrength", smokeSettleStrength);

        simulation.SetFloat("TurbulenceStrength", turbulenceStrength);

        simulation.SetFloat("AmbientTemp", 20);

        simulation.SetFloat("DeltaTime", Time.fixedDeltaTime);

        simulation.SetTexture(buoyancyKernel, "Obstacle", obstacleVolume);

        simulation.Dispatch(buoyancyKernel, Mathf.CeilToInt(gridX / 8f),
                            Mathf.CeilToInt(gridY / 8f),
                            Mathf.CeilToInt(gridZ / 8f));

        RenderTexture velTemp = velocityA;
        velocityA = velocityB;
        velocityB = velTemp;

        simulation.SetTexture(advectVelocityKernel, "VelocityIn", velocityA);

        simulation.SetTexture(advectVelocityKernel, "VelocityOut", velocityB);

        simulation.SetFloat("DeltaTime", Time.fixedDeltaTime);

        simulation.SetTexture(advectVelocityKernel, "Obstacle", obstacleVolume);

        simulation.Dispatch(advectVelocityKernel, Mathf.CeilToInt(gridX / 8f),
                            Mathf.CeilToInt(gridY / 8f),
                            Mathf.CeilToInt(gridZ / 8f));

        RenderTexture velAdvect = velocityA;
        velocityA = velocityB;
        velocityB = velAdvect;

        // curl
        simulation.SetTexture(computeCurlKernel, "VelocityIn", velocityA);

        simulation.SetTexture(computeCurlKernel, "CurlOut", curlA);

        // The baroclinic term needs the temperature field.
        simulation.SetTexture(computeCurlKernel, "TemperatureIn", temperatureA);

        simulation.Dispatch(computeCurlKernel, Mathf.CeilToInt(gridX / 8f),
                            Mathf.CeilToInt(gridY / 8f),
                            Mathf.CeilToInt(gridZ / 8f));

        // curl

        // vorticity
        simulation.SetTexture(vorticityKernel, "VelocityIn", velocityA);

        simulation.SetTexture(vorticityKernel, "VelocityOut", velocityB);

        simulation.SetTexture(vorticityKernel, "CurlIn", curlA);

        simulation.SetFloat("VorticityStrength", vorticityStrength);

        simulation.SetFloat("BaroclinicStrength", baroclinicStrength);

        simulation.Dispatch(vorticityKernel, Mathf.CeilToInt(gridX / 8f),
                            Mathf.CeilToInt(gridY / 8f),
                            Mathf.CeilToInt(gridZ / 8f));

        RenderTexture temp3 = velocityA;
        velocityA = velocityB;
        velocityB = temp3;
        // vorticity

        simulation.SetTexture(advectTemperatureKernel, "TemperatureIn",
                              temperatureA);

        simulation.SetTexture(advectTemperatureKernel, "TemperatureOut",
                              temperatureB);

        simulation.SetTexture(advectTemperatureKernel, "VelocityIn", velocityA);

        simulation.SetFloat("DeltaTime", Time.fixedDeltaTime);

        simulation.SetTexture(advectTemperatureKernel, "Obstacle",
                              obstacleVolume);

        simulation.Dispatch(
            advectTemperatureKernel, Mathf.CeilToInt(gridX / 8f),
            Mathf.CeilToInt(gridY / 8f), Mathf.CeilToInt(gridZ / 8f));

        RenderTexture tempAdvect = temperatureA;
        temperatureA = temperatureB;
        temperatureB = tempAdvect;

        simulation.SetTexture(divergenceKernel, "VelocityIn", velocityA);

        simulation.SetTexture(divergenceKernel, "Divergence", divergence);

        simulation.SetTexture(divergenceKernel, "Obstacle", obstacleVolume);

        simulation.Dispatch(divergenceKernel, Mathf.CeilToInt(gridX / 8f),
                            Mathf.CeilToInt(gridY / 8f),
                            Mathf.CeilToInt(gridZ / 8f));


        simulation.SetTexture(pressureKernel, "Divergence", divergence);
        simulation.SetTexture(pressureKernel, "Obstacle", obstacleVolume);
        simulation.SetTexture(pressureKernel, "OpeningVolume", openingVolume);

        for (int i = 0; i < pressureIterations; i++)
        {
            simulation.SetTexture(pressureKernel, "PressureIn", pressureA);

            simulation.SetTexture(pressureKernel, "PressureOut", pressureB);

            simulation.Dispatch(pressureKernel, Mathf.CeilToInt(gridX / 8f),
                                Mathf.CeilToInt(gridY / 8f),
                                Mathf.CeilToInt(gridZ / 8f));

            RenderTexture pTemp = pressureA;
            pressureA = pressureB;
            pressureB = pTemp;
        }

        simulation.SetTexture(projectKernel, "PressureIn", pressureA);

        simulation.SetTexture(projectKernel, "VelocityIn", velocityA);

        simulation.SetTexture(projectKernel, "VelocityOut", velocityB);

        simulation.SetTexture(projectKernel, "Obstacle", obstacleVolume);

        simulation.SetTexture(projectKernel, "OpeningVolume", openingVolume);

        simulation.Dispatch(projectKernel, Mathf.CeilToInt(gridX / 8f),
                            Mathf.CeilToInt(gridY / 8f),
                            Mathf.CeilToInt(gridZ / 8f));

        RenderTexture velProj = velocityA;
        velocityA = velocityB;
        velocityB = velProj;

        simulation.SetTexture(radiationKernel, "SolidTemperatureIn",
                              solidTemperatureA);

        simulation.SetTexture(radiationKernel, "SolidTemperatureOut",
                              solidTemperatureB);

        simulation.SetTexture(radiationKernel, "Obstacle", obstacleVolume);

        simulation.SetTexture(radiationKernel, "MaterialVolume",
                              materialVolume);

        simulation.SetFloat("BurningRadiationRange", burningRadiationRange);

        simulation.SetFloat("BurningRadiationPower", burningRadiationPower);

        simulation.SetTexture(radiationKernel, "BurningIn", burningA);

        simulation.Dispatch(radiationKernel, Mathf.CeilToInt(gridX / 8f),
                            Mathf.CeilToInt(gridY / 8f),
                            Mathf.CeilToInt(gridZ / 8f));

        RenderTexture radiationTemp = solidTemperatureA;

        solidTemperatureA = solidTemperatureB;

        solidTemperatureB = radiationTemp;

        simulation.SetTexture(solidHeatKernel, "TemperatureIn", temperatureA);

        simulation.SetTexture(solidHeatKernel, "SolidTemperatureIn",
                              solidTemperatureA);

        simulation.SetTexture(solidHeatKernel, "SolidTemperatureOut",
                              solidTemperatureB);

        simulation.SetTexture(solidHeatKernel, "Obstacle", obstacleVolume);

        simulation.SetFloat("SolidHeatTransferRate", solidHeatTransferRate);

        simulation.SetFloat("DeltaTime", Time.fixedDeltaTime);

        simulation.SetFloat("SolidConductionRate", solidConductionRate);

        simulation.SetTexture(solidHeatKernel, "MaterialVolume",
                              materialVolume);

        simulation.Dispatch(solidHeatKernel, Mathf.CeilToInt(gridX / 8f),
                            Mathf.CeilToInt(gridY / 8f),
                            Mathf.CeilToInt(gridZ / 8f));

        RenderTexture solidTemp = solidTemperatureA;

        solidTemperatureA = solidTemperatureB;

        solidTemperatureB = solidTemp;

        simulation.SetTexture(solidConductionKernel, "SolidTemperatureIn",
                              solidTemperatureA);

        simulation.SetTexture(solidConductionKernel, "SolidTemperatureOut",
                              solidTemperatureB);

        simulation.SetTexture(solidConductionKernel, "Obstacle",
                              obstacleVolume);

        simulation.SetFloat("SolidDiffusionRate", solidDiffusionRate);

        simulation.SetFloat("DeltaTime", Time.fixedDeltaTime);

        simulation.SetTexture(solidConductionKernel, "MaterialVolume",
                              materialVolume);

        simulation.Dispatch(solidConductionKernel, Mathf.CeilToInt(gridX / 8f),
                            Mathf.CeilToInt(gridY / 8f),
                            Mathf.CeilToInt(gridZ / 8f));

        RenderTexture solidCond = solidTemperatureA;

        solidTemperatureA = solidTemperatureB;

        solidTemperatureB = solidCond;

        simulation.SetTexture(solidToAirKernel, "TemperatureIn", temperatureA);

        simulation.SetTexture(solidToAirKernel, "TemperatureOut", temperatureB);

        simulation.SetTexture(solidToAirKernel, "SolidTemperatureIn",
                              solidTemperatureA);

        simulation.SetTexture(solidToAirKernel, "Obstacle", obstacleVolume);

        simulation.SetFloat("SolidToAirRate", solidToAirRate);

        simulation.SetFloat("AirToSolidRate", airToSolidRate);

        simulation.SetFloat("SolidCoolingRate", solidCoolingRate);

        simulation.SetFloat("DeltaTime", Time.fixedDeltaTime);

        simulation.SetTexture(solidToAirKernel, "MaterialVolume",
                              materialVolume);

        simulation.Dispatch(solidToAirKernel, Mathf.CeilToInt(gridX / 8f),
                            Mathf.CeilToInt(gridY / 8f),
                            Mathf.CeilToInt(gridZ / 8f));

        RenderTexture tempAir = temperatureA;

        temperatureA = temperatureB;

        temperatureB = tempAir;

        // combustion
        simulation.SetTexture(combustionKernel, "FuelIn", fuelA);

        simulation.SetTexture(combustionKernel, "FuelOut", fuelB);

        simulation.SetTexture(combustionKernel, "BurningIn", burningA);

        simulation.SetTexture(combustionKernel, "BurningOut", burningB);

        simulation.SetTexture(combustionKernel, "SuppressantIn", suppressantA);
        simulation.SetTexture(combustionKernel, "SuppressantOut", suppressantB);

        simulation.SetFloat("BurnRate", burnRate);

        simulation.SetFloat("BurnHeat", burnHeat);


        simulation.SetInt("InfiniteFuel", infiniteFuel ? 1 : 0);

        simulation.SetTexture(combustionKernel, "SolidTemperatureIn",
                              solidTemperatureA);

        simulation.SetTexture(combustionKernel, "SolidTemperatureOut",
                              solidTemperatureB);

        simulation.SetTexture(combustionKernel, "MaterialVolume",
                              materialVolume);

        simulation.SetTexture(combustionKernel, "Obstacle", obstacleVolume);

        simulation.SetFloat("DeltaTime", Time.fixedDeltaTime);

        simulation.Dispatch(combustionKernel, Mathf.CeilToInt(gridX / 8f),
                            Mathf.CeilToInt(gridY / 8f),
                            Mathf.CeilToInt(gridZ / 8f));

        RenderTexture fuelTemp = fuelA;

        fuelA = fuelB;

        fuelB = fuelTemp;

        RenderTexture burningTemp = burningA;

        burningA = burningB;

        burningB = burningTemp;

        RenderTexture suppressantTemp = suppressantA;

        suppressantA = suppressantB;

        suppressantB = suppressantTemp;

        RenderTexture solidTemp2 = solidTemperatureA;

        solidTemperatureA = solidTemperatureB;

        solidTemperatureB = solidTemp2;
        // combustion

        //surpression
        simulation.SetTexture(
            suppressantDecayKernel,
            "SuppressantIn",
            suppressantA);

        simulation.SetTexture(
            suppressantDecayKernel,
            "SuppressantOut",
            suppressantB);

        simulation.SetFloat(
            "DeltaTime",
            Time.fixedDeltaTime);

        simulation.Dispatch(
            suppressantDecayKernel,
            Mathf.CeilToInt(gridX / 8f),
            Mathf.CeilToInt(gridY / 8f),
            Mathf.CeilToInt(gridZ / 8f));

        (suppressantA, suppressantB) =
            (suppressantB, suppressantA);
        //surpression

        // -----------------------------------------------------------------
        // The coarse sim's own smoke generation and advection are skipped
        // whenever SmokeSim is driving. Running both would mean two
        // independent smoke fields on two grids, which is exactly the
        // divergence this coupling exists to eliminate: InjectFineFieldsStep
        // already wrote the downsampled fine smoke into smokeA this tick,
        // and these dispatches would overwrite it.
        // -----------------------------------------------------------------
        if (!HasFineFields())
        {

            // smoke
            simulation.SetTexture(smokeGenerationKernel, "SmokeIn", smokeA);

            simulation.SetTexture(smokeGenerationKernel, "SmokeOut", smokeB);

            simulation.SetTexture(smokeGenerationKernel, "BurningIn", burningA);

            simulation.SetTexture(smokeGenerationKernel, "OpeningVolume", openingVolume);

            simulation.SetFloat("SmokeGenerationRate", smokeGenerationRate);

            simulation.SetFloat("OpeningVentRate", openingVentRate);

            simulation.Dispatch(smokeGenerationKernel, Mathf.CeilToInt(gridX / 8f),
                                Mathf.CeilToInt(gridY / 8f),
                                Mathf.CeilToInt(gridZ / 8f));

            RenderTexture smokeTemp = smokeA;
            smokeA = smokeB;
            smokeB = smokeTemp;
            // smoke

            // smoke2
            simulation.SetTexture(advectSmokeKernel, "SmokeIn", smokeA);

            simulation.SetTexture(advectSmokeKernel, "SmokeOut", smokeB);

            simulation.SetTexture(advectSmokeKernel, "VelocityIn", velocityA);

            simulation.SetTexture(advectSmokeKernel, "Obstacle", obstacleVolume);

            simulation.SetTexture(advectSmokeKernel, "OpeningVolume", openingVolume);

            simulation.SetFloat("SmokeDecayRate", smokeDecayRate);

            simulation.SetFloat("DeltaTime", Time.fixedDeltaTime);

            simulation.SetFloat("SmokeDiffusionRate", smokeDiffusionRate);

            simulation.SetFloat("OpeningVentRate", openingVentRate);

            simulation.Dispatch(advectSmokeKernel, Mathf.CeilToInt(gridX / 8f),
                                Mathf.CeilToInt(gridY / 8f),
                                Mathf.CeilToInt(gridZ / 8f));

            smokeTemp = smokeA;
            smokeA = smokeB;
            smokeB = smokeTemp;
            // smoke2

        } // end !HasFineFields smoke block

        // combustion to air
        simulation.SetTexture(combustionToAirKernel, "BurningIn", burningA);

        simulation.SetTexture(combustionToAirKernel, "TemperatureIn",
                              temperatureA);

        simulation.SetTexture(combustionToAirKernel, "TemperatureOut",
                              temperatureB);

        simulation.SetTexture(combustionToAirKernel, "Obstacle",
                              obstacleVolume);

        simulation.SetFloat("FlameAirHeat", flameAirHeat);

        simulation.SetFloat("DeltaTime", Time.fixedDeltaTime);

        simulation.Dispatch(combustionToAirKernel, Mathf.CeilToInt(gridX / 8f),
                            Mathf.CeilToInt(gridY / 8f),
                            Mathf.CeilToInt(gridZ / 8f));

        RenderTexture flameAirTemp = temperatureA;

        temperatureA = temperatureB;

        temperatureB = flameAirTemp;
        // combustion to air

        // flame generation

        simulation.SetTexture(flameGenerationKernel, "BurningIn", burningA);

        simulation.SetTexture(flameGenerationKernel, "TemperatureIn",
                              temperatureA);

        simulation.SetTexture(flameGenerationKernel, "FlameIn", flameA);

        simulation.SetTexture(flameGenerationKernel, "FlameOut", flameB);

        simulation.SetFloat("FlameTemperatureMin", 200);

        simulation.SetFloat("FlameTemperatureMax", 1000);

        simulation.Dispatch(flameGenerationKernel, Mathf.CeilToInt(gridX / 8f),
                            Mathf.CeilToInt(gridY / 8f),
                            Mathf.CeilToInt(gridZ / 8f));

        RenderTexture flameTemp = flameA;
        flameA = flameB;
        flameB = flameTemp;
        // flame generation

        // flame advection
        simulation.SetTexture(advectFlameKernel, "FlameIn", flameA);

        simulation.SetTexture(advectFlameKernel, "FlameOut", flameB);

        simulation.SetTexture(advectFlameKernel, "VelocityIn", velocityA);

        simulation.SetTexture(advectFlameKernel, "Obstacle", obstacleVolume);

        simulation.SetFloat("DeltaTime", Time.fixedDeltaTime);

        simulation.Dispatch(advectFlameKernel, Mathf.CeilToInt(gridX / 8f),
                            Mathf.CeilToInt(gridY / 8f),
                            Mathf.CeilToInt(gridZ / 8f));

        flameTemp = flameA;
        flameA = flameB;
        flameB = flameTemp;
        // flame advection

        // temp
        simulation.SetTexture(flameHeatKernel, "FlameIn", flameA);

        simulation.SetTexture(flameHeatKernel, "TemperatureIn", temperatureA);

        simulation.SetTexture(flameHeatKernel, "FlameHeatOut", flameHeatA);

        simulation.SetTexture(flameHeatKernel, "Obstacle", obstacleVolume);

        simulation.SetTexture(flameHeatKernel, "SmokeIn", smokeA);

        // The thermal image is built from the same fields the player sees.
        //
        // These MUST be bound on every dispatch, not just when the fine
        // fields exist: this kernel is dispatched unconditionally below,
        // and FlameHeatStep samples both textures with no guard of its own.
        // Before SmokeSim.Init completes -- or if smokeSim is unassigned --
        // the fallback stands in and contributes nothing.
        bool hasFine = HasFineFields();
        simulation.SetTexture(flameHeatKernel, "FineFireIn",
                              hasFine ? smokeSim.FireVolume : fineFallback);
        simulation.SetTexture(flameHeatKernel, "FineSmokeIn",
                              hasFine ? smokeSim.SmokeVolume : fineFallback);

        simulation.SetFloat("AmbientTemp", 20);

        simulation.Dispatch(flameHeatKernel, Mathf.CeilToInt(gridX / 8f),
                            Mathf.CeilToInt(gridY / 8f),
                            Mathf.CeilToInt(gridZ / 8f));
        // temp

        Shader.SetGlobalTexture("_SolidTemperatureTex", solidTemperatureA);

        Shader.SetGlobalTexture("_ObstacleTex", obstacleVolume);


        volumeMaterial.SetTexture("_TemperatureTex", temperatureA);

        simulation.SetFloat("SmokeDecayRate", smokeDecayRate);
        smokeVolumeMaterial.SetTexture("_SmokeTex", smokeA);

        Shader.SetGlobalTexture("_BurningTex", burningA);
        Shader.SetGlobalTexture("_FuelTex", fuelA);

        fireVolumeMaterial.SetTexture("_FlameTex", flameA);
        fireVolumeMaterial.SetTexture("_SmokeTex", smokeA);
        smokeVolumeMaterial.SetTexture("_FlameTex", flameA);
        smokeVolumeMaterial.SetTexture("_TemperatureTex", temperatureA);
        fireHeatVolumeMaterial.SetTexture("_FireHeatTex", flameHeatA);
    }
    void InitializeSolidTemperature()
    {
        bool testVoxel = true;

        Color[] pixels = new Color[gridX * gridY * gridZ];

        for (int i = 0; i < pixels.Length; i++)
        {
            float temp = obstacleData[i] > 0.5f ? 20f : 20f;

            if (!testVoxel && obstacleData[i] > 0.5f)
            {
                temp = 1000f;
                testVoxel = true;

                Debug.Log("Injected test voxel");
            }

            pixels[i] = new Color(temp, temp, temp, temp);
        }

        Texture3D initTex =
            new Texture3D(gridX, gridY, gridZ, TextureFormat.RFloat, false);

        initTex.SetPixels(pixels);

        initTex.Apply();

        Graphics.CopyTexture(initTex, solidTemperatureA);

        Graphics.CopyTexture(initTex, solidTemperatureB);
    }

    RenderTexture CreateVectorVolume()
    {
        RenderTexture rt =
            new RenderTexture(gridX, gridY, 0, RenderTextureFormat.ARGBFloat);

        rt.dimension = TextureDimension.Tex3D;

        rt.volumeDepth = gridZ;

        rt.enableRandomWrite = true;

        rt.wrapMode = TextureWrapMode.Clamp;

        rt.Create();

        return rt;
    }

    // Reads any Tex3D<float> volume (charVolume, burningA, temperatureA,
    // etc.) back to the CPU with the full depth included. Do not call
    // AsyncGPUReadback.Request() directly on a 3D RenderTexture -- see the
    // note on volumeReadBuffer above.
    public void RequestVolumeReadback(RenderTexture volume, Action<NativeArray<float>> onComplete)
    {
        simulation.SetTexture(copyVolumeKernel, "CopySource", volume);
        simulation.SetBuffer(copyVolumeKernel, "CopyDest", volumeReadBuffer);
        simulation.SetInts("CopyDims", gridX, gridY, gridZ);

        simulation.Dispatch(
            copyVolumeKernel,
            Mathf.CeilToInt(gridX / 8f),
            Mathf.CeilToInt(gridY / 8f),
            Mathf.CeilToInt(gridZ / 4f));

        AsyncGPUReadback.Request(volumeReadBuffer, request =>
        {
            if (request.hasError)
            {
                Debug.LogWarning("[ThermalSimulation] Volume readback failed.");
                return;
            }

            onComplete?.Invoke(request.GetData<float>());
        });
    }

    void OnDestroy()
    {
        volumeReadBuffer?.Release();
        fireSourceBuffer?.Release();
        if (fineFallback != null) fineFallback.Release();
    }

    public RenderTexture GetTemperatureVolume()
    {
        return temperatureA;
    }

    public RenderTexture GetSolidTemperatureVolume()
    {
        return solidTemperatureA;
    }

    public RenderTexture GetCharVolume()
    {
        return charVolume;
    }

    public int GridXPublic => gridX;
    public int GridYPublic => gridY;
    public int GridZPublic => gridZ;

    void BuildObstacleVolume()
    {
        System.Array.Clear(obstacleData, 0, obstacleData.Length);
        System.Array.Clear(materialData, 0, materialData.Length);
        System.Array.Clear(fuelData, 0, fuelData.Length);
        System.Array.Clear(burningData, 0, burningData.Length);

        bool[] assigned = new bool[obstacleData.Length];

        Bounds simBounds = GetSimulationBounds();
        Vector3 simMin = simBounds.min;

        // Non-rectangular rooms: anything outside the room-shape mesh is
        // exterior, mark it solid before the per-object obstacle pass below.
        MarkExteriorAsObstacle(simMin);

        for (int c = 0; c < obstacleColliders.Length; c++)
        {
            Collider collider = obstacleColliders[c];
            if (collider == null) continue;

            MeshCollider meshCollider = collider as MeshCollider;
            bool nonConvexMesh = meshCollider != null && !meshCollider.convex;

            //Voxel range covering this collider's world-space  
            Bounds b = collider.bounds;
            Vector3 minV = (b.min - simMin) / cellSize;
            Vector3 maxV = (b.max - simMin) / cellSize;

            int minX = Mathf.Clamp(Mathf.FloorToInt(minV.x), 0, gridX - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(minV.y), 0, gridY - 1);
            int minZ = Mathf.Clamp(Mathf.FloorToInt(minV.z), 0, gridZ - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(maxV.x), 0, gridX - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(maxV.y), 0, gridY - 1);
            int maxZ = Mathf.Clamp(Mathf.CeilToInt(maxV.z), 0, gridZ - 1);

            ThermalMaterial thermalMaterial =
                collider.GetComponentInParent<ThermalMaterial>();

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        int index = x + gridX * (y + gridY * z);
                        if (assigned[index]) continue;

                        Vector3 voxelPos = simMin +
                            new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * cellSize;

                        bool inside;
                        if (nonConvexMesh)
                        {
                            inside = false;
                            Collider[] hits = Physics.OverlapSphere(
                                voxelPos, cellSize * 0.45f);

                            foreach (var hit in hits)
                            {
                                if (hit == meshCollider)
                                {
                                    inside = true;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            Vector3 closest = collider.ClosestPoint(voxelPos);
                            float dist = Vector3.Distance(closest, voxelPos);
                            inside = dist < cellSize * 0.5f;   // within half a voxel counts as solid
                        }

                        if (!inside) continue;

                        float materialId = 0;
                        float fuel = 0;
                        float burning = 0;

                        if (thermalMaterial != null)
                        {
                            materialId = (float)thermalMaterial.materialType;

                            if (thermalMaterial.combustible)
                            {
                                fuel = thermalMaterial.fuelAmount;
                            }
                        }

                        if (igniteEverything)
                        {
                            if (fuel > 0) burning = 1;
                        }

                        if (thermalMaterial != null &&
                            thermalMaterial.igniteInstant)
                        {
                            if (fuel > 0) burning = 1;
                        }

                        obstacleData[index] = 1f;
                        materialData[index] = materialId;
                        fuelData[index] = fuel;
                        burningData[index] = burning;
                        assigned[index] = true;
                    }
                }
            }
        }

        // Doors/windows punch holes through whatever was marked solid above --
        // always wins, so an opening collider on top of a wall still opens it.
        CarveOpenings(simMin);

        UploadObstacleVolume();
        UploadMaterialVolume();
        UploadFuelVolume();
        UploadBurningVolume();
        UploadOpeningVolume();
    }

    void MarkExteriorAsObstacle(Vector3 simMin)
    {
        if (volumeCollider == null)
            return;

        MeshCollider meshCollider = volumeCollider as MeshCollider;

        if (meshCollider == null || meshCollider.convex)
            return;

        for (int z = 0; z < gridZ; z++)
        {
            for (int y = 0; y < gridY; y++)
            {
                for (int x = 0; x < gridX; x++)
                {
                    int index = x + gridX * (y + gridY * z);

                    Vector3 voxelPos = simMin +
                        new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * cellSize;

                    if (!PointInsideMeshCollider(meshCollider, voxelPos))
                    {
                        obstacleData[index] = 1f;
                    }
                }
            }
        }
    }

    void CarveOpenings(Vector3 simMin)
    {
        System.Array.Clear(openingData, 0, openingData.Length);

        if (openingColliders == null)
            return;

        for (int c = 0; c < openingColliders.Length; c++)
        {
            Collider opening = openingColliders[c];
            if (opening == null) continue;

            Bounds b = opening.bounds;
            Vector3 minV = (b.min - simMin) / cellSize;
            Vector3 maxV = (b.max - simMin) / cellSize;

            int minX = Mathf.Clamp(Mathf.FloorToInt(minV.x), 0, gridX - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(minV.y), 0, gridY - 1);
            int minZ = Mathf.Clamp(Mathf.FloorToInt(minV.z), 0, gridZ - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(maxV.x), 0, gridX - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(maxV.y), 0, gridY - 1);
            int maxZ = Mathf.Clamp(Mathf.CeilToInt(maxV.z), 0, gridZ - 1);

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        Vector3 voxelPos = simMin +
                            new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * cellSize;

                        if (!IsVoxelInsideCollider(opening, voxelPos))
                            continue;

                        int index = x + gridX * (y + gridY * z);

                        obstacleData[index] = 0f;
                        openingData[index] = 1f;
                    }
                }
            }
        }
    }

    void UploadOpeningVolume()
    {
        Color[] pixels = new Color[openingData.Length];

        for (int i = 0; i < openingData.Length; i++)
        {
            float v = openingData[i];
            pixels[i] = new Color(v, v, v, v);
        }

        Texture3D temp =
            new Texture3D(gridX, gridY, gridZ, TextureFormat.RFloat, false);

        temp.SetPixels(pixels);
        temp.Apply();

        Graphics.CopyTexture(temp, openingVolume);
    }

    void UploadObstacleVolume()
    {
        Color[] pixels = new Color[obstacleData.Length];

        for (int i = 0; i < obstacleData.Length; i++)
        {
            float v = obstacleData[i];

            pixels[i] = new Color(v, v, v, v);
        }

        Texture3D temp =
            new Texture3D(gridX, gridY, gridZ, TextureFormat.RFloat, false);

        temp.SetPixels(pixels);

        temp.Apply();

        Graphics.CopyTexture(temp, obstacleVolume);
    }

    void UploadMaterialVolume()
    {
        Color[] pixels = new Color[materialData.Length];

        for (int i = 0; i < materialData.Length; i++)
        {
            float v = materialData[i];

            pixels[i] = new Color(v, v, v, v);
        }

        Texture3D temp =
            new Texture3D(gridX, gridY, gridZ, TextureFormat.RFloat, false);

        temp.SetPixels(pixels);

        temp.Apply();

        Graphics.CopyTexture(temp, materialVolume);
    }

    void UploadFuelVolume()
    {
        Color[] pixels = new Color[fuelData.Length];

        for (int i = 0; i < fuelData.Length; i++)
        {
            float v = fuelData[i];

            pixels[i] = new Color(v, v, v, v);
        }

        Texture3D temp =
            new Texture3D(gridX, gridY, gridZ, TextureFormat.RFloat, false);

        temp.SetPixels(pixels);

        temp.Apply();

        Graphics.CopyTexture(temp, fuelA);

        Graphics.CopyTexture(temp, fuelB);
    }

    void UploadBurningVolume()
    {
        Color[] pixels = new Color[burningData.Length];

        for (int i = 0; i < burningData.Length; i++)
        {
            float v = burningData[i];

            pixels[i] = new Color(v, v, v, v);
        }

        Texture3D temp =
            new Texture3D(gridX, gridY, gridZ, TextureFormat.RFloat, false);

        temp.SetPixels(pixels);

        temp.Apply();

        Graphics.CopyTexture(temp, burningA);

        Graphics.CopyTexture(temp, burningB);
    }

    // -------------------------------------------------------------------
    // Fine/coarse coupling.
    //
    // The box-average ratio is coarseCell/fineCell. At 0.3 and 0.1 that is
    // exactly 3, so the 3x3x3 taps land on fine cell centres and the average
    // is exact. Non-integer ratios still work, just approximately.
    // -------------------------------------------------------------------
    bool HasFineFields()
    {
        return smokeSim != null
            && smokeSim.IsInitialized
            && smokeSim.FireVolume != null
            && smokeSim.SmokeVolume != null;
    }

    void UploadFineCoupling()
    {
        simulation.SetFloat("FlameGasTemp", flameGasTemp);
        simulation.SetFloat("SmokeGasTemp", smokeGasTemp);
        simulation.SetFloat("FineCouplingRate", fineCouplingRate);

        if (!HasFineFields())
        {
            simulation.SetInts("FineGridSize", 1, 1, 1);
            simulation.SetFloat("CoarseOverFine", 1f);
            simulation.SetInt("FineBoxTaps", 1);
            return;
        }

        smokeSim.GetGridSize(out int fx, out int fy, out int fz);

        float ratio = cellSize / Mathf.Max(smokeSim.CellSizePublic, 1e-4f);

        simulation.SetInts("FineGridSize", fx, fy, fz);
        simulation.SetFloat("CoarseOverFine", ratio);
        simulation.SetInt("FineBoxTaps", Mathf.Clamp(Mathf.RoundToInt(ratio), 1, 4));
    }

    // Exposed so SmokeSim can adopt these bounds exactly. If the two grids
    // do not share an origin, nothing downstream lines up.
    public Bounds SimBounds => GetSimulationBounds();

    public RenderTexture GetVelocityVolume() => velocityA;

    public void GetGridSize(out int x, out int y, out int z)
    {
        x = gridX; y = gridY; z = gridZ;
    }

    public Vector3 WorldToVoxel(Vector3 worldPos)
    {
        Bounds bounds = GetSimulationBounds();

        Vector3 local = worldPos - bounds.min;

        return local / cellSize;
    }

    public Vector3 VoxelToWorld(Vector3 voxelPos)
    {
        Bounds bounds = GetSimulationBounds();

        return bounds.min + voxelPos * cellSize;
    }

    public void IgniteAtWorldPosition(Vector3 worldPos, float temperature)
    {
        Vector3 voxel = WorldToVoxel(worldPos);

        int x = Mathf.RoundToInt(voxel.x);

        int y = Mathf.RoundToInt(voxel.y);

        int z = Mathf.RoundToInt(voxel.z);
    }

    bool PointInsideMeshCollider(MeshCollider meshCollider, Vector3 point)
    {
        Ray ray = new Ray(point + Vector3.up * 1000f, Vector3.down);

        RaycastHit[] hits = Physics.RaycastAll(ray, 2000f);

        int hitCount = 0;

        foreach (var hit in hits)
        {
            if (hit.collider == meshCollider)
            {
                hitCount++;
            }
        }

        return (hitCount & 1) == 1;
    }

    //Re-scan the scene and rebuild the obstacle/material/fuel grid + thermal
    //clones. Call after the save system finishes loading new thermal objects.
    //Does NOT reallocate the volume textures or re-find kernels (those persist).
    //WARNING: do not call while a fire is actively running -- InitializeSolidTemperature
    //resets all solid temps back to ambient.
    public void Rebake()
    {

        Physics.SyncTransforms();

        //Refresh the cached list -- loaded furniture is now in the scene
        _thermalObjects =
            GameObject.FindObjectsByType<ThermalMaterial>(FindObjectsSortMode.None);

        FindObstacleColliders();
        FindOpenings();
        BuildObstacleVolume();
        InitializeSolidTemperature();
        CreateThermalClones();
    }

    void CreateThermalClones()
    {
        ThermalMaterial[] thermalObjects = _thermalObjects;

        int thermalLayer = LayerMask.NameToLayer("ThermalSurface");

        thermalSurfaceRenderers = new Renderer[thermalObjects.Length];

        for (int i = 0; i < thermalObjects.Length; i++)
        {
            var thermalObject = thermalObjects[i];

            //Skip objects that already have a clone so repeated Rebake calls
            //don't stack duplicate greyscale layers. Reuse the existing one.
            Transform existingClone =
                thermalObject.transform.Find(thermalObject.name + "_Thermal");

            if (existingClone != null)
            {
                thermalSurfaceRenderers[i] = existingClone.GetComponent<Renderer>();
                continue;
            }

            MeshFilter meshFilter = thermalObject.GetComponent<MeshFilter>();

            MeshRenderer meshRenderer =
                thermalObject.GetComponent<MeshRenderer>();

            if (meshFilter == null || meshRenderer == null)
            {
                continue;
            }

            GameObject clone = new GameObject(thermalObject.name + "_Thermal");

            clone.transform.SetParent(thermalObject.transform, false);

            clone.layer = thermalLayer;

            MeshFilter cloneFilter = clone.AddComponent<MeshFilter>();

            cloneFilter.sharedMesh = meshFilter.sharedMesh;

            MeshRenderer cloneRenderer = clone.AddComponent<MeshRenderer>();

            Material[] sourceMaterials = meshRenderer.sharedMaterials;

            Material[] thermalMaterials =
                new Material[sourceMaterials.Length + 1];

            for (int m = 0; m < sourceMaterials.Length; m++)
            {
                Material greyMaterial = new Material(thermalGreyscaleMaterial);

                greyMaterial.CopyPropertiesFromMaterial(sourceMaterials[m]);
                greyMaterial.shaderKeywords = new string[0];

                thermalMaterials[m] = greyMaterial;
            }

            thermalMaterials[thermalMaterials.Length - 1] =
                thermalSurfaceMaterial;

            cloneRenderer.sharedMaterials = thermalMaterials;

            cloneRenderer.gameObject.layer = thermalLayer;

            thermalSurfaceRenderers[i] = cloneRenderer;
        }
    }
    void FindOpenings()
    {
        ThermalOpening[] openings = GameObject.FindObjectsByType<ThermalOpening>(FindObjectsSortMode.None);

        List<Collider> colliders = new List<Collider>();

        Bounds volumeBounds = GetSimulationBounds();

        foreach (var opening in openings)
        {
            Collider col = opening.Collider;
            if (!col) continue;

            colliders.Add(col);
        }

        openingColliders = colliders.ToArray();

        Debug.Log($"Found {openingColliders.Length} thermal openings");
    }
    void FindObstacleColliders()
    {
        List<Collider> colliders = new List<Collider>();

        ThermalMaterial[] thermalObjects = _thermalObjects;

        Bounds volumeBounds = GetSimulationBounds();

        foreach (var thermalObject in thermalObjects)
        {
            Collider[] objectColliders =
                thermalObject.GetComponentsInChildren<Collider>();

            foreach (var collider in objectColliders)
            {
                if (collider == null)
                    continue;

                if (!volumeBounds.Intersects(collider.bounds))
                    continue;

                colliders.Add(collider);
            }
        }

        obstacleColliders = colliders.ToArray();

        Debug.Log($"Found {obstacleColliders.Length} thermal obstacles");
    }

    Bounds GetSimulationBounds()
    {
        if (volumeCollider != null)
            return volumeCollider.bounds; //Based on the shape of the collider

        return new Bounds(thermalVolume.position, thermalVolume.lossyScale);
    }

    bool IsVoxelInsideCollider(Collider collider, Vector3 voxelCenter)
    {
        Collider[] hits = Physics.OverlapSphere(
            voxelCenter,
            cellSize * 0.45f);

        foreach (var hit in hits)
        {
            if (hit == collider)
                return true;
        }

        return false;
    }

    public void ResetChar()
    {
        Color[] pixels = new Color[gridX * gridY * gridZ];
        var tex = new Texture3D(gridX, gridY, gridZ, TextureFormat.RFloat, false);
        tex.SetPixels(pixels);
        tex.Apply();
        Graphics.CopyTexture(tex, charVolume);
    }

    public void RegisterRuntimeBurningObject(
        Collider collider,
        ThermalMaterial material)
    {
        Debug.Log("Making out voxels");

        List<BurningVoxel> voxels =
            new List<BurningVoxel>();

        Bounds bounds = collider.bounds;

        Vector3 minVoxel =
            WorldToVoxel(bounds.min);

        Vector3 maxVoxel =
            WorldToVoxel(bounds.max);

        int minX =
            Mathf.Clamp(
                Mathf.FloorToInt(minVoxel.x),
                0,
                gridX - 1);

        int minY =
            Mathf.Clamp(
                Mathf.FloorToInt(minVoxel.y),
                0,
                gridY - 1);

        int minZ =
            Mathf.Clamp(
                Mathf.FloorToInt(minVoxel.z),
                0,
                gridZ - 1);

        int maxX =
            Mathf.Clamp(
                Mathf.CeilToInt(maxVoxel.x),
                0,
                gridX - 1);

        int maxY =
            Mathf.Clamp(
                Mathf.CeilToInt(maxVoxel.y),
                0,
                gridY - 1);

        int maxZ =
            Mathf.Clamp(
                Mathf.CeilToInt(maxVoxel.z),
                0,
                gridZ - 1);

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector3 voxelCenter =
                        VoxelToWorld(
                            new Vector3(
                                x + 0.5f,
                                y + 0.5f,
                                z + 0.5f));

                    if (!IsVoxelInsideCollider(collider, voxelCenter))
                        continue;

                    BurningVoxel voxel =
                        new BurningVoxel();

                    voxel.x = (uint)x;
                    voxel.y = (uint)y;
                    voxel.z = (uint)z;

                    voxels.Add(voxel);

                    Debug.DrawRay(
                    voxelCenter,
                    Vector3.up * 0.2f,
                    Color.red,
                    30f);
                }
            }
        }


        Debug.Log($"Grid: {gridX}x{gridY}x{gridZ} | SimBounds: {GetSimulationBounds()} | " +
          $"ColliderBounds: {bounds} | VoxelRange: ({minX},{minY},{minZ}) to ({maxX},{maxY},{maxZ})");

        if (voxels.Count == 0)
        {
            Debug.Log("No voxels found");
            return;
        }

        runtimeVoxelBuffer.SetData(voxels);

        simulation.SetBuffer(
            runtimeIgniteKernel,
            "RuntimeVoxels",
            runtimeVoxelBuffer);

        simulation.SetInt(
            "RuntimeVoxelCount",
            voxels.Count);

        simulation.SetFloat(
            "RuntimeFuel",
            material.fuelAmount);

        simulation.SetFloat(
            "RuntimeMaterial",
            (float)material.materialType);

        simulation.SetTexture(
            runtimeIgniteKernel,
            "MaterialVolume",
            materialVolume);

        simulation.SetTexture(
            runtimeIgniteKernel,
            "FuelOut",
            fuelA);

        simulation.SetTexture(
            runtimeIgniteKernel,
            "BurningOut",
            burningA);

        simulation.SetTexture(
            runtimeIgniteKernel,
            "ObstacleOut",
            obstacleVolume);

        simulation.Dispatch(
            runtimeIgniteKernel,
            Mathf.CeilToInt(
                voxels.Count / 64f),
            1,
            1);

        RequestVolumeReadback(burningA, data =>
        {
            int gx = gridX;
            int gy = gridY;

            int Index(int x, int y, int z)
            {
                return x + gx * (y + gy * z);
            }

            float max = 0f;
            int burningCount = 0;

            foreach (float v in data)
            {
                if (v > max)
                    max = v;

                if (v > 0.5f)
                    burningCount++;
            }

            Debug.Log($"Burning MAX = {max}, Burning Voxels = {burningCount}");
        });

        Debug.Log(
            $"Injected {voxels.Count} voxels");
    }

    public void Extinguish(Collider collider, float strength)
    {
        List<BurningVoxel> voxels = new List<BurningVoxel>();

        Bounds bounds = collider.bounds;

        Vector3 minVoxel = WorldToVoxel(bounds.min);
        Vector3 maxVoxel = WorldToVoxel(bounds.max);

        int minX = Mathf.Clamp(Mathf.FloorToInt(minVoxel.x), 0, gridX - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt(minVoxel.y), 0, gridY - 1);
        int minZ = Mathf.Clamp(Mathf.FloorToInt(minVoxel.z), 0, gridZ - 1);

        int maxX = Mathf.Clamp(Mathf.CeilToInt(maxVoxel.x), 0, gridX - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt(maxVoxel.y), 0, gridY - 1);
        int maxZ = Mathf.Clamp(Mathf.CeilToInt(maxVoxel.z), 0, gridZ - 1);

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector3 voxelCenter =
                        VoxelToWorld(new Vector3(
                            x + 0.5f,
                            y + 0.5f,
                            z + 0.5f));

                    Vector3 closest =
                        collider.ClosestPoint(voxelCenter);

                    if ((closest - voxelCenter).sqrMagnitude > 0.000001f)
                        continue;

                    BurningVoxel voxel;

                    voxel.x = (uint)x;
                    voxel.y = (uint)y;
                    voxel.z = (uint)z;
                    voxel.pad = 0;

                    voxels.Add(voxel);
                }
            }
        }

        if (voxels.Count == 0)
            return;

        runtimeVoxelBuffer.SetData(voxels);

        simulation.SetBuffer(
            extinguishKernel,
            "RuntimeVoxels",
            runtimeVoxelBuffer);

        simulation.SetInt(
            "RuntimeVoxelCount",
            voxels.Count);

        simulation.SetFloat(
            "ExtinguishStrength",
            strength);

        simulation.SetFloat(
            "AmbientTemp",
            20);

        simulation.SetFloat(
            "DeltaTime",
            Time.fixedDeltaTime);

        simulation.SetTexture(extinguishKernel, "BurningOut", burningA);
        simulation.SetTexture(extinguishKernel, "SuppressantIn", suppressantA);
        simulation.SetTexture(extinguishKernel, "SuppressantOut", suppressantA);
        simulation.SetTexture(extinguishKernel, "FlameOut", flameA);
        simulation.SetTexture(extinguishKernel, "SmokeOut", smokeA);
        simulation.SetTexture(extinguishKernel, "TemperatureOut", temperatureA);
        simulation.SetTexture(extinguishKernel, "SolidTemperatureIn", solidTemperatureA);
        simulation.SetTexture(extinguishKernel, "SolidTemperatureOut", solidTemperatureA);

        simulation.Dispatch(
            extinguishKernel,
            Mathf.CeilToInt(voxels.Count / 64f),
            1,
            1);
    }
}