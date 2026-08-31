using UnityEngine;

[RequireComponent(typeof(SmokeSim))]
public class SmokeSimDebug : MonoBehaviour
{
    public enum ViewMode
    {
        Off,
        Grid,        // just the empty voxel grid outline
        Obstacles,   // solid voxels only
        Smoke,       // voxels with smoke density > threshold
        Fire,        // voxels with fire density > threshold
        All          // obstacles + fire + smoke together
    }

    [Tooltip("What to draw in the Scene view.")]
    public ViewMode mode = ViewMode.Obstacles;

    [Tooltip("Only draw voxels with density above this. Higher = fewer, denser voxels shown.")]
    [Range(0.01f, 1.0f)]
    public float densityThreshold = 0.1f;

    [Tooltip("How often to read data back from GPU. Higher = smoother sim, less overhead.")]
    [Range(1, 30)]
    public int readbackIntervalFrames = 5;

    [Tooltip("Limit gizmo draws for perf. 0 = unlimited.")]
    public int maxGizmosDrawn = 5000;

    [Tooltip("Show a Y-slice only (useful for very dense volumes). -1 = show all.")]
    public int sliceY = -1;

    SmokeSim sim;
    float[] obstacleData;
    float[] smokeData;
    float[] fireData;
    int gx, gy, gz;
    Vector3 origin;
    float cellSize;
    int frameCounter;
    bool readbackInFlight;

    void OnEnable()
    {
        sim = GetComponent<SmokeSim>();
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        if (mode == ViewMode.Off) return;

        frameCounter++;
        if (frameCounter < readbackIntervalFrames) return;
        if (readbackInFlight) return;
        frameCounter = 0;

        RequestReadback();
    }

    void RequestReadback()
    {
        gx = sim.GridX;
        gy = sim.GridY;
        gz = sim.GridZ;
        origin = sim.Origin;
        cellSize = sim.cellSize;

        int total = gx * gy * gz;
        if (obstacleData == null || obstacleData.Length != total)
        {
            obstacleData = new float[total];
            smokeData = new float[total];
            fireData = new float[total];
        }

        readbackInFlight = true;
        int completed = 0;
        int needed = 0;

        System.Action tryFinish = () =>
        {
            completed++;
            if (completed >= needed) readbackInFlight = false;
        };

        if (mode == ViewMode.Obstacles || mode == ViewMode.All) needed++;
        if (mode == ViewMode.Smoke || mode == ViewMode.All) needed++;
        if (mode == ViewMode.Fire || mode == ViewMode.All) needed++;
        if (needed == 0) { readbackInFlight = false; return; }

        if (mode == ViewMode.Obstacles || mode == ViewMode.All)
            sim.RequestVolumeReadback(sim.ObstacleVolume, data =>
            {
                int n = Mathf.Min(data.Length, obstacleData.Length);
                for (int i = 0; i < n; i++) obstacleData[i] = data[i];
                tryFinish();
            });

        if (mode == ViewMode.Smoke || mode == ViewMode.All)
            sim.RequestVolumeReadback(sim.SmokeVolume, data =>
            {
                int n = Mathf.Min(data.Length, smokeData.Length);
                for (int i = 0; i < n; i++) smokeData[i] = data[i];
                tryFinish();
            });

        if (mode == ViewMode.Fire || mode == ViewMode.All)
            sim.RequestVolumeReadback(sim.FireVolume, data =>
            {
                int n = Mathf.Min(data.Length, fireData.Length);
                for (int i = 0; i < n; i++) fireData[i] = data[i];
                tryFinish();
            });
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        if (mode == ViewMode.Off || obstacleData == null) return;

        int drawn = 0;
        float voxelSize = cellSize * 0.9f;

        int yStart = sliceY >= 0 ? sliceY : 0;
        int yEnd = sliceY >= 0 ? Mathf.Min(sliceY + 1, gy) : gy;

        for (int z = 0; z < gz; z++)
            for (int y = yStart; y < yEnd; y++)
                for (int x = 0; x < gx; x++)
                {
                    if (maxGizmosDrawn > 0 && drawn >= maxGizmosDrawn) return;

                    int idx = x + gx * (y + gy * z);
                    if (idx >= obstacleData.Length) continue;

                    Vector3 center = origin + new Vector3(
                        (x + 0.5f) * cellSize,
                        (y + 0.5f) * cellSize,
                        (z + 0.5f) * cellSize);

                    switch (mode)
                    {
                        case ViewMode.Grid:
                            Gizmos.color = new Color(0.3f, 0.3f, 0.3f, 0.15f);
                            Gizmos.DrawWireCube(center, Vector3.one * voxelSize);
                            drawn++;
                            break;

                        case ViewMode.Obstacles:
                            if (obstacleData[idx] > 0.5f)
                            {
                                Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 0.6f);
                                Gizmos.DrawCube(center, Vector3.one * voxelSize);
                                drawn++;
                            }
                            break;

                        case ViewMode.Smoke:
                            if (smokeData[idx] > densityThreshold)
                            {
                                float a = Mathf.Clamp01(smokeData[idx]);
                                Gizmos.color = new Color(0.2f, 0.2f, 0.2f, a * 0.7f);
                                Gizmos.DrawCube(center, Vector3.one * voxelSize);
                                drawn++;
                            }
                            break;

                        case ViewMode.Fire:
                            if (fireData[idx] > densityThreshold)
                            {
                                float a = Mathf.Clamp01(fireData[idx]);
                                Gizmos.color = new Color(1f, 0.5f * a, 0.1f, a * 0.8f);
                                Gizmos.DrawCube(center, Vector3.one * voxelSize);
                                drawn++;
                            }
                            break;

                        case ViewMode.All:
                            if (obstacleData[idx] > 0.5f)
                            {
                                Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                                Gizmos.DrawCube(center, Vector3.one * voxelSize);
                                drawn++;
                            }
                            else if (fireData[idx] > densityThreshold)
                            {
                                float a = Mathf.Clamp01(fireData[idx]);
                                Gizmos.color = new Color(1f, 0.5f * a, 0.1f, a * 0.8f);
                                Gizmos.DrawCube(center, Vector3.one * voxelSize);
                                drawn++;
                            }
                            else if (smokeData[idx] > densityThreshold)
                            {
                                float a = Mathf.Clamp01(smokeData[idx]);
                                Gizmos.color = new Color(0.2f, 0.2f, 0.2f, a * 0.5f);
                                Gizmos.DrawCube(center, Vector3.one * voxelSize);
                                drawn++;
                            }
                            break;
                    }
                }
    }
}