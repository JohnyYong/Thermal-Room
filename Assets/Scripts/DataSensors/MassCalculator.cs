using UnityEngine;
using TMPro;

public class MassCalculator : MonoBehaviour
{
    public TMP_InputField droneMassInput;
    public TMP_InputField extinguisherMassInput;
    public TMP_Text currentMassText;

    public string suffix = "";

    void Awake()
    {
        Hook(droneMassInput);
        Hook(extinguisherMassInput);
    }

    void Start()
    {
        // If the singleton already has saved data (we returned from another scene),
        // populate the fields with it before the first UpdateTotal.
        if (GameSceneManager.Instance != null)
        {
            if (droneMassInput != null) droneMassInput.text = GameSceneManager.Instance.droneMass.ToString("0.##");
            if (extinguisherMassInput != null) extinguisherMassInput.text = GameSceneManager.Instance.extinguisherMass.ToString("0.##");
        }

        UpdateTotal();
    }

    private void Hook(TMP_InputField field)
    {
        if (field == null) return;
        field.contentType = TMP_InputField.ContentType.DecimalNumber;
        field.onValueChanged.AddListener(_ => UpdateTotal());
    }

    public void UpdateTotal()
    {
        float drone = Parse(droneMassInput);
        float extinguisher = Parse(extinguisherMassInput);
        float total = drone + extinguisher;

        if (currentMassText != null)
            currentMassText.text = total.ToString("0.##") + suffix;

        // Push latest values up to the singleton so they survive scene changes.
        if (GameSceneManager.Instance != null)
            GameSceneManager.Instance.SaveMassData(drone, extinguisher);
    }

    /// <summary>
    /// Called by GameSceneManager.ReinitialiseSetupSceneReferences() on scene return.
    /// Restores saved values into the input fields.
    /// </summary>
    public void SetMass(float drone, float extinguisher)
    {
        if (droneMassInput != null) droneMassInput.text = drone.ToString("0.##");
        if (extinguisherMassInput != null) extinguisherMassInput.text = extinguisher.ToString("0.##");
        UpdateTotal();
    }

    private float Parse(TMP_InputField field)
    {
        if (field != null && float.TryParse(field.text, out float value))
            return value;
        return 0f;
    }
}