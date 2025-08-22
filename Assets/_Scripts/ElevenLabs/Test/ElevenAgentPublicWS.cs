// ElevenAgentPublicWS.cs  (Low-latency + Pre-roll + Barge-in + Worker-thread send)
// Requirements:
// 1) Package Manager -> Add by name -> com.unity.nuget.newtonsoft-json
// 2) Attach to a GameObject with an AudioSource component.
// 3) Set your Agent ID in the Inspector.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class ElevenAgentPublicWS : MonoBehaviour
{
    public enum MicControlMode { PushToTalkHold, ToggleToTalk, AutoOnConnect, AlwaysOn }

    [Header("Agent")]
    [Tooltip("Your public Agent ID from ElevenLabs")]
    public string agentId = "<PUT_YOUR_PUBLIC_AGENT_ID>";

    [Header("Mic Control")]
    public MicControlMode micControl = MicControlMode.PushToTalkHold;
    public KeyCode pushToTalkKey = KeyCode.Space;

    [Header("Microphone (Send Path)")]
    [Tooltip("If true, try to capture at 16 kHz to avoid resampling before sending")]
    public bool tryMicAt16k = true;
    [Tooltip("Mic capture rate if 16k isn't available (typical: 48000)")]
    public int fallbackMicSampleRate = 48000;
    [Range(0.02f, 0.12f)]
    [Tooltip("Seconds of audio per WS chunk (smaller = lower latency, higher overhead)")]
    public float chunkSeconds = 0.05f; // ~50 ms

    [Header("Playback (Receive Path)")]
    [Tooltip("Target pre-roll in milliseconds before starting TTS playback")]
    public int playbackPrerollMs = 250;

    [Header("Barge-in / Echo Control")]
    [Tooltip("Mute/duck agent audio while you speak (PTT or Auto modes)")]
    public bool enableDucking = true;
    [Range(0f, 1f)]
    [Tooltip("Volume factor during ducking (0 = mute)")]
    public float duckVolume = 0.0f; // mute by default for strongest barge-in
    [Tooltip("Drop agent audio chunks while speaking so you don't hear the 'overlapped' words")]
    public bool dropAgentAudioWhileSpeaking = true;

    [Header("UI / Testing")]
    public string textToSend = "Tell me a joke about GPUs.";
    public bool sendTestMessage;

    [Header("Audio Output")]
    public AudioSource outputSource;

    // --- Internals ---
    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;
    private Uri _wssUri;
    private readonly SemaphoreSlim _sendSemaphore = new SemaphoreSlim(1, 1); // serialize sends

    // Mic capture
    private AudioClip _micClip;
    private int _micChannels = 1;
    private int _micSampleRateActual;
    private int _micLastPos = 0;
    private float[] _micReadBuf; // reused
    private FloatRingBuffer _micBuf; // no allocations while slicing

    // Send worker (low-GC)
    private Thread _sendThread;
    private AutoResetEvent _sendKick;
    private volatile bool _sendWorkerRunning;
    private Queue<Chunk> _sendQueue;
    private Stack<Chunk> _chunkPool;
    private int _samplesPerChunk; // at mic sample rate
    private byte[] _pcm16SendBuf; // reused per send
    private float[] _resampleScratch; // reused if we need resampling to 16k

    // Receive / playback
    private ConcurrentQueue<Action> _mainQueue = new ConcurrentQueue<Action>();
    private MemoryStream _recvMs; // reused for WS message assembly

    private const int TargetHz = 16000; // Eleven expects 16k mono PCM16
    private AudioRingBuffer _rb;  // playback ring buffer
    private int _outHz;
    private bool _playbackPrimed;
    private int _prerollTargetSamples;
    private float[] _monoReadTmp; // reused per OnAudioFilterRead

    // Barge-in state
    private bool _speaking;           // mic active
    private float _normalVolume = 1f;

    // Thread-safe send helper (string payload)
    private async Task SendTextAsync(string json)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) return;
        var bytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(json));
        await _sendSemaphore.WaitAsync(_cts.Token);
        try
        {
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token);
        }
        finally { _sendSemaphore.Release(); }
    }

    private void Awake()
    {
        if (!outputSource) outputSource = gameObject.AddComponent<AudioSource>();
        _normalVolume = outputSource.volume;

        // Playback setup
        _outHz = AudioSettings.outputSampleRate;
        _rb = new AudioRingBuffer(_outHz * 6); // ~6 sec buffer for jitter smoothing
        _prerollTargetSamples = Mathf.Max(1, _outHz * playbackPrerollMs / 1000);

        // Keep audio thread running; we feed it via ring buffer
        var silent = AudioClip.Create("AgentStreamSilence", _outHz, 1, _outHz, false);
        outputSource.loop = true;
        outputSource.clip = silent;
        outputSource.Play();

        // Mic ring buffer (2 seconds capacity at worst-case rate)
        _micSampleRateActual = tryMicAt16k ? TargetHz : fallbackMicSampleRate;
        _micBuf = new FloatRingBuffer(Mathf.Max(1, _micSampleRateActual * 2));
    }

    private async void Start()
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            Debug.LogError("Agent ID is empty.");
            enabled = false; return;
        }

        _wssUri = new Uri($"wss://api.elevenlabs.io/v1/convai/conversation?agent_id={agentId}");
        _cts = new CancellationTokenSource();

        // Start receive buffer
        _recvMs = new MemoryStream(1 << 16);

        // Start send worker infra
        _sendQueue = new Queue<Chunk>(32);
        _chunkPool = new Stack<Chunk>(32);
        _sendKick = new AutoResetEvent(false);
        _sendWorkerRunning = true;
        _sendThread = new Thread(SendWorkerLoop) { IsBackground = true, Name = "EL_SendWorker" };
        _sendThread.Start();

        await Connect();

        // Initial overrides (optional)
        _ = SendTextAsync(MiniJson.Serialize(new
        {
            type = "conversation_initiation_client_data",
            conversation_config_override = new
            {
                agent = new { language = "en" }
            }
        }));

        // Auto mic modes
        if (micControl == MicControlMode.AutoOnConnect || micControl == MicControlMode.AlwaysOn)
            StartMic();
    }

    private async Task Connect()
    {
        _ws = new ClientWebSocket();
        try
        {
            await _ws.ConnectAsync(_wssUri, _cts.Token);
            Debug.Log("WS connected.");
            _ = Task.Run(ReceiveLoop, _cts.Token);
        }
        catch (Exception e)
        {
            Debug.LogError($"WS connect failed: {e.Message}");
        }
    }

    private void Update()
    {
        if (sendTestMessage)
        {
            sendTestMessage = false;
            _ = SendTextAsync(MiniJson.Serialize(new { type = "user_message", text = textToSend }));
        }

        // Mic control state machine (PTT / Toggle / Auto / Always)
        switch (micControl)
        {
            case MicControlMode.PushToTalkHold:
                if (!_speaking && Input.GetKeyDown(pushToTalkKey)) StartMic();
                else if (_speaking && Input.GetKeyUp(pushToTalkKey)) StopMic();
                break;

            case MicControlMode.ToggleToTalk:
                if (Input.GetKeyDown(pushToTalkKey))
                {
                    if (_speaking) StopMic(); else StartMic();
                }
                break;

            case MicControlMode.AutoOnConnect:
                // Started once on connect
                break;

            case MicControlMode.AlwaysOn:
                if (!_speaking) StartMic();
                break;
        }

        // Slice mic → enqueue chunks for worker (no heavy work here)
        if (_speaking && _ws != null && _ws.State == WebSocketState.Open && _micClip)
        {
            int pos = Microphone.GetPosition(null);
            if (pos >= 0 && pos != _micLastPos)
            {
                int sampleCount = (pos - _micLastPos);
                if (sampleCount < 0) sampleCount += _micClip.samples;

                // read new mic frames into _micReadBuf
                int neededFloats = sampleCount * _micChannels;
                if (_micReadBuf == null || _micReadBuf.Length < neededFloats)
                    _micReadBuf = new float[neededFloats];

                _micClip.GetData(_micReadBuf, _micLastPos);
                _micLastPos = pos;

                // downmix to mono if needed and write to ring
                if (_micChannels == 1)
                {
                    _micBuf.Write(_micReadBuf, 0, sampleCount);
                }
                else
                {
                    // inline downmix (avoid alloc)
                    for (int i = 0; i < sampleCount; i++)
                    {
                        float sum = 0f;
                        for (int c = 0; c < _micChannels; c++)
                            sum += _micReadBuf[i * _micChannels + c];
                        float mono = sum / _micChannels;
                        _micBuf.WriteSample(mono);
                    }
                }

                // pull fixed-size chunks from ring and enqueue to worker
                while (_micBuf.Available >= _samplesPerChunk && _sendWorkerRunning)
                {
                    var ch = RentChunk();
                    _micBuf.Read(ch.data, 0, _samplesPerChunk);
                    ch.length = _samplesPerChunk;
                    EnqueueChunk(ch);
                }
            }
        }

        // Any main-thread callbacks
        while (_mainQueue.TryDequeue(out var a)) a?.Invoke();
    }

    private void OnDestroy()
    {
        try
        {
            _sendWorkerRunning = false;
            _sendKick?.Set();
        }
        catch { }

        StopMic();

        try { _cts?.Cancel(); } catch { }
        try { _ws?.Dispose(); } catch { }

        try { _sendThread?.Join(200); } catch { }
        _recvMs?.Dispose();
    }

    // --- Mic start/stop & barge-in ---

    private void StartMic()
    {
        if (_speaking) return;
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("No microphone devices.");
            return;
        }

        _micSampleRateActual = tryMicAt16k ? TargetHz : fallbackMicSampleRate;

        int lengthSec = 10;
        _micClip = Microphone.Start(null, true, lengthSec, _micSampleRateActual);
        _micChannels = _micClip.channels;
        _micLastPos = 0;
        _micBuf.Clear();

        // precompute samples per chunk
        _samplesPerChunk = Mathf.Max(1, Mathf.RoundToInt(chunkSeconds * _micSampleRateActual));

        // prepare worker scratch buffers for this rate
        EnsureSendBuffers(_samplesPerChunk);

        _speaking = true;

        // Barge-in: clear playback buffer & duck volume; optionally drop incoming agent audio
        if (enableDucking)
        {
            _normalVolume = outputSource.volume;
            outputSource.volume = duckVolume; // 0 = hard mute
        }
        if (dropAgentAudioWhileSpeaking)
        {
            _rb.Clear();
            _playbackPrimed = false;
        }

        Debug.Log($"Mic started: {_micSampleRateActual} Hz, ch={_micChannels}, chunk={_samplesPerChunk} samples (~{chunkSeconds * 1000f:F0} ms)");
    }

    private void StopMic()
    {
        if (!_speaking) return;

        if (_micClip)
        {
            Microphone.End(null);
            _micClip = null;
        }

        _micBuf.Clear();
        _speaking = false;

        // Restore volume; allow agent audio to play again
        if (enableDucking) outputSource.volume = _normalVolume;
        // keep _playbackPrimed as-is; it'll re-prime if we cleared during speaking

        Debug.Log("Mic stopped.");
    }

    // --- Send worker infra ---

    private class Chunk
    {
        public float[] data; // mono at mic rate
        public int length;   // valid samples
    }

    private void EnsureSendBuffers(int samplesPerChunk)
    {
        if (_pcm16SendBuf == null || _pcm16SendBuf.Length < samplesPerChunk * 2) // 2 bytes per sample after resample (worst case equal length)
            _pcm16SendBuf = new byte[samplesPerChunk * 4]; // headroom
        int maxResampled = Mathf.CeilToInt(samplesPerChunk * (float)TargetHz / Mathf.Max(1, _micSampleRateActual)) + 8;
        if (_resampleScratch == null || _resampleScratch.Length < maxResampled)
            _resampleScratch = new float[maxResampled];

        // prime chunk pool
        if (_chunkPool.Count == 0)
        {
            for (int i = 0; i < 32; i++)
                _chunkPool.Push(new Chunk { data = new float[samplesPerChunk], length = 0 });
        }
    }

    private Chunk RentChunk()
    {
        lock (_chunkPool)
        {
            if (_chunkPool.Count > 0) return _chunkPool.Pop();
        }
        // fallback (rare)
        return new Chunk { data = new float[_samplesPerChunk], length = 0 };
    }

    private void ReturnChunk(Chunk c)
    {
        c.length = 0;
        lock (_chunkPool)
        {
            _chunkPool.Push(c);
        }
    }

    private void EnqueueChunk(Chunk ch)
    {
        lock (_sendQueue)
        {
            _sendQueue.Enqueue(ch);
        }
        _sendKick.Set();
    }

    private void SendWorkerLoop()
    {
        try
        {
            while (_sendWorkerRunning)
            {
                _sendKick.WaitOne(50);
                while (true)
                {
                    Chunk ch = null;
                    lock (_sendQueue)
                    {
                        if (_sendQueue.Count > 0) ch = _sendQueue.Dequeue();
                    }
                    if (ch == null) break;

                    try
                    {
                        // Resample to 16k if needed (in-place into _resampleScratch)
                        float[] toSend;
                        int sendLen;
                        if (_micSampleRateActual == TargetHz)
                        {
                            toSend = ch.data;
                            sendLen = ch.length;
                        }
                        else
                        {
                            sendLen = ResampleLinearInto(ch.data, ch.length, _micSampleRateActual, TargetHz, _resampleScratch);
                            toSend = _resampleScratch;
                        }

                        // PCM16 -> bytes (LE) into _pcm16SendBuf
                        int byteLen = FloatToPcm16LE(toSend, sendLen, ref _pcm16SendBuf);

                        // Base64 & JSON (manual, minimal alloc beyond the string itself)
                        string b64 = Convert.ToBase64String(_pcm16SendBuf, 0, byteLen);
                        string json = "{\"user_audio_chunk\":\"" + b64 + "\"}";

                        // Serialize send operations with semaphore (shared with main thread sends)
                        _sendSemaphore.Wait(_cts.Token);
                        try
                        {
                            if (_ws != null && _ws.State == WebSocketState.Open)
                            {
                                var bytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(json));
                                _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token).Wait(_cts.Token);
                            }
                        }
                        finally { _sendSemaphore.Release(); }
                    }
                    catch (OperationCanceledException) { /* shutting down */ }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"SendWorker error: {e.Message}");
                    }
                    finally
                    {
                        ReturnChunk(ch);
                    }
                }
            }
        }
        catch { /* thread exiting */ }
    }

    // --- Receive loop (reuses a single MemoryStream) ---

    private async Task ReceiveLoop()
    {
        var buf = new byte[1 << 15];
        var seg = new ArraySegment<byte>(buf);

        while (!_cts.IsCancellationRequested && _ws.State == WebSocketState.Open)
        {
            WebSocketReceiveResult res;
            _recvMs.SetLength(0);

            try
            {
                do
                {
                    res = await _ws.ReceiveAsync(seg, _cts.Token);
                    if (res.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.Log("WS closed by server.");
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", _cts.Token);
                        return;
                    }
                    _recvMs.Write(seg.Array, 0, res.Count);
                } while (!res.EndOfMessage);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e)
            {
                Debug.LogWarning($"WS recv error: {e.Message}");
                break;
            }

            string msg = Encoding.UTF8.GetString(_recvMs.GetBuffer(), 0, (int)_recvMs.Length);
            HandleMessage(msg);
        }
    }

    private async void SendPong(object evId)
    {
        await SendTextAsync(MiniJson.Serialize(new { type = "pong", event_id = evId }));
    }

    private void HandleMessage(string json)
    {
        var obj = MiniJson.Deserialize(json) as Dictionary<string, object>;
        if (obj == null) return;

        if (obj.TryGetValue("type", out var typeObj))
        {
            string type = typeObj as string;
            switch (type)
            {
                case "ping":
                    if (obj.TryGetValue("ping_event", out var pingEvObj)
                        && pingEvObj is Dictionary<string, object> pingEv
                        && pingEv.TryGetValue("event_id", out var evId))
                        SendPong(evId);
                    break;

                case "agent_response":
                    if (obj.TryGetValue("agent_response_event", out var evObj)
                        && evObj is Dictionary<string, object> ev
                        && ev.TryGetValue("agent_response", out var txtObj))
                        Debug.Log($"Agent: {txtObj as string}");
                    break;

                case "audio":
                    if (_speaking && dropAgentAudioWhileSpeaking)
                    {
                        // barge-in: discard incoming audio while speaking
                        return;
                    }

                    if (obj.TryGetValue("audio_event", out var aevObj)
                        && aevObj is Dictionary<string, object> aev
                        && aev.TryGetValue("audio_base_64", out var b64Obj))
                    {
                        string b64 = b64Obj as string;
                        if (!string.IsNullOrEmpty(b64))
                        {
                            byte[] pcm = Convert.FromBase64String(b64);   // unavoidable alloc (string -> bytes)
                            float[] s16k = Pcm16ToFloat(pcm);             // 16k mono
                            float[] up = ResampleLinear(s16k, TargetHz, _outHz);
                            _rb.Write(up);                                // enqueue for smooth playback
                        }
                    }
                    break;

                case "user_transcript":
                    if (obj.TryGetValue("user_transcript_event", out var utevObj)
                        && utevObj is Dictionary<string, object> utev
                        && utev.TryGetValue("transcript", out var trObj))
                        Debug.Log($"You said: {trObj as string}");
                    break;
            }
        }
    }

    // --- Audio callback: pre-roll + smooth playback (no alloc) ---

    private void OnAudioFilterRead(float[] data, int channels)
    {
        // Pre-roll: wait until we have enough buffered before starting playback
        if (!_playbackPrimed)
        {
            if (_rb.Count >= _prerollTargetSamples) _playbackPrimed = true;
            Array.Clear(data, 0, data.Length);
            return;
        }

        int neededMono = data.Length / channels;

        if (_monoReadTmp == null || _monoReadTmp.Length < neededMono)
            _monoReadTmp = new float[neededMono]; // grows rarely

        int got = _rb.Read(_monoReadTmp, 0, neededMono);
        if (got < neededMono)
        {
            // underrun -> fill remainder with silence
            Array.Clear(_monoReadTmp, got, neededMono - got);
        }

        // Interleave mono to N channels
        int di = 0;
        for (int i = 0; i < neededMono; i++)
        {
            float s = _monoReadTmp[i];
            for (int c = 0; c < channels; c++) data[di++] = s;
        }
    }

    // --- Utility: resampling + PCM pack/unpack (reused buffers) ---

    private static float[] ResampleLinear(float[] input, int inHz, int outHz)
    {
        if (inHz == outHz) return input; // safe: only used on receive path
        double ratio = (double)outHz / inHz;
        int outLen = (int)Math.Ceiling(input.Length * ratio);
        var output = new float[outLen]; // allocation on receive path is acceptable (not per-frame)
        for (int i = 0; i < outLen; i++)
        {
            double srcPos = i / ratio;
            int i0 = (int)srcPos;
            int i1 = Math.Min(i0 + 1, input.Length - 1);
            double t = srcPos - i0;
            output[i] = (float)((1.0 - t) * input[i0] + t * input[i1]);
        }
        return output;
    }

    // Resample into provided buffer, return length written
    private static int ResampleLinearInto(float[] input, int inLen, int inHz, int outHz, float[] dst)
    {
        if (inHz == outHz)
        {
            Array.Copy(input, 0, dst, 0, inLen);
            return inLen;
        }
        double ratio = (double)outHz / inHz;
        int outLen = (int)Math.Ceiling(inLen * ratio);
        for (int i = 0; i < outLen; i++)
        {
            double srcPos = i / ratio;
            int i0 = (int)srcPos;
            int i1 = Math.Min(i0 + 1, inLen - 1);
            double t = srcPos - i0;
            dst[i] = (float)((1.0 - t) * input[i0] + t * input[i1]);
        }
        return outLen;
    }

    // Pack float[-1..1] -> PCM16 LE into a reusable byte buffer, return bytes written
    private static int FloatToPcm16LE(float[] src, int len, ref byte[] dst)
    {
        int need = len * 2;
        if (dst == null || dst.Length < need) dst = new byte[need];
        int di = 0;
        for (int i = 0; i < len; i++)
        {
            float f = Mathf.Clamp(src[i], -1f, 1f);
            short s = (short)Mathf.RoundToInt(f * 32767f);
            dst[di++] = (byte)(s & 0xFF);
            dst[di++] = (byte)((s >> 8) & 0xFF);
        }
        return di;
    }

    private static float[] Pcm16ToFloat(byte[] pcm)
    {
        int n = pcm.Length / 2;
        var f = new float[n];
        int pi = 0;
        for (int i = 0; i < n; i++)
        {
            short s = (short)(pcm[pi] | (pcm[pi + 1] << 8));
            f[i] = s / 32768f;
            pi += 2;
        }
        return f;
    }
}

// --- Lock-minimal float ring buffer for mic capture ---
public class FloatRingBuffer
{
    private readonly float[] _buf;
    private int _w, _r, _count;
    private readonly object _lock = new object();

    public FloatRingBuffer(int capacity) { _buf = new float[capacity]; }

    public int Available { get { lock (_lock) return _count; } }

    public void Clear() { lock (_lock) { _w = _r = _count = 0; } }

    public void Write(float[] src, int offset, int len)
    {
        lock (_lock)
        {
            for (int i = 0; i < len; i++)
            {
                if (_count == _buf.Length) { _r = (_r + 1) % _buf.Length; _count--; }
                _buf[_w] = src[offset + i];
                _w = (_w + 1) % _buf.Length;
                _count++;
            }
        }
    }

    public void WriteSample(float s)
    {
        lock (_lock)
        {
            if (_count == _buf.Length) { _r = (_r + 1) % _buf.Length; _count--; }
            _buf[_w] = s;
            _w = (_w + 1) % _buf.Length;
            _count++;
        }
    }

    public int Read(float[] dst, int offset, int len)
    {
        int read = 0;
        lock (_lock)
        {
            int toRead = Math.Min(len, _count);
            read = toRead;
            while (toRead-- > 0)
            {
                dst[offset++] = _buf[_r];
                _r = (_r + 1) % _buf.Length;
                _count--;
            }
        }
        if (read < len) Array.Clear(dst, offset, len - read);
        return read;
    }
}

// --- Thread-safe float ring buffer for playback (exposes Count) ---
public class AudioRingBuffer
{
    private readonly float[] _buf;
    private int _w, _r, _count;
    private readonly object _lock = new object();

    public AudioRingBuffer(int capacity) { _buf = new float[capacity]; }

    public int Count { get { lock (_lock) return _count; } }

    public void Clear() { lock (_lock) { _w = _r = _count = 0; } }

    public void Write(float[] src)
    {
        lock (_lock)
        {
            for (int i = 0; i < src.Length; i++)
            {
                if (_count == _buf.Length) { _r = (_r + 1) % _buf.Length; _count--; }
                _buf[_w] = src[i];
                _w = (_w + 1) % _buf.Length;
                _count++;
            }
        }
    }

    public int Read(float[] dst, int offset, int len)
    {
        int read = 0;
        lock (_lock)
        {
            int toRead = Math.Min(len, _count);
            read = toRead;
            while (toRead-- > 0)
            {
                dst[offset++] = _buf[_r];
                _r = (_r + 1) % _buf.Length;
                _count--;
            }
        }
        return read;
    }
}

// --- JSON helper (Newtonsoft-based) ---
static class MiniJson
{
    public static string Serialize(object obj) => JsonConvert.SerializeObject(obj);

    public static object Deserialize(string json)
    {
        var token = JToken.Parse(json);
        return ToPlain(token);
    }

    private static object ToPlain(JToken token)
    {
        if (token is JObject o)
        {
            var dict = new Dictionary<string, object>(o.Count);
            foreach (var p in o) dict[p.Key] = ToPlain(p.Value);
            return dict;
        }
        if (token is JArray a)
        {
            var list = new List<object>(a.Count);
            foreach (var v in a) list.Add(ToPlain(v));
            return list;
        }
        return token.Type switch
        {
            JTokenType.Integer => token.ToObject<long>(),
            JTokenType.Float   => token.ToObject<double>(),
            JTokenType.Boolean => token.ToObject<bool>(),
            JTokenType.Null    => null,
            _                  => token.ToObject<string>()
        };
    }
}
