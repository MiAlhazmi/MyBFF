using UnityEngine;

namespace MyBFF.Voice
{
    /// <summary>
    /// Updated ConversationManager that works with ElevenLabs Conversational AI.
    /// Maintains the same public interface as the previous version but uses
    /// ElevenLabsConversationManager instead of AutoMicVAD + n8n workflow.
    /// </summary>
    public class ConversationManager : MonoBehaviour
    {
        public static ConversationManager Instance { get; private set; }

        [Header("References")]
        [SerializeField] private ElevenLabsConversationManager elevenLabsManager;
        
        [Header("Lulu Settings")]
        [SerializeField] private string npcId = "lulu";
        [SerializeField] private AudioClip greeting;           // Optional greeting before ElevenLabs
        [SerializeField] private AudioSource greetingSource;   // AudioSource for local greeting
        
        // State properties (maintain compatibility with existing code)
        public bool IsActive => elevenLabsManager != null && elevenLabsManager.IsActive;
        
        // Events for other systems (maintain compatibility)
        public System.Action<bool> OnConversationStateChanged;
        public System.Action<string> OnUserSpeechReceived;
        public System.Action<string> OnErrorOccurred;

        /// <summary>
        /// Initialize singleton and validate ElevenLabs manager reference.
        /// </summary>
        private void Awake()
        {
            // Singleton pattern (maintain compatibility)
            if (Instance != null && Instance != this) 
            { 
                Destroy(gameObject); 
                return; 
            }
            Instance = this;

            // Auto-find ElevenLabs manager if not assigned
            if (elevenLabsManager == null)
            {
                elevenLabsManager = FindObjectOfType<ElevenLabsConversationManager>();
            }
            
            if (elevenLabsManager == null)
            {
                Debug.LogError("[ConversationManager] ElevenLabsConversationManager not found! Please add it to the scene.");
                enabled = false;
                return;
            }
            
            // Auto-find greeting audio source
            if (greetingSource == null)
            {
                greetingSource = GetComponent<AudioSource>();
            }
        }

        /// <summary>
        /// Set up event forwarding from ElevenLabs manager.
        /// This maintains compatibility with existing code that listens to ConversationManager events.
        /// </summary>
        private void Start()
        {
            if (elevenLabsManager != null)
            {
                // Forward events to maintain compatibility
                elevenLabsManager.OnConversationStateChanged += OnElevenLabsConversationStateChanged;
                elevenLabsManager.OnUserSpeechTranscript += OnElevenLabsUserSpeech;
                elevenLabsManager.OnErrorOccurred += OnElevenLabsError;
            }

            Log("ConversationManager initialized with ElevenLabs integration.");
        }

        /// <summary>
        /// Begin conversation with Lulu (ElevenLabs agent).
        /// Maintains the same public interface as the previous version.
        /// </summary>
        public void BeginConversation()
        {
            if (elevenLabsManager == null)
            {
                LogError("ElevenLabs manager not available!");
                return;
            }

            if (IsActive)
            {
                Log("Conversation already active.");
                return;
            }

            Log($"Starting conversation with {npcId}...");

            // Play local greeting if configured (separate from ElevenLabs greeting)
            if (greeting != null && greetingSource != null)
            {
                greetingSource.clip = greeting;
                greetingSource.Play();
                Log("Playing local greeting...");
            }

            // Start ElevenLabs conversation
            elevenLabsManager.BeginConversation();
        }

        /// <summary>
        /// End conversation with Lulu (ElevenLabs agent).
        /// Maintains the same public interface as the previous version.
        /// </summary>
        public void EndConversation()
        {
            if (elevenLabsManager == null)
            {
                LogError("ElevenLabs manager not available!");
                return;
            }

            if (!IsActive)
            {
                Log("Conversation not active.");
                return;
            }

            Log("Ending conversation...");

            // Stop local greeting if playing
            if (greetingSource != null && greetingSource.isPlaying)
            {
                greetingSource.Stop();
            }

            // End ElevenLabs conversation
            elevenLabsManager.EndConversation();
        }

        /// <summary>
        /// Get current conversation status for UI or debugging.
        /// </summary>
        /// <returns>Human-readable status string</returns>
        public string GetConversationStatus()
        {
            if (elevenLabsManager == null)
                return "Manager Missing";
                
            return elevenLabsManager.GetConnectionStatus();
        }

        /// <summary>
        /// Check if system is ready to start a conversation.
        /// Useful for UI state management.
        /// </summary>
        /// <returns>True if ready to begin conversation</returns>
        public bool IsReadyForConversation()
        {
            return elevenLabsManager != null && elevenLabsManager.IsReadyForConversation();
        }

        /// <summary>
        /// Force end conversation immediately (emergency stop).
        /// Useful for testing or error recovery.
        /// </summary>
        public void ForceEndConversation()
        {
            Log("Force ending conversation...");

            // Stop local audio
            if (greetingSource != null && greetingSource.isPlaying)
            {
                greetingSource.Stop();
            }

            // Force end ElevenLabs conversation
            if (elevenLabsManager != null)
            {
                elevenLabsManager.ForceEndConversation();
            }
        }

        // Event forwarding methods
        
        /// <summary>
        /// Forward conversation state changes from ElevenLabs manager.
        /// Maintains compatibility with existing code.
        /// </summary>
        /// <param name="isActive">True if conversation started, false if ended</param>
        private void OnElevenLabsConversationStateChanged(bool isActive)
        {
            Log($"Conversation state changed: {(isActive ? "Started" : "Ended")}");
            OnConversationStateChanged?.Invoke(isActive);
        }

        /// <summary>
        /// Forward user speech transcripts from ElevenLabs.
        /// Provides speech recognition results for logging or game logic.
        /// </summary>
        /// <param name="transcript">What the user said</param>
        private void OnElevenLabsUserSpeech(string transcript)
        {
            Log($"User said: {transcript}");
            OnUserSpeechReceived?.Invoke(transcript);
        }

        /// <summary>
        /// Forward error messages from ElevenLabs manager.
        /// Allows other systems to respond to conversation errors.
        /// </summary>
        /// <param name="error">Error message</param>
        private void OnElevenLabsError(string error)
        {
            LogError($"ElevenLabs error: {error}");
            OnErrorOccurred?.Invoke(error);
        }

        /// <summary>
        /// Log debug message.
        /// </summary>
        /// <param name="message">Message to log</param>
        private void Log(string message)
        {
            Debug.Log($"[ConversationManager] {message}");
        }

        /// <summary>
        /// Log error message.
        /// </summary>
        /// <param name="message">Error message to log</param>
        private void LogError(string message)
        {
            Debug.LogError($"[ConversationManager] {message}");
        }

        /// <summary>
        /// Clean up event listeners when component is disabled.
        /// Ensures no memory leaks from event subscriptions.
        /// </summary>
        private void OnDisable()
        {
            if (elevenLabsManager != null)
            {
                elevenLabsManager.OnConversationStateChanged -= OnElevenLabsConversationStateChanged;
                elevenLabsManager.OnUserSpeechTranscript -= OnElevenLabsUserSpeech;
                elevenLabsManager.OnErrorOccurred -= OnElevenLabsError;
            }

            // Force end any active conversation
            if (IsActive)
            {
                ForceEndConversation();
            }
        }

        /// <summary>
        /// Clean up when component is destroyed.
        /// </summary>
        private void OnDestroy()
        {
            // Clean up is handled in OnDisable
        }

        // Legacy compatibility methods (for any existing code that might call these)
        
        /// <summary>
        /// Legacy method compatibility - redirects to BeginConversation().
        /// </summary>
        [System.Obsolete("Use BeginConversation() instead")]
        public void StartConversation()
        {
            BeginConversation();
        }

        /// <summary>
        /// Legacy method compatibility - redirects to EndConversation().
        /// </summary>
        [System.Obsolete("Use EndConversation() instead")]
        public void StopConversation()
        {
            EndConversation();
        }
    }
}