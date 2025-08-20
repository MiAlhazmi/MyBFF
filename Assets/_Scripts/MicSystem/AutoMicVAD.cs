using System;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AutoMicVAD : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Your existing uploader (MicUploadToN8n) on the same GameObject.")]
    public MicUploadToN8n uploader;   // assign in Inspector

    [Header("Mic")]
    public string micDevice = "";     // empty = default
    public int sampleRate = 44100;
    [Range(5, 30)]
    [Tooltip("Looping mic clip length (must be > max speech + pre-roll).")]
    public int micBufferSeconds = 12;

    [Header("VAD")]
    [Tooltip("Frame size used for analysis (ms). 20ms is standard.")]
    [Range(10, 40)] public int frameMs = 20;
    [Tooltip("Extra audio kept before speech start (ms).")]
    [Range(50, 500)] public int preRollMs = 200;
    [Tooltip("Silence required after speech to finalize (ms).")]
    [Range(400, 1500)] public int hangoverMs = 900;
    [Tooltip("Ignore segments shorter than this (ms).")]
    [Range(300, 1200)] public int minSpeechMs = 700;
    [Tooltip("Hard cap per utterance (ms).")]
    [Range(2000, 20000)] public int maxSpeechMs = 15000;

    [Header("Adaptive Thresholds (multiplier on noise floor)")]
    [Tooltip("Start when RMS > noiseFloor * startFactor (used when useFixedThreshold = false).")]
    public float startFactor = 3.0f;
    [Tooltip("Stay speaking while RMS > noiseFloor * stopFactor.")]
    public float stopFactor = 2.0f;
    [Tooltip("Initial guess for noise floor RMS; adapts while idle.")]
    public float initialNoiseFloor = 0.002f; // ~-54 dBFS

    [Header("Volume Threshold (fixed RMS gate)")]
    [Tooltip("Enable fixed RMS gate instead of adaptive noise floor.")]
    public bool useFixedThreshold = true;
    [Tooltip("Start when RMS exceeds this; stop when it falls below stopRms (hysteresis).")]
    public float startRms = 0.020f;   // ~ -34 dBFS
    public float stopRms  = 0.012f;   // ~ -38 dBFS

    [Header("Multi-band Analysis (Advanced)")]
    [Tooltip("Enable frequency-weighted speech detection for better accuracy.")]
    public bool useMultiBandAnalysis = false;
    [Tooltip("Boost factor for speech frequency band (300-3400 Hz).")]
    [Range(1.0f, 5.0f)] public float speechBandWeight = 2.0f;
    [Tooltip("Suppression factor for low frequencies (<300 Hz) - reduces rumble/breathing.")]
    [Range(0.1f, 1.0f)] public float lowFreqSuppression = 0.4f;
    [Tooltip("Suppression factor for high frequencies (>3400 Hz) - reduces hiss/noise.")]
    [Range(0.1f, 1.0f)] public float highFreqSuppression = 0.6f;
    [Tooltip("Minimum speech band energy ratio to consider it speech-like.")]
    [Range(0.3f, 0.8f)] public float speechBandThreshold = 0.5f;

    [Header("Rate limiting")]
    [Tooltip("Ignore new utterances for this long after a send (ms).")]
    [Range(400, 3000)] public int cooldownAfterSendMs = 1200;
    
    [HideInInspector] public float currentRms;  // live input level (0..~1)
    [HideInInspector] public float speechBandRatio; // % of energy in speech band (for debugging)

    // --- internals ---
    AudioClip _micClip;
    int _micChannels = 1, _micClipSamples;
    int _readHead, _frameSamples, _preRollSamples, _hangoverSamples, _minSpeechSamples, _maxSpeechSamples;

    float[] _ring; int _ringSize, _ringWrite; long _monoWritten;

    enum VState { Idle, Speaking }
    VState _state = VState.Idle;
    float _noiseFloor;

    // anti-spam
    bool _uploadInFlight = false;
    long _cooldownUntilAbs = 0;

    // current segment bookkeeping
    long _segmentStartAbs, _lastLoudAbs, _segmentMaxAbs;

    // Multi-band analysis buffers
    float[] _fftBuffer;
    int _fftSize;

    /// <summary>
    /// Initialize microphone recording and VAD parameters.
    /// Sets up ring buffer, FFT analysis, and starts continuous microphone recording.
    /// </summary>
    /// <summary>
    /// One-time initialization of buffers, calculations, and component setup.
    /// Called once when GameObject is first created in the scene.
    /// </summary>
    void Start()
    {
        // Validate microphone availability
        if (Microphone.devices.Length == 0) 
        { 
            Debug.LogError("[AutoMicVAD] No mic devices."); 
            enabled = false; 
            return; 
        }
    
        // Set default microphone device
        if (string.IsNullOrEmpty(micDevice)) 
            micDevice = Microphone.devices[0];

        // Calculate sample-based timing values (one-time calculations)
        _frameSamples     = Mathf.Max(1, sampleRate * frameMs / 1000);
        _preRollSamples   = sampleRate * preRollMs / 1000;
        _hangoverSamples  = sampleRate * hangoverMs / 1000;
        _minSpeechSamples = sampleRate * minSpeechMs / 1000;
        _maxSpeechSamples = sampleRate * maxSpeechMs / 1000;

        // Allocate ring buffer for audio storage (expensive allocation)
        _ringSize = Mathf.Max(sampleRate * micBufferSeconds,
            _maxSpeechSamples + _preRollSamples + _hangoverSamples + sampleRate);
        _ring = new float[_ringSize];

        // Initialize FFT buffer for multi-band analysis (expensive allocation)
        _fftSize = Mathf.NextPowerOfTwo(_frameSamples);
        _fftBuffer = new float[_fftSize];

        // Set up uploader callback (one-time setup)
        if (uploader != null)
        {
            uploader.OnUploadFinished = ok =>
            {
                _uploadInFlight = false;
                int cooldownSamples = sampleRate * cooldownAfterSendMs / 1000;
                _cooldownUntilAbs = _monoWritten + cooldownSamples;
            };
        }
    }
    
    /// <summary>
    /// Initialize microphone recording and reset VAD state.
    /// Called every time the component becomes enabled (conversation starts).
    /// </summary>
    void OnEnable()
    {
        // Reset all state variables for fresh conversation
        _readHead = 0;
        _ringWrite = 0;
        _monoWritten = 0;
        _state = VState.Idle;
        _noiseFloor = initialNoiseFloor;
        _uploadInFlight = false;
        _cooldownUntilAbs = 0;
        currentRms = 0f;
        speechBandRatio = 0f;

        // Start microphone recording
        _micClip = Microphone.Start(micDevice, true, micBufferSeconds, sampleRate);
        if (_micClip == null) 
        { 
            Debug.LogError("[AutoMicVAD] Microphone.Start failed."); 
            enabled = false; 
            return; 
        }

        // Get microphone channel info (may vary between sessions)
        _micChannels = Mathf.Max(1, _micClip.channels);
        _micClipSamples = _micClip.samples; // per-channel

        Debug.Log("[AutoMicVAD] Microphone started for conversation.");
    }

    /// <summary>
    /// Stop microphone recording and clean up when component is disabled.
    /// Called when conversation ends or component is disabled.
    /// </summary>
    void OnDisable()
    {
        // Stop microphone recording
        if (!string.IsNullOrEmpty(micDevice) && Microphone.IsRecording(micDevice))
        {
            Microphone.End(micDevice);
            Debug.Log("[AutoMicVAD] Microphone stopped.");
        }

        // Clean up clip reference
        _micClip = null;

        // Reset upload state to prevent issues on next enable
        _uploadInFlight = false;
    }
    
    /// <summary>
    /// Clean up microphone resources when component is destroyed.
    /// </summary>
    void OnDestroy()
    {
        if (_micClip != null && Microphone.IsRecording(micDevice)) Microphone.End(micDevice);
    }

    /// <summary>
    /// Main VAD processing loop - reads microphone data and detects speech segments.
    /// Processes audio in frames and manages speech detection state machine.
    /// </summary>
    void Update()
    {
        if (_micClip == null) return;

        int micPos = Microphone.GetPosition(micDevice); // per-channel samples written
        if (micPos < 0) return;

        int available = micPos - _readHead;
        if (available < 0) available += _micClipSamples;

        while (available >= _frameSamples)
        {
            // ---- read one frame from looping mic clip (handle wrap) ----
            int toEnd = _micClipSamples - _readHead;
            int readA = Mathf.Min(_frameSamples, toEnd);
            int readB = _frameSamples - readA;

            float[] frameInterleaved = GetTemp(_frameSamples * _micChannels);
            if (readA > 0) _micClip.GetData(frameInterleaved, _readHead);
            if (readB > 0)
            {
                float[] tmp = GetTemp(readB * _micChannels);
                _micClip.GetData(tmp, 0);
                Buffer.BlockCopy(tmp, 0, frameInterleaved, readA * _micChannels * sizeof(float), readB * _micChannels * sizeof(float));
            }
            _readHead = (_readHead + _frameSamples) % _micClipSamples;
            available -= _frameSamples;

            // ---- downmix to mono + push into ring ----
            float[] frameMono = GetTemp(_frameSamples);
            if (_micChannels == 1)
                Array.Copy(frameInterleaved, frameMono, _frameSamples);
            else
            {
                int idx = 0;
                for (int i = 0; i < _frameSamples; i++)
                {
                    float sum = 0f;
                    for (int c = 0; c < _micChannels; c++) sum += frameInterleaved[idx++];
                    frameMono[i] = sum / _micChannels;
                }
            }
            PushMono(frameMono);

            // ---- analyze frame for speech content ----
            float analysisValue;
            if (useMultiBandAnalysis)
            {
                analysisValue = AnalyzeSpectrum(frameMono);
            }
            else
            {
                analysisValue = RMS(frameMono);
                speechBandRatio = 0f; // Not applicable for simple RMS
            }
            
            currentRms = analysisValue;

            if (_state == VState.Idle)
                _noiseFloor = Mathf.Lerp(_noiseFloor, analysisValue, 0.02f); // adapt only in idle

            bool cooling = _monoWritten < _cooldownUntilAbs;

            // gates (use analysisValue instead of raw RMS)
            bool startGate = useFixedThreshold ? (analysisValue >= startRms)
                                               : (analysisValue > Mathf.Max(_noiseFloor * startFactor, 0.0005f));
            bool stopGate  = useFixedThreshold ? (analysisValue >= stopRms)
                                               : (analysisValue > Mathf.Max(_noiseFloor * stopFactor, 0.0004f));

            switch (_state)
            {
                case VState.Idle:
                    if (!cooling && startGate)
                    {
                        _segmentStartAbs = Math.Max(0, _monoWritten - _preRollSamples);
                        _lastLoudAbs = _monoWritten;
                        _segmentMaxAbs = _monoWritten + _maxSpeechSamples;
                        _state = VState.Speaking;
                    }
                    break;

                case VState.Speaking:
                    if (stopGate) _lastLoudAbs = _monoWritten;

                    bool hangoverDone = (_monoWritten - _lastLoudAbs) >= _hangoverSamples;
                    bool hitMax = _monoWritten >= _segmentMaxAbs;

                    if (hangoverDone || hitMax)
                    {
                        long len = _monoWritten - _segmentStartAbs;
                        if (len >= _minSpeechSamples) TryEmitSegment(_segmentStartAbs, (int)len);
                        _state = VState.Idle;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Analyze audio spectrum to detect speech characteristics.
    /// Weights different frequency bands to improve speech vs noise discrimination.
    /// </summary>
    /// <param name="mono">Mono audio frame to analyze</param>
    /// <returns>Weighted energy value representing speech likelihood</returns>
    float AnalyzeSpectrum(float[] mono)
    {
        // Prepare FFT input (zero-pad if necessary)
        Array.Clear(_fftBuffer, 0, _fftSize);
        Array.Copy(mono, _fftBuffer, Mathf.Min(mono.Length, _fftSize));

        // Apply simple window function to reduce spectral leakage
        ApplyHannWindow(_fftBuffer, mono.Length);

        // Perform FFT (Unity doesn't have built-in FFT, so we'll use a simplified approach)
        // For production, you'd want to use a proper FFT implementation
        // This is a simplified frequency domain analysis
        
        float totalEnergy = 0f;
        float speechBandEnergy = 0f;
        float lowFreqEnergy = 0f;
        float highFreqEnergy = 0f;

        // Calculate energy in different frequency bands
        int nyquistBin = _fftSize / 2;
        float freqPerBin = (float)sampleRate / _fftSize;
        
        for (int i = 0; i < nyquistBin; i++)
        {
            float freq = i * freqPerBin;
            float energy = _fftBuffer[i] * _fftBuffer[i];
            
            totalEnergy += energy;
            
            if (freq < 300f)
            {
                lowFreqEnergy += energy;
            }
            else if (freq >= 300f && freq <= 3400f)
            {
                speechBandEnergy += energy;
            }
            else
            {
                highFreqEnergy += energy;
            }
        }

        // Avoid division by zero
        if (totalEnergy < 1e-10f)
        {
            speechBandRatio = 0f;
            return 0f;
        }

        // Calculate speech band ratio for debugging
        speechBandRatio = speechBandEnergy / totalEnergy;

        // Apply frequency weighting
        float weightedEnergy = (speechBandEnergy * speechBandWeight + 
                               lowFreqEnergy * lowFreqSuppression + 
                               highFreqEnergy * highFreqSuppression) / totalEnergy;

        // Additional speech likelihood check
        if (speechBandRatio < speechBandThreshold)
        {
            weightedEnergy *= 0.5f; // Reduce confidence if not speech-like
        }

        // Convert back to RMS-like scale
        return Mathf.Sqrt(weightedEnergy) * Mathf.Sqrt(totalEnergy);
    }

    /// <summary>
    /// Apply Hann window function to reduce spectral leakage in FFT analysis.
    /// </summary>
    /// <param name="buffer">Audio buffer to apply window to</param>
    /// <param name="length">Valid length of audio data</param>
    void ApplyHannWindow(float[] buffer, int length)
    {
        for (int i = 0; i < length; i++)
        {
            float window = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * i / (length - 1)));
            buffer[i] *= window;
        }
    }

    /// <summary>
    /// Attempt to emit a detected speech segment for upload.
    /// Creates AudioClip from ring buffer and triggers upload process.
    /// </summary>
    /// <param name="absStart">Absolute start position in sample stream</param>
    /// <param name="length">Length of segment in samples</param>
    void TryEmitSegment(long absStart, int length)
    {
        if (_uploadInFlight) return;
        if (_monoWritten < _cooldownUntilAbs) return;
        if (length <= 0 || length > _ringSize - 8) return;

        float[] seg = new float[length];
        int start = (int)(absStart % _ringSize);
        if (start < 0) start += _ringSize;

        int first = Mathf.Min(length, _ringSize - start);
        Array.Copy(_ring, start, seg, 0, first);
        if (first < length) Array.Copy(_ring, 0, seg, first, length - first);

        var clip = AudioClip.Create("Utterance", length, 1, sampleRate, false);
        clip.SetData(seg, 0);

        if (uploader == null)
        {
            Debug.LogWarning("[AutoMicVAD] Missing uploader.");
            return;
        }

        _uploadInFlight = true;
        uploader.lastClip = clip;
        uploader.SendLastClipToWebhook();
    }

    /// <summary>
    /// Get temporary array for audio processing (reuses memory to avoid allocations).
    /// </summary>
    /// <param name="len">Required array length</param>
    /// <returns>Temporary float array</returns>
    static float[] _tmp;
    static float[] GetTemp(int len) { if (_tmp == null || _tmp.Length < len) _tmp = new float[len]; return _tmp; }

    /// <summary>
    /// Push mono audio samples into the ring buffer for segment storage.
    /// </summary>
    /// <param name="mono">Mono audio samples to store</param>
    void PushMono(float[] mono)
    {
        for (int i = 0; i < mono.Length; i++)
        {
            _ring[_ringWrite] = mono[i];
            _ringWrite = (_ringWrite + 1) % _ringSize;
        }
        _monoWritten += mono.Length;
    }

    /// <summary>
    /// Calculate Root Mean Square (RMS) energy of audio samples.
    /// Used for simple energy-based voice activity detection.
    /// </summary>
    /// <param name="d">Audio sample array</param>
    /// <returns>RMS energy value</returns>
    static float RMS(float[] d)
    {
        double s = 0;
        for (int i = 0; i < d.Length; i++) s += d[i] * d[i];
        return Mathf.Sqrt((float)(s / d.Length));
    }
}