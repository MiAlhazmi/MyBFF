using UnityEngine;
using UnityEngine.Events;
using ChildGame.Interaction;

namespace MyBFF.Interaction
{
    /// <summary>
    /// Generic interactable component that can be configured in the Inspector.
    /// Uses UnityEvents to allow designers to set up interactions without coding.
    /// Perfect for simple interactions like picking up objects, opening doors, playing sounds, etc.
    /// </summary>
    public class BasicInteractable : MonoBehaviour, IInteractable
    {
        [Header("Interaction Settings")]
        [SerializeField] private string interactionPrompt = "Interact";
        [SerializeField] private bool canInteract = true;
        [SerializeField] private bool oneTimeUse = false;
        
        [Header("Events")]
        [SerializeField] private UnityEvent onInteract;           // Called when interaction happens
        [SerializeField] private UnityEvent onHoverEnter;        // Called when player looks at this
        [SerializeField] private UnityEvent onHoverExit;         // Called when player looks away
        
        [Header("Audio (Optional)")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip interactSound;
        [SerializeField] private AudioClip hoverSound;
        
        [Header("Visual Feedback (Optional)")]
        [SerializeField] private GameObject highlightObject;     // Object to show/hide on hover
        [SerializeField] private Material highlightMaterial;     // Material to swap on hover
        
        // Internal state
        private bool hasBeenUsed = false;
        private Renderer objectRenderer;
        private Material originalMaterial;
        
        /// <summary>
        /// Initialize component and cache references for visual feedback.
        /// </summary>
        private void Awake()
        {
            // Cache renderer for material swapping
            if (highlightMaterial != null)
            {
                objectRenderer = GetComponent<Renderer>();
                if (objectRenderer != null)
                {
                    originalMaterial = objectRenderer.material;
                }
            }
            
            // Hide highlight object initially
            if (highlightObject != null)
            {
                highlightObject.SetActive(false);
            }
            
            // Auto-find AudioSource if not assigned
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }
        }
        
        /// <summary>
        /// Handle interaction when player presses interact button.
        /// Triggers UnityEvents and plays audio if configured.
        /// </summary>
        /// <param name="interactor">The player GameObject that triggered interaction</param>
        public void OnInteract(GameObject interactor)
        {
            // Check if we can still interact
            if (!CanInteract()) return;
            
            // Play interaction sound
            PlaySound(interactSound);
            
            // Trigger the configured interaction events
            onInteract?.Invoke();
            
            // Mark as used if this is one-time only
            if (oneTimeUse)
            {
                hasBeenUsed = true;
                
                // Optionally disable visual feedback after use
                SetHighlightActive(false);
            }
            
        }
        
        /// <summary>
        /// Handle when player starts looking at this interactable.
        /// Shows visual feedback and plays hover sound.
        /// </summary>
        /// <param name="interactor">The player GameObject looking at this</param>
        public void OnInteractHover(GameObject interactor)
        {
            // Only show feedback if we can interact
            if (!CanInteract()) return;
            
            // Play hover sound
            PlaySound(hoverSound);
            
            // Show visual feedback
            SetHighlightActive(true);
            
            // Trigger hover enter events
            onHoverEnter?.Invoke();
        }
        
        /// <summary>
        /// Handle when player stops looking at this interactable.
        /// Hides visual feedback.
        /// </summary>
        /// <param name="interactor">The player GameObject that was looking at this</param>
        public void OnInteractExit(GameObject interactor)
        {
            // Hide visual feedback
            SetHighlightActive(false);
            
            // Trigger hover exit events
            onHoverExit?.Invoke();
        }
        
        /// <summary>
        /// Check if this object can currently be interacted with.
        /// Considers enabled state, one-time use, and custom conditions.
        /// </summary>
        /// <returns>True if interaction is allowed</returns>
        public bool CanInteract()
        {
            // Can't interact if disabled
            if (!canInteract) return false;
            
            // Can't interact if already used and this is one-time only
            if (oneTimeUse && hasBeenUsed) return false;
            
            // Can interact
            return true;
        }
        
        /// <summary>
        /// Get the text prompt to show to the player.
        /// Returns appropriate message based on interaction state.
        /// </summary>
        /// <returns>Interaction prompt text</returns>
        public string GetInteractionPrompt()
        {
            if (!CanInteract())
            {
                return ""; // No prompt if can't interact
            }
            
            return interactionPrompt;
        }
        
        /// <summary>
        /// Enable or disable interaction capability at runtime.
        /// Useful for quest progression, unlocking objects, etc.
        /// </summary>
        /// <param name="enabled">Whether interaction should be allowed</param>
        public void SetInteractionEnabled(bool enabled)
        {
            canInteract = enabled;
            
            // Hide highlight if disabled while being looked at
            if (!enabled)
            {
                SetHighlightActive(false);
            }
        }
        
        /// <summary>
        /// Reset the interaction state (for one-time use objects).
        /// Allows the object to be interacted with again.
        /// </summary>
        public void ResetInteraction()
        {
            hasBeenUsed = false;
        }
        
        /// <summary>
        /// Change the interaction prompt text at runtime.
        /// Useful for dynamic interactions based on game state.
        /// </summary>
        /// <param name="newPrompt">New prompt text to display</param>
        public void SetInteractionPrompt(string newPrompt)
        {
            interactionPrompt = newPrompt;
        }
        
        /// <summary>
        /// Show or hide visual highlight effects.
        /// Handles both highlight objects and material swapping.
        /// </summary>
        /// <param name="active">Whether highlight should be visible</param>
        private void SetHighlightActive(bool active)
        {
            // Toggle highlight object visibility
            if (highlightObject != null)
            {
                highlightObject.SetActive(active);
            }
            
            // Swap materials for highlight effect
            if (objectRenderer != null && highlightMaterial != null && originalMaterial != null)
            {
                objectRenderer.material = active ? highlightMaterial : originalMaterial;
            }
        }
        
        /// <summary>
        /// Play an audio clip if AudioSource and clip are available.
        /// Handles null checks and prevents overlapping sounds.
        /// </summary>
        /// <param name="clip">Audio clip to play</param>
        private void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }
        
        /// <summary>
        /// Clean up when object is destroyed (restore original material).
        /// Prevents material reference leaks.
        /// </summary>
        private void OnDestroy()
        {
            // Restore original material to prevent leaks
            if (objectRenderer != null && originalMaterial != null)
            {
                objectRenderer.material = originalMaterial;
            }
        }
    }
}