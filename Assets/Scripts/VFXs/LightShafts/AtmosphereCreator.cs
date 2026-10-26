// Put this file in a folder named "Editor" (e.g. Assets/Editor/AtmosphereCreator.cs).
// Open it from the menu: Tools > Atmosphere Creator.
//
// Requires LightShaft.shader and DustMote.shader in the project.
// Generated assets (materials, meshes, noise texture) are saved to Assets/Atmosphere.
#if UNITY_EDITOR
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class AtmosphereCreator : EditorWindow
{
    const string Folder = "Assets/Atmosphere";
    const string ShaftShaderName = "Custom/VR/LightShaft";
    const string DustShaderName = "Custom/VR/DustMote";

    // Light shaft
    bool autoHeight = true;
    float height = 2.6f;
    float topOffset = 0.02f;
    float topWidth = 0.4f;
    float bottomWidth = 1.2f;
    int planes = 3;

    // Dust inside the shaft
    bool shaftDust = true;
    int shaftDustCount = 60;

    // Room dust
    int roomDustCount = 120;
    Vector3 roomDustFallbackSize = new Vector3(4f, 2.6f, 8f);

    // Shared
    Vector2 dustSize = new Vector2(0.006f, 0.014f);

    Vector2 scroll;

    [MenuItem("Tools/Atmosphere Creator")]
    static void Open() => GetWindow<AtmosphereCreator>("Atmosphere");

    void OnSelectionChange() => Repaint();

    // ------------------------------------------------------------------ UI

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        var sel = Selection.activeTransform;

        EditorGUILayout.HelpBox(
            sel ? $"Selected: {sel.name}" : "Nothing selected. New objects will be placed at the Scene view pivot.",
            MessageType.None);

        // Light shaft
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Light Shaft", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Select a ceiling light fixture, then click Create. The shaft hangs straight down from the fixture's pivot. " +
            "If the fixture has a LightFlicker component, the shaft and its dust are hooked up to it automatically.",
            MessageType.Info);

        autoHeight = EditorGUILayout.Toggle(
            new GUIContent("Auto Height", "Raycasts down to find the floor. The floor needs a collider."), autoHeight);
        height = Mathf.Max(0.1f, EditorGUILayout.FloatField(autoHeight ? "Fallback Height (m)" : "Height (m)", height));
        topOffset = EditorGUILayout.FloatField(
            new GUIContent("Top Offset (m)", "How far below the fixture's pivot the shaft starts."), topOffset);
        topWidth = Mathf.Max(0.01f, EditorGUILayout.FloatField("Top Width (m)", topWidth));
        bottomWidth = Mathf.Max(0.01f, EditorGUILayout.FloatField("Bottom Width (m)", bottomWidth));
        planes = EditorGUILayout.IntSlider(
            new GUIContent("Crossed Planes", "3 is usually enough. More planes = more overdraw."), planes, 2, 6);

        shaftDust = EditorGUILayout.Toggle("Add Dust In Shaft", shaftDust);
        using (new EditorGUI.DisabledScope(!shaftDust))
            shaftDustCount = EditorGUILayout.IntSlider("Shaft Dust Count", shaftDustCount, 10, 200);

        if (GUILayout.Button("Create Light Shaft", GUILayout.Height(28)))
            CreateLightShaft(sel);

        // Room dust
        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Room Dust", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Faint dust floating through a whole room. Select the room's parent object to fill its bounds. " +
            "Keep this subtle: it glows a little everywhere, not only in the light.",
            MessageType.Info);

        roomDustCount = EditorGUILayout.IntSlider("Room Dust Count", roomDustCount, 20, 400);
        roomDustFallbackSize = EditorGUILayout.Vector3Field("Fallback Size (m)", roomDustFallbackSize);

        if (GUILayout.Button("Create Room Dust", GUILayout.Height(28)))
            CreateRoomDust(sel);

        // Shared
        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Shared", EditorStyles.boldLabel);
        dustSize = EditorGUILayout.Vector2Field("Dust Size Min / Max (m)", dustSize);
        dustSize.x = Mathf.Max(0.001f, dustSize.x);
        dustSize.y = Mathf.Max(dustSize.x, dustSize.y);

        EditorGUILayout.Space(6);
        EditorGUILayout.HelpBox(
            $"Materials are created once in {Folder}. To change color or intensity later, edit " +
            "LightShaft_Mat and DustMote_Mat there; every shaft and dust system shares them.",
            MessageType.None);

        EditorGUILayout.EndScrollView();
    }

    // ------------------------------------------------------------------ Creation

    void CreateLightShaft(Transform fixture)
    {
        EnsureFolder();
        var shaftMat = GetShaftMaterial();
        var dustMat = shaftDust ? GetDustMaterial() : null;
        if (!shaftMat || (shaftDust && !dustMat)) return;

        int undoGroup = Undo.GetCurrentGroup();

        Vector3 top = (fixture ? fixture.position : SceneViewPivot()) + Vector3.down * topOffset;
        float h = height;
        if (autoHeight && TryFindFloor(top, fixture, out float floorDist))
            h = floorDist;
        else if (autoHeight)
            Debug.LogWarning("[Atmosphere] No floor collider found below the fixture; using fallback height.");

        var root = new GameObject(fixture ? $"LightShaft ({fixture.name})" : "LightShaft");
        if (fixture && IsUnitScale(fixture.lossyScale))
            root.transform.SetParent(fixture, false);
        root.transform.SetPositionAndRotation(top, Quaternion.identity); // always points straight down

        // Shaft mesh
        var shaft = new GameObject("Shaft", typeof(MeshFilter), typeof(MeshRenderer));
        shaft.transform.SetParent(root.transform, false);
        shaft.GetComponent<MeshFilter>().sharedMesh = GetShaftMesh(h);
        var mr = shaft.GetComponent<MeshRenderer>();
        mr.sharedMaterial = shaftMat;
        ConfigureFxRenderer(mr);
        GameObjectUtility.SetStaticEditorFlags(shaft, 0);

        // Dust inside the shaft, in a cone matching the taper
        Renderer dustRenderer = null;
        if (shaftDust)
        {
            var dust = CreateDustSystem("Dust", root.transform, shaftDustCount, 0.6f, dustMat);
            var shape = dust.shape;
            shape.shapeType = ParticleSystemShapeType.ConeVolume;
            shape.rotation = new Vector3(90f, 0f, 0f); // cone points down
            shape.radius = topWidth * 0.4f;
            shape.radiusThickness = 1f;
            shape.angle = Mathf.Clamp(Mathf.Atan((bottomWidth - topWidth) * 0.5f / h) * Mathf.Rad2Deg, 0f, 80f);
            shape.length = h * 0.9f;
            dustRenderer = dust.GetComponent<ParticleSystemRenderer>();
        }

        Undo.RegisterCreatedObjectUndo(root, "Create Light Shaft");
        HookUpFlicker(fixture, mr, dustRenderer);
        Undo.CollapseUndoOperations(undoGroup);

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        Debug.Log($"[Atmosphere] Created light shaft, height {h:F2} m.");
    }

    void CreateRoomDust(Transform sel)
    {
        EnsureFolder();
        var mat = GetDustMaterial();
        if (!mat) return;

        Bounds b;
        if (!(sel && TryGetBounds(sel, out b)))
            b = new Bounds(sel ? sel.position : SceneViewPivot(), roomDustFallbackSize);

        var ps = CreateDustSystem("Room Dust", null, roomDustCount, 0.15f, mat);
        ps.transform.position = b.center;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = b.size * 0.9f;

        Undo.RegisterCreatedObjectUndo(ps.gameObject, "Create Room Dust");
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = ps.gameObject;
    }

    ParticleSystem CreateDustSystem(string name, Transform parent, int count, float alpha, Material mat)
    {
        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent, false);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 10f;
        main.loop = true;
        main.prewarm = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 15f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(dustSize.x, dustSize.y);
        main.startColor = new Color(1f, 1f, 1f, alpha);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = count / 11.5f; // count / average lifetime keeps it near max

        var shape = ps.shape;
        shape.enabled = true;

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.05f;
        noise.frequency = 0.3f;
        noise.scrollSpeed = 0.1f;
        noise.octaveCount = 1;
        noise.damping = true;
        noise.quality = ParticleSystemNoiseQuality.Low;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f),
                new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f)
            });
        col.color = grad;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sortMode = ParticleSystemSortMode.None;
        r.sharedMaterial = mat;
        ConfigureFxRenderer(r);

        ps.Play();
        return ps;
    }

    // ------------------------------------------------------------------ Assets

    static Material GetShaftMaterial() =>
        GetOrCreateMaterial("LightShaft_Mat", ShaftShaderName, m =>
        {
            m.SetColor("_Color", new Color(0.75f, 0.85f, 1f, 1f));
            m.SetFloat("_Intensity", 0.12f);
            m.SetTexture("_NoiseTex", GetNoiseTexture());
            m.SetVector("_NoiseScroll", new Vector4(0f, 0.015f, 0f, 0f));
        });

    static Material GetDustMaterial() =>
        GetOrCreateMaterial("DustMote_Mat", DustShaderName, m =>
        {
            m.SetColor("_Color", new Color(1f, 0.97f, 0.9f, 1f));
            m.SetFloat("_Intensity", 1f);
        });

    static Material GetOrCreateMaterial(string name, string shaderName, System.Action<Material> init)
    {
        string path = $"{Folder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat) return mat;

        var shader = Shader.Find(shaderName);
        if (!shader)
        {
            EditorUtility.DisplayDialog("Atmosphere Creator",
                $"Shader \"{shaderName}\" not found.\n\nMake sure LightShaft.shader and DustMote.shader are in your " +
                "project and compile without errors (check the Console).", "OK");
            return null;
        }

        mat = new Material(shader) { name = name };
        init(mat);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    Mesh GetShaftMesh(float h)
    {
        string name = string.Format(CultureInfo.InvariantCulture,
            "LightShaftMesh_{0}p_h{1:F2}_t{2:F2}_b{3:F2}", planes, h, topWidth, bottomWidth);
        string path = $"{Folder}/{name}.asset";

        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh) return mesh;

        mesh = BuildShaftMesh(planes, h, topWidth, bottomWidth, 8);
        mesh.name = name;
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // Crossed, tapered planes in a single mesh. Pivot at the top (the fixture), extends down.
    // UV x = 0..1 across each plane, UV y = 0 at the floor, 1 at the top.
    static Mesh BuildShaftMesh(int planeCount, float h, float topW, float bottomW, int rows)
    {
        int vertsPerPlane = (rows + 1) * 2;
        var verts = new Vector3[planeCount * vertsPerPlane];
        var normals = new Vector3[verts.Length];
        var uvs = new Vector2[verts.Length];
        var tris = new int[planeCount * rows * 6];

        int v = 0, t = 0;
        for (int p = 0; p < planeCount; p++)
        {
            float angle = Mathf.PI * p / planeCount; // spread evenly over 180 degrees
            var across = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            var normal = Vector3.Cross(across, Vector3.up).normalized;
            int start = v;

            for (int r = 0; r <= rows; r++)
            {
                float k = r / (float)rows;               // 0 = floor, 1 = top
                float y = -h * (1f - k);
                float halfW = Mathf.Lerp(bottomW, topW, k) * 0.5f;

                verts[v] = -across * halfW + Vector3.up * y; normals[v] = normal; uvs[v] = new Vector2(0f, k); v++;
                verts[v] =  across * halfW + Vector3.up * y; normals[v] = normal; uvs[v] = new Vector2(1f, k); v++;
            }

            for (int r = 0; r < rows; r++)
            {
                int i = start + r * 2;
                tris[t++] = i;     tris[t++] = i + 2; tris[t++] = i + 1;
                tris[t++] = i + 1; tris[t++] = i + 2; tris[t++] = i + 3;
            }
        }

        var mesh = new Mesh { vertices = verts, normals = normals, uv = uvs, triangles = tris };
        mesh.RecalculateBounds();
        return mesh;
    }

    // Tileable grayscale noise, stretched vertically so it reads as streaks of haze along the beam.
    static Texture2D GetNoiseTexture()
    {
        string path = $"{Folder}/LightShaftNoise.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing) return existing;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.R8, true, true)
        {
            name = "LightShaftNoise",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };

        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = x / (float)size, w = y / (float)size;
            float n = 0f, amp = 0.5f, total = 0f;
            for (int o = 0; o < 3; o++)
            {
                int cx = 6 << o, cy = 2 << o; // more cells across than along = vertical streaks
                n += amp * PeriodicValueNoise(u * cx, w * cy, cx, cy, o);
                total += amp;
                amp *= 0.5f;
            }
            float val = Mathf.Lerp(0.35f, 1f, n / total); // never fully black
            px[y * size + x] = new Color(val, val, val, 1f);
        }

        tex.SetPixels(px);
        tex.Apply(true);
        AssetDatabase.CreateAsset(tex, path);
        return tex;
    }

    static float PeriodicValueNoise(float x, float y, int periodX, int periodY, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);

        int xa = ((x0 % periodX) + periodX) % periodX, xb = (xa + 1) % periodX;
        int ya = ((y0 % periodY) + periodY) % periodY, yb = (ya + 1) % periodY;

        float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed);
        float c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 1442695041u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    // ------------------------------------------------------------------ Helpers

    static void ConfigureFxRenderer(Renderer r)
    {
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    static void HookUpFlicker(Transform fixture, params Renderer[] renderers)
    {
        if (!fixture) return;

        var flicker = fixture.GetComponentsInParent<MonoBehaviour>(true)
            .Concat(fixture.GetComponentsInChildren<MonoBehaviour>(true))
            .FirstOrDefault(m => m && m.GetType().Name == "LightFlicker");
        if (!flicker) return;

        var so = new SerializedObject(flicker);
        var list = so.FindProperty("shaftRenderers");
        if (list == null || !list.isArray) return;

        foreach (var r in renderers)
        {
            if (!r) continue;
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = r;
        }
        so.ApplyModifiedProperties();
        Debug.Log($"[Atmosphere] Added shaft renderers to LightFlicker on {flicker.name}.");
    }

    static bool TryFindFloor(Vector3 top, Transform ignore, out float distance)
    {
        Physics.SyncTransforms();
        var hits = Physics.RaycastAll(top + Vector3.down * 0.01f, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits.OrderBy(h => h.distance))
        {
            if (ignore && hit.transform.IsChildOf(ignore)) continue;
            distance = hit.distance + 0.01f;
            return true;
        }
        distance = 0f;
        return false;
    }

    static bool TryGetBounds(Transform root, out Bounds bounds)
    {
        var renderers = root.GetComponentsInChildren<Renderer>()
            .Where(r => !(r is ParticleSystemRenderer)).ToArray();
        bounds = default;
        if (renderers.Length == 0) return false;

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return true;
    }

    static bool IsUnitScale(Vector3 s) =>
        Mathf.Abs(s.x - 1f) < 0.001f && Mathf.Abs(s.y - 1f) < 0.001f && Mathf.Abs(s.z - 1f) < 0.001f;

    static Vector3 SceneViewPivot() =>
        SceneView.lastActiveSceneView ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets", "Atmosphere");
    }
}
#endif
