using System.Collections;
using UnityEngine;

public class FireVFXFade : MonoBehaviour
{
    [SerializeField] private float fadeDuration = 0.6f;
    [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private Vector3 _targetScale;
    private Coroutine _fadeRoutine;

    void Awake()
    {
        _targetScale = transform.localScale;
    }

    public void FadeIn()
    {
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeRoutine(Vector3.zero, _targetScale, null));
    }

    public void FadeOut(System.Action onComplete = null)
    {
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeRoutine(transform.localScale, Vector3.zero, onComplete));
    }

    private IEnumerator FadeRoutine(Vector3 from, Vector3 to, System.Action onComplete)
    {
        float t = 0f;
        transform.localScale = from;

        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float eval = fadeCurve.Evaluate(Mathf.Clamp01(t / fadeDuration));
            transform.localScale = Vector3.Lerp(from, to, eval);
            yield return null;
        }

        transform.localScale = to;
        onComplete?.Invoke();
    }
}