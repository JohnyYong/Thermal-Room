using UnityEngine;

// Put on ANY object you want to char. Keeps the original material untouched
// and spawns a transparent overlay clone on top that renders only the char.
//
// Char value comes straight from the GPU char volume (already accumulated
// 0->1 by ThermalSimulation), so no CPU-side accumulation is needed --
// this just reads it and drives the overlay's _Burn.
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class CharOverlayController : MonoBehaviour, IThermalSampleReceiver
{
    [Header("Overlay")]
    [Tooltip("A material using the Custom/CharOverlay shader.")]
    [SerializeField] private Material _charOverlayMat;

    [Tooltip("Multiplies incoming char -- raise if char builds too slowly.")]
    [SerializeField] private float _charBoost = 1f;

    [Header("Debug")]
    [SerializeField, Range(0, 1)] private float _burn;

    private Material _overlayInstance;
    private bool _registered;
    private static readonly int BurnID = Shader.PropertyToID("_Burn");

    // Region to search for char in. Char only ever builds in the AIR
    // touching a burning solid -- on whichever face happens to be
    // exposed -- so we pad the object's own bounds out a couple of voxels
    // on every side rather than guessing a single point below it. That
    // way it doesn't matter which face of the object is actually burning.
    public Bounds SampleBounds
    {
        get
        {
            var b = GetComponent<Renderer>().bounds;

            float cellSize = (ThermalSimulation.Instance != null)
                ? ThermalSimulation.Instance.cellSize
                : 0.25f;

            // Bounds.Expand grows the size by `amount` total (i.e. by
            // amount/2 per side), so use 4x cell size to guarantee at
            // least ~2 voxels of padding on every side.
            b.Expand(cellSize * 4f);
            return b;
        }
    }

    private void Start()
    {
        BuildOverlayClone();
        TryRegister();
    }

    private void OnEnable() => TryRegister();
    private void OnDisable()
    {
        if (ThermalSampler.Instance != null)
            ThermalSampler.Instance.Unregister(this);
        _registered = false;
    }

    private void TryRegister()
    {
        if (_registered) return;
        if (ThermalSampler.Instance == null) return;

        ThermalSampler.Instance.Register(this);
        _registered = true;
    }

    // Delivers the MAX char value (0-1) found anywhere in SampleBounds.
    // Char never decreases, so we keep the max here too.
    public void ReceiveTemperature(float charValue)
    {
        float v = Mathf.Clamp01(charValue * _charBoost);
        if (v > _burn) _burn = v;
    }

    private void BuildOverlayClone()
    {
        if (_charOverlayMat == null)
        {
            Debug.LogError("[CharOverlay] No overlay material assigned.", this);
            enabled = false;
            return;
        }

        var srcFilter = GetComponent<MeshFilter>();

        var clone = new GameObject(name + "_CharOverlay");
        clone.transform.SetParent(transform, false);

        var mf = clone.AddComponent<MeshFilter>();
        mf.sharedMesh = srcFilter.sharedMesh;

        var mr = clone.AddComponent<MeshRenderer>();

        _overlayInstance = new Material(_charOverlayMat);
        mr.sharedMaterial = _overlayInstance;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        _overlayInstance.SetFloat(BurnID, _burn);
    }

    private void Update()
    {
        // Registration can be missed if this object's Start/OnEnable ran
        // before ThermalSampler's Awake set Instance (Unity doesn't
        // guarantee script execution order across GameObjects). Keep
        // retrying cheaply until it succeeds instead of failing silently
        // forever.
        if (!_registered) TryRegister();

        if (_overlayInstance != null)
        {
            _overlayInstance.SetFloat(BurnID, _burn);
        }
    }

    public void ResetChar()
    {
        _burn = 0f;
        if (_overlayInstance != null)
            _overlayInstance.SetFloat(BurnID, 0f);
    }
}