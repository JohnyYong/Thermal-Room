#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ScenarioEditor.EditorTools
{
    /// <summary>
    /// Editor-only tool. Menu: Tools → Bake Victim Icons.
    /// Pick a folder of prefabs (that folder only — no subfolders), and it
    /// snapshots each prefab via AssetPreview into "PrefabName.png", imported as
    /// a Sprite. Output defaults to "<source>/Textures" but can be overridden.
    ///
    /// MUST live in an /Editor/ folder. Uses UnityEditor APIs; it is NOT part of
    /// the runtime module and can be deleted without affecting the build.
    ///
    /// AssetPreview is asynchronous (null until rendered), so baking is driven off
    /// EditorApplication.update and polls each prefab until ready — not a blocking
    /// loop, which is the usual cause of blank thumbnails.
    /// </summary>
    public class PrefabIconMaker : EditorWindow
    {
        private const string PrefSource = "PrefabIconMaker.Source";
        private const string PrefOverride = "PrefabIconMaker.Override";
        private const string PrefOutput = "PrefabIconMaker.Output";
        private const int TimeoutTicks = 300;

        private DefaultAsset _sourceFolder;
        private bool _overrideOutput;
        private string _outputOverride = "";

        // batch state
        private Queue<GameObject> _queue;
        private GameObject _current;
        private int _waited, _ok, _fail, _total;
        private readonly List<string> _written = new();
        private bool _baking;
        private string _status = "";

        [MenuItem("Tools/Bake Prefab Icons")]
        private static void Open()
        {
            var w = GetWindow<PrefabIconMaker>("Bake Prefab Icons");
            w.minSize = new Vector2(380, 200);
            w.Show();
        }

        private void OnEnable()
        {
            string savedPath = EditorPrefs.GetString(PrefSource, "");
            if (!string.IsNullOrEmpty(savedPath) && AssetDatabase.IsValidFolder(savedPath))
                _sourceFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(savedPath);

            _overrideOutput = EditorPrefs.GetBool(PrefOverride, false);
            _outputOverride = EditorPrefs.GetString(PrefOutput, "");
        }

        private void OnDisable()
        {
            // Stop a bake in progress if the window is closed.
            EditorApplication.update -= Process;
            _baking = false;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Drag a folder from the Project window. Only prefabs directly " +
                                    "inside it are baked (no subfolders).", MessageType.None);

            EditorGUI.BeginChangeCheck();
            _sourceFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Prefab Folder", _sourceFolder, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(PrefSource, _sourceFolder ? AssetDatabase.GetAssetPath(_sourceFolder) : "");

            string sourcePath = _sourceFolder ? AssetDatabase.GetAssetPath(_sourceFolder) : "";
            bool validSource = !string.IsNullOrEmpty(sourcePath) && AssetDatabase.IsValidFolder(sourcePath);
            if (_sourceFolder && !validSource)
                EditorGUILayout.HelpBox("That asset is not a folder.", MessageType.Error);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _overrideOutput = EditorGUILayout.ToggleLeft(
                "Override output folder (default: <source>/Textures)", _overrideOutput);
            if (EditorGUI.EndChangeCheck()) EditorPrefs.SetBool(PrefOverride, _overrideOutput);

            string outputPath = validSource ? sourcePath + "/Textures" : "";
            if (_overrideOutput)
            {
                EditorGUI.BeginChangeCheck();
                _outputOverride = EditorGUILayout.TextField("Output Folder", _outputOverride);
                if (EditorGUI.EndChangeCheck()) EditorPrefs.SetString(PrefOutput, _outputOverride);
                outputPath = _outputOverride;
            }
            else
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField("Output Folder", outputPath);
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_baking || !validSource))
            {
                if (GUILayout.Button(_baking ? "Baking…" : "Bake Icons", GUILayout.Height(30)))
                    StartBake(sourcePath, outputPath);
            }

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, _baking ? MessageType.Info : MessageType.None);
        }

        private void StartBake(string sourceFolder, string outputFolder)
        {
            if (string.IsNullOrEmpty(outputFolder))
            {
                _status = "No output folder resolved.";
                return;
            }

            // Non-recursive: keep only prefabs whose directory IS the source folder.
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { sourceFolder });
            _queue = new Queue<GameObject>();
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string dir = Path.GetDirectoryName(path).Replace('\\', '/');
                if (dir != sourceFolder) continue; // skip subfolders
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null) _queue.Enqueue(go);
            }

            if (_queue.Count == 0)
            {
                _status = $"No prefabs directly in:\n{sourceFolder}";
                return;
            }

            if (!AssetDatabase.IsValidFolder(outputFolder))
                CreateFolderRecursive(outputFolder);

            _outputResolved = outputFolder;
            _ok = _fail = 0;
            _total = _queue.Count;
            _written.Clear();
            _current = null;
            _waited = 0;
            _baking = true;

            AssetPreview.SetPreviewTextureCacheSize(Mathf.Max(32, _queue.Count + 8));

            EditorApplication.update -= Process;
            EditorApplication.update += Process;
        }

        private string _outputResolved;

        private void Process()
        {
            if (_current == null)
            {
                if (_queue.Count == 0) { Finish(); return; }
                _current = _queue.Dequeue();
                _waited = 0;
            }

            _status = $"Baking {(_ok + _fail + 1)}/{_total}: {_current.name}";
            Repaint();

            Texture2D preview = AssetPreview.GetAssetPreview(_current);

            if (preview == null)
            {
                bool loading = AssetPreview.IsLoadingAssetPreview(_current.GetInstanceID());
                if ((loading || _waited == 0) && _waited < TimeoutTicks)
                {
                    _waited++;
                    return;
                }
                Debug.LogWarning($"[PrefabIconMaker] No preview for '{_current.name}' (timed out).");
                _fail++;
                _current = null;
                return;
            }

            if (SavePng(preview, _current.name, _outputResolved)) _ok++; else _fail++;
            _current = null;
        }

        private bool SavePng(Texture2D preview, string prefabName, string outputFolder)
        {
            try
            {
                Texture2D readable = MakeReadable(preview);
                byte[] png = readable.EncodeToPNG();
                DestroyImmediate(readable);

                string fullPath = $"{outputFolder}/{prefabName}.png";
                File.WriteAllBytes(fullPath, png);
                _written.Add(fullPath);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PrefabIconMaker] Failed to save '{prefabName}': {e.Message}");
                return false;
            }
        }

        private static Texture2D MakeReadable(Texture2D src)
        {
            RenderTexture rt = RenderTexture.GetTemporary(
                src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var readable = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return readable;
        }

        private void Finish()
        {
            EditorApplication.update -= Process;
            _baking = false;
            AssetDatabase.Refresh();

            foreach (string path in _written)
            {
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;
                if (imp.textureType != TextureImporterType.Sprite || !imp.alphaIsTransparency)
                {
                    imp.textureType = TextureImporterType.Sprite;
                    imp.spriteImportMode = SpriteImportMode.Single;
                    imp.alphaIsTransparency = true;
                    imp.SaveAndReimport();
                }
            }

            _status = $"Done. Generated: {_ok}  Failed: {_fail}\nOutput: {_outputResolved}";
            Repaint();

            _queue = null;
            _current = null;
            _written.Clear();
        }

        private static void CreateFolderRecursive(string folder)
        {
            string[] parts = folder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{cur}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
#endif