using System.Collections;
using UnityEngine;

public class RuntimeFire : MonoBehaviour
{
    IEnumerator Start()
    {
        Collider col = GetComponent<Collider>();
        ThermalMaterial mat = GetComponent<ThermalMaterial>();

        while (ThermalSimulation.Instance == null ||
               !ThermalSimulation.Instance.IsInitialized)
        {
            yield return null;
        }

        ThermalSimulation.Instance.RegisterRuntimeBurningObject(col, mat);
    }
}