using MiddleVR;
using UnityEngine;

// Finds MiddleVR's runtime objects (the rig, the wand and the head) so other scripts
// don't depend on each other. MiddleVR creates these when Play starts.
//
// This CAVE's configuration has two users, and every node name starts with the user:
// User0.HandNode follows Flystick0 and User0.HeadNode follows the first head tracker.
// The player is User0.
public static class CaveRig
{
    public static string WandNodeName = "User0.HandNode";
    public static string HeadNodeName = "User0.HeadNode";

    static MVRManagerScript manager;
    static Transform wand;
    static Transform head;

    // Cluster roles. In this CAVE the server drives the operator screen and every wall is a client.
    // If Unity reports that IsServer doesn't exist, check what MVR.ClusterMgr offers in autocomplete.
    public static bool IsCluster => MVR.ClusterMgr != null && MVR.ClusterMgr.IsClusterConfig();
    public static bool IsClusterServer => IsCluster && MVR.ClusterMgr.IsServer();
    public static bool IsClusterClient => IsCluster && !MVR.ClusterMgr.IsServer();

    // MVRManager: the root of the CAVE rig.
    public static Transform Root { get { Find(); return manager != null ? manager.transform : null; } }

    // The node that follows the player's Flystick.
    public static Transform Wand { get { Find(); return wand; } }

    // The node that follows the player's head. Null when there is no head tracking (Editor).
    public static Transform Head { get { Find(); return head; } }

    static void Find()
    {
        if (manager == null) manager = Object.FindAnyObjectByType<MVRManagerScript>();
        if (manager == null) return;

        if (wand == null || !wand.gameObject.activeInHierarchy) wand = FindWand();
        if (head == null) head = FindChild(manager.transform, HeadNodeName) ?? FindChild(manager.transform, "HeadNode");
    }

    static Transform FindWand()
    {
        // 1. The player's hand node from the CAVE configuration.
        var node = FindChild(manager.transform, WandNodeName);
        if (node != null) return node;

        // 2. Standard MiddleVR setups: a node called HandNode.
        node = FindChild(manager.transform, "HandNode");
        if (node != null) return node;

        // 3. MVRWand, if MiddleVR left it switched on (e.g. the Editor's default configuration).
        var mvrWand = FindChild(manager.transform, "MVRWand");
        if (mvrWand != null && mvrWand.gameObject.activeInHierarchy) return mvrWand;

        return null;
    }

    // Any object in the rig by exact name, for debugging.
    public static Transform FindNamed(string exactName)
    {
        Find();
        return manager != null ? FindChild(manager.transform, exactName) : null;
    }

    static Transform FindChild(Transform root, string exactName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == exactName) return t;
        return null;
    }
}