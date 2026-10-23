using System.Collections.Generic;
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
        [Tooltip("Centre of the burning SURFACE. Flame and smoke are emitted " +
                 "in a thin bed here and rise from it -- put this on top of " +
                 "the fuel (or on the floor for a pool/pile), not in the " +
                 "middle of the object.")]
        public Transform transform;

        [Tooltip("World-space horizontal radius of the fuel bed.")]
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

    [Tooltip("Smaller = finer plume detail, but cost grows with the CUBE of " +
             "1/cellSize: halving it is 8x the work. Desktop: 0.1-0.15. " +
             "Quest: 0.2. Keep it a whole fraction of ThermalSimulation's " +
             "cell size (e.g. 0.2 here with 0.4 there).")]
    public float cellSize = 0.2f;

    [Header("Room Auto-Detection")]
    public LayerMask obstacleLayers = ~0;
    public bool ignoreTriggers = true;

    [Tooltip("When ON, the six faces of the sim grid are open air and smoke " +
             "drains out of them. When OFF they act as solid walls, which " +
             "makes the volume a sealed box that can only fill up. Turn OFF " +
             "only if the grid is genuinely flush with real room geometry.")]
    public bool openBoundaries = true;

    [Header("Emitters")]
    [Tooltip("One or more fire/smoke sources. Add as many as you need.")]
    public Emitter[] emitters = new Emitter[] { new Emitter() };

    [Header("Smoke")]
    public float riseSpeed = 1.5f;
    public float settleSpeed = 1.0f;
    public float ceilingSpread = 3.0f;

    [Tooltip("Fraction of density lost per second. This is the only thing " +
             "removing smoke in a closed volume, so if the room fills up and " +
             "stays full, raise this before touching anything else.")]
    public float decayRate = 0.05f;

    [Header("Smoke Noise")]
    [Tooltip("Higher = smaller turbulence features. Must be small enough that " +
             "features are NARROWER than the plume, or the noise translates the " +
             "whole plume rigidly instead of deforming it. Rough guide: feature " +
             "size in voxels is about 1/scale.")]
    public float smokeNoiseScale = 0.25f;
    public float smokeNoiseStrength = 0.7f;
    public float smokeNoiseSpeed = 0.35f;

    [Header("Smoke Turbulence Shaping")]
    [Tooltip("Hard cap on warp displacement, in VOXELS. Keep well below the " +
             "plume radius in voxels or the core gets yanked around. Also caps " +
             "plume speed: 1.2 voxels/tick at 0.1 m cells and 50 Hz is ~6 m/s.")]
    public float smokeWarpClamp = 1.2f;

    [Tooltip("How tightly turbulence is confined to the density boundary. " +
             "Higher = more of the plume interior stays rigid. 0 disables the " +
             "mask (old behaviour). Fire is masked by its own edge, smoke by its.")]
    public float smokeShellSharpness = 4.0f;

    [Tooltip("Height above a fuel bed, in metres, over which turbulence fades " +
             "in. Anchors the base of the plume so it cannot sway at its root. " +
             "0 disables.")]
    public float smokeAnchorRadius = 0.5f;

    [Tooltip("Use divergence-free curl noise. Looks slightly better but costs " +
             "six times the noise evaluations per voxel, in both the smoke and " +
             "fire steps. Leave OFF on Quest.")]
    public bool smokeUseCurlNoise = false;

    [Tooltip("Physical diffusion of soot. Small -- most spreading should come " +
             "from the solver velocity field, not from this.")]
    public float smokeDiffusion = 0.05f;

    [Range(0f, 1f)]
    [Tooltip("How strongly bright flame suppresses smoke production in its own " +
             "cell. Soot inside the reaction zone is incandescent -- it is the " +
             "flame's light, not an obstruction -- and only reads as opaque " +
             "smoke once it cools downstream. Raise this if the fire is hidden " +
             "behind its own smoke.")]
    public float sootLuminousCutoff = 0.85f;

    [Header("Thermal Coupling")]
    [Tooltip("The ThermalSimulation whose Navier-Stokes solve drives this " +
             "grid. Leave empty to fall back to noise-only transport.")]
    public ThermalSimulation thermalSim;

    [Tooltip("When ON, this sim adopts the thermal sim's world bounds exactly. " +
             "The two grids MUST share a world origin or the thermal image " +
             "will not line up with the visible fire. Leave this ON.")]
    public bool matchThermalBounds = true;

    [Range(0f, 1f)]
    [Tooltip("How much of the solver's velocity field drives transport. " +
             "1 = fully physics-driven. Lower it only for art direction.")]
    public float velocityInfluence = 1.0f;

    [Range(0f, 1f)]
    [Tooltip("Fraction of the ideal plume rise speed sqrt(2 g' h) applied on " +
             "this grid. Gives the flame lift from the very first tick, before " +
             "the coarse solver has spun up, and covers what a solver cell too " +
             "coarse to resolve the flame cannot. 0.4-0.6 is reasonable; 0 " +
             "relies on the solver alone.")]
    public float subGridBuoyancy = 0.5f;

    [Tooltip("Entrainment coefficient. Ambient air is drawn into the plume at " +
             "about this fraction of its rise speed, which tapers the flame " +
             "toward its axis. ~0.1 is physical; 0 gives a straight column.")]
    public float plumeEntrainment = 0.1f;

    [Header("Fire Bed")]
    [Range(0.05f, 1f)]
    [Tooltip("Bed half-height as a fraction of emitter radius. The emitter is " +
             "a flat bed of fuel the flame rises from, not a ball.")]
    public float fireBedThickness = 0.25f;

    [Range(0f, 1f)]
    [Tooltip("0 = uniform disc. 1 = bed fully broken into hot spots, each " +
             "feeding its own flame tongue.")]
    public float fireBedBreakup = 0.7f;

    [Tooltip("World size of one hot spot on the bed, metres.")]
    public float fireBedFeatureSize = 0.25f;

    [Tooltip("How fast hot spots come and go, 1/s.")]
    public float fireBedNoiseSpeed = 1.5f;

    [Header("Burning Surfaces (from ThermalSimulation)")]
    [Tooltip("Emit flame and smoke from every burning surface in the thermal " +
             "sim: runtime-ignited objects and fire that has spread. With this " +
             "ON, RuntimeFire needs no emitter of its own -- whatever is " +
             "actually burning is what shows flame.")]
    public bool emitFromBurningSurfaces = true;

    [Range(0f, 2f)]
    [Tooltip("Fire density at a fully burning surface. Above 1 the bed " +
             "saturates and the hot-spot breakup stops showing.")]
    public float surfaceFireRate = 1.0f;

    [Range(0f, 20f)]
    [Tooltip("Smoke density added per second at a burning surface.")]
    public float surfaceSmokeRate = 1.0f;

    [Tooltip("How far out from a burning surface flame is emitted, metres. " +
             "This is the thickness of the fuel bed.")]
    public float surfaceFlameDepth = 0.15f;

    [Header("Flame Appearance")]
    [Tooltip("Per-second subtraction applied to the flame field. Keeps flame " +
             "tips crisp instead of fading into haze.")]
    public float fireCutoff = 0.15f;

    [Tooltip("Height above the fuel bed of the continuously luminous zone, " +
             "metres. Beyond this, flame survives only in intermittent " +
             "pockets. Also where the sub-grid rise speed stops growing. " +
             "1.0-1.5 suits a mattress-sized fire.")]
    public float flamePersistentHeight = 1.0f;

    [Tooltip("Extinction rate in the diluted region, 1/s. 0 gives a flame " +
             "that burns continuously from the fuel to wherever it decays -- " +
             "which looks like a gas burner. Raise it for a wood fire.")]
    public float flameIntermittency = 3.0f;

    [Tooltip("OFF renders the raw flame field, exactly as before the reaction " +
             "sheet was added. Turn this OFF first when debugging a missing " +
             "flame -- it takes the FireEmission kernel out of the path " +
             "entirely, so you can tell a simulation problem from a shading one.")]
    public bool useReactionSheet = false;

    [Tooltip("How strongly the reaction-sheet weighting sharpens the flame " +
             "into tongues. Higher = thinner sheets.")]
    public float reactionSheetScale = 6.0f;

    [Range(0f, 1f)]
    [Tooltip("Blend between the raw flame volume (0) and pure reaction-sheet " +
             "emission (1). 0.5 keeps a hot core with defined tongues.")]
    public float reactionSheetWeight = 0.5f;

    [Tooltip("Measured gas temperature of a hydrocarbon diffusion flame, deg C. " +
             "Adiabatic is ~2000, but radiative loss and entrainment put the " +
             "real measured value near 1000.")]
    public float flameGasTempC = 1000f;

    public float ambientTempC = 20f;

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

    [Header("Performance")]
    [Tooltip("Simulation steps per second. Runs from Update, never more than " +
             "once per frame. 30 is plenty for fire; it also caps how fast " +
             "smoke can move per step via smokeWarpClamp, so keep cell size " +
             "and this together in mind.")]
    public float simulationHz = 30f;

    float _simAccumulator;

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

    // Sheet-weighted copy of the flame field, used for rendering only.
    // The simulation keeps stepping the physical field in fireA/fireB.
    RenderTexture fireEmission;

    // 1x1x1 float4 stand-in bound to CoarseVelocity when no ThermalSimulation
    // is driving. Unity validates every referenced texture at dispatch time
    // regardless of whether the shader path actually reads it.
    RenderTexture fallbackVelocity;

    // 1x1x1 float stand-in bound to CoarseBurning / CoarseObstacle when the
    // thermal sim isn't available, for the same reason as fallbackVelocity.
    RenderTexture fallbackScalar;

    int kEmit, kStep, kEmitFire, kStepFire, kClear, kFireEmission;
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

    // -------------------------------------------------------------------
    // Runtime emitters.
    //
    // There is exactly one smoke grid in the scene, so a fire spawned at
    // play time cannot bring its own SmokeSim -- it registers an Emitter
    // against this one instead. Mirrors ThermalSimulation's
    // RegisterFireSource / _fireSources pattern so both sims are driven
    // the same way.
    //
    // The list holds live Emitter objects and BuildEmitterBuffer re-reads
    // them every sim step, so mutating radius/smokeRate/fireRate on a
    // registered emitter takes effect next tick with no re-registration
    // and no reinit.
    // -------------------------------------------------------------------
    static SmokeSim _instance;

    public static SmokeSim Instance =>
        _instance != null ? _instance : (_instance = FindObjectOfType<SmokeSim>());

    readonly List<Emitter> _runtimeEmitters = new List<Emitter>();

    void Awake()
    {
        if (_instance == null) _instance = this;
    }

    public void RegisterEmitter(Emitter e)
    {
        if (e != null && !_runtimeEmitters.Contains(e)) _runtimeEmitters.Add(e);
    }

    public void UnregisterEmitter(Emitter e)
    {
        _runtimeEmitters.Remove(e);
    }

    /// <summary>
    /// True if a world point falls inside the sim volume. A fire placed
    /// outside it emits into nothing, which is worth warning about at
    /// spawn time rather than leaving as a silent no-op.
    /// </summary>
    public bool ContainsWorldPoint(Vector3 p)
    {
        Vector3 half = volumeSize * 0.5f;
        Vector3 d = p - volumeCenter;
        return Mathf.Abs(d.x) <= half.x
            && Mathf.Abs(d.y) <= half.y
            && Mathf.Abs(d.z) <= half.z;
    }

    void Start()
    {
        if (!Application.isPlaying) return;
        Init();
    }

    void Init()
    {
        // -----------------------------------------------------------------
        // Grid alignment.
        //
        // Two simulations cannot agree unless they share a world-space
        // origin. Everything downstream -- the fine->coarse box average,
        // the coarse->fine velocity fetch, the thermal image lining up with
        // the visible flame -- assumes fine voxel (i+0.5)*fineCell and
        // coarse voxel (j+0.5)*coarseCell measure from the same point.
        // -----------------------------------------------------------------
        if (matchThermalBounds && thermalSim != null)
        {
            Bounds tb = thermalSim.SimBounds;
            volumeCenter = tb.center;
            volumeSize = tb.size;
        }

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
        kFireEmission = compute.FindKernel("FireEmission");
        kCopyVolume = compute.FindKernel("CopyVolumeToBuffer");
        volumeReadBuffer = new ComputeBuffer(gridX * gridY * gridZ, sizeof(float));

        smokeA = MakeVolume();
        smokeB = MakeVolume();
        fireA = MakeVolume();
        fireB = MakeVolume();
        obstacle = MakeVolume();
        fireEmission = MakeVolume();

        fallbackVelocity = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat);
        fallbackVelocity.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        fallbackVelocity.volumeDepth = 1;
        fallbackVelocity.enableRandomWrite = true;
        fallbackVelocity.wrapMode = TextureWrapMode.Clamp;
        fallbackVelocity.Create();

        fallbackScalar = new RenderTexture(1, 1, 0, RenderTextureFormat.RFloat);
        fallbackScalar.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        fallbackScalar.volumeDepth = 1;
        fallbackScalar.enableRandomWrite = true;
        fallbackScalar.wrapMode = TextureWrapMode.Clamp;
        fallbackScalar.Create();
        {
            // Fresh RTs are undefined, not zero.
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = fallbackScalar;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = prev;
        }

        ClearVolume(fireEmission);
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

        initialized = true;
    }

    // -------------------------------------------------------------------
    // Stepping.
    //
    // Runs from Update at simulationHz, at most ONE step per frame. It used
    // to run in FixedUpdate: when a frame ran long, Unity ran several
    // catch-up FixedUpdates the next frame, each a full sim step, which made
    // that frame slower still. On Quest that spiral never recovers. Here a
    // late frame just drops the backlog and the sim runs slightly slow.
    // -------------------------------------------------------------------
    void Update()
    {
        if (!Application.isPlaying || !initialized || compute == null) return;

        float dt = 1f / Mathf.Max(simulationHz, 1f);

        _simAccumulator += Time.deltaTime;
        if (_simAccumulator < dt) return;

        _simAccumulator = Mathf.Min(_simAccumulator - dt, dt);

        StepSimulation(dt);
    }

    void StepSimulation(float dt)
    {
        compute.SetInts("GridSize", gridX, gridY, gridZ);
        compute.SetFloat("DeltaTime", dt);
        compute.SetFloat("Time_", Time.time);
        compute.SetFloat("OpenBoundary", openBoundaries ? 1f : 0f);

        UploadCouplingParams();
        UploadDirectionalParams();
        BuildEmitterBuffer();

        DispatchEmitFire();
        DispatchStepFire();
        DispatchEmit();
        DispatchStep();
        DispatchFireEmission();

        BindRenderTextures();
    }

    // Rebuilds the GPU emitter buffer from BOTH sources each tick: the
    // Inspector list (authored, level-designer fires) and the runtime list
    // (fires spawned by PlaceTool). Skips disabled/null entries. Uploads
    // the full fixed-size array (~768 bytes) rather than a partial range --
    // trivial cost, and it means the compute shader only ever reads the
    // first `count` slots.
    void BuildEmitterBuffer()
    {
        int count = 0;
        int totalCandidates = 0;

        void Add(Emitter e)
        {
            if (e == null || !e.enabled || e.transform == null) return;

            totalCandidates++;
            if (count >= MaxEmitters) return;

            emitterCPUCache[count] = new EmitterGPU
            {
                center = (e.transform.position - origin) / cellSize,
                radius = e.radius / cellSize,
                emitRate = e.smokeRate,
                fireRate = e.fireRate,
            };
            count++;
        }

        if (emitters != null)
        {
            for (int i = 0; i < emitters.Length; i++) Add(emitters[i]);
        }

        for (int i = 0; i < _runtimeEmitters.Count; i++) Add(_runtimeEmitters[i]);

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
        // Step now needs the emitter list too, for the near-source anchor ramp.
        compute.SetBuffer(kStep, "Emitters", emitterBuffer);
        // StepFire reaches the emitters through TransportOffset (buoyancy,
        // entrainment, anchor) and FlameDilution.
        compute.SetBuffer(kStepFire, "Emitters", emitterBuffer);
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

    // -------------------------------------------------------------------
    // Everything the fine grid needs to know about the coarse solver.
    //
    // Velocity is exchanged in METRES PER SECOND. That is the only unit
    // both grids can agree on, since they have different cell sizes -- a
    // velocity in "voxels per second" means different things on each.
    // -------------------------------------------------------------------
    void UploadCouplingParams()
    {
        compute.SetFloat("CellSizeMeters", cellSize);
        compute.SetFloat("SubGridBuoyancy", subGridBuoyancy);
        compute.SetFloat("PlumeEntrainment", plumeEntrainment);
        compute.SetFloat("FlameGasTempK", flameGasTempC + 273.15f);
        compute.SetFloat("AmbientTempK", ambientTempC + 273.15f);
        compute.SetFloat("SmokeDiffusion", smokeDiffusion);
        compute.SetFloat("SootLuminousCutoff", sootLuminousCutoff);
        compute.SetFloat("FlamePersistentRadius", flamePersistentHeight / cellSize);
        compute.SetFloat("FlameIntermittency", flameIntermittency);
        compute.SetFloat("FireCutoff", fireCutoff);
        compute.SetFloat("ReactionSheetScale", reactionSheetScale);
        compute.SetFloat("ReactionSheetWeight", reactionSheetWeight);

        // Fuel bed shape. The noise scale is uploaded per VOXEL so the
        // hot-spot size stays the same in metres whatever the cell size.
        compute.SetFloat("FireBedThickness", fireBedThickness);
        compute.SetFloat("FireBedBreakup", fireBedBreakup);
        compute.SetFloat("FireBedNoiseScale", cellSize / Mathf.Max(fireBedFeatureSize, 1e-3f));
        compute.SetFloat("FireBedNoiseSpeed", fireBedNoiseSpeed);

        RenderTexture coarseVel = null;
        int cx = 1, cy = 1, cz = 1;
        float coarseCell = cellSize;

        if (thermalSim != null && thermalSim.IsInitialized)
        {
            coarseVel = thermalSim.GetVelocityVolume();
            thermalSim.GetGridSize(out cx, out cy, out cz);
            coarseCell = thermalSim.CellSizePublic;
        }

        if (coarseVel != null)
        {
            compute.SetTexture(kStep, "CoarseVelocity", coarseVel);
            compute.SetTexture(kStepFire, "CoarseVelocity", coarseVel);
            compute.SetInts("CoarseGridSize", cx, cy, cz);
            compute.SetFloat("FineOverCoarse", cellSize / Mathf.Max(coarseCell, 1e-4f));
            compute.SetFloat("VelocityInfluence", velocityInfluence);
        }
        else
        {
            // No solver available. VelocityInfluence 0 makes the samples
            // irrelevant, but the texture must STILL be bound or Unity
            // refuses to dispatch the kernel at all. Any 3D texture will do.
            compute.SetTexture(kStep, "CoarseVelocity", fallbackVelocity);
            compute.SetTexture(kStepFire, "CoarseVelocity", fallbackVelocity);
            compute.SetInts("CoarseGridSize", 1, 1, 1);
            compute.SetFloat("FineOverCoarse", 1f);
            compute.SetFloat("VelocityInfluence", 0f);
        }

        // --- burning surfaces from the thermal sim ---
        RenderTexture coarseBurning = null;
        RenderTexture coarseObstacle = null;

        if (coarseVel != null)
        {
            coarseBurning = thermalSim.GetBurningVolume();
            coarseObstacle = thermalSim.GetObstacleVolume();
        }

        bool useSurfaces = emitFromBurningSurfaces
                        && coarseBurning != null
                        && coarseObstacle != null;

        // Every kernel that reaches NearestPlume or BurningSurfaceWeight
        // references these, so all four need them bound -- otherwise Unity
        // refuses the dispatch and the ping-pong swap reads an empty field.
        BindCoarseScalar("CoarseBurning", useSurfaces ? coarseBurning : fallbackScalar);
        BindCoarseScalar("CoarseObstacle", useSurfaces ? coarseObstacle : fallbackScalar);

        compute.SetFloat("UseCoarseBurning", useSurfaces ? 1f : 0f);
        compute.SetFloat("SurfaceFireRate", surfaceFireRate);
        compute.SetFloat("SurfaceSmokeRate", surfaceSmokeRate);
        compute.SetFloat("SurfaceFlameDepth", surfaceFlameDepth / cellSize);
    }

    void BindCoarseScalar(string name, Texture tex)
    {
        compute.SetTexture(kEmit, name, tex);
        compute.SetTexture(kEmitFire, name, tex);
        compute.SetTexture(kStep, name, tex);
        compute.SetTexture(kStepFire, name, tex);
    }

    void DispatchFireEmission()
    {
        if (!useReactionSheet) return;

        compute.SetTexture(kFireEmission, "FireIn", fireA);
        compute.SetTexture(kFireEmission, "FireEmissionOut", fireEmission);
        compute.SetTexture(kFireEmission, "Obstacle", obstacle);
        Dispatch(kFireEmission);
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

        compute.SetFloat("SmokeWarpClamp", Mathf.Max(0f, smokeWarpClamp));
        compute.SetFloat("SmokeShellSharpness", smokeShellSharpness);
        // Inspector value is in metres; the shader works in voxels.
        compute.SetFloat("SmokeAnchorRadius", smokeAnchorRadius / cellSize);
        compute.SetFloat("SmokeUseCurlNoise", smokeUseCurlNoise ? 1f : 0f);

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

        // TransportOffset still references ShellMask -> ReadSmoke for the
        // smoke path, so StepFire's compiled kernel touches SmokeIn even
        // though fire now uses its own edge mask. Without this binding
        // Unity refuses the dispatch outright, StepFire never runs, and the
        // Swap below still executes -- so the fire field flips to a buffer
        // nothing wrote and reads as empty.
        compute.SetTexture(kStepFire, "SmokeIn", smokeA);

        Dispatch(kStepFire);
        Swap(ref fireA, ref fireB);
    }

    void ClearVolume(RenderTexture rt)
    {
        compute.SetInts("GridSize", gridX, gridY, gridZ);
        compute.SetTexture(kClear, "SmokeOut", rt);
        Dispatch(kClear);
    }

    // Note: a failed Dispatch is silent apart from a console warning, and the
    // ping-pong Swap that follows it is NOT skipped. A missing texture binding
    // therefore shows up as a field that mysteriously empties, not as an error
    // at the point of use. Check the console first when a field goes blank.
    void Dispatch(int kernel)
    {
        compute.Dispatch(kernel,
            Mathf.CeilToInt(gridX / 8f),
            Mathf.CeilToInt(gridY / 8f),
            Mathf.CeilToInt(gridZ / 8f));
    }

    void ReleaseCouplingTextures()
    {
        if (fireEmission != null) { fireEmission.Release(); fireEmission = null; }
        if (fallbackVelocity != null) { fallbackVelocity.Release(); fallbackVelocity = null; }
        if (fallbackScalar != null) { fallbackScalar.Release(); fallbackScalar = null; }
    }

    RenderTexture MakeVolume()
    {
        // Half floats: half the bandwidth of RFloat for every kernel and for
        // the ray-marcher, and filterable on Quest. Smoke and fire are 0..1
        // fields, well inside half's precision.
        var format = SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RHalf)
            ? RenderTextureFormat.RHalf
            : RenderTextureFormat.RFloat;

        var rt = new RenderTexture(gridX, gridY, 0, format);
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

        // Must match the obstacle RenderTexture's format for CopyTexture.
        var texFormat = obstacle.format == RenderTextureFormat.RHalf
            ? TextureFormat.RHalf
            : TextureFormat.RFloat;

        var tex = new Texture3D(gridX, gridY, gridZ, texFormat, false);
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
        // Reaction-sheet weighted flame turns the flame from a glowing blob
        // into overlapping tongues, but it also DIMS the field, so it is off
        // by default until the raw flame is confirmed visible.
        bool useSheet = useReactionSheet && fireEmission != null;

        smokeMaterial.SetTexture("_FireTex", useSheet ? fireEmission : fireA);
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
        ReleaseCouplingTextures();
        if (emitterBuffer != null) emitterBuffer.Release();
        if (volumeReadBuffer != null) volumeReadBuffer.Release();
    }

    void UploadDirectionalParams()
    {
        Vector3 raw = GetWindVector();
        Vector3 wind = raw.sqrMagnitude > 0.001f ? raw.normalized : Vector3.zero;

        compute.SetFloats("WindDirection", wind.x, wind.y, wind.z);
        compute.SetFloat("WindStrength", windStrength);

        if (attractorPoint != null && attractorStrength > 0f)
        {
            Vector3 attVoxel = (attractorPoint.position - origin) / cellSize;
            compute.SetFloats("AttractorCenter", attVoxel.x, attVoxel.y, attVoxel.z);
            compute.SetFloat("AttractorRadius", attractorRadius / cellSize);

            // Scale the attractor Transform to size the opening: a 1 x 2 x 0.2
            // cube standing in a doorway behaves like that doorway. Leave the
            // scale at zero for the old point-attractor behaviour.
            Vector3 half = attractorPoint.lossyScale * 0.5f / cellSize;
            compute.SetFloats("AttractorHalfExtents",
                              Mathf.Abs(half.x), Mathf.Abs(half.y), Mathf.Abs(half.z));

            // Smoke inside the opening travels along the Transform's blue
            // axis, so point it the way you want the smoke to go.
            Vector3 flow = attractorPoint.forward.normalized;
            compute.SetFloats("AttractorFlowDir", flow.x, flow.y, flow.z);
            compute.SetFloat("AttractorStrength", attractorStrength);
        }
        else
        {
            compute.SetFloats("AttractorCenter", 0, 0, 0);
            compute.SetFloat("AttractorRadius", 1f);
            compute.SetFloats("AttractorHalfExtents", 0f, 0f, 0f);
            compute.SetFloats("AttractorFlowDir", 0f, 1f, 0f);
            compute.SetFloat("AttractorStrength", 0f);
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

                // Draw the actual emission footprint: a flat bed, not a ball.
                Vector3 c = e.transform.position;
                float halfH = Mathf.Max(e.radius * fireBedThickness, cellSize * 0.75f);
                Gizmos.DrawWireCube(c, new Vector3(e.radius * 2f, halfH * 2f, e.radius * 2f));
                Gizmos.DrawLine(c, c + Vector3.up * flamePersistentHeight);
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

    // Sheet-weighted flame, for rendering. Point the fire material's
    // texture at this rather than at FireVolume.
    public RenderTexture FireEmissionVolume => fireEmission;

    // What should be DRAWN. The physical field (FireVolume) still drives the
    // thermal coupling; this is the reaction-sheet version when that is on.
    public RenderTexture VisibleFireVolume =>
        (useReactionSheet && fireEmission != null) ? fireEmission : fireA;

    public void GetGridSize(out int x, out int y, out int z)
    {
        x = gridX; y = gridY; z = gridZ;
    }

    public float CellSizePublic => cellSize;
    public bool IsInitialized => initialized;
}