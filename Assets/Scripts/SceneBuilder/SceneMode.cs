using DroneSystem;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

//Should usually start at EditorMode
//Maybe need to think about the UI between SceneMode and EditorMode
public class SceneMode : MonoBehaviour
{
    public enum SceneModes
    {
        NONE = 0,
        EDITOR_MODE,
        PLAY_MODE
    }

    public static SceneMode Instance { get; private set; }

    [Header("Settings")]
    public KeyCode EDITOR_MODE_KEY = KeyCode.Alpha1;
    public KeyCode PLAY_MODE_KEY = KeyCode.Alpha2;

    [Header("References")]
    public GameObject editorCamera;
    public GameObject droneCamera;
    public GameObject thirdPersonViewCamera;
    public GameObject droneObject;
    public GameObject UICanvas;
    public GameObject thermalUI;

    [Tooltip("PURELY VISUAL thermal objects to toggle (thermal camera, fire/" +
             "smoke volume renderers, etc.). Do NOT put ThermalSimulation or " +
             "ThermalViewController in here -- they must stay active so the " +
             "simulationActive flag can control them. See thermalSimulation below.")]
    public List<GameObject> thermalSystem;

    [Header("Thermal Simulation (stays active, flag-controlled)")]
    [Tooltip("The ThermalSimulation component. Kept active in both modes; its " +
             "simulationActive flag is what actually pauses/resumes the sim. " +
             "Pausing via the flag already skips all compute dispatches, so " +
             "there is no extra FPS to gain from deactivating the GameObject.")]
    public ThermalSimulation thermalSimulation;

    // Tags used on UICanvas children to identify which UI belongs to which mode
    private const string EDITOR_UI_TAG = "EditorUI";
    private const string PLAY_UI_TAG = "PlayUI";

    public SceneModes CurrentMode { get; private set; } = SceneModes.NONE;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        SwitchTo(SceneModes.EDITOR_MODE);
    }

    void Update()
    {
        if (Input.GetKeyDown(EDITOR_MODE_KEY)) SwitchTo(SceneModes.EDITOR_MODE);
        if (Input.GetKeyDown(PLAY_MODE_KEY)) SwitchTo(SceneModes.PLAY_MODE);
    }

    public void SwitchTo(SceneModes mode)
    {
        if (CurrentMode == mode) return;

        CurrentMode = mode;

        switch (mode)
        {
            case SceneModes.EDITOR_MODE:
                OnEnterEditorMode();
                break;
            case SceneModes.PLAY_MODE:
                OnEnterPlayMode();
                break;
            default:
                OnEnterEditorMode();
                break;
        }

        Debug.Log($"[SceneMode] Switched to {mode}");
    }

    public void OnEnterEditorMode()
    {
        if (editorCamera != null) editorCamera.SetActive(true);
        if (droneCamera != null) droneCamera.SetActive(false);
        if (droneObject != null) droneObject.SetActive(false);
        if (thirdPersonViewCamera != null) thirdPersonViewCamera.SetActive(false);


        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        TurnOnEditorModeUI();
    }

    public void OnEnterPlayMode()
    {
        if (editorCamera != null) editorCamera.SetActive(false);
        if (droneCamera != null) droneCamera.SetActive(true);
        DroneCamera dc = droneCamera.GetComponent<DroneCamera>();
        dc.CurrentView = dc.startView;
        dc.ApplyRenderTargets();
        dc.ApplyFPV();
        Debug.Log("Current View mode: " + dc.CurrentView);
        if (droneObject != null) droneObject.SetActive(true);
        
        if (thirdPersonViewCamera != null) thirdPersonViewCamera.SetActive(true);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        TurnOnPlayModeUI();
    }

    public void TurnOnPlayModeUI()
    {
        if (!UICanvas) { Debug.LogWarning("[SceneMode] UICanvas not assigned!"); return; }
        SetUIByTag(PLAY_UI_TAG, true);
        SetUIByTag(EDITOR_UI_TAG, false);
        //thermalUI.SetActive(true); //Different GO than EditorUI, thermal view only in Simulation

        //Initially false in play, only on if thermal mode view on
        foreach (GameObject go in thermalSystem)
        {
            go.SetActive(false);
        }

        if (thermalSimulation != null)
        {
            thermalSimulation.gameObject.SetActive(true);
            thermalSimulation.SetSimulationActive(false);
        }
    }

    public void TurnOnEditorModeUI()
    {

        if (!UICanvas) { Debug.LogWarning("[SceneMode] UICanvas not assigned!"); return; }
        SetUIByTag(EDITOR_UI_TAG, true);
        SetUIByTag(PLAY_UI_TAG, false);
        //thermalUI.SetActive(false);
        //Initially true, so that we can view it in editor mode
        foreach (GameObject go in thermalSystem)
        {
            go.SetActive(true);
        }

        if (thermalSimulation != null)
        {
            thermalSimulation.gameObject.SetActive(true);
            thermalSimulation.SetSimulationActive(true);
        }
    }

    // Walks all direct children of UICanvas and toggles based on tag
    private void SetUIByTag(string tag, bool active)
    {
        foreach (Transform child in UICanvas.transform)
        {
            if (child.CompareTag(tag))
            {
                child.gameObject.SetActive(active);
                Debug.Log($"[SceneMode] {(active ? "Showing" : "Hiding")} UI: {child.name} (tag: {tag})");
            }
        }
    }
}