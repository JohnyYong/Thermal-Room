using System.Collections;
using UnityEngine;

/// <summary>
/// Flickers a fluorescent fixture: its emissive material, its fake light shafts,
/// an optional small realtime light, and a buzz sound. Stays on most of the time,
/// then stutters in short bursts.
///
/// Notes for Quest:
/// - Baked lighting won't flicker; only the emissive, shafts and optionalLight do.
///   For the room itself to react, add ONE small realtime point light with shadows OFF
///   and a short range, and assign it to optionalLight.
/// - The fixture material must have Emission enabled (URP Lit uses _EmissionColor).
/// </summary>
public class LightFlicker : MonoBehaviour
{
    [Header("Fixture")]
    [SerializeField] Renderer emissiveRenderer;
    [SerializeField] int materialIndex = 0;
    [SerializeField, ColorUsage(false, true)] Color onEmission = new Color(2.0f, 2.1f, 2.3f);
    [SerializeField, Range(0f, 1f)] float dimLevel = 0.05f;

    [Header("Optional")]
    [SerializeField] Renderer[] shaftRenderers;
    [SerializeField] Light optionalLight;
    [SerializeField] AudioSource buzz;

    [Header("Timing (seconds)")]
    [SerializeField] Vector2 stableTime = new Vector2(4f, 12f);
    [SerializeField] Vector2 burstLength = new Vector2(0.2f, 1.0f);
    [SerializeField] Vector2 flickerStep = new Vector2(0.02f, 0.12f);

    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    MaterialPropertyBlock mpb;
    float lightBaseIntensity;
    float buzzBaseVolume;

    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        if (optionalLight) lightBaseIntensity = optionalLight.intensity;
        if (buzz) buzzBaseVolume = buzz.volume;
    }

    void OnEnable()
    {
        SetLevel(1f);
        StartCoroutine(FlickerLoop());
    }

    IEnumerator FlickerLoop()
    {
        while (true)
        {
            SetLevel(1f);
            yield return new WaitForSeconds(Random.Range(stableTime.x, stableTime.y));

            float end = Time.time + Random.Range(burstLength.x, burstLength.y);
            while (Time.time < end)
            {
                bool on = Random.value > 0.5f;
                SetLevel(on ? Random.Range(0.6f, 1f) : dimLevel);
                yield return new WaitForSeconds(Random.Range(flickerStep.x, flickerStep.y));
            }
        }
    }

    void SetLevel(float level)
    {
        if (emissiveRenderer)
        {
            emissiveRenderer.GetPropertyBlock(mpb, materialIndex);
            mpb.SetColor(EmissionId, onEmission * level);
            emissiveRenderer.SetPropertyBlock(mpb, materialIndex);
        }

        bool visible = level > dimLevel + 0.01f;
        foreach (var r in shaftRenderers)
            if (r) r.enabled = visible;

        if (optionalLight) optionalLight.intensity = lightBaseIntensity * level;
        if (buzz) buzz.volume = buzzBaseVolume * level;
    }
}
