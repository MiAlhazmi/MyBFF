using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class MicUploadToN8n : MonoBehaviour
{
    public Action<bool> OnUploadFinished;  // true=ok, false=fail
    
    [Header("n8n")]
    public string webhookUrl;

    [Header("Source")]
    public SimpleMicRecorder micRecorder;

    [Header("Playback (optional)")]
    [Tooltip("If null, will use the AudioSource on this GameObject.")]
    public AudioSource playbackSource;
    
    [HideInInspector] public AudioClip lastClip; // Set by AutoMicVAD

    void Awake()
    {
        if (playbackSource == null)
            playbackSource = GetComponent<AudioSource>();
    }

    // UI Button
    public void SendLastClipToWebhook()
    {
        if (string.IsNullOrEmpty(webhookUrl)) { Debug.LogError("[Upload] Webhook URL is empty."); return; }
    
        // AutoMicVAD sets lastClip directly, so check that instead
        if (lastClip == null) { Debug.LogError("[Upload] No recorded clip."); return; }
    
        string userId = UserIdManager.GetUserId();
        string url = $"{webhookUrl}?userId={UnityWebRequest.EscapeURL(userId)}";
        StartCoroutine(CoSendAndPlayReply(url, lastClip));
    }

    private IEnumerator CoSendAndPlayReply(string url, AudioClip clip)
    {
        // --- Upload recorded audio as WAV ---
        byte[] wavBytes = WavEncoder.EncodeToWAV(clip, out string filename);

        WWWForm form = new WWWForm();
        form.AddBinaryData("file", wavBytes, filename, "audio/wav");

        using (UnityWebRequest req = UnityWebRequest.Post(url, form))
        {
            req.SetRequestHeader("Accept", "audio/wav,audio/mpeg,*/*");

            yield return req.SendWebRequest();

            // Debug (optional)
            Debug.Log("Status: " + req.responseCode);
            Debug.Log("Content-Type: " + req.GetResponseHeader("Content-Type"));

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Upload] Failed ({req.responseCode}): {req.error}\n{req.downloadHandler.text}");
                OnUploadFinished?.Invoke(false);
                yield break;
            }

            byte[] reply = req.downloadHandler.data;
            if (reply == null || reply.Length == 0)
            {
                Debug.LogWarning("[Upload] Empty reply body.");
                OnUploadFinished?.Invoke(false);
                yield break;
            }

            // --- Decide format from magic bytes (override headers) ---
            string ctype = (req.GetResponseHeader("Content-Type") ?? "").ToLowerInvariant();

            // Magic bytes
            bool isRIFF = reply.Length >= 12 &&
                          reply[0] == (byte)'R' && reply[1] == (byte)'I' &&
                          reply[2] == (byte)'F' && reply[3] == (byte)'F' &&
                          reply[8] == (byte)'W' && reply[9] == (byte)'A' &&
                          reply[10] == (byte)'V' && reply[11] == (byte)'E';

            bool isID3  = reply.Length >= 3 &&
                          reply[0] == 0x49 && reply[1] == 0x44 && reply[2] == 0x33; // "ID3"
            bool isMPEGFrame = reply.Length >= 2 &&
                               reply[0] == 0xFF && (reply[1] & 0xE0) == 0xE0;       // MPEG frame sync

            AudioType audioType;
            string ext;

            // Prefer magic bytes over Content-Type
            if (isRIFF) { audioType = AudioType.WAV;  ext = ".wav"; }
            else if (isID3 || isMPEGFrame) { audioType = AudioType.MPEG; ext = ".mp3"; }
            else if (ctype.Contains("wav")) { audioType = AudioType.WAV;  ext = ".wav"; }
            else if (ctype.Contains("mpeg") || ctype.Contains("mp3")) { audioType = AudioType.MPEG; ext = ".mp3"; }
            else { audioType = AudioType.UNKNOWN; ext = ".bin"; }

            string tempPath = Path.Combine(Application.temporaryCachePath,
                $"webhook_reply_{DateTime.UtcNow.Ticks}{ext}");
            File.WriteAllBytes(tempPath, reply);

            // --- Load AudioClip from temp file ---
            using (var get = UnityWebRequestMultimedia.GetAudioClip("file://" + tempPath, audioType))
            {
                yield return get.SendWebRequest();

                if (get.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("[Upload] Could not load reply clip: " + get.error);
                    try { File.Delete(tempPath); } catch {}
                    yield break;
                }

                var clipReply = DownloadHandlerAudioClip.GetContent(get);
                var src = playbackSource != null ? playbackSource : GetComponent<AudioSource>();
                if (src == null) { Debug.LogWarning("[Upload] No AudioSource."); try { File.Delete(tempPath); } catch {} yield break; }

                src.Stop();
                src.clip = clipReply;
                src.Play();
                Debug.Log("[Upload] Played reply audio.");
                OnUploadFinished?.Invoke(true);
            }

            try { File.Delete(tempPath); } catch {}

        }
    }
}
