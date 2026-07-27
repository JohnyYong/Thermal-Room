using UnityEngine;

[RequireComponent(typeof(FluidSimulator))]
public class FluidDebugger : MonoBehaviour
{
    FluidSimulator _sim;
    RenderTexture _readback;
    Texture3D _cpuTex;

    void Start()
    {
        _sim = GetComponent<FluidSimulator>();
    }

    void Update()
    {
        // Only check every 60 frames
        if (Time.frameCount % 60 != 0) return;

        var densityTex = _sim.DensityTexture;
        if (densityTex == null) { Debug.Log("[Debug] DensityTexture is NULL"); return; }

        //Debug.Log($"[Debug] DensityTexture exists: {densityTex.width}x{densityTex.height}x{densityTex.volumeDepth}  IsCreated:{densityTex.IsCreated()}");

        var tempTex = _sim.TemperatureTexture;
        //Debug.Log($"[Debug] TemperatureTex exists: {tempTex.width}  IsCreated:{tempTex.IsCreated()}");
    }

    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 400, 30), $"Frame: {Time.frameCount}");
        GUI.Label(new Rect(10, 40, 400, 30), $"DensityTex: {(_sim?.DensityTexture != null ? "OK" : "NULL")}");
        GUI.Label(new Rect(10, 70, 400, 30), $"TempTex: {(_sim?.TemperatureTexture != null ? "OK" : "NULL")}");
    }
}