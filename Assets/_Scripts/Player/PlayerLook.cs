using UnityEngine;

namespace MyBFF.Player
{
    /// <summary>
    /// Handles first-person camera look controls for both mouse and touch input.
    /// Manages yaw (horizontal) rotation on the player body and pitch (vertical) on the camera.
    /// Automatically detects mobile vs desktop for appropriate sensitivity scaling.
    /// </summary>
    public class PlayerLook : MonoBehaviour
    {
        [SerializeField] private Transform cameraTransform;
        private PlayerConfig config;
        
        // Look state
        private float currentPitch;  // Vertical rotation (up/down)
        private bool isMobile;       // Auto-detected platform for sensitivity
        
        // Public properties for other systems to query camera state
        public float CurrentPitch => currentPitch;
        public float CurrentYaw => transform.eulerAngles.y;
        
        /// <summary>
        /// Initialize component references and detect platform.
        /// Auto-find camera if not manually assigned.
        /// </summary>
        private void Awake()
        {
            // Try to find camera automatically if not assigned
            if (cameraTransform == null)
            {
                Camera cam = GetComponentInChildren<Camera>();
                if (cam != null)
                {
                    cameraTransform = cam.transform;
                }
                else
                {
                    Debug.LogError("PlayerLook: No camera found! Assign cameraTransform or add Camera as child.", this);
                    enabled = false;
                    return;
                }
            }
            
            // Detect if we're running on mobile platform
            isMobile = Application.isMobilePlatform;
            
            // Lock cursor for desktop play (will be released on mobile automatically)
            if (!isMobile)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
        
        /// <summary>
        /// Initialize the look system with player configuration.
        /// Called by PlayerController during setup.
        /// </summary>
        /// <param name="playerConfig">Configuration data for camera settings</param>
        public void Initialize(PlayerConfig playerConfig)
        {
            config = playerConfig;
    
            if (config == null)
            {
                Debug.LogError("PlayerLook: PlayerConfig is null!", this);
                enabled = false;
            }
        }
        
        /// <summary>
        /// Process look input and apply rotation to player body (yaw) and camera (pitch).
        /// Handles both mouse and touch input with appropriate sensitivity scaling.
        /// </summary>
        /// <param name="lookInput">Raw look input from mouse/touch (x=horizontal, y=vertical)</param>
        public void Look(Vector2 lookInput)
        {
            // Skip if no input to avoid unnecessary calculations
            if (lookInput.magnitude < 0.001f) return;
            
            // Get appropriate sensitivity based on input device
            float sensitivity = isMobile ? config.TouchSensitivity : config.MouseSensitivity;
            
            // Apply sensitivity and frame-rate independence
            Vector2 scaledInput = lookInput * sensitivity * Time.deltaTime;
            
            // Handle Y-axis inversion preference
            if (config.InvertY)
            {
                scaledInput.y = -scaledInput.y;
            }
            
            // Apply horizontal rotation to player body (yaw)
            ApplyYaw(scaledInput.x);
            
            // Apply vertical rotation to camera (pitch)
            ApplyPitch(-scaledInput.y); // Negative for natural mouse look feel
        }
        
        /// <summary>
        /// Apply horizontal (yaw) rotation to the player body.
        /// This allows the player to turn left and right.
        /// </summary>
        /// <param name="yawDelta">Change in yaw rotation this frame</param>
        private void ApplyYaw(float yawDelta)
        {
            // Rotate the entire player GameObject around Y-axis
            transform.Rotate(Vector3.up * yawDelta, Space.Self);
        }
        
        /// <summary>
        /// Apply vertical (pitch) rotation to the camera with clamping.
        /// Prevents camera from rotating too far up or down (prevents disorientation).
        /// </summary>
        /// <param name="pitchDelta">Change in pitch rotation this frame</param>
        private void ApplyPitch(float pitchDelta)
        {
            // Update pitch value
            currentPitch += pitchDelta;
            
            // Clamp pitch to prevent over-rotation (looking too far up/down)
            currentPitch = Mathf.Clamp(currentPitch, config.MinPitch, config.MaxPitch);
            
            // Apply rotation to camera
            cameraTransform.localRotation = Quaternion.Euler(currentPitch, 0f, 0f);
        }
        
        /// <summary>
        /// Reset camera to neutral position (looking straight ahead).
        /// Useful for cutscenes or respawn scenarios.
        /// </summary>
        public void ResetLook()
        {
            currentPitch = 0f;
            cameraTransform.localRotation = Quaternion.identity;
        }
        
        /// <summary>
        /// Get the forward direction the camera is looking.
        /// Useful for interaction raycasting and AI line-of-sight.
        /// </summary>
        /// <returns>Normalized forward vector of camera</returns>
        public Vector3 GetLookDirection()
        {
            return cameraTransform.forward;
        }
        
        /// <summary>
        /// Enable or disable cursor lock (useful for UI transitions).
        /// Only affects desktop - mobile ignores cursor lock.
        /// </summary>
        /// <param name="locked">Whether cursor should be locked to center</param>
        public void SetCursorLock(bool locked)
        {
            if (isMobile) return;
            
            if (locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
    }
}