using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

/// Spot meter for the thermal camera, modelled on a real one.
///
/// A real thermal camera's temperature reading doesn't come from a single
/// point: each sensor pixel sees a small cone, so the spot it measures grows
/// with distance (the "distance-to-spot ratio", D:S). A small hot object far
/// away is averaged with its cooler surroundings and reads lower than it is.
///
/// This casts a small cone of rays from the centre of the camera, reads the
/// thermal sim along each, and averages them -- in ONE compute dispatch and
/// one async readback, so it costs about the same as the old single ray.
///
/// The values arrive a frame or two after the raycast (async readback -- a
/// synchronous read would stall the VR frame). Read the latest from the
/// properties, or listen to onSpotTemperature / onSurfaceTemperature.
public class ThermalCameraProbe : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("Camera to aim from. Defaults to Camera.main (use your thermal/drone camera if it has its own).")]
    [SerializeField] private Camera sourceCamera;

    [Tooltip("Assign ThermalProbe.compute.")]
    [SerializeField] private ComputeShader probeShader;

    [Header("Raycast")]
    [SerializeField] private float maxDistance = 30f;
    [SerializeField] private LayerMask hitLayers = ~0;

    [Tooltip("Only accept hits on objects with a ThermalMaterial (i.e. things the thermal sim knows about).")]
    [SerializeField] private bool thermalObjectsOnly = true;

    [Tooltip("Colliders under this object are ignored (your XR Origin / player rig). " +
             "Leave empty to auto-find the XR Origin above the camera.")]
    [SerializeField] private Transform ignoreRoot;

    [Header("Spot (cone)")]
    [Tooltip("Distance-to-spot ratio. At D:S = 20, the spot is 1/20 of the " +
             "distance across: 5 cm at 1 m, 50 cm at 10 m. Firefighting " +
             "cameras are typically around 10-30. Higher = tighter spot.")]
    [Min(1f)]
    [SerializeField] private float distanceToSpotRatio = 20f;

    [Tooltip("Rays across the spot. More = smoother average, a little more " +
             "CPU for the raycasts. 1 = the old single-ray behaviour.")]
    [Range(1, 32)]
    [SerializeField] private int rayCount = 9;

    [Header("Sampling")]
    [Tooltip("Seconds between readings. 0.1 = 10 readings per second.")]
    [SerializeField] private float sampleInterval = 0.1f;

    [Tooltip("A readback that hasn't come back after this many seconds is " +
             "given up on, so one lost readback can't freeze the reading.")]
    [SerializeField] private float readbackTimeout = 1f;

    [Header("Output")]
    [Tooltip("Optional text to show the reading on.")]
    [SerializeField] private TMP_Text readout;

    [Tooltip("Show spot average, min/max, the centre ray's raw numbers and " +
             "whether the sim is running, instead of just the spot temperature.")]
    [SerializeField] private bool showDebugReadout = false;

    [Tooltip("Fires with the spot temperature (deg C) on every reading.")]
    public UnityEvent<float> onSpotTemperature;

    [Tooltip("Kept for existing listeners: fires with the centre ray's solid " +
             "surface temperature.")]
    public UnityEvent<float> onSurfaceTemperature;

    // ---- Latest reading: the spot ------------------------------------------
    public bool HasReading { get; private set; }

    /// What the spot meter reads: the average apparent temperature across
    /// the cone. Per ray, "apparent" is the hottest of the surface, the air
    /// in front of it and any flame along the line of sight -- what a
    /// thermal camera pixel would actually see.
    public float SpotTemperature { get; private set; }

    /// Hottest and coldest apparent temperature inside the spot.
    public float SpotMax { get; private set; }
    public float SpotMin { get; private set; }

    /// Rays in the cone that hit something the thermal sim knows about.
    public int SpotValidRays { get; private set; }

    // ---- Latest reading: the centre ray (unchanged meanings) ---------------

    /// Solid surface temperature at the centre hit, deg C.
    public float SurfaceTemperature { get; private set; }

    /// Hottest solid voxel within one cell of the centre hit, deg C.
    public float SurfaceMaxTemperature { get; private set; }

    /// Gas temperature just in front of the centre hit, deg C.
    public float AirTemperatureAtSurface { get; private set; }

    /// Highest fire/hot-gas value along the centre ray.
    public float FireHeatAlongRay { get; private set; }

    public GameObject HitObject { get; private set; }
    public Vector3 HitPoint { get; private set; }
    public float HitDistance { get; private set; }

    const int ResultsPerRay = 5;
    const int MaxRays = 32;
    const float NoValue = -9000f;   // anything below this is "no data"

    // Must match ProbeRay in ThermalProbe.compute: 4 x float4.
    struct ProbeRayGPU
    {
        public Vector4 surfaceVoxel;
        public Vector4 airVoxel;
        public Vector4 startVoxel;
        public Vector4 dirVoxel;
    }

    ComputeBuffer _rayBuffer;
    ComputeBuffer _resultBuffer;
    readonly ProbeRayGPU[] _rays = new ProbeRayGPU[MaxRays];

    // Cone layout: unit-disk offsets, centre ray first.
    Vector2[] _diskOffsets;
    int _diskOffsetsFor = -1;

    // Last cone, for gizmos.
    readonly Vector3[] _gizmoEnds = new Vector3[MaxRays];
    readonly bool[] _gizmoHit = new bool[MaxRays];
    int _gizmoCount;
    Vector3 _gizmoOrigin;

    int _kernel = -1;
    bool _pending;
    float _pendingSince;
    float _nextSampleTime;

    readonly RaycastHit[] _hits = new RaycastHit[32];
    static readonly IComparer<RaycastHit> ByDistance =
        Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

    void OnEnable()
    {
        if (sourceCamera == null) sourceCamera = Camera.main;

        if (probeShader == null)
        {
            Debug.LogError("[ThermalCameraProbe] Assign ThermalProbe.compute to Probe Shader.");
            enabled = false;
            return;
        }

        _kernel = probeShader.FindKernel("Probe");
        _rayBuffer = new ComputeBuffer(MaxRays, sizeof(float) * 16);
        _resultBuffer = new ComputeBuffer(MaxRays * ResultsPerRay, sizeof(float));
        _pending = false;

        // Auto-find the XR rig so the player's own colliders don't block the ray.
        if (ignoreRoot == null && sourceCamera != null)
        {
            var origin = sourceCamera.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
            if (origin != null) ignoreRoot = origin.transform;
        }
    }

    void OnDisable()
    {
        _rayBuffer?.Release();
        _rayBuffer = null;
        _resultBuffer?.Release();
        _resultBuffer = null;
        _pending = false;
    }

    void Update()
    {
        if (_pending)
        {
            if (Time.unscaledTime - _pendingSince < readbackTimeout) return;

            Debug.LogWarning("[ThermalCameraProbe] Readback timed out; sampling again.");
            _pending = false;
        }

        if (Time.time < _nextSampleTime) return;

        _nextSampleTime = Time.time + sampleInterval;
        Sample();
    }

    // Evenly spread points on the unit disk (Vogel / golden-angle spiral),
    // with the first one exactly at the centre so ray 0 is the crosshair.
    void EnsureDiskOffsets(int n)
    {
        if (_diskOffsetsFor == n && _diskOffsets != null) return;

        _diskOffsets = new Vector2[n];
        const float golden = 2.39996323f;

        for (int i = 0; i < n; i++)
        {
            if (i == 0) { _diskOffsets[i] = Vector2.zero; continue; }

            float r = Mathf.Sqrt((i + 0.5f) / n);
            float a = i * golden;
            _diskOffsets[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        _diskOffsetsFor = n;
    }

    /// Takes one reading now. Result arrives via the properties / events.
    public void Sample()
    {
        ThermalSimulation sim = ThermalSimulation.Instance;
        if (sim == null || !sim.IsInitialized || sourceCamera == null ||
            _rayBuffer == null || _resultBuffer == null)
            return;

        int n = Mathf.Clamp(rayCount, 1, MaxRays);
        EnsureDiskOffsets(n);

        Ray centreRay = sourceCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 fwd = centreRay.direction;
        Vector3 right = sourceCamera.transform.right;
        Vector3 up = Vector3.Cross(fwd, right).normalized;
        right = Vector3.Cross(up, fwd).normalized;

        // Cone half-angle from D:S: the spot's radius is distance / (2 * D:S).
        float tanHalf = 0.5f / Mathf.Max(distanceToSpotRatio, 1f);

        float cell = sim.CellSizePublic;
        Vector3 startVoxel = sim.WorldToVoxel(centreRay.origin);

        int validRays = 0;
        bool centreHit = false;
        RaycastHit centre = default;

        _gizmoOrigin = centreRay.origin;
        _gizmoCount = n;

        for (int i = 0; i < n; i++)
        {
            Vector2 d = _diskOffsets[i];
            Vector3 dir = (fwd + (right * d.x + up * d.y) * tanHalf).normalized;
            Ray ray = new Ray(centreRay.origin, dir);

            if (TryGetHit(ray, out RaycastHit hit))
            {
                // Just inside the surface for the solid, just in front for the air.
                Vector3 sv = sim.WorldToVoxel(hit.point - hit.normal * (cell * 0.5f));
                Vector3 av = sim.WorldToVoxel(hit.point + hit.normal * (cell * 0.75f));

                _rays[i] = new ProbeRayGPU
                {
                    surfaceVoxel = new Vector4(sv.x, sv.y, sv.z, 1f),
                    airVoxel = new Vector4(av.x, av.y, av.z, 0f),
                    startVoxel = new Vector4(startVoxel.x, startVoxel.y, startVoxel.z, hit.distance / cell),
                    dirVoxel = new Vector4(dir.x, dir.y, dir.z, 0f),
                };

                validRays++;
                _gizmoEnds[i] = hit.point;
                _gizmoHit[i] = true;

                if (i == 0) { centreHit = true; centre = hit; }
            }
            else
            {
                _rays[i] = default;   // w = 0: the kernel writes "no data"
                _gizmoEnds[i] = ray.origin + dir * maxDistance;
                _gizmoHit[i] = false;
            }
        }

        if (validRays == 0)
        {
            ClearReading();
            return;
        }

        sim.GetGridSize(out int gx, out int gy, out int gz);

        _rayBuffer.SetData(_rays, 0, 0, n);

        probeShader.SetInts("GridSize", gx, gy, gz);
        probeShader.SetInt("RayCount", n);
        probeShader.SetBuffer(_kernel, "Rays", _rayBuffer);
        probeShader.SetBuffer(_kernel, "Result", _resultBuffer);

        // Bound fresh every sample: these are ping-pong buffers, so the
        // texture that is "current" changes every sim step.
        probeShader.SetTexture(_kernel, "SolidTemp", sim.GetSolidTemperatureVolume());
        probeShader.SetTexture(_kernel, "AirTemp", sim.GetTemperatureVolume());
        probeShader.SetTexture(_kernel, "Obstacle", sim.GetObstacleVolume());
        probeShader.SetTexture(_kernel, "FlameHeat", sim.GetFireHeatVolume());

        probeShader.Dispatch(_kernel, Mathf.CeilToInt(n / 16f), 1, 1);

        GameObject hitObject = centreHit ? centre.collider.gameObject : null;
        Vector3 hitPoint = centreHit ? centre.point : Vector3.zero;
        float hitDistance = centreHit ? centre.distance : 0f;

        _pending = true;
        _pendingSince = Time.unscaledTime;

        AsyncGPUReadback.Request(_resultBuffer, n * ResultsPerRay * sizeof(float), 0, request =>
        {
            if (this == null) return;   // destroyed while waiting
            _pending = false;

            if (request.hasError)
            {
                Debug.LogWarning("[ThermalCameraProbe] Readback failed.");
                return;
            }

            var data = request.GetData<float>();
            Consume(data, n, centreHit, hitObject, hitPoint, hitDistance);
        });
    }

    void Consume(Unity.Collections.NativeArray<float> data, int n, bool centreHit,
                 GameObject hitObject, Vector3 hitPoint, float hitDistance)
    {
        float sum = 0f;
        float max = float.NegativeInfinity;
        float min = float.PositiveInfinity;
        int count = 0;

        for (int i = 0; i < n; i++)
        {
            int o = i * ResultsPerRay;

            float apparent = ApparentFor(data[o + 0], data[o + 1], data[o + 2],
                                         data[o + 3], data[o + 4]);
            if (float.IsNaN(apparent)) continue;

            sum += apparent;
            max = Mathf.Max(max, apparent);
            min = Mathf.Min(min, apparent);
            count++;
        }

        HitObject = hitObject;
        HitPoint = hitPoint;
        HitDistance = hitDistance;

        SpotValidRays = count;
        HasReading = count > 0;
        SpotTemperature = count > 0 ? sum / count : float.NaN;
        SpotMax = count > 0 ? max : float.NaN;
        SpotMin = count > 0 ? min : float.NaN;

        // Centre ray, same meanings as before.
        bool centreSolid = centreHit && data[2] > 0.5f && data[0] > NoValue;
        SurfaceTemperature = centreSolid ? data[0] : float.NaN;
        SurfaceMaxTemperature = centreSolid ? data[1] : float.NaN;
        AirTemperatureAtSurface = centreHit && data[3] > NoValue ? data[3] : float.NaN;
        FireHeatAlongRay = centreHit ? data[4] : float.NaN;

        UpdateReadout();

        if (HasReading)
            onSpotTemperature?.Invoke(SpotTemperature);

        if (centreSolid)
            onSurfaceTemperature?.Invoke(SurfaceTemperature);
    }

    // What one ray's pixel would read: hottest of the surface, the air in
    // front of it and any flame along the line of sight. NaN = no data.
    static float ApparentFor(float surface, float surfaceMax, float solidCount,
                             float air, float fire)
    {
        float a = float.NegativeInfinity;

        // The surface value itself, not the 3x3x3 max: the max picked up any
        // burning voxel next to the hit and made every nearby reading spike.
        if (solidCount > 0.5f && surface > NoValue) a = Mathf.Max(a, surface);
        if (air > NoValue) a = Mathf.Max(a, air);
        if (!float.IsNegativeInfinity(a)) a = Mathf.Max(a, fire);

        return float.IsNegativeInfinity(a) ? float.NaN : a;
    }

    bool TryGetHit(Ray ray, out RaycastHit result)
    {
        result = default;

        int count = Physics.RaycastNonAlloc(ray, _hits, maxDistance, hitLayers,
                                            QueryTriggerInteraction.Ignore);
        if (count == 0) return false;

        System.Array.Sort(_hits, 0, count, ByDistance);

        for (int i = 0; i < count; i++)
        {
            Collider col = _hits[i].collider;
            if (col == null) continue;

            // Skip the player's own hands/body/rig colliders.
            if (ignoreRoot != null && col.transform.IsChildOf(ignoreRoot)) continue;

            if (thermalObjectsOnly && col.GetComponentInParent<ThermalMaterial>() == null)
                continue;

            result = _hits[i];
            return true;
        }

        return false;
    }

    void ClearReading()
    {
        HasReading = false;
        HitObject = null;
        SpotValidRays = 0;
        SpotTemperature = SpotMax = SpotMin = float.NaN;
        SurfaceTemperature = SurfaceMaxTemperature = float.NaN;
        AirTemperatureAtSurface = FireHeatAlongRay = float.NaN;
        UpdateReadout();
    }

    void UpdateReadout()
    {
        if (readout == null) return;

        if (showDebugReadout)
        {
            ThermalSimulation sim = ThermalSimulation.Instance;
            bool running = sim != null && sim.simulationActive;

            readout.text =
                $"{(HitObject ? HitObject.name : "no centre hit")}\n" +
                $"spot {Fmt(SpotTemperature)}  ({SpotValidRays}/{Mathf.Clamp(rayCount, 1, MaxRays)} rays)\n" +
                $"min {Fmt(SpotMin)}  max {Fmt(SpotMax)}\n" +
                $"centre: surf {Fmt(SurfaceTemperature)}  air {Fmt(AirTemperatureAtSurface)}  fire {Fmt(FireHeatAlongRay)}\n" +
                $"sim {(running ? "RUNNING" : "PAUSED")}";
            return;
        }

        readout.text = HasReading ? $"{SpotTemperature:0.0} °C" : "-- °C";
    }

    static string Fmt(float v) =>
        float.IsNaN(v) || v < NoValue ? "--" : v.ToString("0.0");

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        for (int i = 0; i < _gizmoCount; i++)
        {
            Gizmos.color = _gizmoHit[i] ? (i == 0 ? Color.red : new Color(1f, 0.5f, 0f)) : Color.grey;
            Gizmos.DrawLine(_gizmoOrigin, _gizmoEnds[i]);
            if (_gizmoHit[i]) Gizmos.DrawWireSphere(_gizmoEnds[i], 0.02f);
        }
    }
}