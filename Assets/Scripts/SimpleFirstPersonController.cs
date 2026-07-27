using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Attach this to the ManA GameObject (the one with the Character Controller).
/// Assign the child Camera transform to "cameraTransform".
/// WASD to move, mouse to look around (yaw rotates the body, pitch rotates the camera).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class SimpleFirstPersonController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The child Camera transform (used for pitch/up-down look)")]
    public Transform cameraTransform;

    [Header("Movement")]
    public float walkSpeed = 4f;
    public float runSpeed = 7f;
    public KeyCode runKey = KeyCode.LeftShift;
    public float gravity = -9.81f;
    public float jumpHeight = 1.2f;
    public KeyCode jumpKey = KeyCode.Space;

    [Header("Mouse Look")]
    public float mouseSensitivity = 2f;
    public bool invertY = false;
    public float minPitch = -80f;
    public float maxPitch = 80f;

    [Header("Cursor")]
    public bool lockCursorOnStart = true;
    [Tooltip("Press Escape to unlock the cursor mid-play")]
    public bool allowEscapeToUnlock = true;

    private CharacterController controller;
    private Vector3 velocity;
    private float pitch = 0f;

    private void Start()
    {
        controller = GetComponent<CharacterController>();

        if (cameraTransform == null)
        {
            Debug.LogWarning("SimpleFirstPersonController: No camera assigned, trying to find one in children.");
            cameraTransform = GetComponentInChildren<Camera>()?.transform;
        }

        if (lockCursorOnStart)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void Update()
    {
        HandleCursorToggle();
        HandleMouseLook();
        HandleMovement();
    }

    private void HandleCursorToggle()
    {
        if (!allowEscapeToUnlock) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            bool isLocked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = isLocked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = isLocked;
        }

        if (Input.GetKeyDown(KeyCode.F12))
        {
            string currentSceneName = SceneManager.GetActiveScene().name;
            SceneManager.LoadScene(currentSceneName);
        }
    }

    private void HandleMouseLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * (invertY ? 1f : -1f);

        // Yaw: rotate the whole body (this object) left/right
        transform.Rotate(Vector3.up * mouseX);

        // Pitch: rotate only the camera up/down, clamped
        pitch += mouseY;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        if (cameraTransform != null)
        {
            cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }

    private void HandleMovement()
    {
        bool isGrounded = controller.isGrounded;
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f; // small downward force to keep grounded
        }

        float h = Input.GetAxis("Horizontal"); // A/D
        float v = Input.GetAxis("Vertical");   // W/S

        Vector3 move = transform.right * h + transform.forward * v;
        float currentSpeed = Input.GetKey(runKey) ? runSpeed : walkSpeed;
        controller.Move(move * currentSpeed * Time.deltaTime);

        if (Input.GetKeyDown(jumpKey) && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }
}
