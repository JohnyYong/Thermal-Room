using ScenarioEditor;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(FireProp))]
[DisallowMultipleComponent]
public class FireExtinguishable : MonoBehaviour
{
    public static readonly List<FireExtinguishable> Active = new List<FireExtinguishable>();

    [Header("Suppression")]
    [SerializeField] private float _suppressionPerSecond = 0.8f;

    [SerializeField] private float _regrowthPerSecond = 0.15f;

    [SerializeField] private float _extinguishThreshold = 0.15f;

    [SerializeField] private float _lingerAfterOut = 4f;

    [SerializeField] private bool _deactivateInsteadOfDestroy = true;

    private FireProp _fire;
    private FireSmokeEmitter _emitter;
    private float _fullIntensity;      // what it was before any foam landed
    private float _pendingSuppression; // accumulated this frame
    private float _outTimer = -1f;

    public bool IsOut => _outTimer >= 0f;

    private void Awake()
    {
        _fire = GetComponent<FireProp>();
        _emitter = GetComponent<FireSmokeEmitter>();
    }

    private void OnEnable()
    {
        if (!Active.Contains(this)) Active.Add(this);

        _fullIntensity = Mathf.Max(_fire.Intensity, 0.01f);
        _outTimer = -1f;
    }

    private void OnDisable() => Active.Remove(this);

    public void ApplyFoam(float coverage)
    {
        if (IsOut) return;
        _pendingSuppression += Mathf.Max(coverage, 0f);
    }

    private void Update()
    {
        if (IsOut)
        {
            _outTimer += Time.deltaTime;
            if (_outTimer >= _lingerAfterOut)
            {
                if (_deactivateInsteadOfDestroy) gameObject.SetActive(false);
                else Destroy(gameObject);
            }
            return;
        }

        float intensity = _fire.Intensity;

        if (_pendingSuppression > 0f)
        {
            intensity -= _suppressionPerSecond * _pendingSuppression * Time.deltaTime;
        }
        else if (_regrowthPerSecond > 0f && intensity < _fullIntensity)
        {
            intensity += _regrowthPerSecond * Time.deltaTime;
        }

        _pendingSuppression = 0f;
        intensity = Mathf.Min(intensity, _fullIntensity);

        if (intensity <= _extinguishThreshold)
        {
            GoOut();
            return;
        }


        if (!Mathf.Approximately(intensity, _fire.Intensity))
            _fire.SetIntensity(intensity);
    }

    private void GoOut()
    {
        _outTimer = 0f;
        _fire.SetIntensity(0f);          // also drops the thermal heat source
        if (_emitter != null) _emitter.SetEmitting(false);
    }

    /// <summary>Relight at full strength, e.g. for a scenario reset.</summary>
    public void Reignite()
    {
        _outTimer = -1f;
        _pendingSuppression = 0f;
        _fire.SetIntensity(_fullIntensity);
        if (_emitter != null) _emitter.SetEmitting(true);
    }
}