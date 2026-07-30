using UnityEngine;
using UnityEngine.Rendering;

// Attach to the ThermalSimulation GameObject. Draws the voxel grid and colours
// cells by a chosen field (obstacle / burning / temperature / char) so you can
// see cell size and where fire/heat actually is.
//
// Gizmos only -- shows in the Scene view during Play. Reads back the selected
// volume on an interval via AsyncGPUReadback.
[RequireComponent(typeof(ThermalSimulation))]
public class ThermalVoxelVisualizer : MonoBehaviour
{
    public enum Field { Burning, Char, SolidTemperature, AirTemperature, Obstacle }

    [Header("What to show")]
    [SerializeField] private Field _field = Field.Burning;

    [Tooltip("Cells with display value below this are skipped (keeps view readable).")]
    [SerializeField] private float _threshold = 0.01f;

    [Tooltip("Temperatures are divided by this to map into the 0-1 colour range.")]
    [SerializeField] private float _tempNormalize = 400f;

    [Header("Drawing")]
    [Tooltip("Draw only every Nth voxel per axis. Raise if the grid is huge/slow.")]
    [SerializeField, Range(1, 8)] private int _stride = 1;

    [SerializeField, Range(0.1f, 1f)] private float _cubeFill = 0.9f;
    [SerializeField] private bool _drawGridBounds = true;
    [SerializeField] private bool _wireCubes = false;

    [Header("Update")]
    [Tooltip("Seconds between GPU readbacks.")]
    [SerializeField] private float _interval = 0.25f;

    private ThermalSimulation _sim;
    private float[] _data;
    private int _gx, _gy, _gz;
    private float _timer;
    private bool _pending;

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
        AsyncGPUReadback.Request(rt, 0, req =>
        {
            _pending = false;
            if (req.hasError) return;

            var src = req.GetData<float>();
            if (_data == null || _data.Length != src.Length)
                _data = new float[src.Length];
            src.CopyTo(_data);
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
            default:                     return null;
        }
    }

    private void OnDrawGizmos()
    {
        if (_sim == null) _sim = GetComponent<ThermalSimulation>();
        if (_sim == null) return;

        float cell = _sim.CellSizePublic;
        Vector3 min = _sim.SimBoundsMin;

        // Grid bounding box
        if (_drawGridBounds && _gx > 0)
        {
            Gizmos.color = Color.cyan;
            Vector3 size = new Vector3(_gx, _gy, _gz) * cell;
            Gizmos.DrawWireCube(min + size * 0.5f, size);
        }

        if (_data == null || _gx == 0) return;

        bool isTemp = _field == Field.SolidTemperature || _field == Field.AirTemperature;

        for (int z = 0; z < _gz; z += _stride)
        for (int y = 0; y < _gy; y += _stride)
        for (int x = 0; x < _gx; x += _stride)
        {
            int idx = x + _gx * (y + _gy * z);
            if (idx >= _data.Length) continue;

            float v = _data[idx];
            float display = isTemp ? v / _tempNormalize : v;

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
