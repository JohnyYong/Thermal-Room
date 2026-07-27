using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScenarioEditor
{
    public class PlaceTool : MonoBehaviour
    {
        [System.Serializable]
        public class ModeConfig
        {
            public EditorToolManager.ToolMode mode;
            public SelectableProp.PropKind propKind;
            public List<GameObject> prefabs = new List<GameObject>();
        }

        [Header("Mode Configurations")]
        [Tooltip("One entry per placement mode (Fire, Victim, etc).")]
        [SerializeField] private List<ModeConfig> _configs = new List<ModeConfig>();

        [Header("Placement Surface")]
        [SerializeField] private LayerMask _placementMask = ~0;
        [SerializeField] private float _maxRayDistance = 1000f;

        [Tooltip("Keep the placed object upright (recommended for fire and victims).")]
        [SerializeField] private bool _keepUpright = true;

        [Tooltip("Max angle from horizontal a surface can be to count as a floor (degrees).")]
        [SerializeField] private float _maxFloorAngle = 45f;

        [Tooltip("How far down to search for a floor when clicking a wall/ceiling.")]
        [SerializeField] private float _floorSnapRayHeight = 50f;

        [Header("Disc Marker")]
        [SerializeField] private Color _validDiscColor = new Color(1f, 0.5f, 0.05f, 0.55f);
        [SerializeField] private Color _invalidDiscColor = new Color(1f, 0.1f, 0.1f, 0.45f);
        [SerializeField] private float _discRadius = 0.5f;
        [SerializeField] private float _discLift = 0.02f;
        [SerializeField] private int _discSegments = 48;

        [Header("Placed Object Fallback Collider")]
        [SerializeField] private Vector3 _fallbackColliderSize = new Vector3(0.5f, 1f, 0.5f);

        // --- state ----------------------------------------------------------

        private bool _isActive;
        private int _activeIndex;
        private ModeConfig _activeConfig;
        private GameObject _disc;
        private Material _discMat;
        private Mesh _discMesh;

        private bool _hasValidPoint;
        private bool _hasFloorBelow;
        private Vector3 _cursorPoint;
        private Vector3 _floorPoint;
        private Quaternion _placeRot = Quaternion.identity;

        public GameObject ActivePrefab
        {
            get
            {
                if (_activeConfig == null) return null;
                var list = _activeConfig.prefabs;
                return (_activeIndex >= 0 && _activeIndex < list.Count) ? list[_activeIndex] : null;
            }
        }

        // --- lifecycle ------------------------------------------------------

        private void Awake() { _activeIndex = 0; }

        private void OnEnable() { SubscribeMode(); SubscribeInput(); }
        private void Start() { SubscribeMode(); SubscribeInput(); }

        private void OnDisable()
        {
            if (EditorToolManager.Instance != null)
                EditorToolManager.Instance.OnModeChanged -= HandleModeChanged;
            if (EditorViewInput.Instance != null)
                EditorViewInput.Instance.ViewPointerDown -= HandlePointerDown;
            DestroyDisc();
        }

        private void OnDestroy() => DestroyDisc();

        private void SubscribeMode()
        {
            var mgr = EditorToolManager.Instance;
            if (mgr == null) return;
            mgr.OnModeChanged -= HandleModeChanged;
            mgr.OnModeChanged += HandleModeChanged;
            HandleModeChanged(mgr.CurrentMode);
        }

        private void SubscribeInput()
        {
            var input = EditorViewInput.Instance;
            if (input == null) return;
            input.ViewPointerDown -= HandlePointerDown;
            input.ViewPointerDown += HandlePointerDown;
        }

        private void HandleModeChanged(EditorToolManager.ToolMode mode)
        {
            ModeConfig config = GetConfig(mode);
            bool shouldBeActive = config != null && config.prefabs.Count > 0;

            if (shouldBeActive)
            {
                _activeConfig = config;
                _activeIndex = 0;
            }

            SetActive(shouldBeActive);
        }

        private void SetActive(bool active)
        {
            if (active == _isActive) return;
            _isActive = active;
            if (_isActive) CreateDisc();
            else DestroyDisc();
        }

        // --- public API -----------------------------------------------------

        public void SetActiveIndex(int index)
        {
            if (_activeConfig == null)
            {
                Debug.LogWarning("[PlaceTool] SetActiveIndex called but no active config.", this);
                return;
            }
            var list = _activeConfig.prefabs;
            if (index < 0 || index >= list.Count)
            {
                Debug.LogWarning($"[PlaceTool] SetActiveIndex {index} out of range (0..{list.Count - 1}).", this);
                return;
            }
            _activeIndex = index;
            Debug.Log($"[PlaceTool] Active prefab → {list[index].name}");
        }

        public void SetPrefabs(List<GameObject> prefabs, EditorToolManager.ToolMode forMode)
        {
            ModeConfig config = GetConfig(forMode);
            if (config == null)
            {
                config = new ModeConfig { mode = forMode };
                _configs.Add(config);
                Debug.Log($"[PlaceTool] SetPrefabs — created new config for {forMode}.");
            }

            config.prefabs = new List<GameObject>(prefabs);

            var mgr = EditorToolManager.Instance;
            if (mgr != null && mgr.CurrentMode == forMode)
            {
                _activeConfig = config;
                _activeIndex = 0;
                if (!_isActive) SetActive(true);
            }

            Debug.Log($"[PlaceTool] SetPrefabs — {config.prefabs.Count} prefab(s) set for {forMode}.");
        }

        // --- helpers --------------------------------------------------------

        private ModeConfig GetConfig(EditorToolManager.ToolMode mode)
        {
            foreach (var c in _configs)
                if (c.mode == mode) return c;
            return null;
        }

        /// <summary>True if the normal points mostly upward (a floor we can stand on).</summary>
        private bool IsFloorSurface(Vector3 normal) =>
            Vector3.Angle(normal, Vector3.up) <= _maxFloorAngle;

        /// <summary>True if the normal points mostly downward (a ceiling).</summary>
        private bool IsCeilingSurface(Vector3 normal) =>
            Vector3.Angle(normal, Vector3.down) <= _maxFloorAngle;

        /// <summary>
        /// Drops straight down from startPos and returns the first FLOOR surface
        /// found. Ceilings and walls are skipped. The origin is nudged slightly
        /// downward so we never re-hit the surface we started on (e.g. a ceiling).
        /// </summary>
        private bool TryFindFloorBelow(Vector3 startPos, out Vector3 floorPos)
        {
            Vector3 rayOrigin = startPos + Vector3.down * 0.05f;

            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down,
                _floorSnapRayHeight + 200f, _placementMask, QueryTriggerInteraction.Ignore);

            // Top-to-bottom: the first floor we meet going down is the room floor.
            System.Array.Sort(hits, (a, b) => b.point.y.CompareTo(a.point.y));

            foreach (var hit in hits)
            {
                if (IsCeilingSurface(hit.normal)) continue; // skip ceilings
                if (IsFloorSurface(hit.normal))
                {
                    floorPos = hit.point;
                    return true;
                }
                // walls are skipped; keep searching downward
            }

            floorPos = Vector3.zero;
            return false;
        }

        // --- update ---------------------------------------------------------

        private void Update()
        {
            if (!_isActive || _disc == null) return;

            var input = EditorViewInput.Instance;
            if (input == null) { HideDisc(); return; }

            if (!input.TryGetHoverRay(out Ray ray, out _))
            {
                HideDisc();
                return;
            }

            if (!Physics.Raycast(ray, out RaycastHit hit, _maxRayDistance,
                    _placementMask, QueryTriggerInteraction.Ignore))
            {
                HideDisc();
                return;
            }

            _cursorPoint = hit.point;
            _placeRot = _keepUpright ? Quaternion.identity
                                        : Quaternion.FromToRotation(Vector3.up, hit.normal);

            bool cursorOnFloor = IsFloorSurface(hit.normal);

            if (cursorOnFloor)
            {
                // Clicked directly on a floor — use it as-is.
                _floorPoint = hit.point;
                _hasFloorBelow = true;
            }
            else
            {
                // Clicked a wall or ceiling — drop straight down from the hit
                // point to find the room's floor beneath it.
                _hasFloorBelow = TryFindFloorBelow(hit.point, out _floorPoint);
            }

            bool canPlace = _hasFloorBelow;

            Vector3 discPos = _hasFloorBelow ? _floorPoint : hit.point;
            Quaternion discRot = _hasFloorBelow
                ? Quaternion.identity
                : Quaternion.FromToRotation(Vector3.up, hit.normal);

            _disc.transform.SetPositionAndRotation(discPos + Vector3.up * _discLift, discRot);
            SetDiscColor(canPlace ? _validDiscColor : _invalidDiscColor);
            ShowDisc();

            _hasValidPoint = canPlace;
        }

        private void HandlePointerDown(EditorViewInput.ViewPointer p)
        {
            if (!_isActive || !_hasValidPoint) return;

            GameObject prefab = ActivePrefab;
            if (prefab == null)
            {
                Debug.LogWarning("[PlaceTool] No active prefab to place.", this);
                return;
            }

            // _floorPoint is the resolved floor position from Update.
            GameObject placed = Instantiate(prefab, _floorPoint, _placeRot);
            placed.name = prefab.name;
            EnsureSelectable(placed);
            FirePropertiesPanel.Instance?.ApplyDefaultTo(placed);
            EditHistory.Instance?.Push(new SpawnAction(placed));

            //if (EditorToolManager.Instance != null)
            //    EditorToolManager.Instance.SetMode(EditorToolManager.ToolMode.Select);

            //if (SelectionManager.Instance != null)
            //    SelectionManager.Instance.Select(placed);
        }

        // --- disc lifecycle -------------------------------------------------

        private void CreateDisc()
        {
            DestroyDisc();

            _disc = new GameObject("~PlaceDisc");
            _disc.hideFlags = HideFlags.HideAndDontSave;

            _discMesh = BuildDiscMesh(_discRadius, _discSegments);
            _disc.AddComponent<MeshFilter>().sharedMesh = _discMesh;

            _discMat = CreateDiscMaterial(_validDiscColor);
            var mr = _disc.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _discMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

            HideDisc();
        }

        private void DestroyDisc()
        {
            if (_disc != null) { if (Application.isPlaying) Destroy(_disc); else DestroyImmediate(_disc); _disc = null; }
            if (_discMat != null) { if (Application.isPlaying) Destroy(_discMat); else DestroyImmediate(_discMat); _discMat = null; }
            if (_discMesh != null) { if (Application.isPlaying) Destroy(_discMesh); else DestroyImmediate(_discMesh); _discMesh = null; }
            _hasValidPoint = false;
        }

        private void ShowDisc() { if (_disc != null && !_disc.activeSelf) _disc.SetActive(true); }
        private void HideDisc()
        {
            _hasValidPoint = false;
            if (_disc != null && _disc.activeSelf) _disc.SetActive(false);
        }

        private void SetDiscColor(Color c)
        {
            if (_discMat == null) return;
            if (_discMat.HasProperty("_BaseColor")) _discMat.SetColor("_BaseColor", c);
            if (_discMat.HasProperty("_Color")) _discMat.SetColor("_Color", c);
        }

        // --- placed object setup -------------------------------------------

        private void EnsureSelectable(GameObject placed)
        {
            SelectableProp.PropKind kind = _activeConfig?.propKind ?? SelectableProp.PropKind.Fire;

            var prop = placed.GetComponent<SelectableProp>();
            if (prop == null) prop = placed.AddComponent<SelectableProp>();
            prop.kind = kind;

            if (placed.GetComponentInChildren<Collider>(true) == null)
            {
                var box = placed.AddComponent<BoxCollider>();
                box.size = _fallbackColliderSize;
                box.center = new Vector3(0f, _fallbackColliderSize.y * 0.5f, 0f);
            }
        }

        // --- disc geometry / material --------------------------------------

        private Mesh BuildDiscMesh(float r, int segments)
        {
            segments = Mathf.Max(3, segments);
            var verts = new Vector3[segments + 1];
            var normals = new Vector3[segments + 1];
            var tris = new int[segments * 3];

            verts[0] = Vector3.zero; normals[0] = Vector3.up;
            for (int i = 0; i < segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                normals[i + 1] = Vector3.up;
            }
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                tris[i * 3] = 0;
                tris[i * 3 + 1] = next + 1;
                tris[i * 3 + 2] = i + 1;
            }

            var mesh = new Mesh { name = "PlaceDiscMesh" };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        private Material CreateDiscMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Transparent")
                         ?? Shader.Find("Sprites/Default");

            var mat = new Material(shader) { name = "PlaceDisc (runtime)" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            return mat;
        }
    }
}