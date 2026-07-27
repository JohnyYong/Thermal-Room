using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class PreviewDroneController : MonoBehaviour
{
    public float rotationSpeed = 2.0f;
    private Vector3 cachedCenter;

    private float[] scaleValues = { 15f, 25f, 30f };
    private int currentScaleIndex = 1;
    private GameObject theDrone;

    [Header("Orientation Sliders")]
    public Slider yawSlider;
    public Slider pitchSlider;

    [Header("PS5 Connection UI")]
    public Image PS5Icon;
    public Image connectionTypeIcon;
    public Sprite bluetoothSprite;
    public Sprite usbSprite;
    public Color connectedColor = Color.green;
    public Color disconnectedColor = Color.red;

    public GameObject SceneMGR;
    public Button startSimulationButton;
    void Start()
    {
        cachedCenter = GetVisualCenter();
        theDrone = GameObject.FindGameObjectWithTag("Drone");
        Debug.Log("Drone found name: " + theDrone.name);

        theDrone.transform.localScale = Vector3.one * scaleValues[currentScaleIndex];

        if (yawSlider != null) yawSlider.onValueChanged.AddListener(OnYawChanged);
        if (pitchSlider != null) pitchSlider.onValueChanged.AddListener(OnPitchChanged);

        SceneMGR = GameObject.Find("SceneManager");

        if (SceneMGR)
        {
            SceneMGR.GetComponent<GameSceneManager>().CheckPS5Connection();
        }
        else { Debug.Log("Cannot Find SceneManager!"); }

        if (startSimulationButton != null)
            startSimulationButton.onClick.AddListener(() => GameSceneManager.Instance.GoToSimulationScene());
    
    }

    void Update()
    {
        cachedCenter = GetVisualCenter();

        // WASD nudges the sliders, which fires onValueChanged, which calls RotateAround.
        if (Input.GetKey(KeyCode.A) && yawSlider != null)
            yawSlider.value -= rotationSpeed;

        if (Input.GetKey(KeyCode.D) && yawSlider != null)
            yawSlider.value += rotationSpeed;

        if (Input.GetKey(KeyCode.W) && pitchSlider != null)
            pitchSlider.value += rotationSpeed;

        if (Input.GetKey(KeyCode.S) && pitchSlider != null)
            pitchSlider.value -= rotationSpeed;

        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            currentScaleIndex = Mathf.Max(0, currentScaleIndex - 1);
            theDrone.transform.localScale = Vector3.one * scaleValues[currentScaleIndex];
        }

        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            currentScaleIndex = Mathf.Min(scaleValues.Length - 1, currentScaleIndex + 1);
            theDrone.transform.localScale = Vector3.one * scaleValues[currentScaleIndex];
        }
    }

    // Each slider fires a delta: how much it moved since last frame.
    private float lastYaw = 0f;
    private float lastPitch = 0f;

    private void OnYawChanged(float value)
    {
        float delta = value - lastYaw;
        lastYaw = value;
        transform.RotateAround(cachedCenter, Vector3.up, delta);
    }

    private void OnPitchChanged(float value)
    {
        float delta = value - lastPitch;
        lastPitch = value;
        transform.RotateAround(cachedCenter, Vector3.right, delta);
    }

    public void SetSmallSize()
    {
        theDrone.transform.localScale = Vector3.one * scaleValues[0];
        SceneMGR.GetComponent<GameSceneManager>().SaveScaleIndex(0);
    }

    public void SetMediumSize()
    {
        theDrone.transform.localScale = Vector3.one * scaleValues[1];
        SceneMGR.GetComponent<GameSceneManager>().SaveScaleIndex(1);
    }

    public void SetLargeSize()
    {
        theDrone.transform.localScale = Vector3.one * scaleValues[2];
        SceneMGR.GetComponent<GameSceneManager>().SaveScaleIndex(2);
    }

    private Vector3 GetVisualCenter()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers)
            bounds.Encapsulate(r.bounds);
        return bounds.center;
    }

    public void UpdatePS5Icon(bool isPS5Connected, bool isBluetooth = false)
    {
        if (PS5Icon != null)
        {
            PS5Icon.color = isPS5Connected ? connectedColor : disconnectedColor;
            Debug.Log("PS5 Connection: " + isPS5Connected);
        }

        if (connectionTypeIcon != null)
        {
            connectionTypeIcon.gameObject.SetActive(isPS5Connected);
            if (isPS5Connected)
                connectionTypeIcon.sprite = isBluetooth ? bluetoothSprite : usbSprite;
        }
    }

    void OnDestroy()
    {
        if (yawSlider != null) yawSlider.onValueChanged.RemoveListener(OnYawChanged);
        if (pitchSlider != null) pitchSlider.onValueChanged.RemoveListener(OnPitchChanged);
    }
}