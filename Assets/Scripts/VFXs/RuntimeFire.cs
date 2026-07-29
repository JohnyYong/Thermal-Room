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

    private bool _registeredAsFireSource;

    public Vector3 WorldPosition => transform.position;
    public float HeatRadius => _heatRadius;
    public float HeatPower => _heatPower;
    public float RadiationRange => _radiationRange;
    public float RadiationPower => _radiationPower;

    IEnumerator Start()
    {
        Collider col = GetComponent<Collider>();
        ThermalMaterial mat = GetComponent<ThermalMaterial>();

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
}