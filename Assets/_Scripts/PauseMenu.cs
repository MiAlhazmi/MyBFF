using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class PauseMenu : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject panel; // Your pause panel root (contains the Quit button)

    [Header("Input (optional but recommended)")]
    [SerializeField] private PlayerInput playerInput;   // Assign your Player's PlayerInput
    [SerializeField] private string gameplayActionMap = "Player";
    [SerializeField] private string uiActionMap = "UI";

    private bool _paused;

    private void Awake()
    {
        if (panel != null) panel.SetActive(false);
        if (playerInput == null) playerInput = FindObjectOfType<PlayerInput>();
    }

    private void Update()
    {
        // Desktop quick toggle. On mobile, call TogglePause() from a UI button instead.
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            TogglePause();
    }

    public void TogglePause()
    {
        SetPaused(!_paused);
    }

    public void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        _paused = paused;

        // Time & audio
        Time.timeScale = paused ? 0f : 1f;
        AudioListener.pause = paused;

        // UI
        if (panel != null) panel.SetActive(paused);

        // Cursor (desktop only)
        if (!Application.isMobilePlatform)
        {
            Cursor.visible = paused;
            Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
        }

        // Swap action maps if available
        if (playerInput != null)
        {
            var targetMap = paused ? uiActionMap : gameplayActionMap;
            if (!string.IsNullOrEmpty(targetMap))
                playerInput.SwitchCurrentActionMap(targetMap);
        }
    }

    // Hook this to the only button in the panel.
    public void OnQuitButton()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
