using UnityEngine;
using UnityEngine.InputSystem;

namespace MyBFF.Player
{
    /// <summary>
    /// Main player controller that coordinates all player systems.
    /// Receives input from Unity's Input System and delegates to appropriate subsystems.
    /// This is the central hub that connects input to movement, camera, and interactions.
    /// Requires: PlayerInput component with InputSystem_Actions asset configured.
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] private PlayerConfig config;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private PlayerLook look;
        [SerializeField] private PlayerInteractor interactor;

        private PlayerInput playerInput;
        // Input System action references (auto-populated from PlayerInput)
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction jumpAction;
        private InputAction sprintAction;
        private InputAction crouchAction;
        private InputAction interactAction;
        
        // Current input state
        private Vector2 currentMoveInput;
        private Vector2 currentLookInput;
        private bool isSprintHeld;
        private bool isCrouchHeld;
        
        /// <summary>
        /// Initialize component references and validate setup.
        /// Auto-find required components if not manually assigned.
        /// </summary>
        private void Awake()
        {
            // Get PlayerInput component and cache action references
            playerInput = GetComponent<PlayerInput>();
            
            if (playerInput == null)
            {
                Debug.LogError("PlayerController: PlayerInput component not found!", this);
                enabled = false;
                return;
            }
            
            // Auto-find components if not assigned
            if (motor == null)
            {
                motor = GetComponent<PlayerMotor>(); motor.Initialize(config);
            }

            if (look == null)
            {
                look = GetComponent<PlayerLook>(); look.Initialize(config);
            }
            if (interactor == null)
            {
                interactor = GetComponent<PlayerInteractor>();
                interactor.Initialize(config);
            }
            // Validate required components
            if (motor == null)
            {
                Debug.LogError("PlayerController: PlayerMotor component not found!", this);
                enabled = false;
                return;
            }
            
            if (look == null)
            {
                Debug.LogError("PlayerController: PlayerLook component not found!", this);
                enabled = false;
                return;
            }
            
            // Interactor is optional - some games might not need interaction
            if (interactor == null)
            {
                Debug.LogWarning("PlayerController: PlayerInteractor not found. Interaction will be disabled.", this);
            }
        }
        
        /// <summary>
        /// Set up Input System action references.
        /// This is called automatically by PlayerInput component.
        /// </summary>
        private void Start()
        {
            // Cache references to input actions for performance
            moveAction = playerInput.actions["Move"];
            lookAction = playerInput.actions["Look"];
            jumpAction = playerInput.actions["Jump"];
            sprintAction = playerInput.actions["Sprint"];
            crouchAction = playerInput.actions["Crouch"];
            interactAction = playerInput.actions["Interact"];
            
            // Validate that all required actions exist
            if (moveAction == null) Debug.LogError("Move action not found in Input Actions!");
            if (lookAction == null) Debug.LogError("Look action not found in Input Actions!");
            if (jumpAction == null) Debug.LogError("Jump action not found in Input Actions!");
            if (sprintAction == null) Debug.LogError("Sprint action not found in Input Actions!");
            if (crouchAction == null) Debug.LogError("Crouch action not found in Input Actions!");
            if (interactAction == null) Debug.LogError("Interact action not found in Input Actions!");
        }

        /// <summary>
        /// Subscribe to input action events when component becomes active.
        /// This follows Unity's recommended pattern for Input System event handling.
        /// </summary>
        private void OnEnable()
        {
            jumpAction = playerInput.actions["Jump"];
            interactAction = playerInput.actions["Interact"];
            // Subscribe to button press events (performed = pressed, canceled = released)
            if (jumpAction != null) jumpAction.performed += OnJumpPerformed;
            if (interactAction != null) interactAction.started += OnInteractPerformed;
            
            // Enable all actions
            moveAction?.Enable();
            lookAction?.Enable();
            jumpAction?.Enable();
            sprintAction?.Enable();
            crouchAction?.Enable();
            interactAction?.Enable();
        }
        
        /// <summary>
        /// Unsubscribe from input action events when component becomes inactive.
        /// Critical for preventing memory leaks and null reference exceptions.
        /// </summary>
        private void OnDisable()
        {
            // Unsubscribe from events
            if (jumpAction != null) jumpAction.performed -= OnJumpPerformed;
            if (interactAction != null) interactAction.performed -= OnInteractPerformed;
            
            // Disable all actions
            moveAction?.Disable();
            lookAction?.Disable();
            jumpAction?.Disable();
            sprintAction?.Disable();
            crouchAction?.Disable();
            interactAction?.Disable();
        }
        
        /// <summary>
        /// Update input state and delegate to subsystems every frame.
        /// Reads continuous inputs (move, look, sprint, crouch) and updates components.
        /// </summary>
        private void Update()
        {
            ReadInputs();
            UpdateSubsystems();
        }
        
        /// <summary>
        /// Read current input values from Input System actions.
        /// Caches input state for use by subsystems.
        /// </summary>
        private void ReadInputs()
        {
            // Read continuous inputs
            currentMoveInput = moveAction?.ReadValue<Vector2>() ?? Vector2.zero;
            currentLookInput = lookAction?.ReadValue<Vector2>() ?? Vector2.zero;
            
            // Read button states (held vs not held)
            isSprintHeld = sprintAction?.IsPressed() ?? false;
            isCrouchHeld = crouchAction?.IsPressed() ?? false;
        }
        
        /// <summary>
        /// Update all player subsystems with current input state.
        /// Delegates input to appropriate components for processing.
        /// </summary>
        private void UpdateSubsystems()
        {
            // Update movement system
            motor.Move(currentMoveInput, isSprintHeld, isCrouchHeld);
            
            // Update look system
            look.Look(currentLookInput);
            
            // Interactor updates itself in its Update method
        }
        
        /// <summary>
        /// Handle jump input when jump button is pressed.
        /// Called automatically by Input System when jump action is performed.
        /// </summary>
        /// <param name="context">Input action context (contains input information)</param>
        private void OnJumpPerformed(InputAction.CallbackContext context)
        {
            motor.Jump();
        }
        
        /// <summary>
        /// Handle interact input when interact button is pressed.
        /// Called automatically by Input System when interact action is performed.
        /// </summary>
        /// <param name="context">Input action context (contains input information)</param>
        private void OnInteractPerformed(InputAction.CallbackContext context)
        {
            // Only try to interact if interactor component exists
            if (interactor != null)
            {
                interactor.TryInteract();
            }
        }
        
        /// <summary>
        /// Get current movement input for external systems (like animation).
        /// </summary>
        /// <returns>Current movement input vector</returns>
        public Vector2 GetMoveInput() => currentMoveInput;
        
        /// <summary>
        /// Get current look input for external systems.
        /// </summary>
        /// <returns>Current look input vector</returns>
        public Vector2 GetLookInput() => currentLookInput;
        
        /// <summary>
        /// Check if player is currently sprinting.
        /// </summary>
        /// <returns>True if sprint button is held and player is moving fast</returns>
        public bool IsRunning() => motor.IsRunning;
        
        /// <summary>
        /// Check if player is currently crouching.
        /// </summary>
        /// <returns>True if crouch button is held and crouch is active</returns>
        public bool IsCrouching() => motor.IsCrouching;
        
        /// <summary>
        /// Enable or disable player input processing.
        /// Useful for cutscenes, menus, or dialogue where player shouldn't move.
        /// </summary>
        /// <param name="enabled">Whether input should be processed</param>
        public void SetInputEnabled(bool enabled)
        {
            this.enabled = enabled;
            
            // Also disable subsystems when input is disabled
            if (motor != null) motor.enabled = enabled;
            if (look != null) look.enabled = enabled;
            if (interactor != null) interactor.enabled = enabled;
        }
    }
}