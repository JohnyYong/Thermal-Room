using System;
using UnityEngine;

namespace ScenarioEditor
{

    public class SelectionManager : MonoBehaviour
    {
        public static SelectionManager Instance { get; private set; }

        [Header("Raycast Settings")]
        [Tooltip("Layers that CAN be selected. Exclude your walls/floors layer here.")]
        [SerializeField] private LayerMask _selectableMask = ~0;

        [Tooltip("Max ray distance for picking.")]
        [SerializeField] private float _maxRayDistance = 1000f;

        [Tooltip("Tag for objects that must never be selectable. Leave blank to disable.")]
        [SerializeField] private string _unselectableTag = "Unselectable";

        [Header("Behaviour")]
        [Tooltip("If true, clicking empty space clears the current selection.")]
        [SerializeField] private bool _clickEmptyToDeselect = true;

        public event Action<GameObject> OnSelected;
        public event Action OnDeselected;

        public GameObject CurrentSelection { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[SelectionManager] Duplicate instance on '{name}'. Destroying extra.", this);
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            if (EditorViewInput.Instance != null)
                EditorViewInput.Instance.ViewPointerDown += HandleViewPointerDown;
        }

        private void Start()
        {
            if (EditorViewInput.Instance != null)
            {
                EditorViewInput.Instance.ViewPointerDown -= HandleViewPointerDown; // avoid double-subscribe
                EditorViewInput.Instance.ViewPointerDown += HandleViewPointerDown;
            }
            else
            {
                Debug.LogWarning("[SelectionManager] No EditorViewInput found. Clicks won't select anything.", this);
            }
        }

        private void OnDisable()
        {
            if (EditorViewInput.Instance != null)
                EditorViewInput.Instance.ViewPointerDown -= HandleViewPointerDown;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private bool SelectionAllowedInCurrentMode()
        {
            var mgr = EditorToolManager.Instance;
            if (mgr == null) return true; 

            switch (mgr.CurrentMode)
            {
                case EditorToolManager.ToolMode.Select:
                case EditorToolManager.ToolMode.Move:
                case EditorToolManager.ToolMode.Rotate:
                case EditorToolManager.ToolMode.Delete:
                case EditorToolManager.ToolMode.Duplicate:
                    return true;
                default:
                    return false; 
            }
        }

        private void HandleViewPointerDown(EditorViewInput.ViewPointer p)
        {
            if (!p.valid || !SelectionAllowedInCurrentMode())
                return;

            PickFromRay(p.ray);
        }

        public void PickFromRay(Ray ray)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, _maxRayDistance, _selectableMask, QueryTriggerInteraction.Ignore))
            {
                Debug.Log($"[SelectionManager] Ray HIT: {hit.collider.gameObject.name} (layer {hit.collider.gameObject.layer})");
                GameObject picked = ResolveSelectableRoot(hit.collider.gameObject);

                if (picked == null || IsUnselectable(picked))
                {
                    if (_clickEmptyToDeselect) Deselect();
                    return;
                }
                Select(picked);
            }
            else if (_clickEmptyToDeselect)
            {
                Debug.Log("[SelectionManager] Ray hit NOTHING (check colliders / layer mask / max distance)");
                Deselect();
            }
        }

        public void Select(GameObject target)
        {
            if (target == null) { Deselect(); return; }
            if (target == CurrentSelection)
            {
                Debug.Log($"[SelectionManager] {target.name} already selected — event NOT fired");
                return;
            }

            CurrentSelection = target;
            Debug.Log($"[SelectionManager] Firing OnSelected for {target.name}. Listeners: {(OnSelected?.GetInvocationList().Length ?? 0)}");
            OnSelected?.Invoke(target);
        }

        public void Deselect()
        {
            if (CurrentSelection == null) return;
            CurrentSelection = null;
            OnDeselected?.Invoke();
        }

        private GameObject ResolveSelectableRoot(GameObject hitObject)
        {
            var marker = hitObject.GetComponentInParent<SelectableProp>();
            if (marker != null)
                return marker.gameObject;
            return hitObject;
        }

        private bool IsUnselectable(GameObject go)
        {
            return !string.IsNullOrEmpty(_unselectableTag) && go.CompareTag(_unselectableTag);
        }
    }
}
