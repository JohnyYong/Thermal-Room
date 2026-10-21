using System;
using MiddleVR;
using MiddleVR.Unity;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class MVR_Grip : MonoBehaviour
{
    [Header("Parts")]
    public Collider gripCollider;

    [Tooltip("Solid (not trigger) collider around the whole imager. Only switched on " +
             "while it is not held, so it can rest on the floor or a table.")]
    public Collider physicsCollider;

    [Header("Grabbing in the CAVE")]
    [Tooltip("Flystick button that holds the imager. If Unity reports a type error on " +
             "the line that uses it, change 'uint' to 'int' here.")]
    public uint grabButton = 0;

    [Tooltip("How close the hand must be to the grip to grab it, in metres.")]
    public float grabRadius = 0.12f;

    [Tooltip("A wall that has lost the server accepts a grab this many times further away, " +
             "since its copy of a dropped imager may have landed slightly differently.")]
    public float lostServerGrabMultiplier = 3f;

    [Tooltip("Where the player's hand is, relative to the flystick's tracked point.")]
    public Vector3 handOffset = Vector3.zero;

    [Header("How it sits in the hand")]
    public Vector3 holdPosition = Vector3.zero;
    public Vector3 holdRotation = Vector3.zero;

    public bool IsHeld { get; private set; }

    /// <summary>Fired on every machine when the imager is picked up or put down.
    /// Hook the thermal view on/off to this if you want it tied to holding.</summary>
    public event Action<bool> HeldChanged;

    [Serializable]
    public struct ImagerState
    {
        public Vector3 position;
        public Quaternion rotation;
        public int held;
    }


    enum ImagerChannel : int { State = 4202 }

    Rigidbody rb;
    Transform originalParent;
    bool wasHolding;

    bool roleResolved, isServer, isClient;
    ImagerState received;
    float lastReceivedTime = -999f;
    float lastDropTime = -999f;
    int disagreeFrames;

    bool ReceivingFromServer => isClient && Time.unscaledTime - lastReceivedTime < 0.5f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        originalParent = transform.parent;

        if (gripCollider == null)
            Debug.LogWarning("ThermalImagerGrip: no Grip Collider assigned, so it can't be grabbed.", this);
        if (physicsCollider == null)
            Debug.LogWarning("ThermalImagerGrip: no Physics Collider assigned, so a dropped imager " +
                             "will fall through the floor.", this);
    }

    void Start()
    {
        SetPhysics(true);
        ResolveClusterRole();
    }

    // MiddleVR may still be setting up the cluster when Start runs, so keep checking
    // until it's ready, then decide once.
    void ResolveClusterRole()
    {
        if (roleResolved || !CaveRig.IsCluster) return;
        roleResolved = true;

        isServer = CaveRig.IsClusterServer;
        isClient = CaveRig.IsClusterClient;

        // Registered on every machine, the server included (it ignores its own messages).
        Cluster.AddMessageHandler(
            null,
            (ImagerState state) =>
            {
                if (isServer) return;
                received = state;
                lastReceivedTime = Time.unscaledTime;
            },
            channel: ImagerChannel.State);
    }

    void OnDestroy()
    {
        if (roleResolved) Cluster.RemoveMessageHandler(null, channel: ImagerChannel.State);
    }

    void Update()
    {
        ResolveClusterRole();
        UpdateHand();

        // Send in Update, not LateUpdate: MiddleVR may already have packed up this
        // frame's messages by the time LateUpdate runs.
        if (isServer) SendState();
    }

    void UpdateHand()
    {
        Transform hand = CaveRig.Wand;
        if (hand == null || gripCollider == null) return;

        bool holding = MVR.DeviceMgr != null && MVR.DeviceMgr.IsWandButtonPressed(grabButton);
        bool justPressed = holding && !wasHolding;
        wasHolding = holding;

        if (!IsHeld && justPressed)
        {
            float limit = grabRadius;
            if (isClient && !ReceivingFromServer) limit *= lostServerGrabMultiplier;
            if (HandToGrip(hand) <= limit) Grab(hand);
        }
        else if (IsHeld && !holding)
        {
            Drop();
        }
    }

    void LateUpdate()
    {
        if (isServer) return;

        if (ReceivingFromServer)
        {
            ApplyState(received);
        }
        else if (isClient && !IsHeld && rb.isKinematic)
        {
            // Lost the server while loose: let this wall's physics take over so the
            // imager doesn't hang frozen in mid-air.
            SetPhysics(true);
        }
    }

    void SendState()
    {
        var state = new ImagerState
        {
            position = transform.position,
            rotation = transform.rotation,
            held = IsHeld ? 1 : 0
        };
        Cluster.BroadcastMessage(null, state, channel: ImagerChannel.State);
    }

    void ApplyState(ImagerState state)
    {
        bool serverHeld = state.held != 0;
        // Right after letting go, the server's messages still say "held" for a frame
        // or two; don't let that snap the imager back into the hand.
        bool justDropped = Time.unscaledTime - lastDropTime < 0.25f;

        if (serverHeld && !IsHeld && !justDropped && CaveRig.Wand != null)
        {
            Grab(CaveRig.Wand);
            disagreeFrames = 0;
        }
        else
        {
            // Confirm a drop over a few frames, since messages arrive a frame or two late.
            disagreeFrames = !serverHeld && IsHeld ? disagreeFrames + 1 : 0;
            if (disagreeFrames >= 5) { Drop(); disagreeFrames = 0; }
        }

        if (!IsHeld)
        {
            // Loose: copy where the server's physics put it rather than simulating here.
            SetPhysics(false);
            transform.SetPositionAndRotation(state.position, state.rotation);
        }
        // Held: stay on this wall's own copy of the tracked hand, so there's no lag.
    }

    // Distance from the hand to the nearest point on the grip collider.
    public float HandToGrip(Transform hand)
    {
        Vector3 handPoint = hand.TransformPoint(handOffset);
        return Vector3.Distance(handPoint, gripCollider.ClosestPoint(handPoint));
    }

    void Grab(Transform hand)
    {
        IsHeld = true;
        SetPhysics(false);
        transform.SetParent(hand, false);
        transform.localPosition = holdPosition;
        transform.localRotation = Quaternion.Euler(holdRotation);
        HeldChanged?.Invoke(true);
    }

    void Drop()
    {
        IsHeld = false;
        lastDropTime = Time.unscaledTime;
        transform.SetParent(originalParent, true);
        SetPhysics(true);

        // No throwing: just let it fall.
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        HeldChanged?.Invoke(false);
    }

    void SetPhysics(bool on)
    {
        rb.isKinematic = !on;
        rb.useGravity = on;
        rb.interpolation = on ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        if (physicsCollider != null) physicsCollider.enabled = on;
    }
}