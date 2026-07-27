using UnityEngine;

namespace ScenarioEditor
{
    public class ObjectRotator : MonoBehaviour
    {
        [Header("Rotation Feel")]
        [SerializeField] private float _degreesPerPixel = 0.5f;
        [SerializeField] private bool _invertDirection = true;

        [Header("Snapping (optional)")]
        [SerializeField] private bool _snapEnabled = false;
        [SerializeField] private float _snapStep = 15f;

        private bool _isActive;
        private bool _isDragging;
        private Transform _target;
        private float _lastX;
        private float _accumulatedYaw;

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
            _isActive = mgr.CurrentMode == EditorToolManager.ToolMode.Rotate;
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
            _isActive = mode == EditorToolManager.ToolMode.Rotate;
            if (!_isActive) CancelDrag();
        }

        private void HandleBeginDrag(EditorViewInput.ViewPointer p)
        {
            if (!_isActive) return;

            var sel = SelectionManager.Instance != null ? SelectionManager.Instance.CurrentSelection : null;
            if (sel == null) return;

            var prop = sel.GetComponent<SelectableProp>();
            if (prop != null && !prop.canTransform) return;

            _target = sel.transform;
            _dragStartPos = _target.position;
            _dragStartRot = _target.rotation;

            _lastX = p.screenPosition.x;
            _accumulatedYaw = 0f;
            _isDragging = true;
        }

        private void HandleDrag(EditorViewInput.ViewPointer p)
        {
            if (!_isActive || !_isDragging) return;
            if (_target == null) { CancelDrag(); return; }

            float currentX = p.screenPosition.x;
            float deltaX = currentX - _lastX;
            _lastX = currentX;

            float yaw = deltaX * _degreesPerPixel;
            if (_invertDirection) yaw = -yaw;

            if (_snapEnabled && _snapStep > 0f)
            {
                _accumulatedYaw += yaw;
                float steps = Mathf.Floor(Mathf.Abs(_accumulatedYaw) / _snapStep) * Mathf.Sign(_accumulatedYaw);
                if (Mathf.Abs(steps) >= 1f)
                {
                    float applied = steps * _snapStep;
                    _target.Rotate(Vector3.up, applied, Space.World);
                    _accumulatedYaw -= applied;
                }
            }
            else
            {
                _target.Rotate(Vector3.up, yaw, Space.World);
            }
        }

        private void HandleEndDrag(EditorViewInput.ViewPointer p)
        {
            if (_isDragging && _target != null && _target.rotation != _dragStartRot)
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
