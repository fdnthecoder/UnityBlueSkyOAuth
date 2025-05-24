# Bluesky OAuth for Unity

A Unity-based OAuth 2.0 implementation for authenticating users with [Bluesky](https://bsky.social). This project enables Unity mobile apps (such as iOS builds) to initiate a secure authentication flow, receive access tokens, and subsequently open or interact with a user's Bluesky account.

> 🚧 **This project is under active development.**

---

## 🔑 Features

* Full OAuth 2.0 flow with PKCE (Proof Key for Code Exchange)
* Pushed Authorization Requests (PAR) support
* Secure handling of access and refresh tokens
* Local HTTP server for handling callback redirects
* Unity WebView fallback-compatible

---

## 🚀 Quick Setup in Unity

1. **Create a new Unity project** (or open your existing one).
2. **Add the scripts**:

   * Place `BlueskyOAuthManager.cs` and `BlueskyOAuthExample.cs` in your `Assets/Scripts` folder.
3. **Prepare a GameObject**:

   * In the Unity Editor, create an empty GameObject and name it `OAuthManager`.
   * Attach the `BlueskyOAuthManager` component to it.
4. **Create a UI Canvas**:

   * Add a `Button` and `Text` UI element.
   * Create a new GameObject and attach `BlueskyOAuthExample` to it.
   * Assign references to the `Button`, `Text`, and `OAuthManager` in the Inspector.
5. **Host the Metadata Files**:

   * Upload `callback.html` and `client-metadata.json` to a public web host (Cloudflare R2 or similar).
   * Update `clientId` and `redirectUri` in `BlueskyOAuthManager` accordingly.
6. **Play the Scene**:

   * Hit play in the Unity Editor, click the button, and follow the login flow in your browser.

---

## 📄 How It Works

1. **Metadata Discovery**: Retrieves the Bluesky OAuth endpoints.
2. **PKCE & State Setup**: Secures the request against replay and CSRF attacks.
3. **Pushed Authorization Request (optional)**: If required, registers a request and obtains a `request_uri`.
4. **User Login in Browser**: Auth URL opens with PKCE and state params.
5. **Callback Server**: A lightweight local HTTP server receives the redirect with `code`.
6. **Token Exchange**: Sends the code + `code_verifier` to get `access_token` and `refresh_token`.
7. **API Call (optional)**: Demonstrates using the token to call a protected Bluesky endpoint.

---

## 📱 iOS Considerations

* Bluesky requires PKCE for mobile clients.
* You may need to configure custom URL schemes and universal links.
* For WebView-free flows, this implementation relies on `Application.OpenURL()`.

---

## 🔐 Security

* Uses secure random generation for `state` and `code_verifier`.
* Supports token introspection and revocation (to be added).
* Minimal logging to avoid leaking tokens or PKCE values.

---

## 📦 File Structure

```
/Assets/
├── BlueskyOAuthManager.cs       # Core OAuth logic (PKCE, PAR, callbacks)
├── BlueskyOAuthExample.cs       # Sample UI and integration
├── callback.html                # Browser redirect landing page
└── client-metadata.json         # OAuth client registration metadata
```

---

## 🧪 Future Improvements

* Token refresh flow
* Encrypted token storage
* Support for Android/iOS deep linking
* Better error UI feedback

---

## 🧠 Author's Note

This codebase is part of an evolving system that will allow full Bluesky interactions (viewing posts, profiles, etc.) from a Unity app. If you're building a social client or just experimenting with identity on the fediverse, contributions and feedback are welcome!

---

## 📜 License

MIT License — Free to use and modify.

---

## 📫 Contact

Questions or ideas? Open an issue or reach out directly.
