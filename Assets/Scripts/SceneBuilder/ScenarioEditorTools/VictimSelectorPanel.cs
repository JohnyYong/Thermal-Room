using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ScenarioEditor
{
    /// <summary>
    /// Attach to the Victim Properties panel GameObject.
    /// Builds buttons entirely in code from a consumer-assigned list — no prefab
    /// discovery, no AssetPreview, no #if UNITY_EDITOR. Works identically in the
    /// editor and in a build, and is safe inside a runtime assembly (.asmdef).
    ///
    /// You assign each entry's prefab AND its icon sprite in the Inspector. The
    /// icon is a real asset, so it ships in the build like any other sprite.
    ///
    /// Hierarchy expected:
    ///   VictimSelectorPanel (this script)
    ///     └── VictimScrollHorizontal (ScrollRect, Mask)
    ///           └── Viewport
    ///                 └── Content  ← assign to _buttonContainer
    /// </summary>
    public class VictimSelectorPanel : MonoBehaviour
    {
        [System.Serializable]
        public class VictimEntry
        {
            [Tooltip("The prefab placed when this entry is selected.")]
            public GameObject prefab;

            [Tooltip("Thumbnail shown on the button. A real sprite asset — renders in editor AND build.")]
            public Sprite icon;
        }

        [Header("References")]
        [Tooltip("The single unified PlaceTool.")]
        [SerializeField] private PlaceTool _placeTool;

        [Tooltip("The panel object to show/hide. If null, falls back to a child or this GameObject. " +
                 "IMPORTANT: this should NOT be the same GameObject the script is on, or the script " +
                 "stops receiving mode-change events once hidden.")]
        [SerializeField] private GameObject _panelRoot;

        [Tooltip("Content transform inside the ScrollView (has HorizontalLayoutGroup).")]
        [SerializeField] private Transform _buttonContainer;

        [Header("Victims (assign prefab + icon for each)")]
        [SerializeField] private List<VictimEntry> _entries = new();

        [Tooltip("Fallback sprite when an entry has no icon assigned.")]
        [SerializeField] private Sprite _fallbackSprite;

        [Header("Button Size")]
        [SerializeField] private float _buttonWidth = 80f;
        [SerializeField] private float _buttonHeight = 100f;

        [Header("Selection Visuals")]
        [SerializeField] private Color _normalColor = new Color(0.18f, 0.18f, 0.22f, 1f);
        [SerializeField] private Color _selectedColor = new Color(0.30f, 0.55f, 1.00f, 1f);
        [SerializeField] private Color _hoverColor = new Color(0.25f, 0.30f, 0.38f, 1f);

        // ── runtime state ──────────────────────────────────────────────────

        private readonly List<GameObject> _prefabs = new();
        private readonly List<Button> _buttons = new();
        private int _selectedIndex = -1;

        // ── lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            if (_panelRoot == null || _panelRoot == gameObject)
                _panelRoot = null;

            EnsureContentLayout();
            Populate();
            SetPanelVisible(false);
            // Don't subscribe here — EditorToolManager.Instance may not exist yet.
        }

        // Subscribe in both OnEnable and Start so we catch the manager regardless
        // of script execution order or when EditorUIs gets activated by SceneMode.
        private void OnEnable() => SubscribeToMode();
        private void Start() => SubscribeToMode();
        private void OnDisable() => UnsubscribeFromMode();

        /// <summary>
        /// Shows/hides the panel without ever disabling this script's own
        /// GameObject. If a separate _panelRoot is assigned, toggles that;
        /// otherwise toggles all child objects, leaving this GameObject active
        /// so it keeps receiving mode-change events.
        /// </summary>
        private void SetPanelVisible(bool visible)
        {
            if (_panelRoot != null && _panelRoot != gameObject)
            {
                _panelRoot.SetActive(visible);
            }
            else
            {
                foreach (Transform child in transform)
                    child.gameObject.SetActive(visible);
            }
        }

        private void SubscribeToMode()
        {
            var mgr = EditorToolManager.Instance;
            if (mgr == null)
            {
                Debug.LogWarning("[VictimSelectorPanel] EditorToolManager.Instance is null at subscribe " +
                                 "time — will retry on next enable/start.", this);
                return;
            }
            mgr.OnModeChanged -= HandleModeChanged;
            mgr.OnModeChanged += HandleModeChanged;
            HandleModeChanged(mgr.CurrentMode);
        }

        private void UnsubscribeFromMode()
        {
            if (EditorToolManager.Instance != null)
                EditorToolManager.Instance.OnModeChanged -= HandleModeChanged;
        }

        private void HandleModeChanged(EditorToolManager.ToolMode mode)
        {
            bool show = mode == EditorToolManager.ToolMode.PlaceVictim;

            Debug.Log($"[VictimSelectorPanel] HandleModeChanged({mode}) show={show}, " +
                      $"prefabs={_prefabs.Count}", this);

            SetPanelVisible(show);

            if (show && _prefabs.Count > 0)
            {
                int i = _selectedIndex >= 0 ? _selectedIndex : 0;
                SelectVictim(i);
            }
        }

        // ── layout setup ───────────────────────────────────────────────────

        /// Ensures the Content object has a HorizontalLayoutGroup and
        /// ContentSizeFitter so it expands as buttons are added.
        private void EnsureContentLayout()
        {
            if (_buttonContainer == null) return;

            var hlg = _buttonContainer.GetComponent<HorizontalLayoutGroup>();
            if (hlg == null) hlg = _buttonContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.padding = new RectOffset(6, 6, 4, 4);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;

            var csf = _buttonContainer.GetComponent<ContentSizeFitter>();
            if (csf == null) csf = _buttonContainer.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        }

        // ── population (from the serialized list, no discovery) ─────────────

        private void Populate()
        {
            _prefabs.Clear();

            foreach (var btn in _buttons)
                if (btn != null) Destroy(btn.gameObject);
            _buttons.Clear();
            _selectedIndex = -1;

            // Collect valid prefabs (skip empty/incomplete entries).
            foreach (var entry in _entries)
            {
                if (entry == null || entry.prefab == null)
                {
                    Debug.LogWarning("[VictimSelectorPanel] An entry has no prefab assigned — skipping.", this);
                    continue;
                }
                _prefabs.Add(entry.prefab);
            }

            if (_prefabs.Count == 0)
            {
                Debug.LogWarning("[VictimSelectorPanel] No victim entries assigned in the Inspector.", this);
                return;
            }

            if (_placeTool != null)
                _placeTool.SetPrefabs(_prefabs, EditorToolManager.ToolMode.PlaceVictim);

            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i] == null || _entries[i].prefab == null) continue;
                CreateButton(i, _entries[i]);
            }
        }

        // ── button creation (fully procedural) ────────────────────────────

        private void CreateButton(int index, VictimEntry entry)
        {
            if (_buttonContainer == null) return;

            // ── Root button GO ──────────────────────────────────────────────
            var go = new GameObject($"VictimBtn_{entry.prefab.name}");
            go.transform.SetParent(_buttonContainer, false);

            var rootRect = go.AddComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(_buttonWidth, _buttonHeight);

            var rootImg = go.AddComponent<Image>();
            rootImg.color = _normalColor;

            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = rootImg;

            // ── Preview image fills the entire button with a small inset ────
            var previewGo = new GameObject("Preview");
            previewGo.transform.SetParent(go.transform, false);

            var previewRect = previewGo.AddComponent<RectTransform>();
            previewRect.anchorMin = Vector2.zero;
            previewRect.anchorMax = Vector2.one;
            previewRect.offsetMin = new Vector2(4, 4);
            previewRect.offsetMax = new Vector2(-4, -4);

            var previewImg = previewGo.AddComponent<Image>();
            previewImg.preserveAspect = true;
            previewImg.color = Color.white;
            previewImg.sprite = entry.icon != null ? entry.icon : _fallbackSprite;

            // ── Wire up click & hover ───────────────────────────────────────
            int capturedIndex = index;
            btn.onClick.AddListener(() => SelectVictim(capturedIndex));
            AddHoverHighlight(go, index);

            _buttons.Add(btn);
        }

        // ── selection ──────────────────────────────────────────────────────

        private void SelectVictim(int index)
        {
            if (_selectedIndex >= 0 && _selectedIndex < _buttons.Count)
                SetButtonColor(_buttons[_selectedIndex].gameObject, _normalColor);

            _selectedIndex = index;

            if (index >= 0 && index < _buttons.Count)
                SetButtonColor(_buttons[index].gameObject, _selectedColor);

            if (_placeTool != null)
                _placeTool.SetActiveIndex(index);
        }

        // ── helpers ────────────────────────────────────────────────────────

        private void SetButtonColor(GameObject go, Color color)
        {
            var bg = go.GetComponent<Image>();
            if (bg != null) bg.color = color;
        }

        private void AddHoverHighlight(GameObject go, int index)
        {
            var trigger = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();

            var enter = new UnityEngine.EventSystems.EventTrigger.Entry
            { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                if (_selectedIndex != index) SetButtonColor(go, _hoverColor);
            });

            var exit = new UnityEngine.EventSystems.EventTrigger.Entry
            { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
            exit.callback.AddListener(_ =>
            {
                if (_selectedIndex != index) SetButtonColor(go, _normalColor);
            });

            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
        }

        public void Refresh() => Populate();
    }
}