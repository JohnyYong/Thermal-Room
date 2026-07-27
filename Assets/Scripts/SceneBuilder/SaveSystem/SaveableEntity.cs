using UnityEngine;
using System.Collections.Generic;
using System;
using Unity.Collections;



#if UNITY_EDITOR
using UnityEditor;
#endif

//This is the main file which is used to detect what gameobject is to be saveable etcs

namespace SaveSystem
{
    [DisallowMultipleComponent] //Keeping this component to only have one exist (100% singleton)
    public class SaveableEntity : MonoBehaviour
    {
        //[SerializeField]
        //for copy paste


        [SerializeField] private string _prefabId;
        [SerializeField] private string _instanceId; //For this particular instance's unique ID
        [SerializeField] private bool _excludeFromSave; //Just in case

        public string PrefabID => _prefabId;
        public string InstanceId => _instanceId;

        public bool ExcludeFromSave
        {
            get => _excludeFromSave;
            set => _excludeFromSave = value;
        }

        //Checking if object is truly saveable
        public bool IsSaveable => !_excludeFromSave && !string.IsNullOrEmpty(_prefabId) && gameObject.hideFlags == HideFlags.None;

        internal void OverrideInstanceId(string id) => _instanceId = id;

        private void Awake()
        {
            if (string.IsNullOrEmpty(_instanceId))
            {
                _instanceId = Guid.NewGuid().ToString("N");
            }
        }

        public List<ComponentStateDto> CaptureComponents()
        {
            var results = new List<ComponentStateDto>();
            //Need to allocate the number of components here maybe make it with GetComponents

            //Need to think of the children gameobjects as well
            //Make an extra ISaveAbleComponent to list what needs to be saved
            foreach (var c in GetComponentsInChildren<ISaveComponent>(includeInactive: true))
            {

                string json;

                try
                {
                    json = c.CaptureState();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SaveSystem] {c.GetType().Name} threw while capturing: {e.Message}", this);
                    continue;
                }

                if (string.IsNullOrEmpty(json)) continue;

                //Dto (string, json)
                results.Add(new ComponentStateDto
                {
                    type = c.GetType().AssemblyQualifiedName,
                    json = json
                });
            }

            return results; //Temporary value not returned yet
        }

        //Restores component state. Should run after all entities exist in scene first
        public void RestoreComponents(List<ComponentStateDto> states, SaveContext ctx)
        {
            if (states == null || states.Count == 0) return;

            var components = GetComponentsInChildren<ISaveComponent>(includeInactive: true);

            foreach (var state in states)
            {
                Type t = Type.GetType(state.type);
                if (t == null)
                {
                    //Component maybe renames or deleted
                    Debug.LogWarning($"[SaveSystem (Load)] Unknown Component type '{state.type}' skipped", this);
                    continue;
                }

                foreach (var c in components)
                {
                    if (c.GetType() != t) continue;
                    try
                    {
                        c.RestoreState(state.json, ctx);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[SaveSystem (Load)] {t.Name} threw while restoring: {e.Message}", this);
                    }
                    break;

                }
            }
        }

#if UNITY_EDITOR

        private void OnValidate()
        {
            if (Application.isPlaying) return;

            bool isAsset = PrefabUtility.IsPartOfPrefabAsset(gameObject);

            if (isAsset)
            {
                string path = AssetDatabase.GetAssetPath(gameObject);
                string guid = AssetDatabase.AssetPathToGUID(path);

                if (!string.IsNullOrEmpty(guid) && _prefabId != guid)
                {
                    _prefabId = guid;
                    EditorUtility.SetDirty(this);
                }
                if (!string.IsNullOrEmpty(_instanceId))
                {
                    _instanceId = string.Empty;
                    EditorUtility.SetDirty(this);
                }
                return;
            }

            if (string.IsNullOrEmpty(_prefabId))
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                if (source != null)
                {
                    string path = AssetDatabase.GetAssetPath(source);
                    _prefabId = AssetDatabase.AssetPathToGUID(path);
                    EditorUtility.SetDirty(this);
                }
            }
        }
#endif
    }


      //Basically your inheritable class (Virtual Function)
    public interface ISaveComponent
    {
        public string CaptureState();
        public void RestoreState(string json, SaveContext ctx);
    }

    //Parsed to components during loading so they can turn saved instancce ids back into live object references
    public sealed class SaveContext
    {
        private readonly Dictionary<string, SaveableEntity> _byId; //Read only data

        public SaveContext(Dictionary<string, SaveableEntity> byId) => _byId = byId;

        public SaveableEntity Resolve(string instanceId) //Putting a saved instance id to its live entity
        {
            if (string.IsNullOrEmpty(instanceId)) return null;
            return _byId.TryGetValue(instanceId, out var e) ? e : null;
        }

        //Straight to component of target entity
        public T Resolve<T>(string instanceId) where T : Component
        {
            var e = Resolve(instanceId);
            return e != null ? e.GetComponent<T>() : null;
        }
    }
}