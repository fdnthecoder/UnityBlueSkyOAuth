using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// A simple OAuth client for Bluesky authentication in Unity with PKCE support
/// </summary>
public class BlueskyOAuthManager : MonoBehaviour
{
    // Configuration for your OAuth client
    [Header("OAuth Configuration")]
    [SerializeField] private string clientId = "https://pub-ab450520482b446490a31a100e39cd5e.r2.dev/client-metadata.json";
    [SerializeField] private string redirectUri = "https://pub-ab450520482b446490a31a100e39cd5e.r2.dev/callback.html"; 
    [SerializeField] private string scope = "atproto";
    [SerializeField] private int localServerPort = 8080;
    
    // Bluesky server configuration
    [Header("Bluesky Configuration")]
    [SerializeField] private string bskyServiceUrl = "https://bsky.social";
    
    // OAuth server endpoints (discovered dynamically)
    private string authorizationEndpoint;
    private string tokenEndpoint;
    private string parEndpoint; // Pushed Authorization Request endpoint
    
    // State to prevent CSRF attacks
    private string state;
    
    // PKCE parameters
    private string codeVerifier;
    private string codeChallenge;
    
    // OAuth tokens
    private string accessToken;
    private string refreshToken;
    
    // Local HTTP server for handling the redirect
    private HttpListener httpListener;
    private bool isServerRunning = false;
    
    // Events
    public event Action<string> OnAuthSuccess;
    public event Action<string> OnAuthError;
    
    private void Start()
    {
        // Generate a random state value to prevent CSRF attacks
        state = Guid.NewGuid().ToString();
        
        // Generate PKCE parameters
        GeneratePKCEParameters();
    }
    
    private void OnDestroy()
    {
        StopLocalServer();
    }
    
    /// <summary>
    /// Generates PKCE code verifier and challenge
    /// </summary>
    private void GeneratePKCEParameters()
    {
        // Generate code verifier (43-128 characters, URL-safe)
        byte[] randomBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }
        codeVerifier = Convert.ToBase64String(randomBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        
        // Generate code challenge using S256 method
        using (var sha256 = SHA256.Create())
        {
            byte[] challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
            codeChallenge = Convert.ToBase64String(challengeBytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
        
        Debug.Log("[BlueskyAuth] PKCE parameters generated successfully");
    }
    
    /// <summary>
    /// Starts the OAuth flow by discovering endpoints and opening the authorization URL
    /// </summary>
    public void StartOAuthFlow()
    {
        Debug.Log("[BlueskyAuth] Starting OAuth flow...");
        StartCoroutine(DiscoverOAuthEndpoints());
    }
    
    /// <summary>
    /// Discovers OAuth endpoints from the Bluesky server
    /// </summary>
    private IEnumerator DiscoverOAuthEndpoints()
    {
        Debug.Log("[BlueskyAuth] Discovering OAuth endpoints...");
        
        // First, try to get the authorization server metadata
        string metadataUrl = $"{bskyServiceUrl}/.well-known/oauth-authorization-server";
        
        using (UnityWebRequest request = UnityWebRequest.Get(metadataUrl))
        {
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                string response = request.downloadHandler.text;
                
                // Parse the JSON to extract endpoints
                if (TryParseOAuthMetadata(response))
                {
                    Debug.Log("[BlueskyAuth] OAuth endpoints discovered successfully");
                    
                    // Check if PAR is required
                    if (!string.IsNullOrEmpty(parEndpoint))
                    {
                        StartCoroutine(UsePushedAuthorizationRequest());
                    }
                    else
                    {
                        OpenAuthorizationUrl();
                    }
                }
                else
                {
                    Debug.LogError("[BlueskyAuth] Failed to parse OAuth metadata");
                    OnAuthError?.Invoke("Failed to parse OAuth metadata");
                }
            }
            else
            {
                Debug.LogError($"[BlueskyAuth] Failed to discover OAuth endpoints: {request.error}");
                Debug.LogError($"[BlueskyAuth] Response Code: {request.responseCode}");
                Debug.LogError($"[BlueskyAuth] Response Body: {request.downloadHandler?.text}");
                OnAuthError?.Invoke($"Failed to discover OAuth endpoints: {request.error}");
            }
        }
    }
    
    /// <summary>
    /// Tries to parse OAuth metadata from JSON response
    /// </summary>
    private bool TryParseOAuthMetadata(string json)
    {
        try
        {
            // Simple JSON parsing for the endpoints we need
            // In a production app, you'd want to use a proper JSON parser
            
            // Look for authorization_endpoint
            string authKey = "\"authorization_endpoint\":\"";
            int authStart = json.IndexOf(authKey);
            if (authStart >= 0)
            {
                authStart += authKey.Length;
                int authEnd = json.IndexOf("\"", authStart);
                if (authEnd > authStart)
                {
                    authorizationEndpoint = json.Substring(authStart, authEnd - authStart);
                }
            }
            
            // Look for token_endpoint
            string tokenKey = "\"token_endpoint\":\"";
            int tokenStart = json.IndexOf(tokenKey);
            if (tokenStart >= 0)
            {
                tokenStart += tokenKey.Length;
                int tokenEnd = json.IndexOf("\"", tokenStart);
                if (tokenEnd > tokenStart)
                {
                    tokenEndpoint = json.Substring(tokenStart, tokenEnd - tokenStart);
                }
            }
            
            // Look for pushed_authorization_request_endpoint
            string parKey = "\"pushed_authorization_request_endpoint\":\"";
            int parStart = json.IndexOf(parKey);
            if (parStart >= 0)
            {
                parStart += parKey.Length;
                int parEnd = json.IndexOf("\"", parStart);
                if (parEnd > parStart)
                {
                    parEndpoint = json.Substring(parStart, parEnd - parStart);
                }
            }
            
            return !string.IsNullOrEmpty(authorizationEndpoint) && !string.IsNullOrEmpty(tokenEndpoint);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BlueskyAuth] Error parsing OAuth metadata: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Uses Pushed Authorization Request (PAR) if required by the server
    /// </summary>
    private IEnumerator UsePushedAuthorizationRequest()
    {
        Debug.Log("[BlueskyAuth] Using Pushed Authorization Request...");
        
        // Prepare the PAR request form data
        WWWForm form = new WWWForm();
        form.AddField("client_id", clientId);
        form.AddField("redirect_uri", redirectUri);
        form.AddField("response_type", "code");
        form.AddField("scope", scope);
        form.AddField("state", state);
        form.AddField("code_challenge", codeChallenge);
        form.AddField("code_challenge_method", "S256");
        
        using (UnityWebRequest request = UnityWebRequest.Post(parEndpoint, form))
        {
            request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                string response = request.downloadHandler.text;
                
                // Parse the request_uri from the response
                string requestUri = ExtractRequestUri(response);
                if (!string.IsNullOrEmpty(requestUri))
                {
                    OpenAuthorizationUrlWithRequestUri(requestUri);
                }
                else
                {
                    Debug.LogError("[BlueskyAuth] Failed to extract request_uri from PAR response");
                    OnAuthError?.Invoke("Failed to extract request_uri from PAR response");
                }
            }
            else
            {
                Debug.LogError($"[BlueskyAuth] PAR request failed: {request.error}");
                Debug.LogError($"[BlueskyAuth] Response Code: {request.responseCode}");
                Debug.LogError($"[BlueskyAuth] Response Body: {request.downloadHandler?.text}");
                OnAuthError?.Invoke($"PAR request failed: {request.error}");
            }
        }
    }
    
    /// <summary>
    /// Extracts request_uri from PAR response
    /// </summary>
    private string ExtractRequestUri(string json)
    {
        try
        {
            string key = "\"request_uri\":\"";
            int start = json.IndexOf(key);
            if (start >= 0)
            {
                start += key.Length;
                int end = json.IndexOf("\"", start);
                if (end > start)
                {
                    return json.Substring(start, end - start);
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BlueskyAuth] Error extracting request_uri: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Opens authorization URL with request_uri (for PAR)
    /// </summary>
    private void OpenAuthorizationUrlWithRequestUri(string requestUri)
    {
        Debug.Log("[BlueskyAuth] Opening authorization URL...");
        
        string authorizationUrl = $"{authorizationEndpoint}?" +
            $"client_id={Uri.EscapeDataString(clientId)}" +
            $"&request_uri={Uri.EscapeDataString(requestUri)}";
        
        try 
        {
            Application.OpenURL(authorizationUrl);
            Debug.Log("[BlueskyAuth] Authorization URL opened in browser");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BlueskyAuth] Failed to open authorization URL: {ex.Message}");
            OnAuthError?.Invoke($"Failed to open authorization URL: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Opens the standard authorization URL (fallback when PAR is not used)
    /// </summary>
    private void OpenAuthorizationUrl()
    {
        Debug.Log("[BlueskyAuth] Opening authorization URL...");

        string authorizationUrl = $"{authorizationEndpoint}?" +
            $"client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&response_type=code" +
            $"&scope={Uri.EscapeDataString(scope)}" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
            $"&code_challenge_method=S256";
        
        try 
        {
            Application.OpenURL(authorizationUrl);
            Debug.Log("[BlueskyAuth] Authorization URL opened in browser");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BlueskyAuth] Failed to open authorization URL: {ex.Message}");
            OnAuthError?.Invoke($"Failed to open authorization URL: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Starts a local HTTP server to handle the OAuth redirect
    /// </summary>
    private void StartLocalServer()
    {
        if (isServerRunning)
            return;
            
        try
        {
            httpListener = new HttpListener();
            httpListener.Prefixes.Add($"http://localhost:{localServerPort}/");
            httpListener.Start();
            isServerRunning = true;
            
            Debug.Log("Local server started on port " + localServerPort);
            
            // Start listening for connections in a separate thread
            Task.Run(() => ListenForCallbacks());
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to start local server: " + ex.Message);
            OnAuthError?.Invoke("Failed to start local server: " + ex.Message);
        }
    }
    
    /// <summary>
    /// Listens for callbacks from the authorization server
    /// </summary>
    private async void ListenForCallbacks()
    {
        try
        {
            while (isServerRunning)
            {
                var context = await httpListener.GetContextAsync();
                ProcessCallback(context);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Error in local server: " + ex.Message);
            StopLocalServer();
        }
    }
    
    /// <summary>
    /// Processes the callback received from the authorization server
    /// </summary>
    private void ProcessCallback(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;
            
            // Get the path - handle both client-metadata.json and callback requests
            string path = request.Url.AbsolutePath;
            Debug.Log("Received request for: " + path);
            
            if (path == "/client-metadata.json")
            {
                // Serve client metadata
                ServeClientMetadata(response);
            }
            else if (path == "/callback")
            {
                // Handle OAuth callback
                HandleOAuthCallback(request, response);
            }
            else
            {
                // Serve 404 for any other request
                response.StatusCode = 404;
                string error = "Not found";
                byte[] buffer = Encoding.UTF8.GetBytes(error);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            
            response.Close();
        }
        catch (Exception ex)
        {
            Debug.LogError("Error processing callback: " + ex.Message);
        }
    }
    
    /// <summary>
    /// Serves the client metadata JSON when requested
    /// </summary>
    private void ServeClientMetadata(HttpListenerResponse response)
    {
        string clientMetadata = @"{
            ""client_id"": """ + clientId + @""",
            ""client_name"": ""Unity Bluesky OAuth Test"",
            ""client_uri"": ""http://localhost:" + localServerPort + @""",
            ""redirect_uris"": [""" + redirectUri + @"""],
            ""scope"": """ + scope + @""",
            ""grant_types"": [""authorization_code"", ""refresh_token""],
            ""response_types"": [""code""],
            ""token_endpoint_auth_method"": ""none"",
            ""application_type"": ""web"",
            ""dpop_bound_access_tokens"": true
        }";
        
        byte[] buffer = Encoding.UTF8.GetBytes(clientMetadata);
        response.ContentType = "application/json";
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        
        Debug.Log("Served client metadata");
    }
    
    /// <summary>
    /// Handles the OAuth callback from the authorization server
    /// </summary>
    private void HandleOAuthCallback(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            // Check for error parameter
            string error = request.QueryString["error"];
            if (!string.IsNullOrEmpty(error))
            {
                string errorDescription = request.QueryString["error_description"] ?? "Unknown error";
                Debug.LogError($"OAuth error: {error} - {errorDescription}");
                
                // Send error page to browser
                SendResponsePage(response, false, errorDescription);
                
                // Invoke error event on main thread
                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                {
                    OnAuthError?.Invoke($"{error}: {errorDescription}");
                });
                
                return;
            }
            
            // Get the authorization code
            string code = request.QueryString["code"];
            string returnedState = request.QueryString["state"];
            
            // Verify state to prevent CSRF attacks
            if (returnedState != state)
            {
                Debug.LogError("OAuth state mismatch! Possible CSRF attack");
                SendResponsePage(response, false, "Security error: state mismatch");
                
                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                {
                    OnAuthError?.Invoke("Security error: state mismatch");
                });
                
                return;
            }
            
            // Send success page to browser
            SendResponsePage(response, true);
            Debug.Log($"[BlueskyAuth] Authorization code received: {code}");
            
            // Exchange the authorization code for tokens
            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                StartCoroutine(ExchangeCodeForTokens(code));
            });
        }
        catch (Exception ex)
        {
            Debug.LogError("Error handling OAuth callback: " + ex.Message);
            SendResponsePage(response, false, "Internal error: " + ex.Message);
            
            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                OnAuthError?.Invoke("Error handling OAuth callback: " + ex.Message);
            });
        }
    }
    
    /// <summary>
    /// Sends an HTML response page to the browser
    /// </summary>
    private void SendResponsePage(HttpListenerResponse response, bool success, string message = null)
    {
        string html;
        
        if (success)
        {
            html = @"<!DOCTYPE html>
<html>
<head>
    <title>Authentication Successful</title>
    <style>
        body { font-family: Arial, sans-serif; text-align: center; margin-top: 50px; }
        .success { color: green; }
        .container { max-width: 500px; margin: 0 auto; padding: 20px; border: 1px solid #ddd; border-radius: 5px; }
    </style>
</head>
<body>
    <div class='container'>
        <h1 class='success'>Authentication Successful!</h1>
        <p>You have successfully authenticated with Bluesky.</p>
        <p>You can close this window and return to the Unity application.</p>
    </div>
</body>
</html>";
        }
        else
        {
            html = @"<!DOCTYPE html>
<html>
<head>
    <title>Authentication Failed</title>
    <style>
        body { font-family: Arial, sans-serif; text-align: center; margin-top: 50px; }
        .error { color: red; }
        .container { max-width: 500px; margin: 0 auto; padding: 20px; border: 1px solid #ddd; border-radius: 5px; }
    </style>
</head>
<body>
    <div class='container'>
        <h1 class='error'>Authentication Failed</h1>
        <p>" + (message ?? "An error occurred during authentication.") + @"</p>
        <p>You can close this window and return to the Unity application.</p>
    </div>
</body>
</html>";
        }
        
        byte[] buffer = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html";
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
    }
    
    /// <summary>
    /// Stops the local HTTP server
    /// </summary>
    private void StopLocalServer()
    {
        if (!isServerRunning)
            return;
            
        try
        {
            httpListener.Stop();
            isServerRunning = false;
            Debug.Log("Local server stopped");
        }
        catch (Exception ex)
        {
            Debug.LogError("Error stopping local server: " + ex.Message);
        }
    }
    
    /// <summary>
    /// Exchanges the authorization code for access and refresh tokens
    /// </summary>
    private IEnumerator ExchangeCodeForTokens(string code)
    {
        Debug.Log("[BlueskyAuth] Starting token exchange...");
        
        // Use the discovered token endpoint or fallback
        string endpoint = !string.IsNullOrEmpty(tokenEndpoint) ? tokenEndpoint : $"{bskyServiceUrl}/oauth/token";
        Debug.Log($"[BlueskyAuth] Token endpoint: {endpoint}");

        // Prepare the token request form data with PKCE
        WWWForm form = new WWWForm();
        form.AddField("grant_type", "authorization_code");
        form.AddField("code", code);
        form.AddField("redirect_uri", redirectUri);
        form.AddField("client_id", clientId);
        form.AddField("code_verifier", codeVerifier); // PKCE parameter

        Debug.Log("[BlueskyAuth] Sending token exchange request with PKCE...");
        
        using (UnityWebRequest request = UnityWebRequest.Post(endpoint, form))
        {
            // Set content type for OAuth token request
            request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                string response = request.downloadHandler.text;
                Debug.Log($"[BlueskyAuth] Token exchange successful. Response: {response}");
                
                // Parse the JSON response to extract tokens
                if (TryParseTokenResponse(response))
                {
                    Debug.Log("[BlueskyAuth] Tokens parsed and stored successfully");
                    OnAuthSuccess?.Invoke(accessToken);
                    
                    // Test the access token with a simple API call
                    Debug.Log("[BlueskyAuth] Making test API call...");
                    StartCoroutine(TestApiCall());
                }
                else
                {
                    Debug.LogError("[BlueskyAuth] Failed to parse token response");
                    OnAuthError?.Invoke("Failed to parse token response");
                }
            }
            else
            {
                Debug.LogError($"[BlueskyAuth] Token exchange failed: {request.error}");
                Debug.LogError($"[BlueskyAuth] Response Code: {request.responseCode}");
                Debug.LogError($"[BlueskyAuth] Response Body: {request.downloadHandler?.text}");
                OnAuthError?.Invoke($"Token exchange failed: {request.error}");
            }
        }
    }
    
    /// <summary>
    /// Tries to parse the token response JSON
    /// </summary>
    private bool TryParseTokenResponse(string json)
    {
        try
        {
            // Simple JSON parsing for the tokens we need
            // In a production app, you'd want to use a proper JSON parser
            
            // Look for access_token
            string accessKey = "\"access_token\":\"";
            int accessStart = json.IndexOf(accessKey);
            if (accessStart >= 0)
            {
                accessStart += accessKey.Length;
                int accessEnd = json.IndexOf("\"", accessStart);
                if (accessEnd > accessStart)
                {
                    accessToken = json.Substring(accessStart, accessEnd - accessStart);
                }
            }
            
            // Look for refresh_token
            string refreshKey = "\"refresh_token\":\"";
            int refreshStart = json.IndexOf(refreshKey);
            if (refreshStart >= 0)
            {
                refreshStart += refreshKey.Length;
                int refreshEnd = json.IndexOf("\"", refreshStart);
                if (refreshEnd > refreshStart)
                {
                    refreshToken = json.Substring(refreshStart, refreshEnd - refreshStart);
                }
            }
            
            return !string.IsNullOrEmpty(accessToken);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BlueskyAuth] Error parsing token response: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Makes a test API call to verify the access token
    /// </summary>
    private IEnumerator TestApiCall()
    {
        Debug.Log("[BlueskyAuth] Starting test API call...");
        string apiEndpoint = $"{bskyServiceUrl}/xrpc/app.bsky.actor.getProfile?actor=self";
        Debug.Log($"[BlueskyAuth] Test API endpoint: {apiEndpoint}");

        using (UnityWebRequest request = UnityWebRequest.Get(apiEndpoint))
        {
            request.SetRequestHeader("Authorization", "Bearer " + accessToken);
            Debug.Log("[BlueskyAuth] Set Authorization header");

            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                string response = request.downloadHandler.text;
                Debug.Log($"[BlueskyAuth] Test API call successful. User profile: {response}");
            }
            else
            {
                Debug.LogError($"[BlueskyAuth] Test API call failed: {request.error}");
                Debug.LogError($"[BlueskyAuth] Response Code: {request.responseCode}");
                Debug.LogError($"[BlueskyAuth] Response Body: {request.downloadHandler?.text}");
            }
        }
    }
    
    /// <summary>
    /// Gets the current access token (if available)
    /// </summary>
    public string GetAccessToken()
    {
        return accessToken;
    }
    
    /// <summary>
    /// Gets the current refresh token (if available)
    /// </summary>
    public string GetRefreshToken()
    {
        return refreshToken;
    }
    
    /// <summary>
    /// Checks if the user is currently authenticated
    /// </summary>
    public bool IsAuthenticated()
    {
        return !string.IsNullOrEmpty(accessToken);
    }
}