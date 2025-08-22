using System.Collections;
using UnityEngine;

namespace MyBFF.Voice
{
    /// <summary>
    /// Main manager for ElevenLabs Conversational AI integration.
    /// Coordinates WebSocket connection, audio streaming, and conversation state.
    /// Replaces the previous AutoMicVAD + n8n webhook system.
    /// </summary>
    public class ElevenLabsConversationManager : MonoBehaviour
    {
        public static ElevenLabsConversationManager Instance { get; private set; }
        
        [Header("Configuration")]
        [SerializeField] private ElevenLabsConfig config;
        
        [Header("Components")]
        [SerializeField] private ElevenLabsWebSocketClient webSocketClient;
        [SerializeField] private ElevenLabsAudioStreamer audioStreamer;
        [SerializeField] private AudioSource replyAudioSource;
        
        [Header("Conversation Settings")]
        [SerializeField] private AudioClip greetingClip;
        [SerializeField] private float warmupSeconds = 0.6f;
        [SerializeField] private float exitGraceSeconds = 0.5f;
        
        // State
        public bool IsActive { get; private set; }
        private bool isTransitioning;
        private bool isConnected;
        
        // Events for other systems to listen to
        public System.Action<bool> OnConversationStateChanged;  // true = started, false = ended
        public System.Action<string> OnUserSpeechTranscript;    // What user said
        public System.Action<string> OnConnectionStatusChanged; // Connection status updates
        public System.Action<string> OnErrorOccurred;           // Error messages
        
        /// <summary>
        /// Initialize singleton and validate component setup.
        /// </summary>
        private void Awake()
        {
            // Singleton pattern
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            
            // Validate configuration
            if (config == null)
            {
                Debug.LogError("[ElevenLabsConversationManager] ElevenLabsConfig is required!");
                enabled = false;
                return;
            }
            
            // Auto-find components if not assigned
            if (webSocketClient == null)
                webSocketClient = GetComponent<ElevenLabsWebSocketClient>();
            if (audioStreamer == null)
                audioStreamer = GetComponent<ElevenLabsAudioStreamer>();
            if (replyAudioSource == null)
                replyAudioSource = GetComponent<AudioSource>();
            
            // Validate required components
            if (webSocketClient == null)
            {
                Debug.LogError("[ElevenLabsConversationManager] ElevenLabsWebSocketClient component not found!");
                enabled = false;
                return;
            }
            
            if (audioStreamer == null)
            {
                Debug.LogError("[ElevenLabsConversationManager] ElevenLabsAudioStreamer component not found!");
                enabled = false;
                return;
            }
            
            // Initialize state
            IsActive = false;
            isTransitioning = false;
            isConnected = false;
        }
        
        /// <summary>
        /// Initialize components and set up event listeners.
        /// </summary>
        private void Start()
        {
            InitializeComponents();
            SetupEventListeners();
            
            Log("ElevenLabs Conversation Manager initialized.");
        }
        
        /// <summary>
        /// Begin conversation with ElevenLabs agent.
        /// Called by ConversationZone when player enters trigger area.
        /// </summary>
        public void BeginConversation()
        {
            if (IsActive || isTransitioning)
            {
                Log("Conversation already active or transitioning.");
                return;
            }
            IsActive = true;  
            Log("Conversation started successfully!");

            StartCoroutine(BeginConversationCoroutine());
        }
        
        /// <summary>
        /// End conversation with ElevenLabs agent.
        /// Called by ConversationZone when player exits trigger area.
        /// </summary>
        public void EndConversation()
        {
            if (!IsActive || isTransitioning)
            {
                Log("Conversation not active or already transitioning.");
                return;
            }
            
            StartCoroutine(EndConversationCoroutine());
        }
        
        /// <summary>
        /// Initialize all required components with configuration.
        /// </summary>
        private void InitializeComponents()
        {
            // Initialize WebSocket client
            webSocketClient.Initialize(config);
            
            // Initialize audio streamer
            audioStreamer.Initialize(config);
            
            Log("Components initialized successfully.");
        }
        
        /// <summary>
        /// Set up event listeners between components.
        /// Creates communication pathways for audio and connection events.
        /// </summary>
        private void SetupEventListeners()
        {
            // WebSocket events
            webSocketClient.OnConnected += OnWebSocketConnected;
            webSocketClient.OnDisconnected += OnWebSocketDisconnected;
            webSocketClient.OnConnectionError += OnWebSocketError;
            webSocketClient.OnAudioReceived += OnAgentAudioReceived;
            webSocketClient.OnTranscriptReceived += OnUserTranscriptReceived;
            webSocketClient.OnConversationStarted += OnConversationSessionStarted;
            
            // Audio streamer events
            audioStreamer.OnAudioChunkReady += OnMicrophoneAudioReady;
            audioStreamer.OnMicrophoneStarted += OnMicrophoneStarted;
            audioStreamer.OnMicrophoneStopped += OnMicrophoneStopped;
            audioStreamer.OnAudioError += OnAudioStreamError;
            
            Log("Event listeners configured.");
        }
        
        /// <summary>
        /// Coroutine to handle conversation start sequence.
        /// Manages greeting playback, connection establishment, and microphone activation.
        /// </summary>
        /// <returns>Coroutine enumerator</returns>
        private IEnumerator BeginConversationCoroutine()
        {
            isTransitioning = true;
            Log("Starting conversation...");
            
            // Play optional greeting
            if (greetingClip != null && replyAudioSource != null)
            {
                Log("Playing greeting...");
                replyAudioSource.clip = greetingClip;
                replyAudioSource.Play();
                
                // Wait for greeting to finish
                while (replyAudioSource.isPlaying)
                    yield return null;
            }
            
            // Connect to ElevenLabs WebSocket
            Log("Connecting to ElevenLabs...");
            webSocketClient.Connect();
            
            // Wait for connection with timeout
            float connectionTimeout = Time.time + config.ConnectionTimeoutSeconds;
            while (!isConnected && Time.time < connectionTimeout)
            {
                yield return null;
            }
            
            if (!isConnected)
            {
                Log("Failed to connect to ElevenLabs!");
                OnErrorOccurred?.Invoke("Failed to connect to ElevenLabs");
                isTransitioning = false;
                yield break;
            }
            
            // Warmup period to let connection stabilize
            Log($"Connection established. Warming up for {warmupSeconds}s...");
            yield return new WaitForSeconds(warmupSeconds);
            
            // Start microphone recording and streaming
            Log("Starting microphone...");
            audioStreamer.StartRecording();
            
            // Conversation is now active
            IsActive = true;
            isTransitioning = false;
            
            Log("Conversation started successfully!");
            OnConversationStateChanged?.Invoke(true);
            OnConnectionStatusChanged?.Invoke("Connected");
        }
        
        /// <summary>
        /// Coroutine to handle conversation end sequence.
        /// Manages microphone shutdown, connection closure, and cleanup.
        /// </summary>
        /// <returns>Coroutine enumerator</returns>
        private IEnumerator EndConversationCoroutine()
        {
            isTransitioning = true;
            Log("Ending conversation...");
            
            // Stop microphone immediately
            audioStreamer.StopRecording();
            
            // Small grace period to avoid abrupt cutoff
            yield return new WaitForSeconds(exitGraceSeconds);
            
            // Disconnect from WebSocket
            webSocketClient.Disconnect();
            
            // Update state
            IsActive = false;
            isTransitioning = false;
            isConnected = false;
            
            Log("Conversation ended.");
            OnConversationStateChanged?.Invoke(false);
            OnConnectionStatusChanged?.Invoke("Disconnected");
        }
        
        /// <summary>
        /// Handle WebSocket connection established event.
        /// </summary>
        private void OnWebSocketConnected()
        {
            isConnected = true;
            Log("WebSocket connected successfully.");
        }
        
        /// <summary>
        /// Handle WebSocket disconnection event.
        /// </summary>
        private void OnWebSocketDisconnected()
        {
            isConnected = false;
            Log("WebSocket disconnected.");
            
            // If we were in an active conversation, this is unexpected
            if (IsActive && !isTransitioning)
            {
                Log("Unexpected disconnection during conversation.");
                OnErrorOccurred?.Invoke("Lost connection to ElevenLabs");
                
                // End conversation due to connection loss
                EndConversation();
            }
        }
        
        /// <summary>
        /// Handle WebSocket connection error event.
        /// </summary>
        /// <param name="error">Error message</param>
        private void OnWebSocketError(string error)
        {
            Log($"WebSocket error: {error}");
            OnErrorOccurred?.Invoke($"Connection error: {error}");
        }
        
        /// <summary>
        /// Handle audio received from ElevenLabs agent.
        /// Routes audio to audio streamer for playback.
        /// </summary>
        /// <param name="audioData">PCM audio data from agent</param>
        private void OnAgentAudioReceived(float[] audioData)
        {
            Debug.Log($"[ConversationManager] OnAgentAudioReceived called with {audioData?.Length ?? 0} samples");
            if (IsActive)
            {
                audioStreamer.PlayAudioData(audioData);
                Log($"Playing agent audio: {audioData.Length} samples");
            }
            else
            {
                Debug.Log("[ConversationManager] Not active, ignoring audio");
            }
        }
        
        /// <summary>
        /// Handle user speech transcript from ElevenLabs.
        /// Provides speech recognition results for logging/debugging.
        /// </summary>
        /// <param name="transcript">What the user said</param>
        private void OnUserTranscriptReceived(string transcript)
        {
            Log($"User transcript: {transcript}");
            OnUserSpeechTranscript?.Invoke(transcript);
        }
        
        /// <summary>
        /// Handle conversation session started event from ElevenLabs.
        /// </summary>
        /// <param name="conversationId">Unique conversation identifier</param>
        private void OnConversationSessionStarted(string conversationId)
        {
            Log($"ElevenLabs conversation session started: {conversationId}");
        }
        
        /// <summary>
        /// Handle microphone audio chunk ready for transmission.
        /// Routes audio to WebSocket client for sending to ElevenLabs.
        /// </summary>
        /// <param name="audioChunk">Base64-encoded audio data</param>
        private void OnMicrophoneAudioReady(byte[] audioChunk)
        {
            if (IsActive && isConnected)
            {
                webSocketClient.SendAudioChunk(audioChunk);
            }
        }
        
        /// <summary>
        /// Handle microphone started event.
        /// </summary>
        private void OnMicrophoneStarted()
        {
            Log("Microphone started.");
        }
        
        /// <summary>
        /// Handle microphone stopped event.
        /// </summary>
        private void OnMicrophoneStopped()
        {
            Log("Microphone stopped.");
        }
        
        /// <summary>
        /// Handle audio streaming error event.
        /// </summary>
        /// <param name="error">Error message</param>
        private void OnAudioStreamError(string error)
        {
            Log($"Audio stream error: {error}");
            OnErrorOccurred?.Invoke($"Audio error: {error}");
        }
        
        /// <summary>
        /// Get current connection status for UI display.
        /// </summary>
        /// <returns>Human-readable connection status</returns>
        public string GetConnectionStatus()
        {
            if (isTransitioning)
                return "Connecting...";
            if (IsActive && isConnected)
                return "Connected";
            if (!IsActive)
                return "Disconnected";
            return "Error";
        }
        
        /// <summary>
        /// Check if system is ready to start a conversation.
        /// </summary>
        /// <returns>True if ready to start conversation</returns>
        public bool IsReadyForConversation()
        {
            return !IsActive && !isTransitioning && config != null;
        }
        
        /// <summary>
        /// Force end conversation (emergency stop).
        /// Immediately stops all audio and closes connections.
        /// </summary>
        public void ForceEndConversation()
        {
            Log("Force ending conversation...");
            
            // Stop all audio immediately
            audioStreamer.StopRecording();
            if (replyAudioSource != null && replyAudioSource.isPlaying)
            {
                replyAudioSource.Stop();
            }
            
            // Disconnect WebSocket
            webSocketClient.Disconnect();
            
            // Reset state
            IsActive = false;
            isTransitioning = false;
            isConnected = false;
            
            OnConversationStateChanged?.Invoke(false);
            OnConnectionStatusChanged?.Invoke("Force Disconnected");
        }
        
        /// <summary>
        /// Log message if debug logging is enabled.
        /// </summary>
        /// <param name="message">Message to log</param>
        private void Log(string message)
        {
            if (config != null && config.EnableDebugLogging)
            {
                Debug.Log($"[ElevenLabsConversationManager] {message}");
            }
        }
        
        /// <summary>
        /// Clean up event listeners when component is disabled.
        /// </summary>
        private void OnDisable()
        {
            // Clean up event listeners
            if (webSocketClient != null)
            {
                webSocketClient.OnConnected -= OnWebSocketConnected;
                webSocketClient.OnDisconnected -= OnWebSocketDisconnected;
                webSocketClient.OnConnectionError -= OnWebSocketError;
                webSocketClient.OnAudioReceived -= OnAgentAudioReceived;
                webSocketClient.OnTranscriptReceived -= OnUserTranscriptReceived;
                webSocketClient.OnConversationStarted -= OnConversationSessionStarted;
            }
            
            if (audioStreamer != null)
            {
                audioStreamer.OnAudioChunkReady -= OnMicrophoneAudioReady;
                audioStreamer.OnMicrophoneStarted -= OnMicrophoneStarted;
                audioStreamer.OnMicrophoneStopped -= OnMicrophoneStopped;
                audioStreamer.OnAudioError -= OnAudioStreamError;
            }
            
            // Force end any active conversation
            if (IsActive)
            {
                ForceEndConversation();
            }
        }
    }
}