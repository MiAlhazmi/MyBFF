using System.Collections;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;

public class ParentEmailUI : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private Canvas uiCanvas;
    [SerializeField] private Camera uiCamera;
    [SerializeField] private TMP_InputField emailInput;
    [SerializeField] private Button submitButton;
    [SerializeField] private TMP_Text statusText;
    
    [Header("Game Elements")]
    [SerializeField] private GameObject playerGameObject;
    
    [Header("Settings")]
    [SerializeField] private string webhookUrl = "https://your-n8n-webhook-url.com";
    
    private void Start()
    {
        // Check if parent email already submitted
        if (ParentEmailManager.IsEmailSubmitted())
        {
            // Hide UI and enable gameplay
            HideEmailUI();
        }
        else
        {
            // Show UI and disable player
            ShowEmailUI();
        }
        
        submitButton.onClick.AddListener(OnSubmitClicked);
        statusText.text = "Please enter parent's email address";
    }
    
    private void ShowEmailUI()
    {
        uiCanvas.gameObject.SetActive(true);
        uiCamera.gameObject.SetActive(true);
        playerGameObject.SetActive(false);
    }
    
    private void HideEmailUI()
    {
        uiCanvas.gameObject.SetActive(false);
        uiCamera.gameObject.SetActive(false);
        playerGameObject.SetActive(true);
    }
    
    private void OnSubmitClicked()
    {
        string email = emailInput.text.Trim();
        
        if (string.IsNullOrEmpty(email))
        {
            statusText.text = "Please enter an email address";
            return;
        }
        
        if (!IsValidEmail(email))
        {
            statusText.text = "Please enter a valid email address";
            return;
        }
        
        StartCoroutine(SendParentEmail(email));
    }
    
    private IEnumerator SendParentEmail(string email)
    {
        submitButton.interactable = false;
        statusText.text = "Submitting...";
        
        string userId = UserIdManager.GetUserId();
        
        var data = new ParentEmailData
        {
            userId = userId,
            parentEmail = email,
            timestamp = System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };
        
        string jsonData = JsonUtility.ToJson(data);
        
        using (UnityWebRequest request = UnityWebRequest.Post(webhookUrl, jsonData, "application/json"))
        {
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                statusText.text = "Success! Starting game...";
                ParentEmailManager.MarkEmailSubmitted();
                yield return new WaitForSeconds(1f);
                HideEmailUI();
            }
            else
            {
                statusText.text = $"Error: {request.error}. Please try again.";
                submitButton.interactable = true;
            }
        }
    }
    
    private bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;
        
        string pattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
        return Regex.IsMatch(email, pattern);
    }
}

[System.Serializable]
public class ParentEmailData
{
    public string userId;
    public string parentEmail;
    public string timestamp;
}