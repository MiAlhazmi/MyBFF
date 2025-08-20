using UnityEngine;

namespace ChildGame.Interaction
{
    /// <summary>
    /// Interface for objects that can be interacted with by the player.
    /// Implement this on any GameObject that should respond to player interaction.
    /// This follows Unity's component-based architecture for flexible interaction systems.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>
        /// Called when player successfully interacts with this object.
        /// Implement game-specific interaction logic here (pick up item, open door, talk to NPC, etc.).
        /// </summary>
        /// <param name="interactor">The GameObject that initiated the interaction (usually the player)</param>
        void OnInteract(GameObject interactor);
        
        /// <summary>
        /// Called when player looks at this interactable object (for UI prompts).
        /// Use this to show interaction hints like "Press E to talk to Lulu".
        /// </summary>
        /// <param name="interactor">The GameObject that is looking at this object</param>
        void OnInteractHover(GameObject interactor);
        
        /// <summary>
        /// Called when player stops looking at this interactable object.
        /// Use this to hide interaction UI prompts.
        /// </summary>
        /// <param name="interactor">The GameObject that was looking at this object</param>
        void OnInteractExit(GameObject interactor);
        
        /// <summary>
        /// Whether this object can currently be interacted with.
        /// Allows for conditional interactions (e.g., locked doors, NPCs busy talking, etc.).
        /// </summary>
        /// <returns>True if interaction is currently allowed</returns>
        bool CanInteract();
        
        /// <summary>
        /// Get the interaction prompt text to display to the player.
        /// Examples: "Talk to Lulu", "Pick up toy", "Open chest"
        /// </summary>
        /// <returns>Human-readable interaction prompt</returns>
        string GetInteractionPrompt();
    }
}