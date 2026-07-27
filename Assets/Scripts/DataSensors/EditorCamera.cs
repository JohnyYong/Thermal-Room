using UnityEngine;

public class EditorCamera : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 10f;
    public float fastMoveSpeed = 25f;   // hold Shift to go faster
    public float mouseSensitivity = 2f;

    [Header("Keys")]
    public KeyCode FAST_KEY = KeyCode.LeftShift;
    public KeyCode UP_KEY = KeyCode.E;
    public KeyCode DOWN_KEY = KeyCode.Q;

    private float _yaw;
    private float _pitch;
    private bool _isLooking;

    void OnEnable()
    {
        // Sync rotation state to current transform when camera activates
        _yaw = transform.eulerAngles.y;
        _pitch = transform.eulerAngles.x;
    }

    void Update()
    {
        HandleLook();
        HandleMovement();
    }

    void HandleLook()
    {
        // Hold right mouse button to look around
        if (Input.GetMouseButtonDown(1)) _isLooking = true;
        if (Input.GetMouseButtonUp(1)) _isLooking = false;

        if (!_isLooking) return;

        _yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        _pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        _pitch = Mathf.Clamp(_pitch, -89f, 89f);

        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    void HandleMovement()
    {
        float speed = Input.GetKey(FAST_KEY) ? fastMoveSpeed : moveSpeed;

        Vector3 dir = Vector3.zero;

        if (Input.GetKey(KeyCode.W)) dir += transform.forward;
        if (Input.GetKey(KeyCode.S)) dir -= transform.forward;
        if (Input.GetKey(KeyCode.A)) dir -= transform.right;
        if (Input.GetKey(KeyCode.D)) dir += transform.right;
        if (Input.GetKey(UP_KEY)) dir += Vector3.up;
        if (Input.GetKey(DOWN_KEY)) dir -= Vector3.up;

        transform.position += dir.normalized * speed * Time.deltaTime;
    }
}