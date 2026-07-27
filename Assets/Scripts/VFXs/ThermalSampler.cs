using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Add this to the SAME GameObject as ThermalSimulation.
// Burnable objects register themselves; this samples char in a small
// region around each one's bounds, once per readback, and hands back
// the max value found there.
//
// One batched AsyncGPUReadback per cycle instead of a synchronous read
// per object -- no GPU stall, cost is independent of object count.
//
// DefaultExecutionOrder(-100) ensures Awake() here (which sets Instance)
// runs before other objects' OnEnable/Start try to Register() with it --
// Unity does not otherwise guarantee ordering between different
// GameObjects, and a missed registration used to fail silently forever.
[DefaultExecutionOrder(-100)]
public class ThermalSampler : MonoBehaviour
{
    public static ThermalSampler Instance { get; private set; }

    [Tooltip("How often to read back temperatures (seconds). Char doesn't need 60Hz.")]
    [SerializeField] private float _sampleInterval = 0.2f;

    private ThermalSimulation _sim;

    private readonly List<IThermalSampleReceiver> _subscribers = new();

    // Latest char value per subscriber index
    private float[] _temps = new float[0];
    private bool _readbackPending;
    private float _timer;

    private void Awake()
    {
        Instance = this;
        _sim = GetComponent<ThermalSimulation>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Register(IThermalSampleReceiver c)
    {
        if (!_subscribers.Contains(c)) _subscribers.Add(c);
    }

    public void Unregister(IThermalSampleReceiver c)
    {
        _subscribers.Remove(c);
    }

    private void Update()
    {
        if (_sim == null || _subscribers.Count == 0) return;

        _timer += Time.deltaTime;
        if (_timer < _sampleInterval || _readbackPending) return;

        _timer = 0f;
        RequestTemperatures();
    }

    private void RequestTemperatures()
    {
        RenderTexture tempRT = _sim.GetCharVolume();
        if (tempRT == null) return;


        int count = _subscribers.Count;
        var voxelMin = new Vector3Int[count];
        var voxelMax = new Vector3Int[count];

        for (int i = 0; i < count; i++)
        {
            Bounds b = _subscribers[i].SampleBounds;

            Vector3 vA = _sim.WorldToVoxel(b.min);
            Vector3 vB = _sim.WorldToVoxel(b.max);

            voxelMin[i] = new Vector3Int(
                Mathf.FloorToInt(Mathf.Min(vA.x, vB.x)),
                Mathf.FloorToInt(Mathf.Min(vA.y, vB.y)),
                Mathf.FloorToInt(Mathf.Min(vA.z, vB.z)));

            voxelMax[i] = new Vector3Int(
                Mathf.CeilToInt(Mathf.Max(vA.x, vB.x)),
                Mathf.CeilToInt(Mathf.Max(vA.y, vB.y)),
                Mathf.CeilToInt(Mathf.Max(vA.z, vB.z)));
        }

        _readbackPending = true;

        _sim.RequestVolumeReadback(tempRT, data =>
        {
            _readbackPending = false;

            if (_subscribers.Count != count) return; // list changed mid-flight

            int gx = _sim.GridXPublic;
            int gy = _sim.GridYPublic;
            int gz = _sim.GridZPublic;

            if (_temps.Length != count) _temps = new float[count];

            for (int i = 0; i < count; i++)
            {
                int minX = Mathf.Clamp(voxelMin[i].x, 0, gx - 1);
                int minY = Mathf.Clamp(voxelMin[i].y, 0, gy - 1);
                int minZ = Mathf.Clamp(voxelMin[i].z, 0, gz - 1);
                int maxX = Mathf.Clamp(voxelMax[i].x, 0, gx - 1);
                int maxY = Mathf.Clamp(voxelMax[i].y, 0, gy - 1);
                int maxZ = Mathf.Clamp(voxelMax[i].z, 0, gz - 1);

                float maxChar = 0f;

                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int x = minX; x <= maxX; x++)
                        {
                            int index = x + gx * (y + gy * z);
                            if (index < 0 || index >= data.Length) continue;

                            float v = data[index];
                            if (v > maxChar) maxChar = v;
                        }
                    }
                }

                _temps[i] = maxChar;
            }

            // Push results to subscribers
            for (int i = 0; i < _subscribers.Count && i < _temps.Length; i++)
                _subscribers[i].ReceiveTemperature(_temps[i]);
        });
    }
}