using System;
using System.Collections;
using MiddleVR;
using MiddleVR.Unity;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif


[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public enum TurnMode { None, Smooth, Snap }
    public enum WandAxis { Forward, Back, Up, Down, Right, Left }

    [Header("Movement")]
    public float speed = 2f;              // metres per second
    public float bodyRadius = 0.3f;
    public float bodyHeight = 1.8f;
    public float gravity = 9.81f;

    [Tooltip("Ignore stick values smaller than this, so a resting stick doesn't drift.")]
    public float deadZone = 0.15f;

    [Tooltip("On: stick forward walks where the flystick points. Off: toward the front wall. " +
             "CAVE only; in the Editor WASD walks where the view faces.")]
    public bool moveRelativeToWand = true;

    [Tooltip("Which of the flystick's own axes points where the player aims it. Check 'Wand forward' " +
             "on WandDebugDisplay while pointing at the front wall: if it doesn't read about 0, 0, 1, " +
             "try another axis here.")]
    public WandAxis wandPointingAxis = WandAxis.Forward;

    [Tooltip("When the flystick points almost straight up or down, its flat direction is unreliable. " +
             "Below this much horizontal pointing (0 to 1), it keeps the last good direction.")]
    [Range(0.05f, 0.9f)] public float minFlatPointing = 0.3f;

    [Header("Turning")]
    [Tooltip("Smooth: stick left/right turns continuously. Snap: each flick turns a fixed angle. " +
             "None: stick left/right steps sideways instead.")]
    public TurnMode turnMode = TurnMode.Smooth;
    public float turnSpeed = 45f;         // degrees per second, Smooth
    public float snapAngle = 30f;         // degrees per flick, Snap

    [Header("Desktop testing, no CAVE needed")]
    public bool desktopControls = true;
    public float mouseTurnSpeed = 3f;
    [Tooltip("Editor only, when there is no head tracking: lifts the view to this eye height " +
             "while the collision capsule stays on the floor.")]
    public float desktopEyeHeight = 1.7f;

    [Serializable]
    public struct RigState
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    enum RigChannel : int { State = 4204 }

    CharacterController body;
    float fallSpeed;
    bool ready, isServer, isClient, snapReady = true;
    Vector3 lastWandForward = Vector3.zero;
    RigState received;
    float lastReceivedTime = -999f;

    public bool ReceivingFromServer => isClient && Time.unscaledTime - lastReceivedTime < 0.5f;

    bool DesktopMode => desktopControls && Application.isEditor && !CaveRig.IsCluster;

    void Awake()
    {
        body = GetComponent<CharacterController>();
        body.radius = bodyRadius;
        body.height = bodyHeight;
        body.center = new Vector3(0f, bodyHeight / 2f, 0f);
        body.skinWidth = 0.05f;
    }

    IEnumerator Start()
    {
        // MiddleVR copies the Main Camera to make each wall's camera, which can copy this script
        // too. Only the object that MVRManager is attached to should move the player.
        var manager = FindAnyObjectByType<MVRManagerScript>();
        if (manager != null && !manager.transform.IsChildOf(transform))
        {
            body.enabled = false;
            enabled = false;
            yield break;
        }

        // Give MiddleVR a moment to create its nodes.
        float timeout = 2f;
        while (timeout > 0f && CaveRig.Wand == null)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        ResolveClusterRole();

        // Editor without head tracking: MiddleVR's cameras sit at the floor. Lift them.
        if (DesktopMode && CaveRig.Head == null && manager != null)
            manager.transform.localPosition = new Vector3(0f, desktopEyeHeight, 0f);

        // Don't let the rig's own colliders (wand, sword in hand) block the capsule.
        foreach (var c in GetComponentsInChildren<Collider>(true))
            if (c != body) Physics.IgnoreCollision(body, c);

        Debug.Log($"PlayerController: head = {Name(CaveRig.Head)}, wand = {Name(CaveRig.Wand)}");
        ready = true;
    }

    // For WandDebugDisplay.
    public int MessagesSent { get; private set; }
    public int MessagesReceived { get; private set; }

    // Decide server or client once MiddleVR's cluster is ready (it may not be yet at startup).
    bool roleResolved;
    void ResolveClusterRole()
    {
        if (roleResolved || !CaveRig.IsCluster) return;
        roleResolved = true;
        isServer = CaveRig.IsClusterServer;
        isClient = CaveRig.IsClusterClient;
        // Registered on every machine, the server too (it ignores its own messages).
        Cluster.AddMessageHandler(
            null,
            (RigState state) =>
            {
                MessagesReceived++;
                if (isServer) return;
                received = state;
                lastReceivedTime = Time.unscaledTime;
            },
            channel: RigChannel.State);
        Debug.Log($"PlayerController: cluster role = {(isServer ? "server" : "client")}");
    }

    void OnDestroy()
    {
        if (roleResolved) Cluster.RemoveMessageHandler(null, channel: RigChannel.State);
    }

    void Update()
    {
        if (!ready) return;
        ResolveClusterRole();
        Move();

        // Send in Update, not LateUpdate, so MiddleVR includes it in this frame's messages.
        if (isServer)
        {
            var state = new RigState { position = transform.position, rotation = transform.rotation };
            Cluster.BroadcastMessage(null, state, channel: RigChannel.State);
            MessagesSent++;
        }
    }

    void Move()
    {
        // Walls that hear from the server don't simulate; LateUpdate copies the server instead.
        if (ReceivingFromServer)
        {
            if (body.enabled) body.enabled = false;
            return;
        }
        if (!body.enabled) body.enabled = true;

        float dt = DeltaTime();
        Transform head = CaveRig.Head;
        Transform wand = CaveRig.Wand;

        // Keep the capsule under the head, wherever the player stands inside the CAVE.
        if (head != null)
        {
            Vector3 local = transform.InverseTransformPoint(head.position);
            body.center = new Vector3(local.x, bodyHeight / 2f, local.z);
        }

        Vector2 stick = ReadStick();
        Vector2 move = stick;

        // Turning, around the player's head so they don't slide sideways while turning.
        float turn = 0f;
        if (DesktopMode)
        {
            turn = DesktopTurn() * mouseTurnSpeed;
        }
        else if (turnMode == TurnMode.Smooth)
        {
            turn = stick.x * turnSpeed * dt;
            move.x = 0f;
        }
        else if (turnMode == TurnMode.Snap)
        {
            move.x = 0f;
            if (Mathf.Abs(stick.x) > 0.7f && snapReady) { turn = Mathf.Sign(stick.x) * snapAngle; snapReady = false; }
            else if (Mathf.Abs(stick.x) < 0.3f) snapReady = true;
        }

        if (turn != 0f)
        {
            Vector3 pivot = head != null ? head.position : transform.position;
            transform.RotateAround(pivot, Vector3.up, turn);
            Physics.SyncTransforms();   // tell the CharacterController we moved it directly
        }

        // Walking direction: where the flystick points, or the way the player faces.
        Vector3 forward = transform.forward;
        if (!DesktopMode && moveRelativeToWand && wand != null)
            forward = WandWalkDirection(wand, forward);

        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 velocity = (forward * move.y + right * move.x) * speed;

        // Simple gravity so the player stays on the floor.
        fallSpeed = body.isGrounded ? 0f : fallSpeed + gravity * dt;
        velocity += Vector3.down * fallSpeed;

        body.Move(velocity * dt);
    }

    // Where the flystick points, flattened onto the floor.
    Vector3 WandWalkDirection(Transform wand, Vector3 fallback)
    {
        Vector3 pointing;
        switch (wandPointingAxis)
        {
            case WandAxis.Back: pointing = -wand.forward; break;
            case WandAxis.Up: pointing = wand.up; break;
            case WandAxis.Down: pointing = -wand.up; break;
            case WandAxis.Right: pointing = wand.right; break;
            case WandAxis.Left: pointing = -wand.right; break;
            default: pointing = wand.forward; break;
        }

        // Pointing at the floor or the ceiling: keep walking the way it last pointed, instead of
        // swinging wildly or snapping back to the front wall.
        Vector3 flat = Vector3.ProjectOnPlane(pointing, Vector3.up);
        if (flat.magnitude >= minFlatPointing) lastWandForward = flat.normalized;
        return lastWandForward != Vector3.zero ? lastWandForward : fallback;
    }

    void LateUpdate()
    {
        if (!ready || isServer) return;
        if (ReceivingFromServer)
            transform.SetPositionAndRotation(received.position, received.rotation);
    }

    Vector2 ReadStick()
    {
        if (DesktopMode) return DesktopStick();
        if (MVR.DeviceMgr == null) return Vector2.zero;

        var stick = new Vector2(
            MVR.DeviceMgr.GetWandHorizontalAxisValue(),
            MVR.DeviceMgr.GetWandVerticalAxisValue());

        return stick.magnitude < deadZone ? Vector2.zero : Vector2.ClampMagnitude(stick, 1f);
    }

    // MiddleVR's delta time is the same on every machine, which keeps them in step.
    float DeltaTime()
    {
        float d = MVRManagerScript.GetDeltaTime();
        return d > 0f ? d : Time.deltaTime;
    }

    Vector2 DesktopStick()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return Vector2.zero;
        float x = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
        float y = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
        return new Vector2(x, y);
#else
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
    }

    float DesktopTurn()
    {
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        if (m == null || !m.rightButton.isPressed) return 0f;
        return m.delta.ReadValue().x * 0.1f;
#else
        return Input.GetMouseButton(1) ? Input.GetAxis("Mouse X") : 0f;
#endif
    }

    static string Name(Transform t) => t != null ? t.name : "none";
}