using UnityEngine;

namespace ScenarioEditor
{
    public class ObjectMover : MonoBehaviour
    {
        [Header("Grab")]
        [SerializeField] private LayerMask _grabMask = ~0;
        [SerializeField] private float _maxRayDistance = 1000f;

        [Header("Drag Plane Fallback")]
        [SerializeField] private bool _useObjectYAsPlane = true;
        [SerializeField] private float _groundY = 0f;

        [Header("Snapping (optional)")]
        [SerializeField] private bool _snapToGrid = false;
        [SerializeField] private float _gridSize = 0.25f;

        private bool _isActive;
        private bool _isDragging;
        private Transform _target;
        private Plane _dragPlane;
        private Vector3 _grabOffset;


        private Vector3 _dragStartPos;
        private Quaternion _dragStartRot;

        private void OnEnable()
        {
            SubscribeMode();
            Subscribe();
        }

        private void Start()
        {
            SubscribeMode();
            Subscribe();
        }

        private void OnDisable()
        {
            if (EditorToolManager.Instance != null)
                EditorToolManager.Instance.OnModeChanged -= HandleModeChanged;
            Unsubscribe();
            CancelDrag();
        }

        private void SubscribeMode()
        {
            var mgr = EditorToolManager.Instance;
            if (mgr == null) return;
            mgr.OnModeChanged -= HandleModeChanged;
            mgr.OnModeChanged += HandleModeChanged;
            _isActive = mgr.CurrentMode == EditorToolManager.ToolMode.Move;
        }

        private void Subscribe()
        {
            var input = EditorViewInput.Instance;
            if (input == null) return;
            input.ViewBeginDrag -= HandleBeginDrag; input.ViewBeginDrag += HandleBeginDrag;
            input.ViewDrag      -= HandleDrag;      input.ViewDrag      += HandleDrag;
            input.ViewEndDrag   -= HandleEndDrag;   input.ViewEndDrag   += HandleEndDrag;
        }

        private void Unsubscribe()
        {
            var input = EditorViewInput.Instance;
            if (input == null) return;
            input.ViewBeginDrag -= HandleBeginDrag;
            input.ViewDrag      -= HandleDrag;
            input.ViewEndDrag   -= HandleEndDrag;
        }

        private void HandleModeChanged(EditorToolManager.ToolMode mode)
        {
            _isActive = mode == EditorToolManager.ToolMode.Move;
            if (!_isActive) CancelDrag();
        }

        private void HandleBeginDrag(EditorViewInput.ViewPointer p)
        {
            if (!_isActive || !p.valid) return;

            var sel = SelectionManager.Instance != null ? SelectionManager.Instance.CurrentSelection : null;
            if (sel == null) return;

            var prop = sel.GetComponent<SelectableProp>();
            if (prop != null && !prop.canTransform) return;

            _target = sel.transform;
            _dragStartPos = _target.position;
            _dragStartRot = _target.rotation;

            float planeY;
            if (Physics.Raycast(p.ray, out RaycastHit hit, _maxRayDistance, _grabMask, QueryTriggerInteraction.Ignore)
                && hit.transform.IsChildOf(_target))
            {
                planeY = hit.point.y;
                _grabOffset = _target.position - new Vector3(hit.point.x, _target.position.y, hit.point.z);
            }
            else
            {
                planeY = _useObjectYAsPlane ? _target.position.y : _groundY;
                var fallbackPlane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
                if (fallbackPlane.Raycast(p.ray, out float fEnter))
                {
                    Vector3 fp = p.ray.GetPoint(fEnter);
                    _grabOffset = _target.position - new Vector3(fp.x, _target.position.y, fp.z);
                }
                else
                {
                    _grabOffset = Vector3.zero;
                }
            }

            _dragPlane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            _isDragging = true;
        }

        private void HandleDrag(EditorViewInput.ViewPointer p)
        {
            if (!_isActive || !_isDragging || !p.valid) return;
            if (_target == null) { CancelDrag(); return; }

            if (_dragPlane.Raycast(p.ray, out float enter))
            {
                Vector3 hitPoint = p.ray.GetPoint(enter);
                Vector3 target = hitPoint + _grabOffset;

                if (_snapToGrid && _gridSize > 0f)
                {
                    target.x = Mathf.Round(target.x / _gridSize) * _gridSize;
                    target.z = Mathf.Round(target.z / _gridSize) * _gridSize;
                }

                target.y = _target.position.y;
                _target.position = target;
            }
        }

        private void HandleEndDrag(EditorViewInput.ViewPointer p)
        {
            if (_isDragging && _target != null && _target.position != _dragStartPos)
                EditHistory.Instance?.Push(new TransformAction(
                    _target, _dragStartPos, _dragStartRot, _target.position, _target.rotation));

            _isDragging = false;
            _target = null;
        }

        private void CancelDrag()
        {
            _isDragging = false;
            _target = null;
        }
    }
}
