using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Runtime ignition for ThermalSimulation, split into its own file.
//
// Requires `partial` on the main class declaration in ThermalSimulation.cs:
//     public partial class ThermalSimulation : MonoBehaviour
// and the old RegisterRuntimeBurningObject method deleted from that file.
public partial class ThermalSimulation
{
    [Header("Runtime Ignition")]
    [Tooltip("How fast a runtime fire spreads across the object from its " +
             "ignition point, metres per second. Lighting the whole object " +
             "in one frame releases all of its heat at once: the starting " +
             "plume balloons into a dome and the fire only looks like fire " +
             "once that has blown off. Spreading from a point lets the flame " +
             "grow from a small base, the way a real fire does. 0 ignites " +
             "everything instantly (old behaviour).")]
    public float runtimeIgnitionSpreadSpeed = 0.3f;

    readonly List<Coroutine> _ignitionSpreads = new();

    struct PendingVoxel
    {
        public BurningVoxel voxel;
        public float dueTime;   // seconds after registration
    }

    /// <summary>
    /// Makes the collider's voxels a solid fuel object straight away, then
    /// ignites them outward from <paramref name="ignitionPoint"/> at
    /// runtimeIgnitionSpreadSpeed. With no ignition point given, the fire
    /// starts at the bottom-centre of the collider's bounds.
    /// </summary>
    public void RegisterRuntimeBurningObject(
        Collider collider,
        ThermalMaterial material,
        Vector3? ignitionPoint = null)
    {
        List<BurningVoxel> voxels = CollectColliderVoxels(collider);

        Debug.Log($"[ThermalSimulation] Runtime fire '{collider.name}': {voxels.Count} voxels " +
                  $"| Grid {gridX}x{gridY}x{gridZ} | SimBounds {GetSimulationBounds()} " +
                  $"| ColliderBounds {collider.bounds}");

        if (voxels.Count == 0)
        {
            Debug.Log("[ThermalSimulation] No voxels found -- is the object inside the sim volume?");
            return;
        }

        float fuel = material.fuelAmount;
        float materialId = (float)material.materialType;

        // 1. The whole object becomes solid fuel now: it physically exists,
        //    it just isn't alight yet.
        DispatchRuntimeIgnite(voxels, 0, voxels.Count, fuel, materialId, 0);

        // 2. Light it.
        if (runtimeIgnitionSpreadSpeed <= 0f)
        {
            DispatchRuntimeIgnite(voxels, 0, voxels.Count, fuel, materialId, 1);
            return;
        }

        Bounds b = collider.bounds;
        Vector3 origin = ignitionPoint ?? new Vector3(b.center.x, b.min.y, b.center.z);

        _ignitionSpreads.Add(StartCoroutine(SpreadIgnition(voxels, origin, fuel, materialId)));
    }

    /// <summary>
    /// Stops any fire still spreading across a runtime object. Call this
    /// from ResetSimulation so a reset doesn't get re-lit by a spread that
    /// was in progress.
    /// </summary>
    public void StopRuntimeIgnitionSpreads()
    {
        foreach (var c in _ignitionSpreads)
            if (c != null) StopCoroutine(c);

        _ignitionSpreads.Clear();
    }

    List<BurningVoxel> CollectColliderVoxels(Collider collider)
    {
        var voxels = new List<BurningVoxel>();

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
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    Vector3 centre = VoxelToWorld(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f));

                    if (!IsVoxelInsideCollider(collider, centre))
                        continue;

                    voxels.Add(new BurningVoxel { x = (uint)x, y = (uint)y, z = (uint)z, pad = 0 });
                }

        return voxels;
    }

    // Uploads voxels[start .. start+count) to the front of the shared
    // runtime buffer and dispatches RuntimeIgnite over them.
    // mode 0 = register as solid fuel, mode 1 = ignite.
    void DispatchRuntimeIgnite(List<BurningVoxel> voxels, int start, int count,
                               float fuel, float materialId, int mode)
    {
        if (count <= 0) return;

        runtimeVoxelBuffer.SetData(voxels, start, 0, count);

        simulation.SetBuffer(runtimeIgniteKernel, "RuntimeVoxels", runtimeVoxelBuffer);
        simulation.SetInt("RuntimeVoxelCount", count);
        simulation.SetFloat("RuntimeFuel", fuel);
        simulation.SetFloat("RuntimeMaterial", materialId);
        simulation.SetInt("RuntimeIgniteMode", mode);

        // Bound fresh every call: these are ping-pong buffers, so the
        // current "A" side changes from one tick to the next.
        simulation.SetTexture(runtimeIgniteKernel, "MaterialVolume", materialVolume);
        simulation.SetTexture(runtimeIgniteKernel, "FuelOut", fuelA);
        simulation.SetTexture(runtimeIgniteKernel, "BurningOut", burningA);
        simulation.SetTexture(runtimeIgniteKernel, "ObstacleOut", obstacleVolume);

        simulation.Dispatch(runtimeIgniteKernel, Mathf.CeilToInt(count / 64f), 1, 1);
    }

    IEnumerator SpreadIgnition(List<BurningVoxel> voxels, Vector3 originWorld,
                               float fuel, float materialId)
    {
        // Order voxels by when the flame front reaches them.
        Vector3 originVox = WorldToVoxel(originWorld);
        var pending = new List<PendingVoxel>(voxels.Count);

        foreach (var v in voxels)
        {
            Vector3 centre = new Vector3(v.x + 0.5f, v.y + 0.5f, v.z + 0.5f);
            float metres = Vector3.Distance(centre, originVox) * cellSize;

            pending.Add(new PendingVoxel
            {
                voxel = v,
                dueTime = metres / runtimeIgnitionSpreadSpeed
            });
        }

        pending.Sort((a, b) => a.dueTime.CompareTo(b.dueTime));

        var ordered = new List<BurningVoxel>(pending.Count);
        foreach (var p in pending) ordered.Add(p.voxel);

        int next = 0;
        float elapsed = 0f;

        while (next < ordered.Count)
        {
            int end = next;
            while (end < ordered.Count && pending[end].dueTime <= elapsed)
                end++;

            if (end > next)
            {
                DispatchRuntimeIgnite(ordered, next, end - next, fuel, materialId, 1);
                next = end;
            }

            // Per frame, not per FixedUpdate: the sims step from Update now.
            yield return null;
            elapsed += Time.deltaTime;
        }
    }
}
