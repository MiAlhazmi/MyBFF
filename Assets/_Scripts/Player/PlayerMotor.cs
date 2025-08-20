using UnityEngine;

namespace MyBFF.Player
{
    /// <summary>
    /// Handles all player movement mechanics including walking, running, jumping, crouching.
    /// Uses CharacterController for kinematic movement with custom gravity and ground checking.
    /// This follows Unity's recommended pattern for first-person character controllers.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        private PlayerConfig config;
        
        // Component references
        private CharacterController controller;
        
        // Movement state
        private Vector3 velocity;           // Current velocity (includes gravity)
        private Vector3 moveDirection;      // Normalized movement direction in world space
        private float currentSpeed;         // Current horizontal movement speed
        private bool isRunning;
        private bool isCrouching;
        
        // Ground detection
        private bool isGrounded;
        private float lastGroundedTime;     // Used for coyote time
        
        // Crouch system
        private float originalHeight;
        private Vector3 originalCenter;
        
        // Public read-only properties for other systems to query
        public bool IsGrounded => isGrounded;
        public bool IsMoving => moveDirection.magnitude > 0.1f;
        public bool IsRunning => isRunning;
        public bool IsCrouching => isCrouching;
        public float CurrentSpeed => currentSpeed;
        
        /// <summary>
        /// Initialize component references and validate configuration.
        /// Store original CharacterController dimensions for crouch system.
        /// </summary>
        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            
            // Store original controller dimensions for crouch toggle
            originalHeight = controller.height;
            originalCenter = controller.center;
        }
        
        /// <summary>
        /// Handle per-frame updates: ground checking, gravity, and movement application.
        /// This runs every frame to ensure smooth movement.
        /// </summary>
        private void Update()
        {
            CheckGrounded();
            ApplyGravity();
            
            // Apply the final velocity to the CharacterController
            controller.Move(velocity * Time.deltaTime);
        }
        
        
        /// <summary>
        /// Initialize the motor with player configuration.
        /// Called by PlayerController during setup.
        /// </summary>
        /// <param name="playerConfig">Configuration data for player movement</param>
        public void Initialize(PlayerConfig playerConfig)
        {
            config = playerConfig;
    
            if (config == null)
            {
                Debug.LogError("PlayerMotor: PlayerConfig is null!", this);
                enabled = false;
            }
        }
        
        /// <summary>
        /// Process movement input and update horizontal velocity.
        /// Handles acceleration/deceleration and movement state changes.
        /// </summary>
        /// <param name="inputDirection">Normalized input from WASD/stick (x=strafe, y=forward)</param>
        /// <param name="sprint">Whether sprint button is held</param>
        /// <param name="crouch">Whether crouch button is held</param>
        public void Move(Vector2 inputDirection, bool sprint, bool crouch)
        {
            // Handle crouching state change
            HandleCrouch(crouch);
            
            // Can only run when not crouching and actually moving
            isRunning = sprint && !isCrouching && inputDirection.magnitude > 0.1f;
            
            // Get target speed based on current movement state
            float targetSpeed = GetTargetSpeed(inputDirection.magnitude);
            
            // Convert 2D input to 3D world space movement relative to player facing
            Vector3 worldDirection = transform.TransformDirection(new Vector3(inputDirection.x, 0, inputDirection.y));
            worldDirection.Normalize();
            
            // Handle acceleration and deceleration for smooth movement feel
            if (inputDirection.magnitude > 0.1f)
            {
                // Player is giving input - accelerate toward target speed
                currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, config.Acceleration * Time.deltaTime);
                moveDirection = worldDirection;
            }
            else
            {
                // No input - decelerate to stop
                currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, config.Deceleration * Time.deltaTime);
            }
            
            // Apply horizontal movement to velocity
            velocity.x = moveDirection.x * currentSpeed;
            velocity.z = moveDirection.z * currentSpeed;
        }
        
        /// <summary>
        /// Attempt to make the player jump if grounded or within coyote time.
        /// Uses physics formula to calculate jump velocity for desired height.
        /// </summary>
        public void Jump()
        {
            // Coyote time: allow jumping briefly after leaving ground (feels more responsive)
            bool canJump = isGrounded || (Time.time - lastGroundedTime < config.CoyoteTime);
            
            // Only jump if we can jump and aren't already moving upward
            if (canJump && velocity.y <= 0.1f)
            {
                // Physics formula: v = sqrt(2 * h * g) gives initial velocity for desired height
                velocity.y = Mathf.Sqrt(2 * config.JumpHeight * config.Gravity);
            }
        }
        
        /// <summary>
        /// Check if player is touching ground using CharacterController.isGrounded.
        /// Updates grounded state and tracks timing for coyote time feature.
        /// </summary>
        private void CheckGrounded()
        {
            bool wasGrounded = isGrounded;
            isGrounded = controller.isGrounded;
            
            // Track when we were last on ground for coyote time
            if (isGrounded && !wasGrounded)
            {
                // Just landed
                lastGroundedTime = Time.time;
            }
            else if (isGrounded)
            {
                // Still grounded, keep updating time
                lastGroundedTime = Time.time;
            }
        }
        
        /// <summary>
        /// Apply gravity to vertical velocity when not grounded.
        /// Keeps player "sticky" to ground when grounded to prevent bouncing.
        /// </summary>
        private void ApplyGravity()
        {
            if (isGrounded && velocity.y <= 0)
            {
                // Small downward force to keep player attached to ground
                velocity.y = -0.5f;
            }
            else
            {
                // Apply gravity acceleration when in air
                velocity.y -= config.Gravity * Time.deltaTime;
            }
        }
        
        /// <summary>
        /// Handle crouch state changes and CharacterController size modifications.
        /// Includes ceiling check to prevent standing up in tight spaces.
        /// </summary>
        /// <param name="wantsToCrouch">Whether player input wants to crouch</param>
        private void HandleCrouch(bool wantsToCrouch)
        {
            if (wantsToCrouch && !isCrouching)
            {
                // Start crouching - make controller shorter
                isCrouching = true;
                controller.height = originalHeight * 0.5f;
                controller.center = new Vector3(originalCenter.x, originalCenter.y * 0.5f, originalCenter.z);
            }
            else if (!wantsToCrouch && isCrouching)
            {
                // Want to stop crouching - check if there's space above first
                if (CanStandUp())
                {
                    isCrouching = false;
                    controller.height = originalHeight;
                    controller.center = originalCenter;
                }
            }
        }
        
        /// <summary>
        /// Check if there's enough vertical space for player to stand up.
        /// Uses capsule cast to detect ceiling or obstacles above.
        /// </summary>
        /// <returns>True if player can safely stand up</returns>
        private bool CanStandUp()
        {
            // Calculate where the top of the capsule would be when standing
            Vector3 capsuleTop = transform.position + Vector3.up * originalHeight;
            
            // Check for obstacles in the space we want to occupy
            return !Physics.CheckCapsule(
                transform.position + controller.center, 
                capsuleTop, 
                controller.radius, 
                config.GroundLayers
            );
        }
        
        /// <summary>
        /// Calculate target movement speed based on input strength and movement state.
        /// Returns appropriate speed for walking, running, or crouching.
        /// </summary>
        /// <param name="inputMagnitude">Strength of movement input (0-1)</param>
        /// <returns>Target speed in units per second</returns>
        private float GetTargetSpeed(float inputMagnitude)
        {
            if (inputMagnitude < 0.1f) return 0f;
            
            // Priority: crouch > run > walk
            if (isCrouching) return config.CrouchSpeed;
            if (isRunning) return config.RunSpeed;
            return config.WalkSpeed;
        }
        
        /// <summary>
        /// Draw debugging gizmos in Scene view to visualize ground checking.
        /// Only draws when this GameObject is selected.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (controller == null) return;
            
            // Visualize ground check position and status
            Gizmos.color = isGrounded ? Color.green : Color.red;
            Vector3 groundCheckPos = transform.position - Vector3.up * (controller.height * 0.5f + config.GroundCheckDistance);
            Gizmos.DrawWireSphere(groundCheckPos, 0.1f);
        }
    }
}