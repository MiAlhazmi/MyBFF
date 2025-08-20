using UnityEngine;
using ChildGame.Interaction;

namespace MyBFF.Player
{
    /// <summary>
    /// Handles player interaction with objects in the world.
    /// Uses raycasting from camera to detect interactable objects.
    /// Manages interaction prompts and triggers interaction events.
    /// </summary>
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private Camera playerCamera;
        private PlayerConfig config;
        
        // Current interaction state
        private IInteractable currentInteractable;
        private GameObject currentInteractableObject;
        
        // Events for UI system to subscribe to
        public System.Action<string> OnInteractionPromptShow;   // Called when hovering over interactable
        public System.Action OnInteractionPromptHide;          // Called when leaving interactable
        public System.Action<IInteractable> OnInteractionTriggered; // Called when interaction happens
        
        /// <summary>
        /// Initialize component references and validate setup.
        /// Auto-find camera if not manually assigned.
        /// </summary>
        private void Awake()
        {
            // Auto-find camera if not assigned
            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>();
                if (playerCamera == null)
                {
                    Debug.LogError("PlayerInteractor: No camera found! Assign playerCamera or add Camera as child.", this);
                    enabled = false;
                    return;
                }
            }
        }
        
        /// <summary>
        /// Check for interactable objects in front of the player every frame.
        /// Updates interaction prompts based on what player is looking at.
        /// </summary>
        private void Update()
        {
            CheckForInteractable();
        }
        
        /// <summary>
        /// Initialize the interactor with player configuration.
        /// Called by PlayerController during setup.
        /// </summary>
        /// <param name="playerConfig">Configuration data for interaction settings</param>
        public void Initialize(PlayerConfig playerConfig)
        {
            config = playerConfig;
    
            if (config == null)
            {
                Debug.LogError("PlayerInteractor: PlayerConfig is null!", this);
                enabled = false;
            }
        }
        
        /// <summary>
        /// Attempt to interact with the currently targeted interactable object.
        /// Called by PlayerController when interact input is pressed.
        /// </summary>
        public void TryInteract()
        {
            // Check if we have a valid interactable and it allows interaction
            if (currentInteractable != null && currentInteractable.CanInteract())
            {
                // Trigger the interaction
                currentInteractable.OnInteract(gameObject);
                
                // Notify other systems (like UI, audio, analytics)
                OnInteractionTriggered?.Invoke(currentInteractable);
                
            }
        }
        
        /// <summary>
        /// Cast a ray from camera center to detect interactable objects.
        /// Updates interaction prompts when entering/exiting interactable objects.
        /// </summary>
        private void CheckForInteractable()
        {
            // Cast ray from center of camera view
            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            
            // Check for hits on interactable layers within interaction range
            if (Physics.Raycast(ray, out RaycastHit hit, config.InteractRange, config.InteractLayers))
            {
                // Try to get interactable component from hit object
                IInteractable interactable = hit.collider.GetComponent<IInteractable>();
                
                if (interactable != null)
                {
                    // We're looking at an interactable object
                    HandleInteractableFound(interactable, hit.collider.gameObject);
                }
                else
                {
                    // Hit something but it's not interactable
                    HandleNoInteractable();
                }
            }
            else
            {
                // Didn't hit anything or hit something too far away
                HandleNoInteractable();
            }
        }
        
        /// <summary>
        /// Handle when we start or continue looking at an interactable object.
        /// Shows interaction prompts and manages hover state.
        /// </summary>
        /// <param name="interactable">The interactable component we're looking at</param>
        /// <param name="gameObject">The GameObject containing the interactable</param>
        private void HandleInteractableFound(IInteractable interactable, GameObject gameObject)
        {
            // Check if this is a new interactable (different from what we were looking at)
            if (currentInteractable != interactable)
            {
                // Exit previous interactable if we had one
                if (currentInteractable != null)
                {
                    currentInteractable.OnInteractExit(this.gameObject);
                }
                
                // Set new current interactable
                currentInteractable = interactable;
                currentInteractableObject = gameObject;
                
                // Notify new interactable that we're looking at it
                currentInteractable.OnInteractHover(this.gameObject);
                
                // Show interaction prompt if this object can be interacted with
                if (currentInteractable.CanInteract())
                {
                    string prompt = currentInteractable.GetInteractionPrompt();
                    OnInteractionPromptShow?.Invoke(prompt);
                }
            }
        }
        
        /// <summary>
        /// Handle when we're no longer looking at any interactable object.
        /// Hides interaction prompts and clears current interactable.
        /// </summary>
        private void HandleNoInteractable()
        {
            // If we were looking at something interactable, clean up
            if (currentInteractable != null)
            {
                // Notify the interactable that we're no longer looking at it
                currentInteractable.OnInteractExit(gameObject);
                
                // Clear current interactable
                currentInteractable = null;
                currentInteractableObject = null;
                
                // Hide interaction prompt
                OnInteractionPromptHide?.Invoke();
            }
        }
        
        /// <summary>
        /// Get the currently targeted interactable object (for debugging/UI).
        /// </summary>
        /// <returns>Current interactable or null if none</returns>
        public IInteractable GetCurrentInteractable()
        {
            return currentInteractable;
        }
        
        /// <summary>
        /// Draw debug visualization of interaction raycast in Scene view.
        /// Shows interaction range and current target.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (playerCamera == null) return;
            
            // Draw interaction ray
            Gizmos.color = currentInteractable != null ? Color.green : Color.red;
            Vector3 rayStart = playerCamera.transform.position;
            Vector3 rayEnd = rayStart + playerCamera.transform.forward * config.InteractRange;
            Gizmos.DrawLine(rayStart, rayEnd);
            
            // Draw interaction range sphere
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(rayEnd, 0.1f);
        }
    }
}