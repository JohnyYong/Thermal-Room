using System.IO;
using UnityEditor;
using UnityEngine;

public class RoomObjectGenerator : EditorWindow
{
    private string prefabFolder = "Assets/Models/RoomObjects";
    private string outputFolder = "Assets/ScriptableObjects/RoomObjects";
    private RoomObjectDatabase targetDatabase;
    private RoomObjectCategory defaultCategory = RoomObjectCategory.Seatings;
    private bool autoGenerateIcons = true;
    private int iconSize = 256;

    [MenuItem("Room Editor/Generate Room Objects from Prefabs")]
    public static void ShowWindow() =>
        GetWindow<RoomObjectGenerator>("Room Object Generator");

    private void OnGUI()
    {
        GUILayout.Label("Room Object Generator", EditorStyles.boldLabel);
        EditorGUILayout.Space(6);

        prefabFolder = EditorGUILayout.TextField("Prefab Folder", prefabFolder);
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
        defaultCategory = (RoomObjectCategory)EditorGUILayout.EnumPopup("Default Category", defaultCategory);
        targetDatabase = (RoomObjectDatabase)EditorGUILayout.ObjectField(
            "Add to Database", targetDatabase, typeof(RoomObjectDatabase), false);

        EditorGUILayout.Space(4);
        GUILayout.Label("Icon Generation", EditorStyles.boldLabel);
        autoGenerateIcons = EditorGUILayout.Toggle("Auto-Generate Icons", autoGenerateIcons);
        if (autoGenerateIcons)
            iconSize = EditorGUILayout.IntField("Icon Size (px)", iconSize);

        EditorGUILayout.Space(8);

        if (GUILayout.Button("Generate", GUILayout.Height(32)))
            Generate();

        EditorGUILayout.HelpBox(
            "Each prefab in the folder becomes a RoomObject asset.\n" +
            "Existing assets with the same name are skipped.\n" +
            "Icons are captured from an angled isometric camera snapshot.",
            MessageType.Info);
    }

    private void Generate()
    {
        if (!AssetDatabase.IsValidFolder(prefabFolder))
        {
            EditorUtility.DisplayDialog("Error", $"Prefab folder not found:\n{prefabFolder}", "OK");
            return;
        }

        if (!AssetDatabase.IsValidFolder(outputFolder))
        {
            Directory.CreateDirectory(outputFolder);
            AssetDatabase.Refresh();
        }

        string iconFolder = outputFolder + "/Icons";
        if (autoGenerateIcons && !AssetDatabase.IsValidFolder(iconFolder))
        {
            Directory.CreateDirectory(iconFolder);
            AssetDatabase.Refresh();
        }

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { prefabFolder });
        int created = 0, skipped = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string assetName = Path.GetFileNameWithoutExtension(path);
            string outPath = $"{outputFolder}/{assetName}.asset";

            if (File.Exists(outPath)) { skipped++; continue; }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var obj = CreateInstance<RoomObject>();

            obj.DisplayName = ObjectNames.NicifyVariableName(assetName);
            obj.objectType = GuessType(assetName);
            obj.objectCategory = GuessCategory(obj.objectType, defaultCategory);
            obj.prefab = prefab;
            obj.widthTiles = 1;
            obj.heightTiles = 1;
            obj.canRotate = true;
            obj.requireWallPlacement = false;
            obj.isWalkable = false;
            obj.isStackable = false;
            obj.maxOccupants = 30;
            obj.weight = 10f;

            if (autoGenerateIcons)
                obj.icon = CaptureIcon(prefab, assetName, iconFolder, iconSize);

            AssetDatabase.CreateAsset(obj, outPath);

            if (autoGenerateIcons)
            {
                var saved = AssetDatabase.LoadAssetAtPath<RoomObject>(outPath);
                if (saved != null)
                {
                    saved.icon = AssetDatabase.LoadAssetAtPath<Sprite>($"{iconFolder}/{assetName}_icon.png");
                    EditorUtility.SetDirty(saved);
                }
            }

            if (targetDatabase != null)
                targetDatabase.AddEntry(obj);

            created++;
        }

        // Single save pass at the end for everything
        if (targetDatabase != null)
            EditorUtility.SetDirty(targetDatabase);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Done",
            $"Created: {created}   Skipped (already exist): {skipped}", "OK");
    }

    private static Sprite CaptureIcon(GameObject prefab, string name, string iconFolder, int size)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.position = new Vector3(9999f, 9999f, 9999f);

        var bounds = GetBounds(instance);
        float maxExtent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);

        // Angled isometric camera — raise y for more top-down, lower for more side-on
        Vector3 offset = new Vector3(1f, 1.5f, 1f).normalized * (maxExtent * 3f);

        var camGo = new GameObject("_IconCam");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = maxExtent * 1.2f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.clear;
        cam.cullingMask = ~0;
        camGo.transform.position = bounds.center + offset;
        camGo.transform.LookAt(bounds.center);

        var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        tex.Apply();

        RenderTexture.active = null;
        cam.targetTexture = null;
        DestroyImmediate(rt);
        DestroyImmediate(camGo);
        DestroyImmediate(instance);

        // Write the PNG to disk
        string pngPath = $"{iconFolder}/{name}_icon.png";
        File.WriteAllBytes(pngPath, tex.EncodeToPNG());
        DestroyImmediate(tex);

        // Import as Sprite — SaveAndReimport() blocks until Unity is done
        AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
    }

    private static Bounds GetBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(go.transform.position, Vector3.one);

        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    // ── Type / category guessing from prefab name ────────────────────────────

    private static RoomObjectType GuessType(string name)
    {
        string n = name.ToLower();

        if (Contains(n, "human", "person", "npc", "character", "man", "woman"))
            return RoomObjectType.Human;
        if (Contains(n, "bed", "mattress", "bunk", "single", "double", "king", "queen"))
            return RoomObjectType.Bed;
        if (Contains(n, "chair", "sofa", "couch", "stool", "bench", "armchair", "seat"))
            return RoomObjectType.Chair;
        if (Contains(n, "table", "desk", "counter", "coffee", "nightstand", "dining"))
            return RoomObjectType.Table;
        if (Contains(n, "cushion", "pillow", "rug", "carpet", "mat"))
            return RoomObjectType.Cushion;
        if (Contains(n, "closet", "wardrobe", "shelf", "drawer", "cabinet", "dresser", "bookcase", "storage"))
            return RoomObjectType.Closets;
        if (Contains(n, "kitchen", "fridge", "oven", "sink", "microwave", "stove", "kettle", "toaster"))
            return RoomObjectType.Kitchen;

        return RoomObjectType.Chair; // fallback
    }

    private static RoomObjectCategory GuessCategory(RoomObjectType t, RoomObjectCategory fallback)
    {
        return t switch
        {
            RoomObjectType.Human => RoomObjectCategory.People,
            RoomObjectType.Chair => RoomObjectCategory.Seatings,
            RoomObjectType.Table => RoomObjectCategory.Tables,
            RoomObjectType.Closets => RoomObjectCategory.Storage,
            RoomObjectType.Kitchen => RoomObjectCategory.Kitchen,
            _ => fallback
        };
    }

    private static bool Contains(string name, params string[] keywords)
    {
        foreach (var k in keywords)
            if (name.Contains(k)) return true;
        return false;
    }
}