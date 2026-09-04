using ScenarioEditor;
using UnityEngine;

/// <summary>
/// Put this on a runtime-placed fire. It registers a SmokeSim.Emitter against
/// the single shared SmokeSim and keeps its rates in sync with FireProp.Intensity.
///
/// Registration lives in OnEnable/OnDisable rather than Awake/OnDestroy so that
/// an undo which merely deactivates the object also stops the plume, and a redo
/// brings it back.
/// </summary>
[DisallowMultipleComponent]
public class FireSmokeEmitter : MonoBehaviour
{
    [Tooltip("Where the plume starts. Leave empty to use this object's own transform. " +
             "A child lifted ~0.2m off the floor usually reads better.")]
    public Transform emitPoint;

    [Header("Values at intensity 1.0")]
    public float baseRadius = 0.6f;
    public float baseSmokeRate = 2.0f;
    [Tooltip("0 = smoke only, no flame.")]
    public float baseFireRate = 1.0f;

    [Tooltip("radius = baseRadius * intensity^exponent. 1/3 makes emitted VOLUME " +
             "scale linearly with intensity; 1.0 makes a 3x fire three times as wide, " +
             "which is usually far too big for the grid.")]
    [Range(0f, 1f)] public float radiusExponent = 0.333f;

    SmokeSim _sim;
    SmokeSim.Emitter _emitter;
    FireProp _fire;
    float _appliedIntensity = float.NaN;

    void Awake() => _fire = GetComponent<FireProp>();

    void OnEnable()
    {
        _sim = SmokeSim.Instance;
        if (_sim == null)
        {
            Debug.LogWarning("[FireSmokeEmitter] No SmokeSim in the scene — this fire " +
                             "will produce no smoke.", this);
            return;
        }

        _emitter = new SmokeSim.Emitter
        {
            transform = emitPoint != null ? emitPoint : transform,
            enabled = true
        };

        Apply(CurrentIntensity());
        _sim.RegisterEmitter(_emitter);

        if (!_sim.ContainsWorldPoint(_emitter.transform.position))
            Debug.LogWarning($"[FireSmokeEmitter] '{name}' was placed outside the SmokeSim " +
                             "volume — no smoke will be generated there.", this);
    }

    void OnDisable()
    {
        if (_sim != null && _emitter != null) _sim.UnregisterEmitter(_emitter);
        _emitter = null;
        _appliedIntensity = float.NaN;
    }

    // Polling beats an event here: it covers the panel's live drag, undo/redo,
    // and anything else that writes Intensity, without FireProp needing to know
    // this component exists.
    void LateUpdate()
    {
        float i = CurrentIntensity();
        if (!Mathf.Approximately(i, _appliedIntensity)) Apply(i);
    }

    float CurrentIntensity() => _fire != null ? _fire.Intensity : 1f;

    void Apply(float intensity)
    {
        _appliedIntensity = intensity;
        if (_emitter == null) return;

        float i = Mathf.Max(intensity, 0.01f);
        _emitter.radius = baseRadius * Mathf.Pow(i, radiusExponent);
        _emitter.smokeRate = baseSmokeRate * i;
        _emitter.fireRate = baseFireRate * i;
    }

    /// <summary>Optional: call from an extinguish routine to stop the plume without
    /// destroying the object. Existing smoke still decays away naturally.</summary>
    public void SetEmitting(bool on)
    {
        if (_emitter != null) _emitter.enabled = on;
    }
}