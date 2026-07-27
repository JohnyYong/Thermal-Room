using UnityEngine;

namespace ScenarioEditor
{
    /// <summary>
    /// Put this on the fire prefab root. Intensity is a DIRECT multiplier:
    /// it is applied straight to transform scale and to particle emission, so
    /// the slider value you see is the value that gets used — no hidden 0..1
    /// remapping. The min/max range lives on FirePropertiesPanel, not here.
    ///
    /// Everything is applied absolutely from _intensity (never read back from
    /// the live transform), so duplicate and load round-trip with no compounding.
    /// </summary>
    public class FireProp : MonoBehaviour
    {
        [Tooltip("Size / burn multiplier. 1 = the prefab's authored size.")]
        [SerializeField] private float _intensity = 1f;

        [Tooltip("How much the emission rate scales relative to size. " +
                 "1 = emission tracks size 1:1.")]
        [SerializeField] private float _emissionPerIntensity = 1f;

        [Header("Optional point light")]
        [SerializeField] private Light _light;
        [Tooltip("Light intensity per unit of fire intensity.")]
        [SerializeField] private float _lightPerIntensity = 2f;

        private ParticleSystem[] _systems;
        private bool _cached;

        public float Intensity => _intensity;

        private void Awake()
        {
            Cache();
            Apply(); // match whatever value we were spawned / loaded with
        }

        private void Cache()
        {
            if (_cached) return;
            _systems = GetComponentsInChildren<ParticleSystem>(true);
            _cached = true;
        }

        /// <summary>Set the intensity multiplier and update visuals immediately.</summary>
        public void SetIntensity(float value)
        {
            _intensity = Mathf.Max(0f, value);
            Apply();
        }

        private void Apply()
        {
            if (!_cached) Cache();

            // Size — direct. Requires each system's Main -> Scaling Mode to be
            // Hierarchy or Local (NOT Shape) so the flames resize with the root.
            transform.localScale = Vector3.one * _intensity;

            // Burn — absolute multiplier, so no compounding on duplicate/load.
            float emiMul = _intensity * _emissionPerIntensity;
            if (_systems != null)
            {
                foreach (var ps in _systems)
                {
                    if (ps == null) continue;
                    var emission = ps.emission;
                    emission.rateOverTimeMultiplier = emiMul;
                }
            }

            if (_light != null)
                _light.intensity = _intensity * _lightPerIntensity;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying && _cached) Apply();
        }
#endif
    }

    /// <summary>
    /// Undoable intensity change. Push on slider RELEASE so one drag = one undo.
    /// NOTE: if you already added a FireIntensityAction elsewhere, delete one copy.
    /// </summary>
    public class FireIntensityAction : IEditAction
    {
        private readonly FireProp _fire;
        private readonly float _from, _to;

        public FireIntensityAction(FireProp fire, float from, float to)
        {
            _fire = fire; _from = from; _to = to;
        }

        public void Undo() { if (_fire) _fire.SetIntensity(_from); }
        public void Redo() { if (_fire) _fire.SetIntensity(_to); }
        public void Discard() { }
    }
}