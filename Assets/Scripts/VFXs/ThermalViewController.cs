using UnityEngine;
using UnityEngine.InputSystem;

public enum ThermalViewMode { Normal, Thermal }
public class ThermalViewController : MonoBehaviour
{
    public ThermalSimulation thermal;

    public Camera currentCamera;

    public ThermalViewMode currentView = ThermalViewMode.Normal;

    public float thermalMinTemp = 20;

    public float thermalMaxTemp = 200;

    public bool autoRange = true;

    public float thermalFocusTemp = 50f;

    public float thermalRangeWidth = 150f;

    public float thermalRangeSpeed = 5f;

    float displayedMinTemp = 20f;

    float displayedMaxTemp = 200f;

    public float thermalVolumeDensity = 1.0f;
    public float fireVolumeDensity = 3.0f;
    public float smokeDensity = 40.0f;
    public float fireHeatDensity = 0.5f;

    int normalMask;
    int thermalMask;

    void Start()
    {
        thermal = ThermalSimulation.Instance;

        normalMask = currentCamera.cullingMask;

        thermalMask = LayerMask.GetMask("Default", "UI", "Environment", "ThermalSurface");
    }

    void Update()
    {
        if (Keyboard.current.tKey.wasPressedThisFrame)
        {
            CycleView();
        }
        UpdateThermalRange();
        UpdateViewMode();
    }

    void CycleView()
    {
        switch (currentView)
        {
            case ThermalViewMode.Normal:

                currentView = ThermalViewMode.Thermal;
                break;

            case ThermalViewMode.Thermal:

                currentView = ThermalViewMode.Normal;
                break;

            default:

                currentView = ThermalViewMode.Normal;
                break;
        }
    }

    void UpdateThermalRange()
    {
        float targetMin;
        float targetMax;

        if (autoRange)
        {
            targetMin = thermalFocusTemp - thermalRangeWidth * 0.5f;

            targetMax = thermalFocusTemp + thermalRangeWidth * 0.5f;
        }
        else
        {
            targetMin = thermalMinTemp;

            targetMax = thermalMaxTemp;
        }

        displayedMinTemp = Mathf.Lerp(displayedMinTemp, targetMin,
                                      Time.deltaTime * thermalRangeSpeed);

        displayedMaxTemp = Mathf.Lerp(displayedMaxTemp, targetMax,
                                      Time.deltaTime * thermalRangeSpeed);

        Shader.SetGlobalFloat("_ThermalMinTemp", displayedMinTemp);

        Shader.SetGlobalFloat("_ThermalMaxTemp", displayedMaxTemp);
    }

    public void SetThermalActive(bool on)
    {
        currentView = on ? ThermalViewMode.Thermal : ThermalViewMode.Normal;

        // Apply right away if we're initialised; otherwise the existing Update()
        // will apply it next frame. The guard avoids a null-ref if DroneCamera
        // calls this during its Start before our Start has assigned `thermal`.
        if (thermal != null)
            UpdateViewMode();
    }

    void UpdateViewMode()
    {
        switch (currentView)
        {
            case ThermalViewMode.Normal:

                currentCamera.cullingMask = normalMask;

                thermal.volumeMaterial.SetFloat("_Density", 0);
                thermal.fireVolumeMaterial.SetFloat("_Density",
                                                    fireVolumeDensity);
                thermal.smokeVolumeMaterial.SetFloat("_Density", smokeDensity);
                thermal.fireHeatVolumeMaterial.SetFloat("_Density",
                                                        fireHeatDensity);

                break;

            case ThermalViewMode.Thermal:

                currentCamera.cullingMask = thermalMask;

                thermal.volumeMaterial.SetFloat("_Density", 0);
                // thermal.volumeMaterial.SetFloat("_Density",
                // thermalVolumeDensity);

                thermal.fireVolumeMaterial.SetFloat("_Density", 0);
                thermal.smokeVolumeMaterial.SetFloat("_Density", 0);
                thermal.fireHeatVolumeMaterial.SetFloat("_Density",
                                                        fireHeatDensity);

                break;
        }
    }
}