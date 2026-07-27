using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Unity.VisualScripting;

public class SceneTransitionManager : MonoBehaviour
{
    private static SceneTransitionManager instance;
    public static SceneTransitionManager Instance => instance;

    [Header("UI References")]
    public CanvasGroup canvasGroup;
    public Image logoImage;            // HTX transition image

    [Header("Settings")]
    public float fadeDuration = 0.4f;
    public float scaleInDuration = 0.5f;
    public float scaleOutDuration = 0.35f;
    public float startScale = 0.3f;
    public float overshootScale = 1.1f;
    public float endScale = 1f;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            canvasGroup.alpha = 0;
            canvasGroup.blocksRaycasts = false;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void LoadScene(string sceneName)
    {
        StartCoroutine(DoTransition(sceneName));
    }

    private IEnumerator DoTransition(string sceneName)
    {
        canvasGroup.blocksRaycasts = true;

        yield return ScaleAndFadeIn();

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = false;

        while (op.progress < 0.9f)
            yield return null;

        op.allowSceneActivation = true;
        yield return new WaitUntil(() => op.isDone);

        yield return new WaitForSeconds(0.1f);

        yield return ScaleAndFadeOut();

        canvasGroup.blocksRaycasts = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.M))
        {
            GameSceneManager sceneMGR = GameObject.Find("SceneManager").GetComponent<GameSceneManager>();
            sceneMGR.GoToDroneSetUpScene();
        }
    }

    // Scales up from small with a bouncy overshoot, fades alpha in parallel
    private IEnumerator ScaleAndFadeIn()
    {
        float elapsed = 0f;
        Vector3 from = Vector3.one * startScale;
        Vector3 overshoot = Vector3.one * overshootScale;
        Vector3 to = Vector3.one * endScale;

        if (logoImage != null)
            logoImage.rectTransform.localScale = from;

        // Phase 1: scale up past target (overshoot) while fading in
        while (elapsed < scaleInDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / scaleInDuration);
            float eased = EaseOutBack(t);

            canvasGroup.alpha = Mathf.Clamp01(t / fadeDuration);

            if (logoImage != null)
                logoImage.rectTransform.localScale = Vector3.LerpUnclamped(from, overshoot, eased);

            yield return null;
        }

        canvasGroup.alpha = 1f;

        // Phase 2: settle back from overshoot to final scale (the "bounce back")
        elapsed = 0f;
        float settleDuration = 0.15f;
        Vector3 settleFrom = logoImage != null ? logoImage.rectTransform.localScale : to;

        while (elapsed < settleDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / settleDuration);

            if (logoImage != null)
                logoImage.rectTransform.localScale = Vector3.Lerp(settleFrom, to, t);

            yield return null;
        }

        if (logoImage != null)
            logoImage.rectTransform.localScale = to;
    }

    // Scales down to small while fading out
    private IEnumerator ScaleAndFadeOut()
    {
        float elapsed = 0f;
        Vector3 from = logoImage != null ? logoImage.rectTransform.localScale : Vector3.one * endScale;
        Vector3 to = Vector3.one * startScale;

        while (elapsed < scaleOutDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / scaleOutDuration);
            float eased = EaseInBack(t);

            canvasGroup.alpha = 1f - t;

            if (logoImage != null)
                logoImage.rectTransform.localScale = Vector3.LerpUnclamped(from, to, eased);

            yield return null;
        }

        canvasGroup.alpha = 0f;
        if (logoImage != null)
            logoImage.rectTransform.localScale = to;
    }

    // Easing functions for the "bouncy" feel
    private float EaseOutBack(float t)
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3) + c1 * Mathf.Pow(t - 1f, 2);
    }

    private float EaseInBack(float t)
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        return c3 * t * t * t - c1 * t * t;
    }
}