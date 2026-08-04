using System.Collections;
using UnityEngine;

// Put this on anything that's on fire at runtime -- a crate that ignites
// where it sits, OR a thrown fireball/torch that moves. Handles both with
// one script instead of needing RuntimeFire + MobileFireSource together:
//
//  - If this object has a Collider + ThermalMaterial, it ignites that
//    collider's voxels ONCE, in place (same as before) -- for a solid
//    object that's on fire where it stands.
//  - Regardless of whether it has a collider, it ALSO registers itself as
//    a live heat source every FixedUpdate, tracking its CURRENT position --
//    so if this object moves (a fireball, a torch someone's carrying),
//    heat genuinely follows it instead of staying behind at spawn.
//
// A static burning crate just won't move, so the second part is harmless
// for it -- it becomes a (redundant but free) heat source at a fixed
// point, on top of the combustion heat its own burning voxels already
// generate. A collider-less fireball skips ignition entirely and is
// purely a moving heat source.
public class RuntimeFire : MonoBehaviour, IMobileFireSource
{
    [Header("Air heat (HeatStep)")]
    [Tooltip("Voxel-space falloff radius for heating the surrounding air.")]
    [SerializeField] private float _heatRadius = 6f;
    [SerializeField] private float _heatPower = 40f;

    [Header("Solid radiant heat (RadiationStep)")]
    [Tooltip("Voxel-space falloff range for radiantly heating nearby solids. " +
             "A solid that stays inside this range long enough to cross its " +
             "own ignition temperature will catch fire on its own -- no " +
             "extra ignition code needed here.")]
    [SerializeField] private float _radiationRange = 10f;
    [SerializeField] private float _radiationPower = 60f;

    [Header("Debug: fuel depletion")]
    [Tooltip("Periodically read back fuel over this object's own ignited " +
             "voxel range and log once it hits zero -- pinpoints exactly " +
             "when/why this object's fire dies out.")]
    [SerializeField] private bool _logFuelDepletion = true;
    [SerializeField] private float _fuelCheckInterval = 0.5f;

    private bool _registeredAsFireSource;

    private Collider _collider;
    private bool _hasIgnitedVoxelRange;
    private Vector3Int _fuelVoxelMin, _fuelVoxelMax;
    private bool _everSeenFuel;
    private bool _loggedDepletion;
    private float _fuelCheckTimer;
    private bool _fuelReadbackPending;

    public Vector3 WorldPosition => transform.position;
    public float HeatRadius => _heatRadius;
    public float HeatPower => _heatPower;
    public float RadiationRange => _radiationRange;
    public float RadiationPower => _radiationPower;

    IEnumerator Start()
    {
        Collider col = GetComponent<Collider>();
        ThermalMaterial mat = GetComponent<ThermalMaterial>();
        _collider = col;

        while (ThermalSimulation.Instance == null ||
               !ThermalSimulation.Instance.IsInitialized)
        {
            yield return null;
        }

        // Optional: only ignites a fixed solid region if this object
        // actually has both a collider and a material to burn. A
        // collider-less/material-less fireball just skips this and is
        // purely a moving heat source below.
        if (col != null && mat != null)
        {
            Debug.Log("RunTimeFire: " + col.bounds.center);
            ThermalSimulation.Instance.RegisterRuntimeBurningObject(col, mat);

            CacheFuelVoxelRange(col);
        }

        TryRegisterFireSource();
    }

    private void OnEnable() => TryRegisterFireSource();

    private void Update()
    {
        // Covers the case where this object is enabled before
        // ThermalSimulation finishes initializing -- keep retrying cheaply
        // until registration succeeds, same reasoning as the Start()
        // coroutine above but for re-enables after the initial Start().
        if (!_registeredAsFireSource) TryRegisterFireSource();

        if (_logFuelDepletion && _hasIgnitedVoxelRange && !_loggedDepletion)
            UpdateFuelWatch();
    }

    private void TryRegisterFireSource()
    {
        if (_registeredAsFireSource) return;
        if (ThermalSimulation.Instance == null || !ThermalSimulation.Instance.IsInitialized) return;

        ThermalSimulation.Instance.RegisterFireSource(this);
        _registeredAsFireSource = true;
    }

    private void OnDisable()
    {
        // Also covers destruction (Unity calls OnDisable before an object
        // is destroyed), so a despawned/pooled fire can't keep
        // contributing heat from its last known position.
        if (ThermalSimulation.Instance != null)
            ThermalSimulation.Instance.UnregisterFireSource(this);
        _registeredAsFireSource = false;
    }

    // ------------------------------------------------------------------
    // Fuel depletion watcher (debug only)
    // ------------------------------------------------------------------

    private void CacheFuelVoxelRange(Collider col)
    {
        ThermalSimulation sim = ThermalSimulation.Instance;

        Vector3 vA = sim.WorldToVoxel(col.bounds.min);
        Vector3 vB = sim.WorldToVoxel(col.bounds.max);

        int gx = sim.GridXPublic;
        int gy = sim.GridYPublic;
        int gz = sim.GridZPublic;

        _fuelVoxelMin = new Vector3Int(
            Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(vA.x, vB.x)), 0, gx - 1),
            Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(vA.y, vB.y)), 0, gy - 1),
            Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(vA.z, vB.z)), 0, gz - 1));

        _fuelVoxelMax = new Vector3Int(
            Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(vA.x, vB.x)), 0, gx - 1),
            Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(vA.y, vB.y)), 0, gy - 1),
            Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(vA.z, vB.z)), 0, gz - 1));

        _hasIgnitedVoxelRange = true;
    }

    private void UpdateFuelWatch()
    {
        _fuelCheckTimer += Time.deltaTime;
        if (_fuelCheckTimer < _fuelCheckInterval || _fuelReadbackPending) return;
        _fuelCheckTimer = 0f;

        ThermalSimulation sim = ThermalSimulation.Instance;
        if (sim == null) return;

        RenderTexture fuelRT = sim.GetFuelVolume();
        if (fuelRT == null) return;

        _fuelReadbackPending = true;
        float requestTime = Time.time;

        sim.RequestVolumeReadback(fuelRT, data =>
        {
            _fuelReadbackPending = false;
            if (_loggedDepletion) return; // already logged from a previous request

            int gx = sim.GridXPublic;
            int gy = sim.GridYPublic;

            float maxFuel = 0f;

            for (int z = _fuelVoxelMin.z; z <= _fuelVoxelMax.z; z++)
                for (int y = _fuelVoxelMin.y; y <= _fuelVoxelMax.y; y++)
                    for (int x = _fuelVoxelMin.x; x <= _fuelVoxelMax.x; x++)
                    {
                        int idx = x + gx * (y + gy * z);
                        if (idx < 0 || idx >= data.Length) continue;
                        if (data[idx] > maxFuel) maxFuel = data[idx];
                    }

            if (maxFuel > 0.001f)
            {
                _everSeenFuel = true;
                return;
            }

            // Only log the transition from "had fuel" to "out of fuel" --
            // not the very first frame, in case ignition itself takes a
            // beat to write RuntimeFuel into the voxels.
            if (_everSeenFuel)
            {
                _loggedDepletion = true;
                Debug.Log($"[RuntimeFire] '{name}' fuel depleted (snap to burning=0) " +
                          $"at t={requestTime:F2}s (detected at t={Time.time:F2}s).");
            }
        });
    }
}