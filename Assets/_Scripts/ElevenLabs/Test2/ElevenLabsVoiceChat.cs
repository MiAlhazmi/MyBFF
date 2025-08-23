using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using MyBFF.Character;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MyBFF.Voice
{
    /// <summary>
    /// Complete ElevenLabs Conversational AI integration for Unity.
    /// Handles WebSocket connection, microphone capture, audio streaming, and playback.
    /// Based on proven working implementation.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class ElevenLabsVoiceChat : MonoBehaviour
    {
        [Header("ElevenLabs Agent")]
        [Tooltip("Your ElevenLabs Agent ID")]
        public string agentId = "agent_4001k20ry6gyfj2bt9nm607v3hqz";
        
        [Header("Audio Settings")]
        [Tooltip("Microphone device (empty for default)")]
        public string microphoneDevice = "";
        [Tooltip("Microphone sample rate (will be resampled to 16kHz for ElevenLabs)")]
        public int micSampleRate = 44100;
        [Range(0.1f, 0.5f)]
        [Tooltip("Seconds of audio per chunk sent to ElevenLabs")]
        public float chunkSeconds = 0.25f;
        
        [Header("Debug")]
        [Tooltip("Enable detailed logging")]
        public bool enableDebugLogging = true;
        [Tooltip("Log audio chunks (very verbose)")]
        public bool logAudioChunks = false;
        
        public bool IsConversationActive => isConversationActive;
        
        // WebSocket connection
        private ClientWebSocket webSocket;
        private CancellationTokenSource cancellationTokenSource;
        private Uri webSocketUri;
        
        // Microphone capture
        private AudioClip microphoneClip;
        private int microphoneChannels = 1;
        private int lastMicrophonePosition = 0;
        private float[] microphoneReadBuffer;
        private List<float> pendingMonoAudio;
        
        // Audio playback with ring buffer for smooth streaming
        private AudioRingBuffer audioRingBuffer;
        private int outputSampleRate;
        private AudioSource audioSource;
        
        // Threading
        private ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
        
        // State
        private bool isConnected = false;
        private bool isRecording = false;
        private bool isConversationActive = false;
        
        // Constants
        private const int TARGET_SAMPLE_RATE = 16000; // ElevenLabs expects 16kHz PCM16
        private byte[] pcm16SendBuffer;
        
        // Events for external integration
        public event Action OnConversationStarted;
        public event Action OnConversationEnded;
        public event Action<string> OnUserTranscript;
        public event Action<string> OnAgentResponse;
        public event Action<string> OnError;
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            // Initialize audio source
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();
            
            // Initialize audio collections
            pendingMonoAudio = new List<float>(8192);
            
            // Setup streaming audio playback
            outputSampleRate = AudioSettings.outputSampleRate;
            audioRingBuffer = new AudioRingBuffer(outputSampleRate * 20); // 10 second buffer
            
            // Create silent looping clip for continuous audio thread
            AudioClip silentClip = AudioClip.Create("ElevenLabsSilence", outputSampleRate, 1, outputSampleRate, false);
            audioSource.clip = silentClip;
            audioSource.loop = true;
            audioSource.Play();
        }
        
        private void Update()
        {
            // Process main thread actions
            while (mainThreadActions.TryDequeue(out Action action))
            {
                action?.Invoke();
            }
            
            // Stream microphone audio to WebSocket
            ProcessMicrophoneAudio();
        }
        
        private void OnDestroy()
        {
            StopConversation();
        }
        
        // Unity audio callback for continuous playback
        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (audioRingBuffer == null) return;
    
            int samplesNeeded = data.Length / channels;
    
            // Check if we have enough samples - if not, don't play anything yet
            int availableSamples = audioRingBuffer.AvailableSamples;
            if (availableSamples < samplesNeeded * 10) // 10x safety margin
            {
                // Not enough data - fill with silence and wait for more audio
                Array.Clear(data, 0, data.Length);
                return;
            }
    
            // We have enough samples - read them
            int samplesGot = audioRingBuffer.ReadSamples(samplesNeeded, out float[] monoSamples);
    
            // Convert mono to multi-channel output
            int dataIndex = 0;
            for (int i = 0; i < samplesGot; i++)
            {
                float sample = monoSamples[i];
                for (int c = 0; c < channels; c++)
                {
                    data[dataIndex++] = sample;
                }
            }
        }
        
        #endregion
        
        #region Public Interface
        
        /// <summary>
        /// Start a conversation with the ElevenLabs agent.
        /// Connects WebSocket and begins microphone capture.
        /// </summary>
        public async void StartConversation()
        {
            if (isConversationActive)
            {
                Log("Conversation already active.");
                return;
            }
            
            if (string.IsNullOrWhiteSpace(agentId))
            {
                LogError("Agent ID is required!");
                OnError?.Invoke("Agent ID is required");
                return;
            }
            
            Log("Starting conversation...");
            
            try
            {
                // Initialize WebSocket connection
                webSocketUri = new Uri($"wss://api.elevenlabs.io/v1/convai/conversation?agent_id={agentId}");
                cancellationTokenSource = new CancellationTokenSource();
                
                await ConnectWebSocket();
                
                if (isConnected)
                {
                    // Get userId and send conversation initiation
                    string userId = UserIdManager.GetUserId();
                    Debug.Log(userId);
                    // Send conversation initiation
                    SendJsonMessage(new
                    {
                        type = "conversation_initiation_client_data",
                        user_id = userId, // Try at root level
                        conversation_config_override = new
                        {
                            agent = new { language = "en" }
                        },
                        dynamic_variables = new
                        {
                            user_id = userId
                        }
                    });
                    
                    // Start microphone
                    StartMicrophone();
                    
                    isConversationActive = true;
                    Log("Conversation started successfully!");
                    OnConversationStarted?.Invoke();
                }
            }
            catch (Exception e)
            {
                LogError($"Failed to start conversation: {e.Message}");
                OnError?.Invoke($"Failed to start conversation: {e.Message}");
            }
        }
        
        /// <summary>
        /// Stop the current conversation.
        /// Closes WebSocket and stops microphone capture.
        /// </summary>
        public void StopConversation()
        {
            if (!isConversationActive)
            {
                Log("No active conversation to stop.");
                return;
            }
            
            Log("Stopping conversation...");
            
            isConversationActive = false;
            
            // Stop microphone
            StopMicrophone();
            
            // Close WebSocket
            try
            {
                cancellationTokenSource?.Cancel();
                webSocket?.CloseAsync(WebSocketCloseStatus.NormalClosure, "Conversation ended", CancellationToken.None);
                webSocket?.Dispose();
                webSocket = null;
            }
            catch (Exception e)
            {
                LogError($"Error closing WebSocket: {e.Message}");
            }
            
            isConnected = false;
            
            Log("Conversation stopped.");
            OnConversationEnded?.Invoke();
            
            // Notify interaction system that conversation ended
            FindObjectOfType<LuluInteractable>()?.EndConversation();
        }
        
        /// <summary>
        /// Send a text message to the agent.
        /// </summary>
        /// <param name="message">Text message to send</param>
        public void SendTextMessage(string message)
        {
            if (!isConnected || string.IsNullOrWhiteSpace(message))
                return;
            
            SendJsonMessage(new { type = "user_message", text = message });
            Log($"Sent text message: {message}");
        }
        
        #endregion
        
        #region WebSocket Management
        
        private async Task ConnectWebSocket()
        {
            try
            {
                webSocket = new ClientWebSocket();
                await webSocket.ConnectAsync(webSocketUri, cancellationTokenSource.Token);
                
                isConnected = true;
                Log("WebSocket connected successfully.");
                
                // Start receive loop
                _ = Task.Run(WebSocketReceiveLoop, cancellationTokenSource.Token);
            }
            catch (Exception e)
            {
                LogError($"WebSocket connection failed: {e.Message}");
                throw;
            }
        }
        
        private async Task WebSocketReceiveLoop()
        {
            byte[] buffer = new byte[1024 * 16];
            ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
            
            while (!cancellationTokenSource.IsCancellationRequested && 
                   webSocket.State == WebSocketState.Open)
            {
                try
                {
                    WebSocketReceiveResult result;
                    MemoryStream messageStream = new MemoryStream();
                    
                    // Receive complete message
                    do
                    {
                        result = await webSocket.ReceiveAsync(segment, cancellationTokenSource.Token);
                        
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Log("WebSocket closed by server.");
                            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by server", cancellationTokenSource.Token);
                            return;
                        }
                        
                        messageStream.Write(segment.Array, 0, result.Count);
                    }
                    while (!result.EndOfMessage);
                    
                    // Process message
                    string messageText = Encoding.UTF8.GetString(messageStream.ToArray());
                    HandleWebSocketMessage(messageText);
                }
                catch (Exception e)
                {
                    LogError($"WebSocket receive error: {e.Message}");
                    break;
                }
            }
        }
        
        private void HandleConversationInitiationMetadata(Dictionary<string, object> message)
        {
            if (message.TryGetValue("conversation_initiation_metadata_event", out object metadataEventObj) &&
                metadataEventObj is Dictionary<string, object> metadataEvent &&
                metadataEvent.TryGetValue("conversation_id", out object conversationIdObj))
            {
                string conversationId = conversationIdObj as string;
                Log($"Conversation initialized successfully with ID: {conversationId}");
        
                // The conversation is now properly initialized
                // This is where you'd know the userId was received
            }
        }
        
        private void HandleWebSocketMessage(string jsonMessage)
        {
            try
            {
                Dictionary<string, object> message = MiniJson.Deserialize(jsonMessage) as Dictionary<string, object>;
                if (message == null) return;
        
                if (message.TryGetValue("type", out object typeObj))
                {
                    string messageType = typeObj as string;
            
                    switch (messageType)
                    {
                        case "ping":
                            HandlePingMessage(message);
                            break;
                    
                        case "conversation_initiation_metadata":
                            HandleConversationInitiationMetadata(message);
                            break;
                    
                        case "audio":
                            HandleAudioMessage(message);
                            break;
                    
                        case "agent_response":
                            HandleAgentResponseMessage(message);
                            break;
                    
                        case "user_transcript":
                            HandleUserTranscriptMessage(message);
                            break;
                    
                        default:
                            if (enableDebugLogging)
                                Log($"Unknown message type: {messageType}");
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                LogError($"Failed to handle WebSocket message: {e.Message}");
            }
        }
        
        private void HandlePingMessage(Dictionary<string, object> message)
        {
            // Respond to ping with pong to keep connection alive
            if (message.TryGetValue("ping_event", out object pingEventObj) &&
                pingEventObj is Dictionary<string, object> pingEvent &&
                pingEvent.TryGetValue("event_id", out object eventIdObj))
            {
                SendJsonMessage(new { type = "pong", event_id = eventIdObj });
                
                if (enableDebugLogging)
                    Log($"Sent pong response for event {eventIdObj}");
            }
        }
        
        private void HandleAudioMessage(Dictionary<string, object> message)
        {
            if (message.TryGetValue("audio_event", out object audioEventObj) &&
                audioEventObj is Dictionary<string, object> audioEvent &&
                audioEvent.TryGetValue("audio_base_64", out object base64Obj))
            {
                string base64Audio = base64Obj as string;
                if (!string.IsNullOrEmpty(base64Audio))
                {
                    try
                    {
                        // Decode base64 to PCM16 bytes
                        byte[] pcmBytes = Convert.FromBase64String(base64Audio);
                        
                        // Convert PCM16 to float array (16kHz mono)
                        float[] audioSamples16k = ConvertPCM16ToFloat(pcmBytes);
                        
                        // Resample to output rate and queue for playback
                        float[] audioSamplesOutput = ResampleAudio(audioSamples16k, TARGET_SAMPLE_RATE, outputSampleRate);
                        audioRingBuffer.WriteSamples(audioSamplesOutput);
                        
                        if (logAudioChunks)
                            Log($"Received audio: {pcmBytes.Length} bytes -> {audioSamples16k.Length} samples");
                    }
                    catch (Exception e)
                    {
                        LogError($"Failed to process audio: {e.Message}");
                    }
                }
            }
        }
        
        private void HandleAgentResponseMessage(Dictionary<string, object> message)
        {
            if (message.TryGetValue("agent_response_event", out object responseEventObj) &&
                responseEventObj is Dictionary<string, object> responseEvent &&
                responseEvent.TryGetValue("agent_response", out object responseObj))
            {
                string agentResponse = responseObj as string;
                Log($"Agent: {agentResponse}");
                
                mainThreadActions.Enqueue(() => OnAgentResponse?.Invoke(agentResponse));
            }
        }
        
        private void HandleUserTranscriptMessage(Dictionary<string, object> message)
        {
            if (message.TryGetValue("user_transcript_event", out object transcriptEventObj) &&
                transcriptEventObj is Dictionary<string, object> transcriptEvent &&
                transcriptEvent.TryGetValue("transcript", out object transcriptObj))
            {
                string transcript = transcriptObj as string;
                Log($"You said: {transcript}");
                
                mainThreadActions.Enqueue(() => OnUserTranscript?.Invoke(transcript));
            }
        }
        
        private async void SendJsonMessage(object messageObject)
        {
            if (webSocket == null || webSocket.State != WebSocketState.Open)
                return;
            
            try
            {
                string json = MiniJson.Serialize(messageObject);
                byte[] messageBytes = Encoding.UTF8.GetBytes(json);
                ArraySegment<byte> segment = new ArraySegment<byte>(messageBytes);
                
                await webSocket.SendAsync(segment, WebSocketMessageType.Text, true, cancellationTokenSource.Token);
            }
            catch (Exception e)
            {
                LogError($"Failed to send WebSocket message: {e.Message}");
            }
        }
        
        #endregion
        
        #region Microphone Management
        
        private void StartMicrophone()
        {
            if (Microphone.devices.Length == 0)
            {
                LogError("No microphone devices found!");
                OnError?.Invoke("No microphone devices found");
                return;
            }
            
            if (isRecording)
            {
                Log("Microphone already recording.");
                return;
            }
            
            try
            {
                // Start microphone recording with looping buffer
                int lengthSeconds = 10;
                microphoneClip = Microphone.Start(microphoneDevice, true, lengthSeconds, micSampleRate);
                
                if (microphoneClip == null)
                {
                    LogError("Failed to start microphone!");
                    OnError?.Invoke("Failed to start microphone");
                    return;
                }
                
                microphoneChannels = microphoneClip.channels;
                lastMicrophonePosition = 0;
                pendingMonoAudio.Clear();
                isRecording = true;
                
                Log($"Microphone started: {microphoneDevice} @ {micSampleRate}Hz, channels: {microphoneChannels}");
            }
            catch (Exception e)
            {
                LogError($"Failed to start microphone: {e.Message}");
                OnError?.Invoke($"Failed to start microphone: {e.Message}");
            }
        }
        
        private void StopMicrophone()
        {
            if (!isRecording)
                return;
            
            isRecording = false;
            
            if (microphoneClip != null)
            {
                Microphone.End(microphoneDevice);
                microphoneClip = null;
                pendingMonoAudio.Clear();
                Log("Microphone stopped.");
            }
        }
        
        private void ProcessMicrophoneAudio()
        {
            if (!isRecording || !isConnected || microphoneClip == null)
                return;
            
            // Get current microphone position
            int currentPosition = Microphone.GetPosition(microphoneDevice);
            if (currentPosition < 0 || currentPosition == lastMicrophonePosition)
                return;
            
            // Calculate available samples
            int availableSamples = currentPosition - lastMicrophonePosition;
            if (availableSamples < 0)
            {
                // Handle buffer wrap-around
                availableSamples += microphoneClip.samples;
            }
            
            if (availableSamples > 0)
            {
                // Allocate read buffer if needed
                int totalSamples = availableSamples * microphoneChannels;
                if (microphoneReadBuffer == null || microphoneReadBuffer.Length < totalSamples)
                {
                    microphoneReadBuffer = new float[totalSamples];
                }
                
                // Read audio data from microphone clip
                microphoneClip.GetData(microphoneReadBuffer, lastMicrophonePosition);
                lastMicrophonePosition = currentPosition;
                
                // Convert to mono and add to pending buffer
                if (microphoneChannels == 1)
                {
                    // Already mono
                    for (int i = 0; i < availableSamples; i++)
                    {
                        pendingMonoAudio.Add(microphoneReadBuffer[i]);
                    }
                }
                else
                {
                    // Convert multi-channel to mono by averaging
                    for (int i = 0; i < availableSamples; i++)
                    {
                        float sum = 0f;
                        for (int c = 0; c < microphoneChannels; c++)
                        {
                            sum += microphoneReadBuffer[i * microphoneChannels + c];
                        }
                        pendingMonoAudio.Add(sum / microphoneChannels);
                    }
                }
                
                // Send audio chunks when we have enough data
                int samplesPerChunk = Mathf.CeilToInt(chunkSeconds * micSampleRate);
                while (pendingMonoAudio.Count >= samplesPerChunk)
                {
                    // Extract chunk
                    float[] chunk = pendingMonoAudio.GetRange(0, samplesPerChunk).ToArray();
                    pendingMonoAudio.RemoveRange(0, samplesPerChunk);
                    
                    // Process and send chunk
                    SendAudioChunk(chunk);
                }
            }
        }
        
        private void SendAudioChunk(float[] audioChunk)
        {
            try
            {
                // Resample to 16kHz for ElevenLabs
                float[] resampled = ResampleAudio(audioChunk, micSampleRate, TARGET_SAMPLE_RATE);
                
                // Convert to PCM16 bytes
                if (pcm16SendBuffer == null || pcm16SendBuffer.Length < resampled.Length * 2)
                {
                    pcm16SendBuffer = new byte[resampled.Length * 2];
                }
                
                ConvertFloatToPCM16(resampled, pcm16SendBuffer);
                
                // Encode to base64 and send
                string base64Audio = Convert.ToBase64String(pcm16SendBuffer, 0, resampled.Length * 2);
                SendJsonMessage(new { user_audio_chunk = base64Audio });
                
                if (logAudioChunks)
                {
                    // Calculate audio amplitude for debugging
                    float maxAmplitude = 0f;
                    for (int i = 0; i < resampled.Length; i++)
                    {
                        maxAmplitude = Mathf.Max(maxAmplitude, Mathf.Abs(resampled[i]));
                    }
                    Log($"Sent audio chunk: {resampled.Length} samples, amplitude: {maxAmplitude:F4}");
                }
            }
            catch (Exception e)
            {
                LogError($"Failed to send audio chunk: {e.Message}");
            }
        }
        
        #endregion
        
        #region Audio Utilities
        
        private static float[] ResampleAudio(float[] input, int inputSampleRate, int outputSampleRate)
        {
            if (inputSampleRate == outputSampleRate)
                return (float[])input.Clone();
            
            double ratio = (double)outputSampleRate / inputSampleRate;
            int outputLength = (int)Math.Ceiling(input.Length * ratio);
            float[] output = new float[outputLength];
            
            for (int i = 0; i < outputLength; i++)
            {
                double sourcePosition = i / ratio;
                int index0 = (int)sourcePosition;
                int index1 = Math.Min(index0 + 1, input.Length - 1);
                double t = sourcePosition - index0;
                
                output[i] = (float)((1.0 - t) * input[index0] + t * input[index1]);
            }
            
            return output;
        }
        
        private static void ConvertFloatToPCM16(float[] source, byte[] destination)
        {
            int destinationIndex = 0;
            for (int i = 0; i < source.Length; i++)
            {
                float sample = Mathf.Clamp(source[i], -1f, 1f);
                short pcm16Sample = (short)Mathf.RoundToInt(sample * 32767f);
                
                // Little-endian encoding
                destination[destinationIndex++] = (byte)(pcm16Sample & 0xFF);
                destination[destinationIndex++] = (byte)((pcm16Sample >> 8) & 0xFF);
            }
        }
        
        private static float[] ConvertPCM16ToFloat(byte[] pcmBytes)
        {
            int sampleCount = pcmBytes.Length / 2;
            float[] floatSamples = new float[sampleCount];
            
            int byteIndex = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                // Little-endian decoding
                short pcm16Sample = (short)(pcmBytes[byteIndex] | (pcmBytes[byteIndex + 1] << 8));
                floatSamples[i] = pcm16Sample / 32768f;
                byteIndex += 2;
            }
            
            return floatSamples;
        }
        
        #endregion
        
        #region Logging
        
        private void Log(string message)
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[ElevenLabsVoiceChat] {message}");
            }
        }
        
        private void LogError(string message)
        {
            Debug.LogError($"[ElevenLabsVoiceChat] {message}");
        }
        
        #endregion
    }
    
    #region Audio Ring Buffer
    
    /// <summary>
    /// Thread-safe ring buffer for streaming audio playback.
    /// Prevents audio gaps and handles variable buffering.
    /// </summary>
    public class AudioRingBuffer
    {
        private readonly float[] buffer;
        private int writePosition = 0;
        private int readPosition = 0;
        private int sampleCount = 0;
        private readonly object lockObject = new object();
        
        public int AvailableSamples
        {
            get
            {
                lock (lockObject)
                {
                    return sampleCount;
                }
            }
        }
        
        public AudioRingBuffer(int capacity)
        {
            buffer = new float[capacity];
        }
        
        public void WriteSamples(float[] samples)
        {
            lock (lockObject)
            {
                foreach (float sample in samples)
                {
                    // Drop oldest sample if buffer is full
                    if (sampleCount == buffer.Length)
                    {
                        readPosition = (readPosition + 1) % buffer.Length;
                        sampleCount--;
                    }
                    
                    buffer[writePosition] = sample;
                    writePosition = (writePosition + 1) % buffer.Length;
                    sampleCount++;
                }
            }
        }
        
        public int ReadSamples(int requestedSamples, out float[] outputSamples)
        {
            outputSamples = new float[requestedSamples];
            return ReadSamples(outputSamples, 0, requestedSamples);
        }
        
        public int ReadSamples(float[] outputSamples, int offset, int requestedSamples)
        {
            int samplesRead = 0;
            
            lock (lockObject)
            {
                int samplesToRead = Math.Min(requestedSamples, sampleCount);
                samplesRead = samplesToRead;
                
                while (samplesToRead > 0)
                {
                    outputSamples[offset++] = buffer[readPosition];
                    readPosition = (readPosition + 1) % buffer.Length;
                    sampleCount--;
                    samplesToRead--;
                }
            }
            
            return samplesRead;
        }
    }
    
    #endregion
    
    #region JSON Utilities
    
    /// <summary>
    /// Minimal JSON serialization utilities using Newtonsoft.Json.
    /// Provides compatibility with the proven working implementation.
    /// </summary>
    static class MiniJson
    {
        public static string Serialize(object obj)
        {
            return JsonConvert.SerializeObject(obj);
        }
        
        public static object Deserialize(string json)
        {
            JToken token = JToken.Parse(json);
            return ConvertJTokenToPlainObject(token);
        }
        
        private static object ConvertJTokenToPlainObject(JToken token)
        {
            if (token is JObject jobject)
            {
                Dictionary<string, object> dictionary = new Dictionary<string, object>(jobject.Count);
                foreach (KeyValuePair<string, JToken> property in jobject)
                {
                    dictionary[property.Key] = ConvertJTokenToPlainObject(property.Value);
                }
                return dictionary;
            }
            
            if (token is JArray jarray)
            {
                List<object> list = new List<object>(jarray.Count);
                foreach (JToken value in jarray)
                {
                    list.Add(ConvertJTokenToPlainObject(value));
                }
                return list;
            }
            
            return token.Type switch
            {
                JTokenType.Integer => token.ToObject<long>(),
                JTokenType.Float => token.ToObject<double>(),
                JTokenType.Boolean => token.ToObject<bool>(),
                JTokenType.Null => null,
                _ => token.ToObject<string>()
            };
        }
    }
    
    #endregion
}