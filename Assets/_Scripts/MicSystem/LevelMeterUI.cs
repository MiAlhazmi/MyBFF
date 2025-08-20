using UnityEngine;
using UnityEngine.UI;

public class LevelMeterUI : MonoBehaviour
{
    [Header("Sources")]
    public AutoMicVAD vad;

    [Header("UI")]
    [Tooltip("Filled Image (fillAmount 0..1) or leave null and use Slider instead.")]
    public Image fillImage;
    public Slider slider;

    [Header("Display")]
    [Tooltip("Map dB range to 0..1")]
    public float minDb = -60f;
    public float maxDb = 0f;
    [Tooltip("How fast the meter follows (higher = snappier)")]
    public float lerpSpeed = 10f;

    float _value; // smoothed 0..1

    void Update()
    {
        if (vad == null) return;

        // Convert RMS -> dB, then normalize to 0..1
        float rms = Mathf.Max(vad.currentRms, 1e-7f);
        float db = 20f * Mathf.Log10(rms);                // ~ -80..0 dBFS
        float t = Mathf.InverseLerp(minDb, maxDb, db);    // 0..1
        _value = Mathf.Lerp(_value, t, Time.deltaTime * lerpSpeed);

        if (fillImage) fillImage.fillAmount = _value;
        if (slider)     slider.value = _value;
    }
}