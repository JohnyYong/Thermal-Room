using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using static UnityEngine.InputSystem.HID.HID;

public class ThermalMaterial : MonoBehaviour
{
    public ThermalMaterialType materialType = ThermalMaterialType.Wood;

    public bool includeChildren = true;

    [Header("Combustion")]
    public bool combustible = true;

    public bool igniteInstant = false;

    public float fuelAmount = 100f;
}

public enum ThermalMaterialType
{
    Air = 0,
    Wood = 1,
    Steel = 2,
    Concrete = 3,
    Glass = 4,
    Cloth = 5
}