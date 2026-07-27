using UnityEngine;
using UnityEngine.UI;
using DroneSystem;

public class ExtinguisherCapacityUI : MonoBehaviour
{
    [Header("References")]
    public DroneExtinguisher extinguisher;
    public Transform cameraTransform;

    [Header("FPV Overlay (Screen Space)")]
    public GameObject fpvUIRoot;
    public RectTransform fpvBarFill;      

    [Header("World Space Bar (3rd Person)")]
    public GameObject worldUIRoot;
    public RectTransform worldBarFill;    // same for world bar
    public Vector3 worldOffset = new Vector3(1.2f, 0.3f, 0f);

    private float _fpvBarMaxWidth;
    private float _worldBarMaxWidth;
    private bool _isFPVMode = true;

    void Start()
    {
        // Cache the full width as 100%
        if (fpvBarFill != null) _fpvBarMaxWidth = fpvBarFill.rect.width;
        if (worldBarFill != null) _worldBarMaxWidth = worldBarFill.rect.width;

        ApplyMode();
    }

    void Update()
    {
        // Billboard world bar
        if (worldUIRoot != null && cameraTransform != null)
        {
            worldUIRoot.transform.position = transform.position + worldOffset;
            worldUIRoot.transform.LookAt(
                worldUIRoot.transform.position + cameraTransform.forward);
        }

        float pct = extinguisher != null ? extinguisher.CapacityPercent : 1f;

        if (fpvBarFill != null)
            fpvBarFill.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal, _fpvBarMaxWidth * pct);

        if (worldBarFill != null)
            worldBarFill.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal, _worldBarMaxWidth * pct);
    }

    public void SetFPVMode(bool fpv)
    {
        Debug.Log($"[ExtUI] SetFPVMode called with fpv={fpv}");
        _isFPVMode = fpv;
        ApplyMode();
    }

    private void ApplyMode()
    {
        Debug.Log($"[ExtUI] ApplyMode called. isFPV={_isFPVMode}, fpvRoot={fpvUIRoot?.name}, worldRoot={worldUIRoot?.name}");

        if (fpvUIRoot != null) fpvUIRoot.SetActive(_isFPVMode);
        if (worldUIRoot != null) worldUIRoot.SetActive(!_isFPVMode);

        Debug.Log($"[ExtUI] After apply. fpvRoot active={fpvUIRoot?.activeSelf}, worldRoot active={worldUIRoot?.activeSelf}");
    }
}