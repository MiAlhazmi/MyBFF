using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using NativeWebSocket;

namespace MyBFF.Voice
{
    /// <summary>
    /// WebSocket client for ElevenLabs Conversational AI.
    /// Handles real-time bidirectional communication with ElevenLabs agent.
    /// Manages connection lifecycle, message parsing, and error handling.
    /// </summary>
    public class ElevenLabsWebSocketClient : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ElevenLabsConfig config;
        
        // WebSocket connection (using NativeWebSocket library)
        private WebSocket webSocket;
        private bool isConnected = false;
        private bool isConnecting = false;
        private int reconnectAttempts = 0;
        
        // Message queues for thread-safe communication
        private Queue<string> outgoingMessages;
        private Queue<byte[]> outgoingAudioChunks;
        private Queue<string> incomingMessages;
        
        // Connection state
        private string conversationId;
        private bool isConversationActive = false;
        
        // Events for communication with other systems
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnConnectionError;
        public event Action<float[]> OnAudioReceived;  // PCM audio data
        public event Action<string> OnTranscriptReceived;
        public event Action<string> OnConversationStarted;
        
        /// <summary>
        /// Initialize WebSocket client with configuration.
        /// Sets up message queues and validates settings.
        /// </summary>
        /// <param name="elevenLabsConfig">Configuration for connection settings</param>
        public void Initialize(ElevenLabsConfig elevenLabsConfig)
        {
            config = elevenLabsConfig;
            
            if (config == null)
            {
                LogError("ElevenLabsConfig is required!");
                return;
            }
            
            if (!config.ValidateSettings())
            {
                LogError("Invalid configuration settings!");
                return;
            }
            
            // Initialize message queues
            outgoingMessages = new Queue<string>();
            outgoingAudioChunks = new Queue<byte[]>();
            incomingMessages = new Queue<string>();
            
            Log("WebSocket client initialized successfully.");
        }
        
        /// <summary>
        /// Connect to ElevenLabs Conversational AI WebSocket.
        /// Establishes connection and begins conversation session.
        /// </summary>
        public void Connect()
        {
            if (isConnected || isConnecting)
            {
                Log("Already connected or connecting.");
                return;
            }
            
            if (config == null)
            {
                LogError("Configuration not set!");
                return;
            }
            
            StartCoroutine(ConnectToWebSocket());
        }
        
        /// <summary>
        /// Disconnect from ElevenLabs WebSocket.
        /// Closes connection and cleans up conversation state.
        /// </summary>
        public void Disconnect()
        {
            if (!isConnected && !isConnecting)
            {
                Log("Not connected.");
                return;
            }
            
            isConnecting = false;
            
            if (webSocket != null)
            {
                webSocket.Close();
                webSocket = null;
            }
            
            OnConnectionClosed();
        }
        
        /// <summary>
        /// Send audio chunk to ElevenLabs agent.
        /// Queues base64-encoded audio data for transmission.
        /// </summary>
        /// <param name="audioChunkBase64">Base64-encoded audio data</param>
        public void SendAudioChunk(byte[] audioChunkBase64)
        {
            if (!isConnected)
            {
                Log("Cannot send audio - not connected.");
                return;
            }
            
            outgoingAudioChunks.Enqueue(audioChunkBase64);
        }
        
        /// <summary>
        /// Send text message to ElevenLabs agent (for debugging/testing).
        /// </summary>
        /// <param name="message">Text message to send</param>
        public void SendTextMessage(string message)
        {
            if (!isConnected)
            {
                Log("Cannot send message - not connected.");
                return;
            }
            
            outgoingMessages.Enqueue(message);
        }
        
        /// <summary>
        /// Main update loop for processing WebSocket messages.
        /// Handles incoming and outgoing message queues, plus NativeWebSocket dispatch.
        /// </summary>
        private void Update()
        {
            // Process NativeWebSocket events
            if (webSocket != null)
            {
                webSocket.DispatchMessageQueue();
            }
            
            if (isConnected && webSocket != null)
            {
                // Process incoming messages
                ProcessIncomingMessages();
                
                // Send queued outgoing messages
                ProcessOutgoingMessages();
                
                // Send queued audio chunks
                ProcessOutgoingAudio();
            }
        }
        
        /// <summary>
        /// Establish WebSocket connection to ElevenLabs.
        /// Handles connection timeout and initial conversation setup.
        /// </summary>
        /// <returns>Coroutine enumerator</returns>
        private IEnumerator ConnectToWebSocket()
        {
            isConnecting = true;
            string websocketUrl = config.GetWebSocketUrl();
            
            Log($"Connecting to: {websocketUrl}");
            
            // Setup connection outside try/catch to avoid yield issues
            try
            {
                // Create WebSocket connection using NativeWebSocket
                webSocket = new WebSocket(websocketUrl);
                
                // Set up event handlers
                webSocket.OnOpen += OnWebSocketOpen;
                webSocket.OnMessage += OnWebSocketMessage;
                webSocket.OnError += OnWebSocketError;
                webSocket.OnClose += OnWebSocketClose;
                
                // Start connection (synchronous call, not async)
                webSocket.Connect();
            }
            catch (Exception e)
            {
                LogError($"Connection setup failed: {e.Message}");
                OnConnectionError?.Invoke(e.Message);
                isConnecting = false;
                yield break;
            }
            
            // Wait for connection with timeout (outside try/catch)
            float timeoutTime = Time.time + config.ConnectionTimeoutSeconds;
            while (isConnecting && Time.time < timeoutTime)
            {
                yield return null;
            }
            
            // Check connection result
            if (!isConnected)
            {
                LogError("Connection timed out!");
                OnConnectionError?.Invoke("Connection timeout");
                yield break;
            }
            
            // Send conversation initiation
            try
            {
                SendConversationInitiation();
                Log("Connected successfully!");
                OnConnected?.Invoke();
            }
            catch (Exception e)
            {
                LogError($"Conversation initiation failed: {e.Message}");
                OnConnectionError?.Invoke(e.Message);
            }
        }
        
        /// <summary>
        /// Handle WebSocket connection opened event.
        /// </summary>
        private void OnWebSocketOpen()
        {
            isConnected = true;
            isConnecting = false;
            reconnectAttempts = 0;
            
            Log("WebSocket connection opened.");
        }
        
        /// <summary>
        /// Handle incoming WebSocket message.
        /// Queues message for processing in main thread.
        /// </summary>
        /// <param name="message">Raw message data</param>
        private void OnWebSocketMessage(byte[] message)
        {
            try
            {
                string messageText = Encoding.UTF8.GetString(message);
                incomingMessages.Enqueue(messageText);
                
                if (config.LogAudioChunks)
                {
                    Log($"Message received: {messageText.Substring(0, Mathf.Min(100, messageText.Length))}...");
                }
            }
            catch (Exception e)
            {
                LogError($"Failed to process message: {e.Message}");
            }
        }
        
        /// <summary>
        /// Handle WebSocket error event.
        /// </summary>
        /// <param name="error">Error message</param>
        private void OnWebSocketError(string error)
        {
            LogError($"WebSocket error: {error}");
            OnConnectionError?.Invoke(error);
        }
        
        /// <summary>
        /// Handle WebSocket connection closed event.
        /// </summary>
        /// <param name="closeCode">WebSocket close code</param>
        private void OnWebSocketClose(WebSocketCloseCode closeCode)
        {
            Log($"WebSocket closed with code: {closeCode}");
            OnConnectionClosed();
        }
        
        /// <summary>
        /// Handle connection closure and attempt reconnection if appropriate.
        /// </summary>
        private void OnConnectionClosed()
        {
            bool wasConnected = isConnected;
            isConnected = false;
            isConnecting = false;
            isConversationActive = false;
            
            if (wasConnected)
            {
                Log("WebSocket connection closed.");
                OnDisconnected?.Invoke();
                
                // Attempt reconnection if enabled
                if (reconnectAttempts < config.MaxReconnectAttempts)
                {
                    StartCoroutine(AttemptReconnection());
                }
            }
        }
        
        /// <summary>
        /// Attempt to reconnect to WebSocket after delay.
        /// </summary>
        /// <returns>Coroutine enumerator</returns>
        private IEnumerator AttemptReconnection()
        {
            reconnectAttempts++;
            Log($"Attempting reconnection {reconnectAttempts}/{config.MaxReconnectAttempts}...");
            
            yield return new WaitForSeconds(config.ReconnectDelaySeconds);
            
            Connect();
        }
        
        /// <summary>
        /// Send conversation initiation message to start the session.
        /// </summary>
        private void SendConversationInitiation()
        {
            var initMessage = new
            {
                type = "conversation_initiation_client_data",
                conversation_config_override = new
                {
                    agent = new
                    {
                        language = "en"
                    },
                    tts = new
                    {
                        // Agent voice settings will use defaults from ElevenLabs agent config
                    }
                }
            };
            
            string jsonMessage = JsonUtility.ToJson(initMessage);
            outgoingMessages.Enqueue(jsonMessage);
            
            Log("Conversation initiation sent.");
        }
        
        /// <summary>
        /// Process all queued incoming messages.
        /// Parses JSON and handles different message types.
        /// </summary>
        private void ProcessIncomingMessages()
        {
            while (incomingMessages.Count > 0)
            {
                string message = incomingMessages.Dequeue();
                ParseIncomingMessage(message);
            }
        }
        
        /// <summary>
        /// Parse and handle specific incoming message types.
        /// </summary>
        /// <param name="messageJson">JSON message from ElevenLabs</param>
        private void ParseIncomingMessage(string messageJson)
        {
            try
            {
                // Simple JSON parsing - look for message type
                if (messageJson.Contains("\"type\":"))
                {
                    if (messageJson.Contains("conversation_initiation_metadata"))
                    {
                        HandleConversationInitiation(messageJson);
                    }
                    else if (messageJson.Contains("\"type\":\"audio\""))
                    {
                        HandleAudioMessage(messageJson);
                    }
                    else if (messageJson.Contains("user_transcript"))
                    {
                        HandleTranscriptMessage(messageJson);
                    }
                    else if (messageJson.Contains("agent_response"))
                    {
                        HandleAgentResponse(messageJson);
                    }
                    else
                    {
                        Log($"Unknown message type in: {messageJson.Substring(0, Mathf.Min(100, messageJson.Length))}...");
                    }
                }
            }
            catch (Exception e)
            {
                LogError($"Failed to parse message: {e.Message}");
            }
        }
        
        /// <summary>
        /// Handle conversation initiation metadata from ElevenLabs.
        /// Extract conversation ID and audio format information.
        /// </summary>
        /// <param name="messageJson">Conversation initiation message</param>
        private void HandleConversationInitiation(string messageJson)
        {
            try
            {
                // Note: In a production app, you'd use a proper JSON parser like Newtonsoft.Json
                // For this example, we'll extract conversation_id manually
                if (messageJson.Contains("conversation_id"))
                {
                    int startIndex = messageJson.IndexOf("\"conversation_id\":\"") + 19;
                    int endIndex = messageJson.IndexOf("\"", startIndex);
                    conversationId = messageJson.Substring(startIndex, endIndex - startIndex);
                    
                    isConversationActive = true;
                    Log($"Conversation started with ID: {conversationId}");
                    OnConversationStarted?.Invoke(conversationId);
                }
            }
            catch (Exception e)
            {
                LogError($"Failed to handle conversation initiation: {e.Message}");
            }
        }
        
        /// <summary>
        /// Handle audio message from ElevenLabs agent.
        /// Decode base64 audio and convert to PCM float array.
        /// </summary>
        /// <param name="messageJson">Audio message with base64 data</param>
        private void HandleAudioMessage(string messageJson)
        {
            try
            {
                // Extract base64 audio data from JSON
                // Note: In production, use proper JSON parsing
                string searchString = "\"audio\":\"";
                int startIndex = messageJson.IndexOf(searchString);
                if (startIndex == -1) return;
                
                startIndex += searchString.Length;
                int endIndex = messageJson.IndexOf("\"", startIndex);
                if (endIndex == -1) return;
                
                string base64Audio = messageJson.Substring(startIndex, endIndex - startIndex);
                
                // Decode base64 to PCM bytes
                byte[] pcmBytes = Convert.FromBase64String(base64Audio);
                
                // Convert PCM bytes to float array
                float[] pcmFloats = ConvertPCM16ToFloat(pcmBytes);
                
                // Send to audio streamer for playback
                OnAudioReceived?.Invoke(pcmFloats);
                
                if (config.LogAudioChunks)
                {
                    Log($"Audio received: {pcmBytes.Length} bytes -> {pcmFloats.Length} samples");
                }
            }
            catch (Exception e)
            {
                LogError($"Failed to handle audio message: {e.Message}");
            }
        }
        
        /// <summary>
        /// Handle transcript message from ElevenLabs (user speech recognition).
        /// </summary>
        /// <param name="messageJson">Transcript message</param>
        private void HandleTranscriptMessage(string messageJson)
        {
            try
            {
                // Extract transcript text
                string searchString = "\"user_transcript\":\"";
                int startIndex = messageJson.IndexOf(searchString);
                if (startIndex == -1) return;
                
                startIndex += searchString.Length;
                int endIndex = messageJson.IndexOf("\"", startIndex);
                if (endIndex == -1) return;
                
                string transcript = messageJson.Substring(startIndex, endIndex - startIndex);
                
                Log($"User said: {transcript}");
                OnTranscriptReceived?.Invoke(transcript);
            }
            catch (Exception e)
            {
                LogError($"Failed to handle transcript: {e.Message}");
            }
        }
        
        /// <summary>
        /// Handle agent response message (for future use).
        /// </summary>
        /// <param name="messageJson">Agent response message</param>
        private void HandleAgentResponse(string messageJson)
        {
            Log("Agent response received.");
        }
        
        /// <summary>
        /// Convert 16-bit PCM byte array to float array for Unity AudioClip.
        /// </summary>
        /// <param name="pcmBytes">16-bit PCM byte data</param>
        /// <returns>Float array for Unity audio</returns>
        private float[] ConvertPCM16ToFloat(byte[] pcmBytes)
        {
            float[] floatArray = new float[pcmBytes.Length / 2];
            
            for (int i = 0; i < floatArray.Length; i++)
            {
                // Read little-endian 16-bit signed integer
                short pcmSample = (short)(pcmBytes[i * 2] | (pcmBytes[i * 2 + 1] << 8));
                
                // Convert to float (-1.0 to 1.0)
                floatArray[i] = pcmSample / 32768f;
            }
            
            return floatArray;
        }
        
        /// <summary>
        /// Process all queued outgoing text messages.
        /// </summary>
        private void ProcessOutgoingMessages()
        {
            while (outgoingMessages.Count > 0 && webSocket != null)
            {
                string message = outgoingMessages.Dequeue();
                
                try
                {
                    // Send message using NativeWebSocket (synchronous)
                    webSocket.SendText(message);
                    
                    if (config.LogAudioChunks)
                    {
                        Log($"Message sent: {message.Substring(0, Mathf.Min(100, message.Length))}...");
                    }
                }
                catch (Exception e)
                {
                    LogError($"Failed to send message: {e.Message}");
                }
            }
        }
        
        /// <summary>
        /// Process all queued outgoing audio chunks.
        /// </summary>
        private void ProcessOutgoingAudio()
        {
            while (outgoingAudioChunks.Count > 0 && webSocket != null)
            {
                byte[] audioChunk = outgoingAudioChunks.Dequeue();
                
                try
                {
                    // Send audio message using NativeWebSocket (synchronous)
                    string base64Audio = Encoding.UTF8.GetString(audioChunk);
                    var audioMessage = new
                    {
                        user_audio_chunk = base64Audio
                    };
                    
                    string messageJson = JsonUtility.ToJson(audioMessage);
                    webSocket.SendText(messageJson);
                    
                    if (config.LogAudioChunks)
                    {
                        Log($"Audio chunk sent: {base64Audio.Length} characters");
                    }
                }
                catch (Exception e)
                {
                    LogError($"Failed to send audio chunk: {e.Message}");
                }
            }
        }
        
        /// <summary>
        /// Log message if debug logging is enabled.
        /// </summary>
        /// <param name="message">Message to log</param>
        private void Log(string message)
        {
            if (config != null && config.EnableDebugLogging)
            {
                Debug.Log($"[ElevenLabsWebSocket] {message}");
            }
        }
        
        /// <summary>
        /// Log error message (always shown).
        /// </summary>
        /// <param name="message">Error message to log</param>
        private void LogError(string message)
        {
            Debug.LogError($"[ElevenLabsWebSocket] {message}");
        }
        
        /// <summary>
        /// Clean up WebSocket connection when component is disabled.
        /// </summary>
        private void OnDisable()
        {
            Disconnect();
        }
        
        /// <summary>
        /// Clean up WebSocket connection when component is destroyed.
        /// </summary>
        private void OnDestroy()
        {
            Disconnect();
        }
    }
}