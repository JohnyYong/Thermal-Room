using UnityEngine;

// Marks a Collider as an opening (door, window, broken wall) that the
// thermal simulation treats as a vent to the exterior: a low-pressure sink
// that pulls hot gas/smoke toward it, and a drain that lets temperature
// and smoke bleed out to ambient instead of pooling forever.
// Place on any GameObject with a Collider (BoxCollider is typical) sized
// to cover the opening.
[RequireComponent(typeof(Collider))]
public class ThermalOpening : MonoBehaviour
{
    [Tooltip("How strongly this opening drains heat/smoke to ambient. Same units as ThermalSimulation.openingVentRate -- kept per-opening in case you want some vents stronger than others later.")]
    public float ventRate = 1.0f;

    Collider _collider;
    public Collider Collider => _collider != null ? _collider : (_collider = GetComponent<Collider>());

    void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }
}