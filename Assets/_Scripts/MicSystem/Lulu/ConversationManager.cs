using System;
using System.Collections;
using UnityEngine;

public class ConversationManager : MonoBehaviour
{
    public static ConversationManager Instance { get; private set; }

    [Header("References (Mic System)")]
    public AutoMicVAD vad;               // your existing VAD on MicSystem
    public MicUploadToN8n uploader;      // your existing uploader on MicSystem
    public AudioSource replySource;      // same AudioSource MicUploadToN8n uses (optional, for greeting)

    [Header("Lulu")]
    public string npcId = "lulu";
    public AudioClip greeting;           // optional voice line “Hi, I’m Lulu…”
    public float warmupSeconds = 0.6f;   // let noise floor settle before listening
    public float exitGraceSeconds = 0.5f;// small grace to avoid flicker on edge

    public bool IsActive;
    bool _isTransitioning;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        vad.enabled = false;
        IsActive = false;
        _isTransitioning = false;
    }

    private void Start()
    {
    }

    public void BeginConversation()
    {
        if (IsActive || _isTransitioning) return;
        StartCoroutine(CoBegin());
    }

    public void EndConversation()
    {
        if (!IsActive || _isTransitioning) return;
        StartCoroutine(CoEnd());
    }

    IEnumerator CoBegin()
    {
        _isTransitioning = true;

        // Make sure webhook carries Lulu’s id (only add once)
        // if (uploader != null && !string.IsNullOrEmpty(uploader.webhookUrl) && !uploader.webhookUrl.Contains("npcId="))
        // {
        //     uploader.webhookUrl += (uploader.webhookUrl.Contains("?") ? "&" : "?") + "npcId=" + WWW.EscapeURL(npcId);
        // }

        // Optional greeting: gate VAD during the clip
        if (greeting && replySource)
        {
            vad.enabled = false;
            replySource.Stop();
            replySource.clip = greeting;
            replySource.Play();
            while (replySource.isPlaying) yield return null;
        }

        // Warmup (lets fixed/adaptive thresholds settle)
        yield return new WaitForSeconds(warmupSeconds);

        // Start listening (hands‑free)
        vad.enabled = true;
        IsActive = true;
        _isTransitioning = false;
    }

    IEnumerator CoEnd()
    {
        _isTransitioning = true;
        // Soft stop: disable VAD so it won’t emit more segments
        if (vad) vad.enabled = false;

        // Small grace so stepping across the boundary doesn’t cut mid‑word
        yield return new WaitForSeconds(exitGraceSeconds);

        // Hard stop mic (saves CPU and prevents background pickup outside the zone)
        if (vad && !string.IsNullOrEmpty(vad.micDevice) && Microphone.IsRecording(vad.micDevice))
            Microphone.End(vad.micDevice);

        IsActive = false;
        _isTransitioning = false;
    }
}
