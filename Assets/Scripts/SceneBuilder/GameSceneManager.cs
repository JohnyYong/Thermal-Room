using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using DroneSystem;

public class GameSceneManager : MonoBehaviour
{

    private static GameSceneManager instance;
    public static GameSceneManager Instance => instance;


    [Header("Data Carry Over")]
    public float droneMass = 0.1f;
    public float extinguisherMass = 0.1f;
    public float totalMass => droneMass + extinguisherMass; // read-only convenience
    public int droneScaleIndex = 1; // 0 Small | 1 Medium | 2 Large

    public bool PS5Connected = false;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        QualitySettings.asyncUploadBufferSize = 64;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        InputSystem.onDeviceChange += OnDeviceChange;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        InputSystem.onDeviceChange -= OnDeviceChange;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "SimulationScene")
            ApplyDataToSimulationDrone();

        if (scene.name == "DroneSetUpScene")
            ApplyDataToSetupUI();
    }

    public void GoToSimulationScene()
    {
        SceneTransitionManager sceneTransition = GameObject.FindGameObjectWithTag("SceneTransition").GetComponent<SceneTransitionManager>();

        sceneTransition.LoadScene("SimulationScene");
    }

    public void GoToDroneSetUpScene()
    {
        SceneTransitionManager sceneTransition = GameObject.FindGameObjectWithTag("SceneTransition").GetComponent<SceneTransitionManager>();

        sceneTransition.LoadScene("DroneSetUpScene");
    }

    public void SaveMassData(float drone, float extinguisher)
    {
        droneMass = drone;
        extinguisherMass = extinguisher;
    }

    public void SaveScaleIndex(int index)
    {
        droneScaleIndex = index;
    }

    private void ApplyDataToSimulationDrone()
    {
        DronePhysics drone = FindFirstObjectByType<DronePhysics>(FindObjectsInactive.Include);

        if (drone == null)
        {
            Debug.LogWarning("[GameSceneManager] No DronePhysics found in SimulationScene.");
            return;
        }

        drone.SetDisplayMass(totalMass);

        // Apply scale based on saved index
        float[] scaleValues = { 15f, 25f, 30f };
        float scale = scaleValues[Mathf.Clamp(droneScaleIndex, 0, scaleValues.Length - 1)];
        drone.SetScale(Vector3.one * scale);

        Debug.Log($"[GameSceneManager] Applied to simulation drone — " +
                  $"Total Mass: {totalMass} kg (Drone: {droneMass} + Extinguisher: {extinguisherMass}), Scale: {scale}");
    }

    // ----------------------------------------------------------------
    //  Re-populates setup UI when returning to the setup scene
    // ----------------------------------------------------------------
    private void ApplyDataToSetupUI()
    {
        MassCalculator mc = FindFirstObjectByType<MassCalculator>();

        if (mc == null)
        {
            Debug.LogWarning("[GameSceneManager] No MassCalculator found in DroneSetUpScene.");
            return;
        }

        mc.SetMass(droneMass, extinguisherMass);

        Debug.Log($"[GameSceneManager] Restored setup UI — " +
                  $"Drone: {droneMass} kg | Extinguisher: {extinguisherMass} kg");
    }

    public void CheckPS5Connection()
    {
        PS5Connected = false;
        bool isBluetooth = false;

        foreach (var gamepad in Gamepad.all)
        {
            if (gamepad.displayName.Contains("DualSense") || gamepad.displayName.Contains("Wireless Controller"))
            {
                PS5Connected = true;
                Debug.Log(gamepad);
                isBluetooth = gamepad.description.capabilities.ToLower().Contains("bluetooth");
                break;
            }
        }

        GameObject previewDrone = GameObject.FindGameObjectWithTag("PreviewDrone");
        if (previewDrone)
            previewDrone.GetComponent<PreviewDroneController>().UpdatePS5Icon(PS5Connected, isBluetooth);
    }

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (device is Gamepad)
        {
            switch (change)
            {
                case InputDeviceChange.Added:
                case InputDeviceChange.Removed:
                case InputDeviceChange.Disconnected:
                case InputDeviceChange.Reconnected:
                    CheckPS5Connection();
                    break;
            }
        }
    }
}