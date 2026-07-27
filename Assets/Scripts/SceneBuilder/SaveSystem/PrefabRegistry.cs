
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

//Mainly gets used in Unity 

namespace SaveSystem
{
    [CreateAssetMenu(menuName = "Scenario Editor/Prefab Registry", fileName = "PrefabRegistry")]

    public sealed class PrefabRegistry : ScriptableObject
    {
        [System.Serializable]
        private struct Entry
        {
            public string prefabId;
            public GameObject prefab;
        }

        [SerializeField] private string[] _scanFolders = { "Assets/Models & Prefabs/" }; //Allow multiple directories, maybe need recursive for folders
        [SerializeField] private List<Entry> _entries = new();

        private Dictionary<string, GameObject> _lookUp; //Search availables

        private void BuildLookUp()
        {
            _lookUp = new Dictionary<string, GameObject>(_entries.Count);
            foreach(var e in _entries)
            {
                if (string.IsNullOrEmpty(e.prefabId) || e.prefab == null) continue;
                _lookUp[e.prefabId] = e.prefab; //Register the prefab into it 
            }
        }

        private void OnEnable() => _lookUp = null; // rebuild lazily after domain reload
        private void OnDisable() => _lookUp = null;

        public GameObject Get(string prefabID)
        {
            if (_lookUp == null) BuildLookUp();
            return _lookUp.TryGetValue(prefabID, out var p) ? p : null; //Temp
        }

#if UNITY_EDITOR
        [ContextMenu("Rescan")]
        public void Rescan()
        {
            _entries.Clear();

            string[] guids = AssetDatabase.FindAssets("t:Prefab", _scanFolders);
            foreach (string guid in guids) { 
            
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!prefab) continue;
                if (!prefab.GetComponent<SaveableEntity>()) continue;

                _entries.Add(new Entry { prefabId = guid, prefab = prefab });
            }

            _lookUp = null;
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PrefabRegistry] Registered {_entries.Count} saveable prefab(s).", this);
        }
#endif

    }
}

