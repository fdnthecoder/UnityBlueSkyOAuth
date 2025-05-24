using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple Bluesky OAuth test - just opens browser and displays auth info
/// </summary>
public class BlueskyOAuthExample : MonoBehaviour
{
    [SerializeField] private BlueskyOAuthManager oauthManager;
    [SerializeField] private Text statusText;
    [SerializeField] private Button testButton;
    
    private void Start()
    {
        // Validate references
        if (oauthManager == null)
        {
            Debug.LogError("BlueskyOAuthManager reference is missing! Please assign it in the Inspector.");
            return;
        }
        
        // Subscribe to events
        oauthManager.OnAuthSuccess += HandleAuthSuccess;
        oauthManager.OnAuthError += HandleAuthError;
        
        // Set up button click handler
        if (testButton != null)
        {
            testButton.onClick.AddListener(StartOAuthTest);
        }
        
        UpdateStatus("Click 'Test OAuth' to start authentication flow");
    }
    
    private void OnDestroy()
    {
        // Clean up event subscriptions
        if (oauthManager != null)
        {
            oauthManager.OnAuthSuccess -= HandleAuthSuccess;
            oauthManager.OnAuthError -= HandleAuthError;
        }
        
        if (testButton != null)
        {
            testButton.onClick.RemoveListener(StartOAuthTest);
        }
    }
    
    /// <summary>
    /// Starts the OAuth test flow
    /// </summary>
    public void StartOAuthTest()
    {
        UpdateStatus("Opening browser for Bluesky authentication...\nPlease complete login in your browser.");
        
        // Start the OAuth flow - this will open the browser
        oauthManager.StartOAuthFlow();
    }
    
    /// <summary>
    /// Handles successful authentication - displays all the auth info
    /// </summary>
    private void HandleAuthSuccess(string accessToken)
    {
        Debug.Log("=== AUTHENTICATION SUCCESS ===");
        Debug.Log($"Access Token: {accessToken}");
        
        string refreshToken = oauthManager.GetRefreshToken();
        if (!string.IsNullOrEmpty(refreshToken))
        {
            Debug.Log($"Refresh Token: {refreshToken}");
        }
        
        // Display auth info in UI
        string displayInfo = "🎉 AUTHENTICATION SUCCESSFUL!\n\n";
        displayInfo += $"Access Token:\n{FormatTokenForDisplay(accessToken)}\n\n";
        
        if (!string.IsNullOrEmpty(refreshToken))
        {
            displayInfo += $"Refresh Token:\n{FormatTokenForDisplay(refreshToken)}\n\n";
        }
        
        displayInfo += "Check Unity Console for full token details.";
        
        UpdateStatus(displayInfo);
    }
    
    /// <summary>
    /// Handles authentication errors
    /// </summary>
    private void HandleAuthError(string errorMessage)
    {
        Debug.LogError($"Authentication failed: {errorMessage}");
        UpdateStatus($"❌ AUTHENTICATION FAILED\n\n{errorMessage}\n\nCheck Unity Console for details.");
    }
    
    /// <summary>
    /// Updates the status display
    /// </summary>
    private void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log($"[OAuth Test] {message}");
    }
    
    /// <summary>
    /// Formats tokens for display (shows first/last characters)
    /// </summary>
    private string FormatTokenForDisplay(string token)
    {
        if (string.IsNullOrEmpty(token)) return "None";
        
        if (token.Length <= 20)
        {
            return token; // Show short tokens completely
        }
        
        // Show first 10 and last 10 characters with ... in middle
        return $"{token.Substring(0, 10)}...{token.Substring(token.Length - 10)}";
    }
}