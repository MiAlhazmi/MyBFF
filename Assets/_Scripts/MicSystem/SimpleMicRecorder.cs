using UnityEngine;
using System.Collections;

[RequireComponent(typeof(AudioSource))]
public class SimpleMicRecorder : MonoBehaviour
{
    [Header("Recording")]
    public int sampleRate = 44100;
    public int maxRecordSeconds = 120;
    [Tooltip("Leave empty to use default mic")]
    public string micDevice = "";

    [Header("State (read-only)")]
    public bool isRecording;
    public AudioClip lastClip;

    private AudioSource _audio;

    void Awake()
    {
        _audio = GetComponent<AudioSource>();

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("No microphone devices found.");
            return;
        }

        if (string.IsNullOrEmpty(micDevice))
            micDevice = Microphone.devices[0];

        Debug.Log("Mic devices: " + string.Join(", ", Microphone.devices));
        Debug.Log("Using device: " + micDevice);
    }

    // UI Button
    public void StartRecording()
    {
        if (isRecording) return;
        if (Microphone.devices.Length == 0) { Debug.LogError("No microphone."); return; }
        StartCoroutine(CoStartRecording());
    }

    IEnumerator CoStartRecording()
    {
        isRecording = true;
        lastClip = Microphone.Start(micDevice, false, maxRecordSeconds, sampleRate);
        if (lastClip == null)
        {
            Debug.LogError("Microphone.Start returned null. Check permissions/device name.");
            isRecording = false;
            yield break;
        }

        // Wait until the mic actually starts
        int safety = 0;
        while (Microphone.GetPosition(micDevice) <= 0)
        {
            if (++safety > 200) { Debug.LogError("Mic never started."); isRecording = false; yield break; }
            yield return null;
        }
        Debug.Log("Recording…");
    }

    // UI Button
    public void StopRecording()
    {
        if (!isRecording) return;
        StartCoroutine(CoStopRecording());
    }

    IEnumerator CoStopRecording()
    {
        yield return null; // let buffer flush
        int pos = Microphone.GetPosition(micDevice);
        Microphone.End(micDevice);
        isRecording = false;

        if (lastClip == null) yield break;
        if (pos <= 0) pos = lastClip.samples;

        // Trim to actual size
        float[] data = new float[pos * lastClip.channels];
        lastClip.GetData(data, 0);

        var trimmed = AudioClip.Create("MicRecording", pos, lastClip.channels, lastClip.frequency, false);
        trimmed.SetData(data, 0);
        lastClip = trimmed;

        Debug.Log($"Stopped. Length: {lastClip.length:0.00}s, Samples: {pos}");
    }

    // UI Button (optional)
    public void PlayLast()
    {
        if (lastClip == null) { Debug.LogWarning("No recording."); return; }
        _audio.Stop();
        _audio.clip = lastClip;
        _audio.Play();
        Debug.Log("Playing last recording.");
    }
}
