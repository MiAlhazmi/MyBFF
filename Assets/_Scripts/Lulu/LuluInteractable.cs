using System.Collections;
using UnityEngine;
using ChildGame.Interaction;
using MyBFF.Voice;

namespace MyBFF.Character
{
    public class LuluInteractable : MonoBehaviour, IInteractable
    {
        [Header("References")]
        [SerializeField] private LuluAnimationManager animationManager;
        [SerializeField] private ElevenLabsVoiceChat voiceChat;
        
        [Header("Interaction Settings")]
        [SerializeField] private string interactionPrompt = "Talk to Lulu";
        [SerializeField] private string endConversationPrompt = "Stop talking (E)";
        
        [Header("Visual Feedback")]
        [SerializeField] private GameObject highlightObject; // Object to show on hover
        [SerializeField] private Material highlightMaterial; // Material for glow effect
        
        [Header("Timeout Settings")]
        [SerializeField] private float conversationTimeoutSeconds = 410f; // almost 7 minutes - Elevenlabs Max conversation duration is 7 minutes
        private float conversationStartTime;

        private bool isInConversation = false;
        private Renderer luluRenderer;
        private Material originalMaterial;
        
        public void OnInteract(GameObject interactor)
        {
            if (isInConversation)
            {
                EndConversation();
            }
            else
            {
                StartConversation();
            }
        }
        
        public void OnInteractHover(GameObject interactor)
        {
            if (!CanInteract()) return;
            
            // Show visual feedback
            if (highlightObject != null)
                highlightObject.SetActive(true);
                
            if (luluRenderer != null && highlightMaterial != null)
                luluRenderer.material = highlightMaterial;
        }
        
        public void OnInteractExit(GameObject interactor)
        {
            // Hide visual feedback
            if (highlightObject != null)
                highlightObject.SetActive(false);
                
            if (luluRenderer != null && originalMaterial != null)
                luluRenderer.material = originalMaterial;
        }
        
        public bool CanInteract()
        {
            return true;
        }
        
        public string GetInteractionPrompt()
        {
            return isInConversation ? endConversationPrompt : interactionPrompt;
        }
        
        private void StartConversation()
        {
            isInConversation = true;
            
            // Face the player
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                StartCoroutine(FacePlayer(player.transform));
            }
            
            if (animationManager != null) animationManager.StartConversation();
            if (voiceChat != null) voiceChat.StartConversation();
            
            conversationStartTime = Time.time;
        }
        
        public void EndConversation()
        {
            if (!isInConversation) return;
            isInConversation = false;
            if (animationManager != null) animationManager.EndConversation();
            if (voiceChat != null) voiceChat.StopConversation();
        }
        
        private IEnumerator FacePlayer(Transform player)
        {
            Vector3 direction = (player.position - transform.position).normalized;
            direction.y = 0; // Keep on horizontal plane
            
            if (direction != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                
                while (Quaternion.Angle(transform.rotation, targetRotation) > 5f)
                {
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, 180f * Time.deltaTime);
                    yield return null;
                }
            }
        }
        
        private void Update()
        {
            // Timeout handling
            if (isInConversation && Time.time - conversationStartTime > conversationTimeoutSeconds)
            {
                Debug.Log("Conversation timed out");
                EndConversation();
            }
        }
        
        private void Awake()
        {
            if (animationManager == null) animationManager = GetComponent<LuluAnimationManager>();
            if (voiceChat == null) voiceChat = FindObjectOfType<ElevenLabsVoiceChat>();
            
            // Cache renderer for material swapping
            luluRenderer = GetComponent<Renderer>();
            if (luluRenderer != null && highlightMaterial != null)
            {
                originalMaterial = luluRenderer.material;
            }
            
            // Hide highlight initially
            if (highlightObject != null)
                highlightObject.SetActive(false);
        }
    }
}