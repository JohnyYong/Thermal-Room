using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

/// Fires a ray from the centre of a camera, finds the surface it hits, and
/// reads that surface's temperature from ThermalSimulation.
///
/// The temperatures live on the GPU, so the value arrives a frame or two after
/// the raycast (async readback -- a synchronous read would stall the VR frame).
/// Read the latest value from SurfaceTemperature, or listen to
/// onSurfaceTemperature.
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

    [Header("Sampling")]
    [Tooltip("Seconds between readings. 0.1 = 10 readings per second.")]
    [SerializeField] private float sampleInterval = 0.1f;

    [Tooltip("A readback that hasn't come back after this many seconds is " +
             "given up on. Without this, one lost readback left the probe " +
             "'pending' forever and the reading froze at its last value.")]
    [SerializeField] private float readbackTimeout = 1f;

    [Header("Output")]
    [Tooltip("Optional text to show the reading on.")]
    [SerializeField] private TMP_Text readout;

    [Tooltip("Show the raw numbers (surface, max, air, fire along the ray, " +
             "whether the sim is running) on the readout instead of just the " +
             "surface temperature. Turn on while debugging.")]
    [SerializeField] private bool showDebugReadout = false;

    public UnityEvent<float> onSurfaceTemperature;

    // ---- Latest reading ----------------------------------------------------
    public bool HasReading { get; private set; }

    /// Solid surface temperature at the hit point, deg C.
    public float SurfaceTemperature { get; private set; }

    /// Hottest solid voxel within one cell of the hit, deg C.
    public float SurfaceMaxTemperature { get; private set; }

    /// Gas temperature just in front of the surface, deg C.
    public float AirTemperatureAtSurface { get; private set; }

    /// Highest fire/hot-gas value between the camera and the surface
    /// (raw FlameHeatStep output -- the same field the thermal image uses).
    public float FireHeatAlongRay { get; private set; }

    /// What a thermal camera would actually report for this pixel: the
    /// hottest of the surface, the air in front of it, and any flame or hot
    /// gas in between. A real imager can't see "behind" a flame -- it reads
    /// the flame. The surface value alone stays near ambient for anything
    /// that isn't itself burning, which is why it can look like it never moves.
    public float ApparentTemperature { get; private set; }

    public GameObject HitObject { get; private set; }
    public Vector3 HitPoint { get; private set; }
    public float HitDistance { get; private set; }

    const int ResultCount = 5;

    ComputeBuffer _resultBuffer;
    int _kernel = -1;
    bool _pending;
    float _pendingSince;
    float _nextSampleTime;

    readonly RaycastHit[] _hits = new RaycastHit[32];
    static readonly IComparer<RaycastHit> ByDistance =
        Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

    Ray _lastRay;
    bool _lastHit;

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
        _resultBuffer = new ComputeBuffer(ResultCount, sizeof(float));
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

    /// Takes one reading now. Result arrives via the properties / event.
    public void Sample()
    {
        ThermalSimulation sim = ThermalSimulation.Instance;
        if (sim == null || !sim.IsInitialized || sourceCamera == null || _resultBuffer == null)
            return;

        Ray ray = sourceCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        _lastRay = ray;

        if (!TryGetHit(ray, out RaycastHit hit))
        {
            _lastHit = false;
            ClearReading();
            return;
        }

        _lastHit = true;

        float cell = sim.CellSizePublic;

        // Just inside the surface for the solid, just in front for the air.
        Vector3 surfaceVoxel = sim.WorldToVoxel(hit.point - hit.normal * (cell * 0.5f));
        Vector3 airVoxel = sim.WorldToVoxel(hit.point + hit.normal * (cell * 0.75f));
        Vector3 startVoxel = sim.WorldToVoxel(ray.origin);

        sim.GetGridSize(out int gx, out int gy, out int gz);

        probeShader.SetInts("GridSize", gx, gy, gz);
        probeShader.SetVector("SurfaceVoxel", surfaceVoxel);
        probeShader.SetVector("AirVoxel", airVoxel);
        probeShader.SetVector("RayStartVoxel", startVoxel);
        probeShader.SetVector("RayDirVoxel", ray.direction);   // voxel space is just scaled world space
        probeShader.SetFloat("RayLengthVoxels", hit.distance / cell);

        // Bound fresh every sample: these are ping-pong buffers, so the
        // texture that is "current" changes every sim step.
        probeShader.SetTexture(_kernel, "SolidTemp", sim.GetSolidTemperatureVolume());
        probeShader.SetTexture(_kernel, "AirTemp", sim.GetTemperatureVolume());
        probeShader.SetTexture(_kernel, "Obstacle", sim.GetObstacleVolume());
        probeShader.SetTexture(_kernel, "FlameHeat", sim.GetFireHeatVolume());
        probeShader.SetBuffer(_kernel, "Result", _resultBuffer);

        probeShader.Dispatch(_kernel, 1, 1, 1);

        GameObject hitObject = hit.collider.gameObject;
        Vector3 hitPoint = hit.point;
        float hitDistance = hit.distance;

        _pending = true;
        _pendingSince = Time.unscaledTime;

        AsyncGPUReadback.Request(_resultBuffer, request =>
        {
            if (this == null) return;   // destroyed while waiting
            _pending = false;

            if (request.hasError)
            {
                Debug.LogWarning("[ThermalCameraProbe] Readback failed.");
                return;
            }

            var data = request.GetData<float>();

            HitObject = hitObject;
            HitPoint = hitPoint;
            HitDistance = hitDistance;

            bool foundSolid = data[2] > 0.5f && data[0] > -9000f;

            HasReading = foundSolid;
            SurfaceTemperature = foundSolid ? data[0] : float.NaN;
            SurfaceMaxTemperature = foundSolid ? data[1] : float.NaN;
            AirTemperatureAtSurface = data[3];
            FireHeatAlongRay = data[4];

            float apparent = foundSolid ? data[1] : float.NegativeInfinity;
            if (data[3] > -9000f) apparent = Mathf.Max(apparent, data[3]);
            apparent = Mathf.Max(apparent, data[4]);
            ApparentTemperature = float.IsNegativeInfinity(apparent) ? float.NaN : apparent;

            UpdateReadout();

            if (HasReading)
                onSurfaceTemperature?.Invoke(SurfaceTemperature);
        });
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
        SurfaceTemperature = SurfaceMaxTemperature = ApparentTemperature = float.NaN;
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
                $"{(HitObject ? HitObject.name : "no hit")}\n" +
                $"surface {Fmt(SurfaceTemperature)}  max {Fmt(SurfaceMaxTemperature)}\n" +
                $"air {Fmt(AirTemperatureAtSurface)}  fire {Fmt(FireHeatAlongRay)}\n" +
                $"sim {(running ? "RUNNING" : "PAUSED")}";
            return;
        }

        readout.text = float.IsNaN(ApparentTemperature)
            ? "-- °C"
            : $"{ApparentTemperature:0.0} °C";
    }

    static string Fmt(float v) =>
        float.IsNaN(v) || v < -9000f ? "--" : v.ToString("0.0");

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        Gizmos.color = _lastHit ? Color.red : Color.grey;
        float len = _lastHit ? HitDistance : maxDistance;
        Gizmos.DrawLine(_lastRay.origin, _lastRay.origin + _lastRay.direction * len);

        if (_lastHit) Gizmos.DrawWireSphere(HitPoint, 0.05f);
    }
}