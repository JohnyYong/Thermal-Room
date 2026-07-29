using System.Collections;
using UnityEngine;

/// <summary>
/// Attach this to the ThermalImager GameObject.
/// Toggles it between two target Transforms (e.g. ThermalPositionA / ThermalPositionB)
/// when ToggleThermalPosition() is called — hook that up to a UI Button's OnClick().
/// </summary>
public class ThermalPositionToggle : MonoBehaviour
{
    [Header("Position Targets")]
    [Tooltip("Default / raised position (e.g. ThermalPositionA)")]
    public Transform positionA;

    [Tooltip("Lowered / down position (e.g. ThermalPositionB)")]
    public Transform positionB;

    [Header("Movement Settings")]
    [Tooltip("How fast the imager moves between positions (units/sec)")]
    public float moveSpeed = 2f;

    [Tooltip("Also rotate to match the target's rotation")]
    public bool matchRotation = true;

    [Tooltip("Degrees/sec when matching rotation")]
    public float rotateSpeed = 180f;

    [Header("Keyboard Input")]
    [Tooltip("Press this key to toggle between positions")]
    public KeyCode toggleKey = KeyCode.T;

    private bool isAtPositionB = false;
    private Coroutine moveRoutine;

    [SerializeField] private bool inXRMode = false; //Need to make it use #if XR_ENABLED 

    private void Start()
    {
        // Start at Position A by default
        if (positionA != null)
        {
            transform.position = positionA.position;
            if (matchRotation) transform.rotation = positionA.rotation;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey) && !inXRMode)
        {
            ToggleThermalPosition();
        }
    }

    /// <summary>
    /// Call this from a UI Button's OnClick() event.
    /// Alternates the imager between positionA and positionB each time it's called.
    /// </summary>
    public void ToggleThermalPosition()
    {
        isAtPositionB = !isAtPositionB;
        Transform target = isAtPositionB ? positionB : positionA;

        if (target == null)
        {
            Debug.LogWarning("ThermalPositionToggle: target position is not assigned.");
            return;
        }

        if (moveRoutine != null) StopCoroutine(moveRoutine);
        moveRoutine = StartCoroutine(MoveToTarget(target));
    }

    /// <summary>
    /// Optional: directly move to a specific state instead of toggling.
    /// </summary>
    public void MoveToPositionA()
    {
        isAtPositionB = false;
        if (moveRoutine != null) StopCoroutine(moveRoutine);
        moveRoutine = StartCoroutine(MoveToTarget(positionA));
    }

    public void MoveToPositionB()
    {
        isAtPositionB = true;
        if (moveRoutine != null) StopCoroutine(moveRoutine);
        moveRoutine = StartCoroutine(MoveToTarget(positionB));
    }

    private IEnumerator MoveToTarget(Transform target)
    {
        if (target == null) yield break;

        while (Vector3.Distance(transform.position, target.position) > 0.001f ||
               (matchRotation && Quaternion.Angle(transform.rotation, target.rotation) > 0.1f))
        {
            transform.position = Vector3.MoveTowards(
                transform.position, target.position, moveSpeed * Time.deltaTime);

            if (matchRotation)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, target.rotation, rotateSpeed * Time.deltaTime);
            }

            yield return null;
        }

        // Snap exactly at the end to avoid float drift
        transform.position = target.position;
        if (matchRotation) transform.rotation = target.rotation;
    }
}