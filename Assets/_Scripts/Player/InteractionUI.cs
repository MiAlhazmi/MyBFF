using TMPro;
using UnityEngine;

namespace MyBFF.Player
{
    public class InteractionUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI promptText; // or Text

        private void Start()
        {
            // Find player and subscribe to events
            PlayerInteractor interactor = FindObjectOfType<PlayerInteractor>();
            interactor.OnInteractionPromptShow += ShowPrompt;
            interactor.OnInteractionPromptHide += HidePrompt;
        }

        private void ShowPrompt(string message)
        {
            promptText.text = message;
            promptText.gameObject.SetActive(true);
        }

        private void HidePrompt()
        {
            promptText.gameObject.SetActive(false);
        }
    }
}