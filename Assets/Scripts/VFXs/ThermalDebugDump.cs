using UnityEngine;

public class ThermalDebugDump : MonoBehaviour
{
    void Update()
    {
        Vector3 min = Shader.GetGlobalVector("_ThermalVolumeMin");
        Vector3 size = Shader.GetGlobalVector("_ThermalVolumeSize");

        GameObject overlay = GameObject.Find("Wooden_Box_Wooden_Box_MAT_0_CharOverlay");
        if (overlay != null)
        {
            Vector3 worldCenter = overlay.GetComponent<Renderer>().bounds.center;

            Vector3 uvw = new Vector3(
                (worldCenter.x - min.x) / size.x,
                (worldCenter.y - min.y) / size.y,
                (worldCenter.z - min.z) / size.z
            );

            //Debug.Log($"OverlayWorldCenter={worldCenter}  UVW={uvw}  Min={min} Size={size}");
        }
        else
        {
            Debug.LogWarning("Overlay object not found by name -- check exact name in Hierarchy.");
        }
    }
}