using System;
using TMPro;
using UnityEngine;

namespace ScenarioEditor
{
    /// <summary>
    /// Central state machine for the scenario editor tools.
    /// Every other tool component listens to OnModeChanged and decides
    /// whether it should be active. This keeps tools decoupled: no tool
    /// needs to know about any other tool, only about the current mode.
    /// </summary>
    public class EditorToolManager : MonoBehaviour
    {
        public enum ToolMode
        {
            None,
            Select,
            Move,
            Rotate,
            PlaceFire,
            PlaceVictim,
            Delete,
            Duplicate
        }

        public static EditorToolManager Instance { get; private set; }

        [Header("Starting Mode")]
        [SerializeField] private ToolMode _startMode = ToolMode.Select;

        /// <summary>Fired whenever the active tool changes. Payload = new mode.</summary>
        public event Action<ToolMode> OnModeChanged;

        [SerializeField] private TextMeshProUGUI _victimCount;
        [SerializeField] private TextMeshProUGUI _fireCount;

        public ToolMode CurrentMode { get; private set; } = ToolMode.None;

        private void Awake()
        {
            // Robust singleton: warn instead of silently allowing duplicates.
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(
                    $"[EditorToolManager] Duplicate instance on '{name}'. Destroying the extra one.",
                    this);
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            // Apply the starting mode after all listeners have subscribed in their own Awake/OnEnable.
            SetMode(_startMode);
        }

        private void Update()
        {
            //_fireCount.text = GameObject.FindSceneObjectsOfType(typeof(FireProp)).Length.ToString();
            _fireCount.text = GameObject.FindObjectsByType(typeof(FireProp), FindObjectsSortMode.None).Length.ToString();
            _victimCount.text = GameObject.FindGameObjectsWithTag("Victim").Length.ToString();

        }
        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Switch the active tool. Safe to call with the same mode (no-op).
        /// </summary>
        public void SetMode(ToolMode mode)
        {
            if (mode == CurrentMode)
            {
                Debug.Log($"[ToolManager] SetMode({mode}) ignored — already in this mode.");
                return;
            }

            Debug.Log($"[ToolManager] Mode changing: {CurrentMode} -> {mode}");
            CurrentMode = mode;
            OnModeChanged?.Invoke(mode);
        }


        public void SelectMode() { Debug.Log("[ToolManager] SelectMode pressed"); SetMode(ToolMode.Select); }
        public void MoveMode() { Debug.Log("[ToolManager] MoveMode pressed"); SetMode(ToolMode.Move); }
        public void RotateMode() { Debug.Log("[ToolManager] RotateMode pressed"); SetMode(ToolMode.Rotate); }
        public void PlaceFireMode() { Debug.Log("[ToolManager] PlaceFireMode pressed"); SetMode(ToolMode.PlaceFire); }
        public void PlaceVictimMode() { Debug.Log("[ToolManager] PlaceVictimMode pressed"); SetMode(ToolMode.PlaceVictim); }
        public void DeleteMode() { Debug.Log("[ToolManager] DeleteMode pressed"); SetMode(ToolMode.Delete); }
        public void DuplicateMode() { Debug.Log("[ToolManager] DuplicateMode pressed"); SetMode(ToolMode.Duplicate); }
    }
}
