using UnityEngine;

namespace DroneSystem
{
    /// <summary>
    /// FPV / Third-Person / Thermal camera. Two responsibilities:
    ///   1. Positioning: FPV transform, third-person follow.
    ///   2. View switching: Tab cycles which camera draws to the screen; ONE
    ///      other camera fills the single inset RenderTexture.
    ///
    /// FPS design: only TWO cameras are ever active at once (screen + inset).
    /// The third is disabled. The thermal camera AND the thermal compute sim
    /// are turned on ONLY while thermal view is on screen -- so FPV and
    /// Third-Person modes pay zero thermal cost.
    ///
    /// Inset mapping:
    ///   FPV on screen         -> inset shows Third-Person
    ///   Third-Person on screen-> inset shows FPV
    ///   Thermal on screen     -> inset shows FPV
    ///
    /// Input-agnostic for look: DroneMoveInput calls ApplyLook() each frame. This
    /// script does NOT read the Input System or arrow keys itself.
    /// </summary>
    public class DroneCamera : MonoBehaviour
    {
        public enum ViewMode { FPV, ThirdPerson, Thermal }

        [Header("Target")]
        [SerializeField] private Transform _target;

        [Header("FPV")]
        [SerializeField] private Transform _fpvTransform;

        [Header("Third-Person Offset")]
        [SerializeField] private float _followDistance = 5f;
        [SerializeField] private float _heightOffset = 2f;

        [Header("Rotation Smoothing (Third-Person Only)")]
        [SerializeField] private float _rotationSpeed = 15f;

        [Header("Inset Render Texture")]
        [Tooltip("The single RenderTexture shown in your inset RawImage. " +
                 "Whichever view is on screen, one other camera fills this.")]
        public RenderTexture insetRT_A;

        [Header("View Switching")]
        [Tooltip("Tab cycles FPV -> Third-Person -> Thermal.")]
        public KeyCode ViewToggleKey = KeyCode.Tab;
        public ViewMode startView = ViewMode.FPV;

        [Header("FPV Culling")]
        [Tooltip("Layer(s) the FPV camera should NOT render (e.g. the Drone layer).")]
        public LayerMask fpvCullingExclude;

        [Header("FPV Free-Look")]
        [Tooltip("Degrees per second the camera rotates at full input.")]
        public float lookSpeed = 90f;

        [Range(10f, 89f)]
        public float maxPitchAngle = 60f;

        [Header("Cameras")]
        [SerializeField] public Camera _thirdPersonCamera;
        [Tooltip("The dedicated thermal-view camera.")]
        [SerializeField] private Camera _thermalCamera;

        [Header("Extinguisher UI")]
        public ExtinguisherCapacityUI extinguisherUI;

        [Header("Thermal Hand-off (optional)")]
        [SerializeField] private ThermalViewController _thermalViewController;

        [Header("Thermal System")]
        [Tooltip("The fire/thermal compute sim. Paused while not in thermal view. " +
                 "If left empty, ThermalSimulation.Instance is used at runtime.")]
        [SerializeField] private ThermalSimulation _thermalSimulation;
        [SerializeField] private GameObject _thermalSystem;

        public ViewMode CurrentView { get;  set; }

        private Camera _mainCamera; // the FPV camera (this GameObject)
        private float _yawOffset = 0f;
        private float _pitchOffset = 0f;

        void Start()
        {
            if (_target == null)
            {
                Debug.LogError("[DroneCamera] No target assigned.");
                enabled = false;
                return;
            }

            if (_fpvTransform == null)
            {
                Debug.LogError("[DroneCamera] _fpvTransform is not assigned.");
                enabled = false;
                return;
            }

            _mainCamera = GetComponent<Camera>();
            if (_mainCamera == null)
            {
                Debug.LogError("[DroneCamera] No Camera component on this GameObject.");
                enabled = false;
                return;
            }

            if (_thirdPersonCamera == null)
            {
                Debug.LogError("[DroneCamera] Third-person camera not assigned.");
                enabled = false;
                return;
            }

            if (_thermalCamera == null)
                Debug.LogWarning("[DroneCamera] Thermal camera not assigned — thermal view will be skipped.");

            if (insetRT_A == null)
                Debug.LogWarning("[DroneCamera] Inset RenderTexture not assigned.");

            // Fall back to the singleton if the sim wasn't wired in the Inspector.
            if (_thermalSimulation == null)
                _thermalSimulation = ThermalSimulation.Instance;

            _mainCamera.cullingMask &= ~fpvCullingExclude;

            CurrentView = startView;
            ApplyRenderTargets();
            ApplyFPV();
            ApplyThirdPersonCamera(snap: true);
        }

        /// <summary>
        /// Called by the input hub (DroneMoveInput) each frame with the raw look
        /// vector: x = yaw (left/right), y = pitch (up/down), each in [-1, 1].
        /// This is the only place free-look offsets change.
        /// </summary>
        public void ApplyLook(Vector2 look)
        {
            _yawOffset += look.x * lookSpeed * Time.deltaTime;
            _pitchOffset -= look.y * lookSpeed * Time.deltaTime; // up = look up
            _pitchOffset = Mathf.Clamp(_pitchOffset, -maxPitchAngle, maxPitchAngle);
        }

        void Update()
        {
            if (Input.GetKeyDown(ViewToggleKey))
            {
                CycleView();
                ApplyRenderTargets();
                Debug.Log($"[DroneCamera] On screen: {CurrentView}");
            }
        }

        public void CycleView()
        {
            switch (CurrentView)
            {
                case ViewMode.FPV: CurrentView = ViewMode.ThirdPerson; break;
                case ViewMode.ThirdPerson: CurrentView = ViewMode.Thermal; break;
                case ViewMode.Thermal: CurrentView = ViewMode.FPV; break;
            }

            // Skip thermal if no thermal camera is wired.
            if (CurrentView == ViewMode.Thermal && _thermalCamera == null)
                CurrentView = ViewMode.FPV;
        }

        public void ApplyRenderTargets()
        {
            extinguisherUI.SetFPVMode(CurrentView == ViewMode.FPV);

            bool inThermal = (CurrentView == ViewMode.Thermal);


            if (_thermalViewController != null)
                _thermalViewController.SetThermalActive(inThermal);

            if (_thermalSimulation != null)
                _thermalSimulation.SetSimulationActive(inThermal);

            if (_thermalCamera != null)
                _thermalCamera.gameObject.SetActive(inThermal);

            _thermalSystem.SetActive(inThermal);

            // Exactly two cameras active at a time: on-screen + single inset.
            // The unused third camera is disabled so it doesn't render at all.
            switch (CurrentView)
            {
                case ViewMode.FPV:
                    // Screen: FPV.  Inset: Third-Person.  Thermal off.
                    SetActive(_mainCamera, true);
                    SetActive(_thirdPersonCamera, true);
                    SetActive(_thermalCamera, false);

                    SetScreen(_mainCamera);
                    SetInset(_thirdPersonCamera);
                    break;

                case ViewMode.ThirdPerson:
                    // Screen: Third-Person.  Inset: FPV.  Thermal off.
                    SetActive(_mainCamera, true);
                    SetActive(_thirdPersonCamera, true);
                    SetActive(_thermalCamera, false);

                    SetScreen(_thirdPersonCamera);
                    SetInset(_mainCamera);
                    break;

                case ViewMode.Thermal:
                    // Screen: Thermal.  Inset: FPV.  Third-Person off.
                    SetActive(_mainCamera, true);
                    SetActive(_thirdPersonCamera, false);
                    SetActive(_thermalCamera, true);

                    SetScreen(_thermalCamera);
                    SetInset(_mainCamera);
                    break;
            }
        }

        void SetActive(Camera cam, bool on)
        {
            if (cam != null) cam.enabled = on;
        }

        void SetScreen(Camera cam)
        {
            if (cam != null) cam.targetTexture = null;
        }

        void SetInset(Camera cam)
        {
            if (cam != null) cam.targetTexture = insetRT_A;
        }

        void LateUpdate()
        {
            if (_target == null) return;

            ApplyFPV();
            ApplyThirdPersonCamera(snap: false);
        }

        public void ApplyFPV()
        {
            Quaternion baseRotation = _fpvTransform.rotation;
            Quaternion yaw = Quaternion.AngleAxis(_yawOffset, Vector3.up);
            Quaternion pitch = Quaternion.AngleAxis(_pitchOffset, Vector3.right);
            Quaternion finalRotation = yaw * baseRotation * pitch;

            transform.SetPositionAndRotation(_fpvTransform.position, finalRotation);
        }

        public void SetCameraView(ViewMode mode)
        {
            CurrentView = mode; //Change mode
        }
        void ApplyThirdPersonCamera(bool snap)
        {
            Vector3 desiredPos = GetDesiredThirdPersonPosition();
            _thirdPersonCamera.transform.position = desiredPos;

            Vector3 directionToDrone = _target.position - desiredPos;
            if (directionToDrone == Vector3.zero) return;

            Quaternion desiredRotation = Quaternion.LookRotation(directionToDrone, Vector3.up);

            _thirdPersonCamera.transform.rotation = snap
                ? desiredRotation
                : Quaternion.Slerp(
                    _thirdPersonCamera.transform.rotation,
                    desiredRotation,
                    _rotationSpeed * Time.deltaTime);
        }

        Vector3 GetDesiredThirdPersonPosition()
        {
            Vector3 flatForward = new Vector3(_target.forward.x, 0f, _target.forward.z);

            if (flatForward.sqrMagnitude < 0.001f)
                flatForward = new Vector3(_target.up.x, 0f, _target.up.z);

            if (flatForward.sqrMagnitude < 0.001f)
                flatForward = Vector3.forward;

            flatForward.Normalize();

            return _target.position
                   - flatForward * _followDistance
                   + Vector3.up * _heightOffset;
        }
    }
}