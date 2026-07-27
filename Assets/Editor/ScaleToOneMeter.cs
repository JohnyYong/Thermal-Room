using UnityEngine;
using UnityEditor;

public class ScaleToOneMeter : EditorWindow
{
    private enum ScaleMode { LargestDimension, Height, Width, Depth }
    private ScaleMode mode = ScaleMode.Height;
    private float targetSize = 1f;
    private bool topLevelOnly = true;

    [MenuItem("Tools/Scale To 1 Meter")]
    public static void ShowWindow()
    {
        GetWindow<ScaleToOneMeter>("Scale To 1m");
    }

    private void OnGUI()
    {
        GUILayout.Label("Scale ALL objects in the scene so chosen dimension = target size", EditorStyles.wordWrappedLabel);

        mode = (ScaleMode)EditorGUILayout.EnumPopup("Reference Dimension", mode);
        targetSize = EditorGUILayout.FloatField("Target Size (units)", targetSize);
        topLevelOnly = EditorGUILayout.Toggle("Top-Level Only (skip children)", topLevelOnly);

        EditorGUILayout.HelpBox(
            topLevelOnly
                ? "Only root objects in the Hierarchy will be scaled (recommended — avoids double-scaling nested children)."
                : "EVERY object with a renderer will be individually scaled, including children. This can produce unexpected results for nested objects.",
            MessageType.Info);

        if (GUILayout.Button("Apply to All Objects in Scene"))
        {
            if (EditorUtility.DisplayDialog("Confirm",
                "This will modify the scale of all matching GameObjects in the open scene(s). Continue?",
                "Yes", "Cancel"))
            {
                ApplyScaling();
            }
        }
    }

    private void ApplyScaling()
    {
        GameObject[] allObjects = topLevelOnly
            ? GetRootObjects()
            : FindAllObjectsWithRenderers();

        int processed = 0;

        foreach (GameObject go in allObjects)
        {
            Bounds bounds = GetBounds(go);

            if (bounds.size == Vector3.zero)
                continue; // skip objects with no renderers

            float currentDimension = mode switch
            {
                ScaleMode.LargestDimension => Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z),
                ScaleMode.Height => bounds.size.y,
                ScaleMode.Width => bounds.size.x,
                ScaleMode.Depth => bounds.size.z,
                _ => Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z)
            };

            if (currentDimension <= 0f)
                continue;

            float multiplier = targetSize / currentDimension;

            Undo.RecordObject(go.transform, "Scale To One Meter");
            go.transform.localScale *= multiplier;

            Debug.Log($"[ScaleToOneMeter] '{go.name}' scaled by {multiplier:F4} (was {currentDimension:F4}, now {targetSize})");
            processed++;
        }

        Debug.Log($"[ScaleToOneMeter] Done. {processed} object(s) scaled.");
    }

    // Root objects in the active scene (top of hierarchy)
    private GameObject[] GetRootObjects()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        return scene.GetRootGameObjects();
    }

    // Every object in the scene that has at least one Renderer
    private GameObject[] FindAllObjectsWithRenderers()
    {
        Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        var unique = new System.Collections.Generic.HashSet<GameObject>();

        foreach (var r in renderers)
            unique.Add(r.gameObject);

        var result = new GameObject[unique.Count];
        unique.CopyTo(result);
        return result;
    }

    private Bounds GetBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
            return new Bounds(go.transform.position, Vector3.zero);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds;
    }
}