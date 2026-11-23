using UnityEngine;
using System.Collections;
using System.Security.Cryptography.X509Certificates;

public class FlashbangLight : MonoBehaviour
{
    [Header("Light")]
    public Light flashLight;
    public float peakIntensity = 7000000f;   // URP: 10–50 là rất chói
    public float peakRange = 20f;
    public Color flashColor = new Color(1f, 0.97f, 0.9f);

    [Header("Timing")]
    public float riseTime = 0.03f;      // lên đỉnh gần như tức thì
    public float fadeTime = 0.5f;       // tắt dần
    public float power = 8f;

    [Header("Options")]
    public bool playOnStart = false;
    public bool destroyWhenDone = true;

    bool playing;

    void Awake()
    {
        if (flashLight != null)
        {
            flashLight.color = flashColor;
            flashLight.intensity = 0f;
            flashLight.enabled = false;
        }
    }

    void Start()
    {
        if (playOnStart) Explode();
    }

    [ContextMenu("Test Flash")]
    public void Explode()
    {
        if (playing) return;            // đảm bảo chỉ chạy 1 lần
        playing = true;
        StartCoroutine(FlashRoutine());
    }

    IEnumerator FlashRoutine()
    {
        flashLight.enabled = true;

        // 1) Vụt lên trong 0.3s
        float t = 0f;
        while (t < riseTime)
        {
            float k = t / riseTime;
            flashLight.intensity = peakIntensity * k;
            flashLight.range = peakRange * k;
            t += Time.deltaTime;
            yield return null;
        }
        flashLight.intensity = peakIntensity;
        flashLight.range = peakRange;

        // 2) Tắt dần ngay lập tức trong 1s, càng về cuối càng chậm (bình phương)
        t = 0f;
        while (t < fadeTime)
        {
            float k = t / fadeTime;
            float f = Mathf.Pow(1f - k, power);
            flashLight.intensity = peakIntensity * f;
            t += Time.deltaTime;
            yield return null;
        }

        flashLight.intensity = 0f;
        flashLight.enabled = false;
        playing = false;

        if (destroyWhenDone) Destroy(gameObject);
    }
}