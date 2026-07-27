using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ScenarioEditor
{
    /// <summary>
    /// Attach to the Fire Properties panel GameObject.
    ///
    /// The slider's range is whatever YOU type into _minIntensity / _maxIntensity
    /// below — the slider value IS the fire's size/burn multiplier, fed straight
    /// to FireProp.SetIntensity. No hidden remapping.
    ///
    /// Two jobs from one slider:
    ///   - PlaceFire mode, nothing selected -> slider sets the DEFAULT value new
    ///     fires spawn with (PlaceTool reads it via ApplyDefaultTo). No undo.
    ///   - A fire is selected -> slider edits THAT fire live, and pushes one
    ///     undoable FireIntensityAction on release.
    ///
    /// Assign _panelRoot to a CHILD, not this GameObject, or the script stops
    /// receiving mode/selection events once hidden.
    /// </summary>
    public class FirePropertiesPanel : MonoBehaviour
    {
        public static FirePropertiesPanel Instance { get; private set; }

        [Header("References")]
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private Slider _intensitySlider;
        [Tooltip("Optional label, e.g. shows '1.8'.")]
        [SerializeField] private TMP_Text _valueLabel;

        [Header("Range (you control this)")]
        [Tooltip("Smallest size/burn multiplier the slider allows.")]
        [SerializeField] private float _minIntensity = 0.5f;
        [Tooltip("Largest size/burn multiplier the slider allows.")]
        [SerializeField] private float _maxIntensity = 3f;
        [Tooltip("Value new fires get when nothing is selected.")]
        [SerializeField] private float _defaultIntensity = 1f;

        // ── runtime state ──────────────────────────────────────────────────
        private FireProp _target;       // non-null when editing a selected fire
        private bool _suppressCallback; // true while we set the slider in code
        private bool _editing;          // true between slider PointerDown/Up
        private float _editStartValue;

        // ── lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance == null) Instance = this;

            if (_panelRoot == gameObject) _panelRoot = null;

            _defaultIntensity = Mathf.Clamp(_defaultIntensity, _minIntensity, _maxIntensity);

            if (_intensitySlider != null)
            {
                _intensitySlider.minValue = _minIntensity;
                _intensitySlider.maxValue = _maxIntensity;
                _intensitySlider.wholeNumbers = false;
            }

            WireSlider();
            SetPanelVisible(false);
            // Don't subscribe here — managers may not exist yet.
        }

        private void OnEnable() => Subscribe();
        private void Start() => Subscribe();
        private void OnDisable() => Unsubscribe();
        private void OnDestroy() { if (Instance == this) Instance = null; }

        // ── visibility (same trick as VictimSelectorPanel) ──────────────────

        private void SetPanelVisible(bool visible)
        {
            if (_panelRoot != null && _panelRoot != gameObject)
                _panelRoot.SetActive(visible);
            else
                foreach (Transform child in transform)
                    child.gameObject.SetActive(visible);
        }

        // ── subscriptions ───────────────────────────────────────────────────

        private void Subscribe()
        {
            var mgr = EditorToolManager.Instance;
            if (mgr == null)
            {
                Debug.Log("[FirePropertiesPanel] EditorToolManager.Instance is null at " +
                                 "subscribe time — will retry on next enable/start.", this);
            }
            else
            {
                mgr.OnModeChanged -= HandleModeChanged;
                mgr.OnModeChanged += HandleModeChanged;
            }

            var sel = SelectionManager.Instance;
            if (sel != null)
            {
                sel.OnSelected -= HandleSelected;
                sel.OnSelected += HandleSelected;
                sel.OnDeselected -= HandleDeselected;
                sel.OnDeselected += HandleDeselected;
            }

            var hist = EditHistory.Instance;
            if (hist != null)
            {
                hist.OnHistoryChanged -= HandleHistoryChanged;
                hist.OnHistoryChanged += HandleHistoryChanged;
            }

            RefreshTargetAndVisibility();
        }

        private void Unsubscribe()
        {
            if (EditorToolManager.Instance != null)
                EditorToolManager.Instance.OnModeChanged -= HandleModeChanged;

            if (SelectionManager.Instance != null)
            {
                SelectionManager.Instance.OnSelected -= HandleSelected;
                SelectionManager.Instance.OnDeselected -= HandleDeselected;
            }

            if (EditHistory.Instance != null)
                EditHistory.Instance.OnHistoryChanged -= HandleHistoryChanged;
        }

        // ── event handlers ──────────────────────────────────────────────────

        private void HandleModeChanged(EditorToolManager.ToolMode mode) => RefreshTargetAndVisibility();
        private void HandleSelected(GameObject go) => RefreshTargetAndVisibility();
        private void HandleDeselected() => RefreshTargetAndVisibility();

        // Keep the slider in sync if undo/redo changed the selected fire.
        private void HandleHistoryChanged()
        {
            if (_target != null) SetSliderSilently(_target.Intensity);
        }

        private void RefreshTargetAndVisibility()
        {
            var selGo = SelectionManager.Instance != null
                ? SelectionManager.Instance.CurrentSelection : null;
            FireProp selFire = selGo != null ? selGo.GetComponent<FireProp>() : null;

            bool placeFire = EditorToolManager.Instance != null &&
                             EditorToolManager.Instance.CurrentMode ==
                             EditorToolManager.ToolMode.PlaceFire;

            if (selFire != null)
            {
                _target = selFire;
                SetPanelVisible(true);
                SetSliderSilently(selFire.Intensity);
            }
            else if (placeFire)
            {
                _target = null;
                SetPanelVisible(true);
                SetSliderSilently(_defaultIntensity);
            }
            else
            {
                _target = null;
                SetPanelVisible(false);
            }
        }

        // ── slider wiring ───────────────────────────────────────────────────

        private void WireSlider()
        {
            if (_intensitySlider == null)
            {
                Debug.LogWarning("[FirePropertiesPanel] No slider assigned.", this);
                return;
            }

            _intensitySlider.onValueChanged.RemoveListener(OnSliderChanged);
            _intensitySlider.onValueChanged.AddListener(OnSliderChanged);

            var trigger = _intensitySlider.GetComponent<EventTrigger>();
            if (trigger == null) trigger = _intensitySlider.gameObject.AddComponent<EventTrigger>();
            trigger.triggers.Clear();

            var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            down.callback.AddListener(_ => BeginEdit());
            trigger.triggers.Add(down);

            var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            up.callback.AddListener(_ => EndEdit());
            trigger.triggers.Add(up);
        }

        private void OnSliderChanged(float value)
        {
            if (_suppressCallback) return;

            if (_target != null)
                _target.SetIntensity(value);   // live edit of the selected fire
            else
                _defaultIntensity = value;      // adjust the placement default

            UpdateLabel(value);
        }

        private void BeginEdit()
        {
            _editing = true;
            _editStartValue = _target != null ? _target.Intensity : _defaultIntensity;
        }

        private void EndEdit()
        {
            if (_editing && _target != null)
            {
                float now = _target.Intensity;
                if (!Mathf.Approximately(now, _editStartValue) && EditHistory.Instance != null)
                    EditHistory.Instance.Push(new FireIntensityAction(_target, _editStartValue, now));
            }
            _editing = false;
        }

        private void SetSliderSilently(float value)
        {
            if (_intensitySlider == null) return;
            _suppressCallback = true;
            _intensitySlider.value = value;
            _suppressCallback = false;
            UpdateLabel(value);
        }

        private void UpdateLabel(float value)
        {
            if (_valueLabel != null)
                _valueLabel.text = value.ToString("0.0");
        }

        // ── placement hook ──────────────────────────────────────────────────

        /// <summary>
        /// Call from PlaceTool right after spawning, so a new fire inherits the
        /// slider's current default. No-op for non-fire objects (no FireProp).
        ///     FirePropertiesPanel.Instance?.ApplyDefaultTo(placed);
        /// </summary>
        public void ApplyDefaultTo(GameObject obj)
        {
            if (obj == null) return;
            var fp = obj.GetComponent<FireProp>();
            if (fp != null)
                fp.SetIntensity(Mathf.Clamp(_defaultIntensity, _minIntensity, _maxIntensity));
        }
    }
}