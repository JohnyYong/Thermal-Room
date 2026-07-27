using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SliderInputLink : MonoBehaviour
{
    public Slider slider;
    public TMP_InputField input;

    [Tooltip("Decimal places shown in the input field.")]
    public int decimals = 1;

    private bool isUpdating = false; // guards against an update feedback loop

    void Awake()
    {
        if (slider != null)
            slider.onValueChanged.AddListener(OnSliderChanged);

        if (input != null)
        {
            input.contentType = TMP_InputField.ContentType.DecimalNumber;
            input.onEndEdit.AddListener(OnInputChanged); //On end edit is basically the value is changed and released
        }
    }

    void Start()
    {
        if (slider != null)
            OnSliderChanged(slider.value);
    }

    private void OnSliderChanged(float value)
    {
        if (isUpdating) return;
        isUpdating = true;

        if (input != null)
            input.text = value.ToString("F" + decimals);

        isUpdating = false;
    }

    private void OnInputChanged(string text)
    {
        if (isUpdating) return;
        isUpdating = true;

        if (slider != null && float.TryParse(text, out float value))
        {
            value = Mathf.Clamp(value, slider.minValue, slider.maxValue);
            slider.value = value;
            input.text = value.ToString("F" + decimals);
        }

        isUpdating = false;
    }

    void OnDestroy()
    {
        if (slider != null)
            slider.onValueChanged.RemoveListener(OnSliderChanged);
        if (input != null)
            input.onEndEdit.RemoveListener(OnInputChanged);
    }
}