using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

#if SFB_PRESENT
using SFB;
#endif

namespace SaveSystem
{
    //Making it sealed class such that it cannot be easily overridden
    public sealed class ScenarioSaveSystem : MonoBehaviour
    {
        public static ScenarioSaveSystem Instance { get; private set; } //Basic singletons

        [SerializeField] private PrefabRegistry _registry;

        //Maybe add optional parent just in case? Ignore first
        [SerializeField] private Transform _entityRoot;

        //Default file used when no path is chosen via the dialog
        [SerializeField] private string _fileName = "scenario.json";
        [SerializeField] private string _saveSubFolder = "Scenarios";
        [SerializeField] private bool _keepBackup = true;

        //Loading performance, like how many entities to be spawned per frame
        [SerializeField, UnityEngine.Min(1)] private int _instantiatePerFrame = 20;

        public event Action<string> onSavedCompleted; //passes the path used
        public event Action<string> onLoadCompleted;
        public event Action<string> onError;

        //Default folder. Dialog can save anywhere the user picks
        private string DefaultDir => Path.Combine(Application.persistentDataPath, _saveSubFolder);
        private string DefaultPath => Path.Combine(DefaultDir, _fileName);

        private bool _isLoading = false;
        public bool loading => _isLoading;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.Log("An existing instance of the current save system already exists, destroying new SaveSystem");
                Destroy(this);
                return;
            }
            Instance = this;

            Directory.CreateDirectory(DefaultDir); //So the dialog opens somewhere sensible
        }

        void OnDestroy()
        {
            if (Instance == this) { Instance = null; } //Clean up instance value since it is static
        }

        #region Dialog

        //Native OS "Save As". Wire the Save button here
        public void SaveWithDialog()
        {
#if SFB_PRESENT
            string path = StandaloneFileBrowser.SaveFilePanel("Save Scenario", DefaultDir, "scenario", "json");
            if (string.IsNullOrEmpty(path)) return; //User cancelled
            SaveToPath(path);
#else
            Debug.LogWarning("[SaveSystem] StandaloneFileBrowser not imported, using default file. Add SFB_PRESENT define once plugin is in.");
            SaveToPath(DefaultPath);
#endif
        }

        //Native OS "Open". Wire the Load button here
        public void LoadWithDialog()
        {
#if SFB_PRESENT
            var paths = StandaloneFileBrowser.OpenFilePanel("Load Scenario", DefaultDir, "json", false);
            if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;
            LoadFromPath(paths[0]);
#else
            Debug.LogWarning("[SaveSystem] StandaloneFileBrowser not imported, using default file. Add SFB_PRESENT define once plugin is in.");
            LoadFromPath(DefaultPath);
#endif
        }

        #endregion

        #region SaveSystem

        //Write temp file first instead of overwriting save
        //Such that previous save file won't get corrupted
        private void WriteAtomic(string finalPath, string json)
        {
            string dir = Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tempPath = finalPath + ".tmp";
            string backupPath = finalPath + ".bak";

            File.WriteAllText(tempPath, json);

            if (File.Exists(finalPath))
            {
                if (_keepBackup) File.Replace(tempPath, finalPath, backupPath);
                else File.Replace(tempPath, finalPath, null);
            }
            else
            {
                File.Move(tempPath, finalPath);
            }
        }

        //Core save. Any front-end (dialog, default, file list) feeds a path here
        public void SaveToPath(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) throw new ArgumentException("Empty save path.");
                if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) path += ".json";

                ScenarioSaveDto dto = Capture();
                string json = JsonUtility.ToJson(dto);
                WriteAtomic(path, json);

                Debug.Log($"[SaveSystem] Saved {dto.entities.Count} entities to {path}");
                onSavedCompleted?.Invoke(path);
            }
            catch (Exception e)
            {
                string msg = $"[SaveSystem] Save failed: {e.Message}";
                Debug.LogError(msg, this);
                onError?.Invoke(msg);
            }
        }

        private ScenarioSaveDto Capture()
        {
            var dto = new ScenarioSaveDto
            {
                version = ScenarioSaveDto.CurrentVersion,
                savedAtUtc = DateTime.UtcNow.ToString("o"),
                sceneName = SceneManager.GetActiveScene().name
            };

            //Find objects
            var entities = UnityEngine.Object.FindObjectsByType<SaveableEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            dto.entities.Capacity = entities.Length;

            foreach (var entity in entities)
            {
                if (!entity.IsSaveable) continue; //Skipping those non saveable stuffs

                Transform t = entity.transform;
                dto.entities.Add(new EntityDto
                {
                    prefabID = entity.PrefabID,
                    instanceID = entity.InstanceId,

                    position = t.position,
                    rotation = t.rotation,
                    scale = t.localScale,

                    components = entity.CaptureComponents()
                });
            }
            //Foreach object
            return dto;
        }
        #endregion

        #region LoadSystem

        public bool SaveFileExists() => File.Exists(DefaultPath);

        //Core load. Any front-end feeds a path here
        public void LoadFromPath(string path)
        {
            if (_isLoading)
            {
                Debug.Log("[SaveSystem] Loading is already currently in process.");
                return;
            }

            StartCoroutine(LoadRoutine(path));
        }

        //Clear old entities before loading again
        private void ClearExistingEntities()
        {
            var existing = UnityEngine.Object.FindObjectsByType<SaveableEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var e in existing)
            {
                if (e == null) continue;
                Destroy(e.gameObject);
            }
        }

        private ScenarioSaveDto Migrate(ScenarioSaveDto dto)
        {
            if (dto.version == ScenarioSaveDto.CurrentVersion) return dto; //Same version no point 

            if (dto.version > ScenarioSaveDto.CurrentVersion) //Only update if version surpasses current version
            {
                throw new InvalidDataException($"[SaveSystem] Save file version {dto.version} is newer than current build {ScenarioSaveDto.CurrentVersion}");
            }

            Debug.Log($"[SaveSystem] Migrated save file from v{dto.version} to v{ScenarioSaveDto.CurrentVersion}.");
            dto.version = ScenarioSaveDto.CurrentVersion;
            return dto;
        }

        private IEnumerator LoadRoutine(string path)
        {
            ScenarioSaveDto dto = null;
            _isLoading = true;

            try
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("No save file found.", path);
                }

                string json = File.ReadAllText(path);
                dto = JsonUtility.FromJson<ScenarioSaveDto>(json);

                if (dto == null) { throw new InvalidDataException("Save file could not be parsed"); }

                dto = Migrate(dto);
            }
            catch (Exception e)
            {
                string msg = $"[SaveSystem] Load failed: {e.Message}";
                Debug.LogError(msg, this);
                onError?.Invoke(msg);
                _isLoading = false; //Stop the loading state
                yield break;
            }

            //Receive the registry
            if (_registry == null)
            {
                onError?.Invoke("[SaveSystem] Load failed: No PrefabRegistry assigned.");
                _isLoading = false;
                yield break;
            }

            //Successfully going through the loading
            ClearExistingEntities();

            //First pass, instantiation of everything as well as the ID to build the Entity Map
            var byId = new Dictionary<string, SaveableEntity>(dto.entities.Count);
            var spawned = new List<(SaveableEntity entity, EntityDto data)>(dto.entities.Count);

            int sinceYield = 0;

            foreach (EntityDto data in dto.entities)
            {
                GameObject prefab = _registry.Get(data.prefabID);
                if (!prefab)
                {
                    Debug.Log($"[SaveSystem] Unknown prefab ID: {data.prefabID}, skipping. Please try rescanning the Registry ScriptableObject.");
                    continue;
                }

                GameObject go = Instantiate(prefab, data.position, data.rotation, _entityRoot);
                go.transform.localScale = data.scale;
                go.name = prefab.name;

                var entity = go.GetComponent<SaveableEntity>();
                if (entity)
                {
                    entity.OverrideInstanceId(data.instanceID);
                    byId[data.instanceID] = entity;
                    spawned.Add((entity, data));
                }

                if (++sinceYield >= _instantiatePerFrame)
                {
                    sinceYield = 0;
                    yield return null; //Don't kill the process
                }
            }

            //2nd pass update the components back to their correct state
            var ctx = new SaveContext(byId);
            sinceYield = 0;

            foreach (var (entity, data) in spawned)
            {
                entity.RestoreComponents(data.components, ctx);

                if (++sinceYield >= _instantiatePerFrame)
                {
                    sinceYield = 0;
                    yield return null;
                }
            }

            Debug.Log($"[SaveSystem] Loaded {spawned.Count} entities from {path}");
            _isLoading = false; //Load completed
            if (ThermalSimulation.Instance != null)
                ThermalSimulation.Instance.Rebake(); //For the thermal clones

            onLoadCompleted?.Invoke(path);
        }
        #endregion
    }
}