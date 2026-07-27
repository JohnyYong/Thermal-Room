using UnityEngine;

public class IgnitionController : MonoBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private LayerMask flammableLayer;
    [SerializeField] public float ignitionRadius = 3f;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F2))
        {
            IgniteNearest();
        }

        if (Input.GetKeyDown(KeyCode.F4))
        {
            ExtinguishNearest();
        }
    }

    private void IgniteNearest()
    {
        Debug.Log("Trying to ignite nearest");

        Collider[] nearby = Physics.OverlapSphere(transform.position, ignitionRadius, flammableLayer);
        FlammableSurface closest = null;
        float closestDist = Mathf.Infinity;

        foreach (var col in nearby)
        {
            if (col.TryGetComponent<FlammableSurface>(out var surface) && !surface.IsBurning && !surface.HasBurned)
            {
                float dist = Vector3.Distance(transform.position, col.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = surface;
                }
            }
        }

        closest?.Ignite();
    }

    private void ExtinguishNearest()
    {
        Debug.Log("Trying to extinguish nearest");

        Collider[] nearby = Physics.OverlapSphere(transform.position, ignitionRadius, flammableLayer);
        FlammableSurface closest = null;
        float closestDist = Mathf.Infinity;

        foreach (var col in nearby)
        {
            // Only target surfaces that are currently burning
            if (col.TryGetComponent<FlammableSurface>(out var surface) && surface.IsBurning)
            {
                float dist = Vector3.Distance(transform.position, col.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = surface;
                }
            }
        }

        closest?.Extinguish();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 0f, 0f);
        Gizmos.DrawWireSphere(transform.position, ignitionRadius);
    }
}