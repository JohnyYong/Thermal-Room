using UnityEngine;

public class FrameRateManager : MonoBehaviour
{
    [Header("Frame Rate")]
    [SerializeField] private int _targetFrameRate = 60;
    [SerializeField] private bool _useVSync = true;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Apply();
    }

    public void Apply()
    {
        if (_useVSync)
        {
            QualitySettings.vSyncCount = 1;     
            Application.targetFrameRate = -1;   
        }
        else
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = _targetFrameRate;
        }
    }

    public void SetTargetFrameRate(int fps)
    {
        _targetFrameRate = fps;
        _useVSync = false;
        Apply();
    }
}