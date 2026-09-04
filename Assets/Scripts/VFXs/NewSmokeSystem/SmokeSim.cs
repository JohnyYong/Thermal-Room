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

    [Tooltip("Smaller = finer plume detail. At 0.25 with a 0.6 emitter radius " +
             "the plume is only ~5 voxels wide, which leaves almost no interior " +
             "for the shell mask to hold still. 0.15 is a good target.")]
    public float cellSize = 0.15f;

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
             "plume radius in voxels or the core gets yanked around.")]
    public float smokeWarpClamp = 1.2f;

    [Tooltip("How tightly turbulence is confined to the density boundary. " +
             "Higher = more of the plume interior stays rigid. 0 disables the " +
             "mask (old behaviour).")]
    public float smokeShellSharpness = 4.0f;

    [Tooltip("World-space distance from an emitter over which turbulence fades " +
             "in. Anchors the base of the plume so it cannot sway at its root. " +
             "0 disables.")]
    public float smokeAnchorRadius = 0.5f;

    [Tooltip("Use divergence-free curl noise for smoke. Costs ~2x the noise " +
             "evaluations but stops the plume silhouette breathing in and out.")]
    public bool smokeUseCurlNoise = true;

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
    [Tooltip("Buoyant acceleration added on this grid because the coarse " +
             "solver cannot resolve a flame only a couple of its cells wide. " +
             "0.3-0.5 is reasonable; 0 relies on the solver alone.")]
    public float subGridBuoyancy = 0.4f;

    [Header("Flame Appearance")]
    [Tooltip("Per-second subtraction applied to the flame field. Keeps flame " +
             "tips crisp instead of fading into haze.")]
    public float fireCutoff = 0.15f;

    [Tooltip("World-space extent of the continuously luminous zone above the " +
             "fuel bed. Beyond this, flame survives only in intermittent " +
             "pockets. Roughly the height at which a real flame stops being " +
             "solid and starts flickering into detached tongues.")]
    public float flamePersistentHeight = 0.8f;

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
    // them every FixedUpdate, so mutating radius/smokeRate/fireRate on a
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
            Debug.Log($"[SmokeSim] Read SimBounds size={tb.size} center={tb.center}");
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

    void FixedUpdate()
    {
        if (!Application.isPlaying || !initialized || compute == null) return;

        float dt = Time.fixedDeltaTime;
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
        // StepFire reaches EmitterAnchor through TransportOffset as well.
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
        compute.SetFloat("FlameGasTempK", flameGasTempC + 273.15f);
        compute.SetFloat("AmbientTempK", ambientTempC + 273.15f);
        compute.SetFloat("SmokeDiffusion", smokeDiffusion);
        compute.SetFloat("SootLuminousCutoff", sootLuminousCutoff);
        compute.SetFloat("FlamePersistentRadius", flamePersistentHeight / cellSize);
        compute.SetFloat("FlameIntermittency", flameIntermittency);
        compute.SetFloat("FireCutoff", fireCutoff);
        compute.SetFloat("ReactionSheetScale", reactionSheetScale);
        compute.SetFloat("ReactionSheetWeight", reactionSheetWeight);

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

        // StepFire calls TransportOffset, which calls ShellMask -> ReadSmoke.
        // Without this binding Unity refuses the dispatch outright, StepFire
        // never runs, and the Swap below still executes -- so the fire field
        // flips to a buffer nothing wrote and reads as empty.
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
            compute.SetFloat("AttractorStrength", attractorStrength);
        }
        else
        {
            compute.SetFloats("AttractorCenter", 0, 0, 0);
            compute.SetFloat("AttractorRadius", 1f);
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

    // Sheet-weighted flame, for rendering. Point the fire material's
    // texture at this rather than at FireVolume.
    public RenderTexture FireEmissionVolume => fireEmission;

    public void GetGridSize(out int x, out int y, out int z)
    {
        x = gridX; y = gridY; z = gridZ;
    }

    public float CellSizePublic => cellSize;
    public bool IsInitialized => initialized;
}