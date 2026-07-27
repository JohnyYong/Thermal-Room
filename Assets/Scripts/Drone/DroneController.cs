using UnityEngine;

namespace DroneSystem
{
    //CrowdSimulation: Maybe can add flamethrower
    //When Fire is lit, based on the smoke can add infrared vision

    //Working on YAW, PITCH, ROLL as much as possible
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(DronePhysics))]
    public class DroneController : MonoBehaviour
    {
        //During waypoint mode, it is basically in auto mode, where GOAP and behavior tree maybe relevant
        [SerializeField]
        public bool WaypointMode = false; //In way point mode, ignore inputs for movement of the drone

        [Header("Player Movement Inputs")]
        [SerializeField]
        public KeyCode FORWARD = KeyCode.W;
        public KeyCode BACKWARD = KeyCode.S;
        public KeyCode LEFT = KeyCode.A;
        public KeyCode RIGHT = KeyCode.D;
        public KeyCode ELEVATE = KeyCode.R;
        public KeyCode DESCENT = KeyCode.F;
        public KeyCode ROTATE_LEFT = KeyCode.Q;
        public KeyCode ROTATE_RIGHT = KeyCode.E;
        public KeyCode HOVER_TOGGLE = KeyCode.H; //Toggle hover mode on/off
        public KeyCode SPRAY_TOGGLE = KeyCode.Mouse0;

        [Header("Extinguisher Settings")]
        public GameObject foamVFXPrefab;  // drag FoamVFX prefab here
        public Transform sprayPoint;      // drag SprayPoint here
                                          //Propeller transforms are assigned directly on DronePhysics in the Inspector.
                                          //DroneController no longer needs to hold or forward them.

        //DronePhysics is fetched at runtime via GetComponent — NOT serialized.
        //If it appeared as a serialized field before, that caused it to show "None" and go null.
        private DronePhysics _dronePhysics;
        private GameObject _activeFoam;
        public DroneExtinguisher _extinguisher;

        void Start()
        {
            _dronePhysics = GetComponent<DronePhysics>();
            _extinguisher = GetComponent<DroneExtinguisher>();
        }

        //Try moving with standard translation first
        // Update is called once per frame
        void Update()
        {
            if (!WaypointMode) { HandlingInputs(); }
            HandleSpray();
        }

        void HandleSpray()
        {
            // Don't spray if empty
            bool canSpray = _extinguisher == null || !_extinguisher.IsEmpty;

            if (Input.GetKey(SPRAY_TOGGLE) && canSpray)
            {
                // Drain capacity every frame while held
                _extinguisher?.DrainCapacity();

                if (foamVFXPrefab != null && sprayPoint != null && _activeFoam == null)
                {
                    _activeFoam = Instantiate(foamVFXPrefab, sprayPoint.position, sprayPoint.rotation);
                }
            }

            // Stop spraying if ran out mid-hold
            if (_activeFoam != null && (_extinguisher?.IsEmpty ?? false))
            {
                StopFoam();
            }

            // Sync foam position to sprayPoint
            if (_activeFoam != null)
            {
                _activeFoam.transform.position = sprayPoint.position;
                _activeFoam.transform.rotation = sprayPoint.rotation;
            }

            if (Input.GetKeyUp(SPRAY_TOGGLE))
            {
                StopFoam();
            }
        }

        void HandlingInputs()
        {
            //Need to change position handling to more about YAW, PITCH, ROLL, actual physics

            //Toggle hover mode on key press (not hold)
            if (Input.GetKeyDown(HOVER_TOGGLE)) { _dronePhysics.ToggleHover(); }

            //Collect all axis inputs this frame
            float throttle = 0f;
            float pitchInput = 0f;
            float rollInput = 0f;
            float yawInput = 0f;

            if (Input.GetKey(ELEVATE)) { throttle += 1f; }
            if (Input.GetKey(DESCENT)) { throttle -= 1f; }
            if (Input.GetKey(FORWARD)) { pitchInput += 1f; } //Nose pitches down → moves forward
            if (Input.GetKey(BACKWARD)) { pitchInput -= 1f; } //Nose pitches up   → moves backward
            if (Input.GetKey(LEFT)) { rollInput -= 1f; } //Roll left         → translates left
            if (Input.GetKey(RIGHT)) { rollInput += 1f; } //Roll right        → translates right
            if (Input.GetKey(ROTATE_LEFT)) { yawInput -= 1f; } //Yaw counter-clockwise
            if (Input.GetKey(ROTATE_RIGHT)) { yawInput += 1f; } //Yaw clockwise

            //Pass normalised inputs to the physics layer
            _dronePhysics.SetInputs(throttle, pitchInput, rollInput, yawInput);
        }

        void StopFoam()
        {
            if (_activeFoam == null) return;

            ParticleSystem ps = _activeFoam.GetComponent<ParticleSystem>();
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            Destroy(_activeFoam, 3f);
            _activeFoam = null;
        }
    }
}