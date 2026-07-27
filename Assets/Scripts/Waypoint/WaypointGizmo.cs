using UnityEngine;

public class WaypointGizmo : MonoBehaviour
{
    [Header("Gizmo Settings")]
    [SerializeField] private Color _gizmoColor = Color.cyan;
    [SerializeField] private float _sphereRadius = 0.3f;
    [SerializeField] private string _label = ""; //Leave empty to use the GameObject name

    //OnDrawGizmos runs in the Editor even when the object is not selected
    void OnDrawGizmos()
    {
        Gizmos.color = _gizmoColor;
        Gizmos.DrawSphere(transform.position, _sphereRadius);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * _sphereRadius * 2f);

#if UNITY_EDITOR
        //Draw label at the waypoint position, offset slightly above the sphere
        string display = string.IsNullOrEmpty(_label) ? gameObject.name : _label;
        UnityEditor.Handles.color = _gizmoColor;
        UnityEditor.Handles.Label(transform.position + Vector3.up * (_sphereRadius * 2.5f), display);
#endif
    }

    //OnDrawGizmosSelected draws an additional wire sphere when the object is clicked in the Editor
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, _sphereRadius * 1.5f);
    }
}