using UnityEngine;
using UnityEditor;

namespace ScenarioEditor.EditorTools
{
    /// <summary>
    /// Editor utility that ensures every object carrying a SelectableProp also has
    /// a collider, so raycast picking always works. Adds colliders intelligently:
    ///
    ///   - Already has ANY collider (on the root or a child)  -> skip.
    ///   - Has a SkinnedMeshRenderer (characters/victims)     -> add a BoxCollider
    ///       sized to the renderer bounds (MeshCollider can't use skinned meshes).
    ///   - Has a readable/static MeshFilter mesh              -> add a MeshCollider.
    ///   - Otherwise (meshes only on children, or no mesh)     -> add a BoxCollider
    ///       sized to the combined renderer bounds.
    ///
    /// Why it's Editor-only: it bakes real components into your scene objects and
    /// prefabs (undoable, saved with the scene) instead of creating them every play.
    ///
    /// Menu: Tools > Scenario Editor > Add Colliders To Selectable Props
    ///   - "(Scene)"     processes every SelectableProp in the open scene(s).
    ///   - "(Selection)" processes only the currently selected objects.
    /// </summary>
    public static class SelectablePropColliderTool
    {
        private const string MenuRoot = "Tools/Scenario Editor/";

        [MenuItem(MenuRoot + "Add Colliders To Selectable Props (Scene)")]
        private static void AddToScene()
        {
            // Find all SelectableProps in the loaded scene(s), including inactive.
            var props = Object.FindObjectsByType<SelectableProp>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            ProcessProps(props, "Add Colliders (Scene)");
        }

        [MenuItem(MenuRoot + "Add Colliders To Selectable Props (Selection)")]
        private static void AddToSelection()
        {
            var collected = new System.Collections.Generic.List<SelectableProp>();
            foreach (var go in Selection.gameObjects)
                collected.AddRange(go.GetComponentsInChildren<SelectableProp>(true));

            ProcessProps(collected.ToArray(), "Add Colliders (Selection)");
        }

        // Validate: only enable the Selection menu item when something is selected.
        [MenuItem(MenuRoot + "Add Colliders To Selectable Props (Selection)", true)]
        private static bool ValidateAddToSelection() => Selection.gameObjects.Length > 0;

        private static void ProcessProps(SelectableProp[] props, string undoLabel)
        {
            if (props == null || props.Length == 0)
            {
                Debug.Log("[ColliderTool] No SelectableProp objects found.");
                return;
            }

            int added = 0, skipped = 0;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();

            foreach (var prop in props)
            {
                if (prop == null) continue;
                GameObject go = prop.gameObject;

                // Skip if this prop already has a collider anywhere in its hierarchy.
                if (go.GetComponentInChildren<Collider>(true) != null)
                {
                    skipped++;
                    continue;
                }

                Collider addedCollider = AddBestCollider(go);
                if (addedCollider != null)
                {
                    // Note: AddBestCollider uses Undo.AddComponent, which already
                    // records the creation for undo. No extra registration needed.
                    EditorUtility.SetDirty(go);
                    added++;
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"[ColliderTool] Done. Added: {added}, Skipped (already had collider): {skipped}, Total props: {props.Length}");
        }

        /// <summary>
        /// Chooses and adds the most appropriate collider for the given prop root.
        /// Returns the component added, or null if nothing could be added.
        /// </summary>
        private static Collider AddBestCollider(GameObject go)
        {
            // Characters / victims: skinned meshes can't drive a MeshCollider.
            var skinned = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skinned != null)
                return AddBoundsBox(go);

            // A mesh sitting directly on the root -> exact MeshCollider.
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var mc = Undo.AddComponent<MeshCollider>(go);
                mc.sharedMesh = mf.sharedMesh;
                // convex=false is fine for static props; leave non-convex for accuracy.
                return mc;
            }

            // Meshes only on children, or no mesh at all -> bounds box on the root.
            return AddBoundsBox(go);
        }

        /// <summary>
        /// Adds a BoxCollider to the root sized to the combined world-space bounds
        /// of all child renderers, converted back into the root's local space.
        /// </summary>
        private static Collider AddBoundsBox(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            var box = Undo.AddComponent<BoxCollider>(go);

            if (renderers.Length == 0)
            {
                // No renderers to measure: leave a unit box the user can resize.
                box.size = Vector3.one;
                return box;
            }

            // Build combined bounds in world space.
            Bounds world = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                world.Encapsulate(renderers[i].bounds);

            // Convert world center/size into the root's local space so the box
            // stays correct under rotation/scale.
            Transform t = go.transform;
            box.center = t.InverseTransformPoint(world.center);

            // Size: divide world size by lossy scale to get local size.
            Vector3 ls = t.lossyScale;
            box.size = new Vector3(
                Mathf.Abs(ls.x) > 1e-5f ? world.size.x / ls.x : world.size.x,
                Mathf.Abs(ls.y) > 1e-5f ? world.size.y / ls.y : world.size.y,
                Mathf.Abs(ls.z) > 1e-5f ? world.size.z / ls.z : world.size.z);

            return box;
        }
    }
}
