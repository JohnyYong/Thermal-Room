using UnityEngine;
using UnityEngine.SceneManagement;

public class FireVFX : MonoBehaviour
{
    [Header("Extinguish Settings")]
    public float shrinkSpeed = 0.5f;
    public float destroyThreshold = 0.05f;
    public float foamTimeout = 0.3f;
    public float recoverSpeed = 0.2f;

    private float _lastFoamContactTime = -999f;
    private Vector3 _originalScale;

    private bool IsBeingExtinguished => Time.time - _lastFoamContactTime < foamTimeout;

    void Start()
    {
        Transform root = transform.parent != null ? transform.parent : transform;
        _originalScale = root.localScale;
    }

    public void OnFoamContact()
    {
        _lastFoamContactTime = Time.time;
        Debug.Log($"[FireVFX] OnFoamContact called on {gameObject.name}");
    }

    void Update()
    {
        Transform root = transform.parent != null ? transform.parent : transform;

        if (SceneManager.GetActiveScene().name != "SimulationScene") { return; }

        //Lazy method first
        if (SceneMode.Instance.CurrentMode == SceneMode.SceneModes.EDITOR_MODE)
        {
            gameObject.GetComponentInParent<BoxCollider>().enabled = true;
        }
        else
        {
            gameObject.GetComponentInParent<BoxCollider>().enabled = false;
        }

        if (IsBeingExtinguished)
        {
            root.localScale = Vector3.MoveTowards(
                root.localScale, Vector3.zero, shrinkSpeed * Time.deltaTime);

            if (root.localScale.magnitude <= destroyThreshold)
            {
                Debug.Log($"[FireVFX] {root.name} extinguished, destroying.");
                Destroy(root.gameObject);
            }
        }
        else
        {
            // Grow back toward original scale when foam stops hitting
            root.localScale = Vector3.MoveTowards(
                root.localScale, _originalScale, recoverSpeed * Time.deltaTime);
        }
    }
}