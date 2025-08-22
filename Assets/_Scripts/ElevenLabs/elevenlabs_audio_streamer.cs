using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MyBFF.Voice
{
    /// <summary>
    /// Handles real-time audio streaming for ElevenLabs Conversational AI.
    /// Manages microphone input capture and audio output playback.
    /// Works in conjunction with ElevenLabsConversationManager for WebSocket communication.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class ElevenLabsAudioStreamer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ElevenLabsConfig config;
        [SerializeField] private AudioSource audioSource;
        
        // Audio input (microphone)
        private AudioClip microphoneClip;
        private string microphoneDevice;
        private int lastMicrophonePosition;
        private float[] audioBuffer;
        private Queue<float[]> audioChunkQueue;
        
        // Audio output (playback)
        private Queue<float[]> playbackQueue;
        private bool isPlayingAudio;
        
        // State
        private bool isRecording;
        private bool isInitialized;
        
        // Events for communication with conversation manager
        public event Action<byte[]> OnAudioChunkReady;  // Sends base64-encoded audio chunks
        public event Action OnMicrophoneStarted;
        public event Action OnMicrophoneStopped;
        public event Action<string> OnAudioError;
        
        /// <summary>
        /// Initialize audio streamer with configuration.
        /// Sets up microphone and audio playback systems.
        /// </summary>
        /// <param name="elevenLabsConfig">Configuration for audio settings</param>
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
            
            // Initialize audio source
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();
                
            // Initialize microphone
            if (!InitializeMicrophone())
            {
                LogError("Failed to initialize microphone!");
                return;
            }
            
            // Initialize audio buffers
            InitializeAudioBuffers();
            
            isInitialized = true;
            Log("Audio streamer initialized successfully.");
        }
        
        /// <summary>
        /// Start recording and streaming microphone audio.
        /// Begins continuous audio capture and chunk generation.
        /// </summary>
        public void StartRecording()
        {
            if (!isInitialized)
            {
                LogError("Audio streamer not initialized!");
                return;
            }
            
            if (isRecording)
            {
                Log("Already recording.");
                return;
            }
            
            // Start microphone recording
            microphoneClip = Microphone.Start(microphoneDevice, true, config.BufferLengthSeconds, config.SampleRate);
            if (microphoneClip == null)
            {
                OnAudioError?.Invoke("Failed to start microphone recording");
                return;
            }
            
            // Reset state
            lastMicrophonePosition = 0;
            audioChunkQueue.Clear();
            isRecording = true;
            
            // Start audio processing coroutine
            StartCoroutine(ProcessAudioChunks());
            
            OnMicrophoneStarted?.Invoke();
            Log("Microphone recording started.");
        }
        
        /// <summary>
        /// Stop recording and streaming microphone audio.
        /// Stops microphone capture and clears audio buffers.
        /// </summary>
        public void StopRecording()
        {
            if (!isRecording)
            {
                Log("Not currently recording.");
                return;
            }
            
            isRecording = false;
            
            // Stop microphone
            if (!string.IsNullOrEmpty(microphoneDevice) && Microphone.IsRecording(microphoneDevice))
            {
                Microphone.End(microphoneDevice);
            }
            
            // Clear audio buffers
            audioChunkQueue.Clear();
            
            OnMicrophoneStopped?.Invoke();
            Log("Microphone recording stopped.");
        }
        
        /// <summary>
        /// Play received audio data from ElevenLabs agent.
        /// Queues PCM audio data for playback through AudioSource.
        /// </summary>
        /// <param name="pcmData">PCM audio data from ElevenLabs</param>
        public void PlayAudioData(float[] pcmData)
        {
            Debug.Log($"[AudioStreamer] PlayAudioData called with {pcmData?.Length ?? 0} samples");
            
            if (pcmData == null || pcmData.Length == 0)
            {
                LogError("Invalid audio data received!");
                return;
            }
            
            // Queue audio data for playback
            playbackQueue.Enqueue(pcmData);
            
            // Start playback if not already playing
            if (!isPlayingAudio)
            {
                StartCoroutine(ProcessAudioPlayback());
            }
        }
        
        /// <summary>
        /// Initialize microphone device and validate availability.
        /// Selects default microphone if none specified.
        /// </summary>
        /// <returns>True if microphone initialized successfully</returns>
        private bool InitializeMicrophone()
        {
            if (Microphone.devices.Length == 0)
            {
                LogError("No microphone devices found!");
                return false;
            }
            
            // Use default microphone device
            microphoneDevice = Microphone.devices[0];
            Log($"Using microphone: {microphoneDevice}");
            
            return true;
        }
        
        /// <summary>
        /// Initialize audio buffers and queues for processing.
        /// Sets up memory for audio chunk processing and playback.
        /// </summary>
        private void InitializeAudioBuffers()
        {
            int samplesPerChunk = config.GetSamplesPerChunk();
            audioBuffer = new float[samplesPerChunk];
            audioChunkQueue = new Queue<float[]>();
            playbackQueue = new Queue<float[]>();
            
            Log($"Audio buffers initialized. Samples per chunk: {samplesPerChunk}");
        }
        
        /// <summary>
        /// Continuously process microphone audio and generate chunks for streaming.
        /// Runs as coroutine while recording is active.
        /// </summary>
        /// <returns>Coroutine enumerator</returns>
        private IEnumerator ProcessAudioChunks()
        {
            WaitForSeconds chunkInterval = new WaitForSeconds(config.ChunkIntervalMs / 1000f);
            
            while (isRecording)
            {
                yield return chunkInterval;
                
                if (microphoneClip == null) continue;
                
                // Get current microphone position
                int currentPosition = Microphone.GetPosition(microphoneDevice);
                if (currentPosition < 0) continue;
                
                // Calculate available samples
                int availableSamples = currentPosition - lastMicrophonePosition;
                if (availableSamples < 0)
                {
                    // Handle microphone buffer wrap-around
                    availableSamples += microphoneClip.samples;
                }
                
                // Process available audio if we have enough for a chunk
                int samplesPerChunk = config.GetSamplesPerChunk();
                if (availableSamples >= samplesPerChunk)
                {
                    // Extract audio chunk from microphone clip
                    float[] chunkData = new float[samplesPerChunk];
                    
                    // Handle potential wrap-around in circular buffer
                    int samplesToEnd = microphoneClip.samples - lastMicrophonePosition;
                    if (samplesToEnd >= samplesPerChunk)
                    {
                        // Simple case: no wrap-around
                        microphoneClip.GetData(chunkData, lastMicrophonePosition);
                    }
                    else
                    {
                        // Complex case: handle wrap-around
                        float[] tempBuffer = new float[microphoneClip.samples];
                        microphoneClip.GetData(tempBuffer, 0);
                        
                        // Copy from end of buffer
                        Array.Copy(tempBuffer, lastMicrophonePosition, chunkData, 0, samplesToEnd);
                        // Copy from beginning of buffer
                        Array.Copy(tempBuffer, 0, chunkData, samplesToEnd, samplesPerChunk - samplesToEnd);
                    }
                    
                    // Update position
                    lastMicrophonePosition = (lastMicrophonePosition + samplesPerChunk) % microphoneClip.samples;
                    
                    // Convert to base64 and send
                    ProcessAndSendAudioChunk(chunkData);
                }
            }
        }
        
        /// <summary>
        /// Convert audio chunk to base64 format and send via event.
        /// Handles PCM to byte conversion and base64 encoding.
        /// </summary>
        /// <param name="audioData">Raw PCM audio data</param>
        private void ProcessAndSendAudioChunk(float[] audioData)
        {
            try
            {
                // Convert float PCM to 16-bit PCM bytes
                byte[] pcmBytes = ConvertFloatToPCM16(audioData);
                
                // Encode to base64
                string base64Audio = Convert.ToBase64String(pcmBytes);
                byte[] base64Bytes = System.Text.Encoding.UTF8.GetBytes(base64Audio);
                
                // Send via event
                OnAudioChunkReady?.Invoke(base64Bytes);
                
                if (config.LogAudioChunks)
                {
                    Log($"Audio chunk sent: {pcmBytes.Length} bytes, base64: {base64Audio.Length} chars");
                }
            }
            catch (Exception e)
            {
                LogError($"Failed to process audio chunk: {e.Message}");
            }
        }
        
        /// <summary>
        /// Convert float PCM audio to 16-bit PCM byte array.
        /// Used for encoding audio data for ElevenLabs WebSocket.
        /// </summary>
        /// <param name="floatArray">Float PCM audio data (-1.0 to 1.0)</param>
        /// <returns>16-bit PCM byte array</returns>
        private byte[] ConvertFloatToPCM16(float[] floatArray)
        {
            byte[] byteArray = new byte[floatArray.Length * 2];
            int byteIndex = 0;
            
            for (int i = 0; i < floatArray.Length; i++)
            {
                // Clamp and convert to 16-bit signed integer
                float sample = Mathf.Clamp(floatArray[i], -1f, 1f);
                short pcmSample = (short)(sample * 32767f);
                
                // Write as little-endian bytes
                byteArray[byteIndex++] = (byte)(pcmSample & 0xFF);
                byteArray[byteIndex++] = (byte)((pcmSample >> 8) & 0xFF);
            }
            
            return byteArray;
        }
        
        /// <summary>
        /// Process queued audio playback from ElevenLabs responses.
        /// Plays audio chunks sequentially through AudioSource.
        /// </summary>
        /// <returns>Coroutine enumerator</returns>
        private IEnumerator ProcessAudioPlayback()
        {
            isPlayingAudio = true;
            
            while (playbackQueue.Count > 0)
            {
                float[] audioData = playbackQueue.Dequeue();
                
                // Create AudioClip from PCM data
                AudioClip clip = AudioClip.Create("ElevenLabsResponse", audioData.Length, 1, config.SampleRate, false);
                clip.SetData(audioData, 0);
                
                // Play the clip
                audioSource.clip = clip;
                audioSource.Play();
                
                // Wait for clip to finish playing
                yield return new WaitForSeconds(clip.length);
                
                // Clean up
                if (clip != null)
                    DestroyImmediate(clip);
            }
            
            isPlayingAudio = false;
        }
        
        /// <summary>
        /// Stop all audio streaming and clean up resources.
        /// Called when component is disabled or destroyed.
        /// </summary>
        public void Cleanup()
        {
            StopRecording();
            
            // Clear playback queue and stop audio
            if (playbackQueue != null)
            {
                playbackQueue.Clear();
            }
            
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
            
            isInitialized = false;
            Log("Audio streamer cleaned up.");
        }
        
        /// <summary>
        /// Log message if debug logging is enabled.
        /// </summary>
        /// <param name="message">Message to log</param>
        private void Log(string message)
        {
            if (config != null && config.EnableDebugLogging)
            {
                Debug.Log($"[ElevenLabsAudioStreamer] {message}");
            }
        }
        
        /// <summary>
        /// Log error message (always shown).
        /// </summary>
        /// <param name="message">Error message to log</param>
        private void LogError(string message)
        {
            Debug.LogError($"[ElevenLabsAudioStreamer] {message}");
        }
        
        /// <summary>
        /// Clean up resources when component is disabled.
        /// </summary>
        private void OnDisable()
        {
            Cleanup();
        }
        
        /// <summary>
        /// Clean up resources when component is destroyed.
        /// </summary>
        private void OnDestroy()
        {
            Cleanup();
        }
    }
}