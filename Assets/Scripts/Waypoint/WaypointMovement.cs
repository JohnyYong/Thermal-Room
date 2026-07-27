using UnityEngine;
using DroneSystem;

public class WaypointMovement : MonoBehaviour
{
    [Header("Waypoints to Follow")] //Procedurally in order
    public Transform[] Waypoints;

    [Header("Waypoint Settings")]
    [Tooltip("How close the drone must get to a waypoint (3D distance) before it counts as reached.")]
    [SerializeField] private float _arrivalRadius = 1.5f;

    [Tooltip("Stop and hover at each waypoint for this many seconds before continuing.")]
    [SerializeField] private float _waypointPauseDuration = 1f;

    [Tooltip("Loop back to the first waypoint after reaching the last one.")]
    [SerializeField] private bool _loop = false;

    [Header("Input Scaling")]
    [Tooltip("Altitude error (metres) that maps to full throttle. Larger = gentler correction.")]
    [SerializeField] private float _altitudeFullRange = 3f;

    [Tooltip("Yaw error (degrees) that maps to full yaw input.")]
    [SerializeField] private float _yawFullRange = 45f;

    [Tooltip("Horizontal distance that maps to full pitch input.")]
    [SerializeField] private float _pitchFullRange = 8f;

    [Tooltip("Only pitch forward when yaw error is within this many degrees.")]
    [SerializeField] private float _pitchYawThreshold = 20f;

    [Tooltip("Scale pitch down within this horizontal distance of the waypoint to avoid overshoot.")]
    [SerializeField] private float _brakingDistance = 3f;

    //------------------------------------------------------------
    //  Runtime State
    //------------------------------------------------------------

    private DroneController _droneController;
    private DronePhysics _dronePhysics;
    private int _currentWaypointIndex = 0;
    private float _pauseTimer = 0f;
    private bool _isPaused = false;
    private bool _finished = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        print("Num Waypoints: " + Waypoints.Length);
        foreach (Transform w in Waypoints)
        {
            print(w.name + " positioned at: " + w.transform.position);
        }

        _droneController = GameObject.FindGameObjectWithTag("Drone").GetComponent<DroneController>();
        _dronePhysics = _droneController.GetComponent<DronePhysics>();

        //Hover enabled so altitude is locked during waypoint flight
        //_dronePhysics.SetHover(true);
    }

    // Update is called once per frame
    void Update()
    {
        //Need to make enumeration state on what robot we are going to use
        //DRONE, QUADRAPLE, HUMAN

        if (_droneController == null || !_droneController.WaypointMode) { return; }
        if (_finished) { return; }
        if (Waypoints.Length == 0) { return; }

        if (_isPaused)
        {
            _dronePhysics.SetInputs(0f, 0f, 0f, 0f);
            _pauseTimer -= Time.deltaTime;
            if (_pauseTimer <= 0f) { _isPaused = false; }
            return;
        }

        DriveToWaypoint(Waypoints[_currentWaypointIndex]);
    }

    //------------------------------------------------------------
    //  Internal Helpers
    //------------------------------------------------------------

    //Direct proportional inputs — no extra smoothing layer needed.
    //DronePhysics now uses MoveTowards on velocity directly so inputs translate
    //cleanly to movement without oscillation.
    void DriveToWaypoint(Transform waypoint)
    {
        Vector3 dronePos = _droneController.transform.position;
        Vector3 targetPos = waypoint.position;

        //---- Arrival check ----
        if (Vector3.Distance(dronePos, targetPos) <= _arrivalRadius)
        {
            OnWaypointReached();
            return;
        }

        //---- Altitude ----
        float altError = targetPos.y - dronePos.y;
        float throttle = Mathf.Clamp(altError / _altitudeFullRange, -1f, 1f);

        //---- Yaw toward target ----
        Vector3 flatToTarget = new Vector3(targetPos.x - dronePos.x, 0f, targetPos.z - dronePos.z);
        Vector3 flatForward = new Vector3(_droneController.transform.forward.x, 0f,
                                           _droneController.transform.forward.z).normalized;
        float yawError = Vector3.SignedAngle(flatForward, flatToTarget.normalized, Vector3.up);
        float yaw = Mathf.Clamp(yawError / _yawFullRange, -1f, 1f);

        //---- Pitch forward ----
        float flatDist = flatToTarget.magnitude;
        float pitch = 0f;
        if (Mathf.Abs(yawError) < _pitchYawThreshold && flatDist > _arrivalRadius)
        {
            float distanceFactor = Mathf.Clamp01(flatDist / _pitchFullRange);
            float brakingFactor = Mathf.Clamp01(flatDist / _brakingDistance);
            pitch = distanceFactor * brakingFactor;
        }

        _dronePhysics.SetInputs(throttle, pitch, 0f, yaw);
    }

    void OnWaypointReached()
    {
        print("Reached waypoint: " + Waypoints[_currentWaypointIndex].name);

        _dronePhysics.SetInputs(0f, 0f, 0f, 0f);
        _isPaused = true;
        _pauseTimer = _waypointPauseDuration;

        _currentWaypointIndex++;

        if (_currentWaypointIndex >= Waypoints.Length)
        {
            if (_loop)
            {
                _currentWaypointIndex = 0;
                print("Looping back to first waypoint.");
            }
            else
            {
                _finished = true;
                print("All waypoints reached. Drone hovering.");
            }
        }
    }
}