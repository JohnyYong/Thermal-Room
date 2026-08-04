using UnityEngine;

// Attach to the ThermalSimulation GameObject. Draws the voxel grid and colours
// cells by a chosen field (obstacle / burning / temperature / char / fire heat)
// so you can see cell size and where fire/heat actually is.
//
// Gizmos only -- shows in the Scene view during Play. Reads back the selected
// volume on an interval via ThermalSimulation.RequestVolumeReadback (NOT
// AsyncGPUReadback directly on the Tex3D -- that silently truncates to the
// z=0 slice on some backends; RequestVolumeReadback routes through the
// CopyVolumeToBuffer compute kernel into a linear buffer instead).
[RequireComponent(typeof(ThermalSimulation))]
public class ThermalVoxelVisualizer : MonoBehaviour
{
    public enum Field { Burning, Char, SolidTemperature, AirTemperature, Obstacle, FireHeat }

    [Header("What to show")]
    [SerializeField] private Field _field = Field.Burning;

    [Tooltip("Cells with display value below this are skipped (keeps view readable).")]
    [SerializeField] private float _threshold = 0.01f;

    [Tooltip("Temperatures (and FireHeat, which is temp + smoke*80 + flame*200) are divided by this to map into the 0-1 colour range.")]
    [SerializeField] private float _tempNormalize = 400f;

    [Header("Drawing")]
    [Tooltip("Draw only every Nth voxel per axis. Raise if the grid is huge/slow.")]
    [SerializeField, Range(1, 8)] private int _stride = 1;

    [SerializeField, Range(0.1f, 1f)] private float _cubeFill = 0.9f;
    [SerializeField] private bool _drawGridBounds = true;
    [SerializeField] private bool _wireCubes = false;

    [Header("Data bounds")]
    [Tooltip("Draw a box around the actual extent of cells currently above threshold for the selected field.")]
    [SerializeField] private bool _drawDataBounds = true;

    [Tooltip("Optional: assign the mesh/renderer that the corresponding volumetric shader (e.g. FireHeatVolume) actually draws onto, to compare its world bounds against where the data really is.")]
    [SerializeField] private Renderer _renderVolumeRenderer;

    [Header("Update")]
    [Tooltip("Seconds between GPU readbacks.")]
    [SerializeField] private float _interval = 0.25f;

    private ThermalSimulation _sim;
    private float[] _data;
    private int _gx, _gy, _gz;
    private float _timer;
    private bool _pending;

    private bool _hasDataBounds;
    private Vector3Int _dataMin, _dataMax;

    private void OnEnable()
    {
        _sim = GetComponent<ThermalSimulation>();
    }

    private void Update()
    {
        if (_sim == null || !Application.isPlaying) return;

        _timer += Time.deltaTime;
        if (_timer < _interval || _pending) return;
        _timer = 0f;

        RenderTexture rt = PickTexture();
        if (rt == null) return;

        _gx = _sim.GridXPublic;
        _gy = _sim.GridYPublic;
        _gz = _sim.GridZPublic;

        _pending = true;
        _sim.RequestVolumeReadback(rt, data =>
        {
            _pending = false;

            if (_data == null || _data.Length != data.Length)
                _data = new float[data.Length];
            data.CopyTo(_data);

            ComputeDataBounds();
        });
    }

    private RenderTexture PickTexture()
    {
        switch (_field)
        {
            case Field.Burning:          return _sim.GetBurningVolume();
            case Field.Char:             return _sim.GetCharVolume();
            case Field.SolidTemperature: return _sim.GetSolidTemperatureVolume();
            case Field.AirTemperature:   return _sim.GetTemperatureVolume();
            case Field.Obstacle:         return _sim.GetObstacleVolume();
            case Field.FireHeat:         return _sim.GetFireHeatVolume();
            default:                     return null;
        }
    }

    // display value for a raw field sample, matching what OnDrawGizmos uses
    // per-cell -- kept in one place so the bounds box and the cube colouring
    // can never disagree about what counts as "above threshold".
    private float ToDisplay(float raw)
    {
        bool normalized = _field == Field.SolidTemperature ||
                           _field == Field.AirTemperature ||
                           _field == Field.FireHeat;

        return normalized ? raw / _tempNormalize : raw;
    }

    private void ComputeDataBounds()
    {
        _hasDataBounds = false;
        if (_data == null || _gx == 0) return;

        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;

        for (int z = 0; z < _gz; z++)
        for (int y = 0; y < _gy; y++)
        for (int x = 0; x < _gx; x++)
        {
            int idx = x + _gx * (y + _gy * z);
            if (idx >= _data.Length) continue;

            float display = ToDisplay(_data[idx]);
            if (display < _threshold) continue;

            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
            if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
            _hasDataBounds = true;
        }

        if (_hasDataBounds)
        {
            _dataMin = new Vector3Int(minX, minY, minZ);
            _dataMax = new Vector3Int(maxX, maxY, maxZ);
        }
    }

    private void OnDrawGizmos()
    {
        if (_sim == null) _sim = GetComponent<ThermalSimulation>();
        if (_sim == null) return;

        float cell = _sim.CellSizePublic;
        Vector3 min = _sim.SimBoundsMin;

        // Full simulation grid bounds (cyan)
        if (_drawGridBounds && _gx > 0)
        {
            Gizmos.color = Color.cyan;
            Vector3 size = new Vector3(_gx, _gy, _gz) * cell;
            Gizmos.DrawWireCube(min + size * 0.5f, size);
        }

        // Actual extent of the selected field's non-trivial data (magenta)
        if (_drawDataBounds && _hasDataBounds)
        {
            Gizmos.color = Color.magenta;
            Vector3 boxMin = min + new Vector3(_dataMin.x, _dataMin.y, _dataMin.z) * cell;
            Vector3 boxMax = min + new Vector3(_dataMax.x + 1, _dataMax.y + 1, _dataMax.z + 1) * cell;
            Gizmos.DrawWireCube((boxMin + boxMax) * 0.5f, boxMax - boxMin);
        }

        // The render volume mesh's actual world bounds (yellow), for comparison
        if (_renderVolumeRenderer != null)
        {
            Gizmos.color = Color.yellow;
            Bounds rb = _renderVolumeRenderer.bounds;
            Gizmos.DrawWireCube(rb.center, rb.size);
        }

        if (_data == null || _gx == 0) return;

        for (int z = 0; z < _gz; z += _stride)
        for (int y = 0; y < _gy; y += _stride)
        for (int x = 0; x < _gx; x += _stride)
        {
            int idx = x + _gx * (y + _gy * z);
            if (idx >= _data.Length) continue;

            float display = ToDisplay(_data[idx]);
            if (display < _threshold) continue;
            display = Mathf.Clamp01(display);

            // green (low) -> red (high); obstacle drawn grey
            if (_field == Field.Obstacle)
                Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            else
                Gizmos.color = new Color(display, 1f - display, 0f, 0.6f);

            Vector3 center = min + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * cell;
            Vector3 cube = Vector3.one * cell * _cubeFill;

            if (_wireCubes) Gizmos.DrawWireCube(center, cube);
            else            Gizmos.DrawCube(center, cube);
        }
    }
}
