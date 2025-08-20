using UnityEngine;

namespace MyBFF.Voice
{
    /// <summary>
    /// Configuration settings for ElevenLabs Conversational AI integration.
    /// Contains agent settings, audio parameters, and connection options.
    /// Create via: Assets > Create > Voice > ElevenLabs Config
    /// </summary>
    [CreateAssetMenu(fileName = "ElevenLabsConfig", menuName = "Voice/ElevenLabs Config")]
    public class ElevenLabsConfig : ScriptableObject
    {
        [Header("Agent Settings")]
        [Tooltip("Your ElevenLabs agent ID from the agent settings page.")]
        [SerializeField] private string agentId = "agent_4001k20ry6gyfj2bt9nm607v3hqz";
        
        [Header("Audio Settings")]
        [Tooltip("Sample rate for microphone input and audio output.")]
        [SerializeField] private int sampleRate = 16000;
        [Tooltip("How often to send audio chunks to ElevenLabs (milliseconds).")]
        [Range(100, 500)] [SerializeField] private int chunkIntervalMs = 250;
        [Tooltip("Size of audio buffer for microphone recording.")]
        [Range(1, 10)] [SerializeField] private int bufferLengthSeconds = 3;
        
        [Header("Connection Settings")]
        [Tooltip("Timeout for WebSocket connection attempts (seconds).")]
        [Range(5, 30)] [SerializeField] private int connectionTimeoutSeconds = 10;
        [Tooltip("Maximum number of reconnection attempts.")]
        [Range(0, 10)] [SerializeField] private int maxReconnectAttempts = 3;
        [Tooltip("Delay between reconnection attempts (seconds).")]
        [Range(1, 10)] [SerializeField] private float reconnectDelaySeconds = 2f;
        
        [Header("Audio Quality")]
        [Tooltip("Enable/disable audio compression for better bandwidth usage.")]
        [SerializeField] private bool useAudioCompression = false;
        [Tooltip("Audio quality factor (0.1 = low quality/bandwidth, 1.0 = high quality).")]
        [Range(0.1f, 1.0f)] [SerializeField] private float audioQuality = 0.8f;
        
        [Header("Debug")]
        [Tooltip("Enable detailed logging for debugging connection issues.")]
        [SerializeField] private bool enableDebugLogging = true;
        [Tooltip("Log audio chunk data (warning: very verbose).")]
        [SerializeField] private bool logAudioChunks = false;

        // Public properties for read-only access
        public string AgentId => agentId;
        public int SampleRate => sampleRate;
        public int ChunkIntervalMs => chunkIntervalMs;
        public int BufferLengthSeconds => bufferLengthSeconds;
        public int ConnectionTimeoutSeconds => connectionTimeoutSeconds;
        public int MaxReconnectAttempts => maxReconnectAttempts;
        public float ReconnectDelaySeconds => reconnectDelaySeconds;
        public bool UseAudioCompression => useAudioCompression;
        public float AudioQuality => audioQuality;
        public bool EnableDebugLogging => enableDebugLogging;
        public bool LogAudioChunks => logAudioChunks;
        
        /// <summary>
        /// Get the complete WebSocket URL for connecting to ElevenLabs agent.
        /// </summary>
        /// <returns>Complete WebSocket URL with agent ID</returns>
        public string GetWebSocketUrl()
        {
            return $"wss://api.elevenlabs.io/v1/convai/conversation?agent_id={agentId}";
        }
        
        /// <summary>
        /// Get the number of samples per audio chunk based on interval and sample rate.
        /// </summary>
        /// <returns>Number of samples per chunk</returns>
        public int GetSamplesPerChunk()
        {
            return (sampleRate * chunkIntervalMs) / 1000;
        }
        
        /// <summary>
        /// Validate configuration settings and log any issues.
        /// Call this during initialization to catch configuration problems early.
        /// </summary>
        /// <returns>True if configuration is valid</returns>
        public bool ValidateSettings()
        {
            bool isValid = true;
            
            if (string.IsNullOrEmpty(agentId))
            {
                Debug.LogError("[ElevenLabsConfig] Agent ID is required!");
                isValid = false;
            }
            
            if (sampleRate != 16000 && sampleRate != 44100)
            {
                Debug.LogWarning("[ElevenLabsConfig] Recommended sample rates are 16000 or 44100 Hz.");
            }
            
            if (chunkIntervalMs < 100)
            {
                Debug.LogWarning("[ElevenLabsConfig] Very low chunk interval may cause network overhead.");
            }
            
            if (chunkIntervalMs > 500)
            {
                Debug.LogWarning("[ElevenLabsConfig] High chunk interval may cause noticeable latency.");
            }
            
            return isValid;
        }
    }
}