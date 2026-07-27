using UnityEngine;

namespace DroneSystem
{
    [RequireComponent(typeof(Rigidbody))]
    public class DronePhysics : MonoBehaviour
    {
        [Header("Drone Propeller Transforms")]
        [SerializeField] public Transform TOP_LEFT_PROPELLER;
        [SerializeField] public Transform TOP_RIGHT_PROPELLER;
        [SerializeField] public Transform BOTTOM_LEFT_PROPELLER;
        [SerializeField] public Transform BOTTOM_RIGHT_PROPELLER;

        [Header("Flight Controller Core")]
        [Tooltip("Target Thrust-to-Weight Ratio at 100% throttle. \n" +
                 "3.0 means the drone can lift 3x its own weight, IF the fixed motor limit allows it.")]
        [SerializeField] private float _targetTWR = 3.0f;

        [Tooltip("Fixed maximum thrust the motors can physically produce (Newtons). " +
                 "This is the hard ceiling — heavier drones get less effective TWR as a result.")]
        [SerializeField] private float _fixedMaxThrustNewtons = 30f;

        [Tooltip("Maximum horizontal movement speed (m/s) before drag caps it.")]
        [SerializeField] private float _maxHorizontalSpeed = 15f;

        [Tooltip("Yaw rotation speed (degrees per second).")]
        [SerializeField] private float _yawSpeed = 120f;

        [Header("Tilt Settings (Cosmetic Flight Controller)")]
        [Tooltip("Maximum pitch/roll lean (degrees). Real drones tilt to move horizontally.")]
        [SerializeField][Range(5f, 55f)] private float _maxTiltAngle = 35f;

        [Tooltip("How fast the drone snaps to its target tilt angle (deg/s).")]
        [SerializeField] private float _tiltSpeed = 220f;

        [Header("Hover / Alt Hold")]
        [SerializeField] private bool _hoverActive = true;

        [Header("Mass")]
        [SerializeField] private float _massScale = 100f;

        private const float _gravity = 9.81f;

        // Aerodynamic Tuning
        private float _horizontalDrag;
        private float _verticalDrag;

        // Raw inputs [-1, 1]
        private float _inputThrottle;
        private float _inputPitch;
        private float _inputRoll;
        private float _inputYaw;

        private Rigidbody _rb;

        // Effective thrust pool — capped by the fixed motor limit
        private float _maxAbsoluteThrustNewtons;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = true; // Use Unity's native gravity to handle structural weight properly
            _rb.constraints = RigidbodyConstraints.FreezeRotation; // Flight controller overrides rotation forces
            _rb.interpolation = RigidbodyInterpolation.Interpolate;

            CalculateEngineCapabilities();
        }

        void FixedUpdate()
        {
            // Dynamic drag calculations ensuring linear behavior across different masses
            _horizontalDrag = _maxAbsoluteThrustNewtons / (_maxHorizontalSpeed * _rb.mass);
            _verticalDrag = _horizontalDrag * 1.5f;

            ApplyVerticalThrust();
            ApplyHorizontalMovement();
            ApplyYaw();
            ApplyTilt();
        }

        /// <summary>
        /// Establishes the effective thrust pool based on current mass and TWR,
        /// capped by the drone's fixed physical motor limit.
        /// Call this on initialization or whenever mass changes.
        /// </summary>
        public void CalculateEngineCapabilities()
        {
            float hoverForceRequired = _rb.mass * _gravity;
            float desiredThrust = hoverForceRequired * _targetTWR;

            // Hard cap: motors cannot exceed their physical limit regardless of TWR target
            _maxAbsoluteThrustNewtons = Mathf.Min(desiredThrust, _fixedMaxThrustNewtons);

            Debug.Log($"[FlightController] Mass: {_rb.mass}kg | Hover Required: {hoverForceRequired:F2}N | " +
                      $"Max Thrust Pool: {_maxAbsoluteThrustNewtons:F1}N (Desired: {desiredThrust:F1}N, Cap: {_fixedMaxThrustNewtons}N)");
        }

        // ----------------------------------------------------------------
        //  Public Flight Interface (API)
        // ----------------------------------------------------------------

        public void SetInputs(float throttle, float pitch, float roll, float yaw)
        {
            _inputThrottle = Mathf.Clamp(throttle, -1f, 1f);
            _inputPitch = Mathf.Clamp(pitch, -1f, 1f);
            _inputRoll = Mathf.Clamp(roll, -1f, 1f);
            _inputYaw = Mathf.Clamp(yaw, -1f, 1f);
        }

        public void ToggleHover() => _hoverActive = !_hoverActive;

        public void SetDisplayMass(float displayMassKg)
        {
            if (_rb == null) _rb = GetComponent<Rigidbody>();

            float simMass = displayMassKg / _massScale;
            simMass = Mathf.Max(simMass, 0.01f);
            _rb.mass = simMass;
            CalculateEngineCapabilities();
        }

        public void SetScale(Vector3 newScale)
        {
            transform.localScale = newScale;
        }

        /// <summary>
        /// Call this when you script adding a camera, armor plate, or package cargo to the drone.
        /// </summary>
        public void UpdatePayloadMass(float addedMass)
        {
            _rb.mass += addedMass;
            if (_rb.mass < 0.1f) _rb.mass = 0.1f;

            CalculateEngineCapabilities();
        }

        // ----------------------------------------------------------------
        //  Internal Flight Dynamics
        // ----------------------------------------------------------------

        void ApplyVerticalThrust()
        {
            float hoverThrustRequired = _rb.mass * _gravity;
            float commandedThrust;

            if (_hoverActive)
            {
                // Hover Baseline: Fight gravity, scale upward/downward using remaining motor overhead
                float thrustOverhead = _maxAbsoluteThrustNewtons - hoverThrustRequired;
                commandedThrust = hoverThrustRequired + (_inputThrottle * thrustOverhead * 0.7f);
            }
            else
            {
                // Manual Throttle: Directly map the input from 0 to absolute motor capacity
                float normalizedThrottle = (_inputThrottle + 1f) * 0.5f; // Map [-1, 1] to [0, 1]
                commandedThrust = normalizedThrottle * _maxAbsoluteThrustNewtons;
            }

            // CRITICAL REALISM CHECK: The motors cannot pull the drone downward, nor push past their physical limit
            commandedThrust = Mathf.Clamp(commandedThrust, 0f, _maxAbsoluteThrustNewtons);

            // Apply upward vertical force relative to the world coordinate system
            _rb.AddForce(Vector3.up * commandedThrust, ForceMode.Force);

            // Natural aerodynamic damping (air resistance resisting vertical movement)
            float currentVertVelocity = _rb.linearVelocity.y;
            float verticalDampingForce = currentVertVelocity * _verticalDrag * _rb.mass;
            _rb.AddForce(Vector3.down * verticalDampingForce, ForceMode.Force);
        }

        void ApplyHorizontalMovement()
        {
            // Map stick input to spatial vectors based on current orientation
            Vector3 localMove = new Vector3(_inputRoll, 0f, _inputPitch);
            Vector3 worldMoveDirection = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * localMove;

            // Calculate maximum horizontal force allocation (derived from engine limits)
            float maxHorizontalForce = _maxAbsoluteThrustNewtons * 0.4f;
            Vector3 movementForce = worldMoveDirection * maxHorizontalForce;

            _rb.AddForce(movementForce, ForceMode.Force);

            // Aerodynamic Drag: Resists horizontal velocity cleanly based on drone weight profile
            Vector3 horizontalVelocity = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
            Vector3 dragForce = -horizontalVelocity * _horizontalDrag * _rb.mass;

            _rb.AddForce(dragForce, ForceMode.Force);
        }

        void ApplyYaw()
        {
            // Direct yaw manipulation mimicking active rotor-differential adjustments
            float yawDelta = _inputYaw * _yawSpeed * Time.fixedDeltaTime;
            transform.Rotate(Vector3.up, yawDelta, Space.World);
        }

        void ApplyTilt()
        {
            // Calculate flight dynamics lean based on input directions
            float targetPitch = _inputPitch * _maxTiltAngle;
            float targetRoll = -_inputRoll * _maxTiltAngle;

            // Ensure current directional heading is preserved while resolving Pitch/Roll adjustments
            Quaternion targetRotation = Quaternion.Euler(targetPitch, transform.eulerAngles.y, targetRoll);

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRotation, _tiltSpeed * Time.fixedDeltaTime);
        }
    }
}
