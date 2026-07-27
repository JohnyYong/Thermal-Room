using UnityEngine;
using UnityEngine.InputSystem;

namespace DroneSystem
{
    [RequireComponent(typeof(DronePhysics))]
    public class DroneMoveInput : MonoBehaviour
    {
        [Header("Mode")]
        [Tooltip("In waypoint mode, manual move/throttle/yaw inputs are ignored.")]
        public bool WaypointMode = false;

        [Header("Actions")]
        [SerializeField] private InputActionReference _moveAction;
        [SerializeField] private InputActionReference _throttleAction;
        [SerializeField] private InputActionReference _lookAction;
        [SerializeField] private InputActionReference _sprayAction;
        [SerializeField] private InputActionReference _rotateAction;     // NEW
        [SerializeField] private InputActionReference _changeViewAction; // NEW

        [Header("Targets")]
        [Tooltip("Camera that receives Look input. Optional.")]
        [SerializeField] private DroneCamera _camera;

        [Header("Extinguisher")]
        public GameObject foamVFXPrefab;
        public Transform sprayPoint;
        public FireExtinguisher fireExtinguisherScript;

        private DronePhysics _dronePhysics;
        private DroneExtinguisher _extinguisher;
        private GameObject _activeFoam;

        private void Awake()
        {
            _dronePhysics = GetComponent<DronePhysics>();
            _extinguisher = GetComponent<DroneExtinguisher>();
        }

        private void OnEnable()
        {
            EnableAction(_moveAction, "Move");
            EnableAction(_throttleAction, "Throttle");
            EnableAction(_sprayAction, "Spray");
            EnableAction(_rotateAction, "Rotate");
            if (_lookAction != null) _lookAction.action.Enable();
            else Debug.LogWarning("[DroneMoveInput] No Look action — free-look disabled.", this);

            if (_changeViewAction != null)
            {
                _changeViewAction.action.Enable();
                _changeViewAction.action.performed += OnChangeView;
            }
        }

        private void OnDisable()
        {
            DisableAction(_moveAction);
            DisableAction(_throttleAction);
            DisableAction(_lookAction);
            DisableAction(_sprayAction);
            DisableAction(_rotateAction);

            if (_changeViewAction != null)
            {
                _changeViewAction.action.performed -= OnChangeView;
                _changeViewAction.action.Disable();
            }
        }

        private void Update()
        {
            HandleFlight();
            HandleSpray();

            if (_camera != null && _lookAction != null)
                _camera.ApplyLook(_lookAction.action.ReadValue<Vector2>());
            else
            {
                _camera = GameObject.FindGameObjectWithTag("MainCamera").GetComponent<DroneCamera>();
            }
        }

        private void HandleFlight()
        {
            if (WaypointMode)
            {
                _dronePhysics.SetInputs(0f, 0f, 0f, 0f);
                return;
            }

            Vector2 move = _moveAction != null ? _moveAction.action.ReadValue<Vector2>() : Vector2.zero;
            float throttle = _throttleAction != null ? _throttleAction.action.ReadValue<float>() : 0f;
            float yaw = _rotateAction != null ? _rotateAction.action.ReadValue<float>() : 0f; // NEW

            _dronePhysics.SetInputs(throttle, move.y, move.x, yaw);
        }

        private void OnChangeView(InputAction.CallbackContext ctx)
        {
            if (_camera != null)
            {
                _camera.CycleView();
                _camera.ApplyRenderTargets();
            }
            else
            {
                Debug.LogWarning("[DroneMoveInput] _camera is null!");
            }
        }

        private void HandleSpray()
        {
            bool sprayHeld = _sprayAction != null && _sprayAction.action.IsPressed();
            bool canSpray = _extinguisher == null || !_extinguisher.IsEmpty;

            if (sprayHeld && canSpray)
            {
                _extinguisher?.DrainCapacity();

                if (foamVFXPrefab != null && sprayPoint != null && _activeFoam == null)
                    _activeFoam = Instantiate(foamVFXPrefab, sprayPoint.position, sprayPoint.rotation);
                fireExtinguisherScript.Spray();
            }

            if (_activeFoam != null && (_extinguisher?.IsEmpty ?? false))
                StopFoam();

            if (_activeFoam != null)
            {
                _activeFoam.transform.position = sprayPoint.position;
                _activeFoam.transform.rotation = sprayPoint.rotation;
            }

            if (_sprayAction != null && _sprayAction.action.WasReleasedThisFrame())
                StopFoam();
        }

        private void StopFoam()
        {
            if (_activeFoam == null) return;

            ParticleSystem ps = _activeFoam.GetComponent<ParticleSystem>();
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            Destroy(_activeFoam, 3f);
            _activeFoam = null;
        }

        private void EnableAction(InputActionReference r, string label)
        {
            if (r != null) r.action.Enable();
            else Debug.LogError($"[DroneMoveInput] No {label} action assigned.", this);
        }

        private void DisableAction(InputActionReference r)
        {
            if (r != null) r.action.Disable();
        }
    }
}