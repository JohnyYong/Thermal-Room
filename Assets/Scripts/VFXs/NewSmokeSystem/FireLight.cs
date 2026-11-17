using UnityEngine;

//Real light from the fire. Follows the SmokeSim emitters: sits over the burning fuel,
//grows with fireRate, flickers, and goes dull and red once smoke builds up around it.
//No shadows on purpose, a shadowed point light renders the scene 6 more times (too heavy for Quest).
[RequireComponent(typeof(SmokeSim))]
public class FireLight : MonoBehaviour
{
    [SerializeField] private Color _color = new Color(1f, 0.55f, 0.24f);      //~1800K, wood flame
    [SerializeField] private Color _smokyColor = new Color(1f, 0.32f, 0.1f);  //what gets through thick smoke
    [SerializeField] private float _brightness = 10f;
    [SerializeField] private float _range = 9f;
    [SerializeField] private float _height = 0.8f;      //metres above the fuel, about the middle of the flame
    [SerializeField, Range(0f, 0.5f)] private float _flicker = 0.22f;
    [SerializeField, Range(0f, 1f)] private float _smokeDimming = 0.6f;  //how much a smoke-filled room swallows the light
    [SerializeField] private float _fullFireRate = 1f;  //emitter fireRate that counts as fully alight

    private const string LightName = "FireLight";

    private SmokeSim _sim;
    private Light _light;
    private Vector3 _centre;
    private float _seed, _level;
    private float _smoke, _smokeTarget, _nextSmokeSample;
    private bool _smokePending;

    void Awake()
    {
        _sim = GetComponent<SmokeSim>();
        _seed = Random.value * 100f;

        //Reuse the child if it's already there instead of stacking a second light
        var existing = transform.Find(LightName);
        _light = existing != null ? existing.GetComponent<Light>() : null;
        if (_light == null)
        {
            var go = new GameObject(LightName);
            go.transform.SetParent(transform, false);
            _light = go.AddComponent<Light>();
        }

        _light.type = LightType.Point;
        _light.shadows = LightShadows.None;
        _light.renderMode = LightRenderMode.ForcePixel;
        _light.enabled = false;
    }

    void OnDisable()
    {
        if (_light != null) _light.enabled = false;
    }

    void LateUpdate()
    {
        float fire = ReadFire();

        //Fades in/out over ~2 s instead of popping when the fire starts or gets reset
        _level = Mathf.MoveTowards(_level, fire, Time.deltaTime * 0.5f);
        bool lit = _level > 0.01f;
        if (_light.enabled != lit) _light.enabled = lit;
        if (!lit) return;

        SampleSmoke();
        _smoke = Mathf.MoveTowards(_smoke, _smokeTarget, Time.deltaTime * 0.5f);
        float smoky = Mathf.Clamp01(_smoke) * _smokeDimming;

        //Two octaves of noise: slow breathing plus fast licks
        float t = Time.time * 6f + _seed;
        float n = Mathf.PerlinNoise(t, _seed) * 0.65f + Mathf.PerlinNoise(t * 2.7f, _seed + 7.3f) * 0.35f;
        float flick = 1f + (n - 0.5f) * 2f * _flicker;

        _light.intensity = _brightness * Mathf.Pow(_level, 0.7f) * flick * (1f - smoky);
        _light.range = _range * Mathf.Pow(_level, 0.4f);
        _light.color = Color.Lerp(_color, _smokyColor, smoky);

        //Wanders a little inside the flame so the light on the walls moves like real firelight
        Vector3 jitter = new Vector3(Mathf.PerlinNoise(t * 0.8f, 3.1f) - 0.5f, 0f, Mathf.PerlinNoise(5.7f, t * 0.8f) - 0.5f) * 0.25f;
        _light.transform.position = _centre + Vector3.up * (_height * Mathf.Sqrt(_level)) + jitter;
    }

    //Average fire over the emitters (0..1), and the fire-weighted middle of the fuel
    float ReadFire()
    {
        var emitters = _sim.emitters;
        if (emitters == null) return 0f;

        float sum = 0f;
        int count = 0;
        Vector3 pos = Vector3.zero;
        for (int i = 0; i < emitters.Length; i++)
        {
            var e = emitters[i];
            if (e == null || !e.enabled || e.transform == null) continue;
            count++;

            float f = Mathf.Clamp01(e.fireRate / _fullFireRate);
            if (f <= 0f) continue;
            sum += f;
            pos += e.transform.position * f;
        }

        if (sum <= 0f) return 0f;
        _centre = pos / sum;
        return sum / count;
    }

    //A few times a second, read the smoke density just above the light. Async, never stalls the GPU.
    void SampleSmoke()
    {
        if (_smokePending || Time.time < _nextSmokeSample || _smokeDimming <= 0f) return;
        _nextSmokeSample = Time.time + 0.25f;

        _smokePending = _sim.RequestSmokeAt(_light.transform.position + Vector3.up * 0.5f, d =>
        {
            _smokeTarget = d;
            _smokePending = false;
        });
    }
}
