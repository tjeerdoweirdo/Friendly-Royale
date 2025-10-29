using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

/// <summary>
/// Drop this in battle scenes. It ensures a NetworkManager (and optional helpers) exist
/// and can optionally auto-start networking if it's not already running.
/// It is a thin wrapper around Unity's NetworkManager — it will not create a duplicate if one exists.
/// </summary>
[DisallowMultipleComponent]
public class BattleNetworkAutoManager : MonoBehaviour
{
    [Header("Ensure Components")]
    [Tooltip("Make sure a NetworkManager exists (create one if missing) and mark it DontDestroyOnLoad.")]
    public bool ensureNetworkManager = true;

    [Tooltip("Make sure NetworkGameManager exists and is spawned when server is running.")]
    public bool ensureNetworkGameManager = true;

    [Tooltip("Make sure NetworkCardPlacementSystem exists and is spawned when server is running.")]
    public bool ensurePlacementSystem = true;

    [Header("Auto Start (optional)")]
    [Tooltip("If true, will start Host/Client/Server only when Netcode isn't listening yet.")]
    public bool autoStartIfNotListening = false;

    public enum StartMode { None, Host, Client, Server }
    [Tooltip("How to start if not listening. Prefer None when scenes are loaded by a lobby/matchmaker.")]
    public StartMode startMode = StartMode.None;

    [Tooltip("If startMode=None and this is enabled, auto-start by saved player side: Player1=Host, Player2=Client.")]
    public bool startBySavedPlayerSideIfNotListening = true;

    [Header("Dev HUD (optional)")]
    [Tooltip("Create a lightweight dev HUD in Editor/Development builds for start/stop.")]
    public bool addDevHudInDevBuilds = true;

    void Awake()
    {
        if (ensureNetworkManager)
        {
            EnsureNetworkManager();
        }
    }

    void Start()
    {
        // Add the HUD first (independent of build type) if requested
        if (addDevHudInDevBuilds && FindAnyObjectByType<NetworkHUDDev>() == null)
        {
            var hudGo = new GameObject("NetworkHUD_DEV");
            hudGo.AddComponent<NetworkHUDDev>();
            DontDestroyOnLoad(hudGo);
        }

        // After Awake so any existing managers created elsewhere can initialize first
        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            Debug.LogWarning("[BattleNetworkAutoManager] No NetworkManager after Awake. HUD created; waiting for external network init.");
            return;
        }

        // Ensure UnityTransport exists on the active NetworkManager
        if (nm.GetComponent<UnityTransport>() == null)
        {
            nm.gameObject.AddComponent<UnityTransport>();
        }
        // Safety: if connection approval is enabled but no callback is set, disable approval to avoid start failures
        try
        {
            if (nm.NetworkConfig.ConnectionApproval && nm.ConnectionApprovalCallback == null)
            {
                nm.NetworkConfig.ConnectionApproval = false;
                Debug.Log("[BattleNetworkAutoManager] Disabled ConnectionApproval (no callback) to allow auto-start.");
            }
        }
        catch { }

        // Check if matchmaking requested auto network start (set in PlayerPrefs by matchmaking flow)
        int autoFlag = 0;
        try { autoFlag = PlayerPrefs.GetInt("AutoNetworkStart", 0); } catch { autoFlag = 0; }

        // Don't auto-start in practice/offline unless explicitly asked via AutoNetworkStart
        bool isOffline = false;
        try { isOffline = (GameModeManager.Instance != null && GameModeManager.Instance.IsOfflineMode()); } catch { isOffline = false; }
        if (isOffline && autoFlag == 0)
        {
            return;
        }

        if (autoStartIfNotListening && !nm.IsListening && startMode != StartMode.None)
        {
            TryAutoStart(startMode);
        }
        else if (!nm.IsListening && startMode == StartMode.None && startBySavedPlayerSideIfNotListening)
        {
            // Decide host/client by saved player side (set by matchmaking before scene load)
            int side = PlayerPrefs.GetInt("LocalPlayerIsPlayer1", 1);
            if (side == 1)
            {
                Debug.Log("[BattleNetworkAutoManager] Auto-starting as Host (Player1)");
                TryAutoStart(StartMode.Host);
            }
            else
            {
                Debug.Log("[BattleNetworkAutoManager] Auto-starting as Client (Player2)");
                TryAutoStart(StartMode.Client);
            }
        }

        // Correct role if another script started server-only but we need Host
        if (autoFlag == 1)
        {
            int sideForAuto = 1;
            try { sideForAuto = PlayerPrefs.GetInt("LocalPlayerIsPlayer1", 1); } catch { sideForAuto = 1; }
            if (sideForAuto == 1)
            {
                if (nm.IsServer && !nm.IsClient)
                {
                    Debug.LogWarning("[BattleNetworkAutoManager] Detected Server-only but Host required. Restarting as Host...");
                    StartCoroutine(RestartAsHost());
                }
                else if (!nm.IsListening && !nm.IsServer && !nm.IsClient)
                {
                    TryAutoStart(StartMode.Host);
                }
            }
            else
            {
                if (!nm.IsListening && !nm.IsServer && !nm.IsClient)
                {
                    TryAutoStart(StartMode.Client);
                }
            }
            // If auto-start was requested but we're still not listening, retry shortly (handles race conditions)
            if (!nm.IsListening)
            {
                StartCoroutine(RetryAutoStartBySide());
            }
        }

        // Clear the auto-start flag so subsequent scenes don't accidentally auto-start
        if (autoFlag == 1)
        {
            try { PlayerPrefs.SetInt("AutoNetworkStart", 0); PlayerPrefs.Save(); } catch { }
        }

        // If we're already listening and are server/host, ensure helpers
        if (nm.IsListening && (nm.IsServer || nm.IsHost))
        {
            if (ensureNetworkGameManager)
            {
                EnsureNetworkGameManager();
            }
            if (ensurePlacementSystem)
            {
                EnsurePlacementSystem();
            }
        }
        else
        {
            // Subscribe to server started to spawn helpers later
            nm.OnServerStarted += OnServerStarted;
        }
    }

    private void OnServerStarted()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !(nm.IsServer || nm.IsHost)) return;
        if (ensureNetworkGameManager) EnsureNetworkGameManager();
        if (ensurePlacementSystem) EnsurePlacementSystem();
        // Unsubscribe after first run
        nm.OnServerStarted -= OnServerStarted;
    }

    private void EnsureNetworkManager()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            var go = new GameObject("NetworkManager_AUTO");
            nm = go.AddComponent<NetworkManager>();
            if (go.GetComponent<UnityTransport>() == null)
            {
                go.AddComponent<UnityTransport>();
            }
            DontDestroyOnLoad(go);
            Debug.Log("[BattleNetworkAutoManager] Created NetworkManager_AUTO");
        }
        else
        {
            DontDestroyOnLoad(nm.gameObject);
        }
    }

    private void TryAutoStart(StartMode mode)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        switch (mode)
        {
            case StartMode.Host:
                nm.StartHost();
                break;
            case StartMode.Client:
                nm.StartClient();
                break;
            case StartMode.Server:
                nm.StartServer();
                break;
        }
    }

    private void EnsureNetworkGameManager()
    {
        var existing = FindAnyObjectByType<NetworkGameManager>();
        if (existing != null)
        {
            var no = existing.GetComponent<NetworkObject>();
            if (no == null) no = existing.gameObject.AddComponent<NetworkObject>();
            if (!no.IsSpawned)
            {
                DontDestroyOnLoad(existing.gameObject);
                no.Spawn();
                Debug.Log("[BattleNetworkAutoManager] Spawned existing NetworkGameManager");
            }
            return;
        }

        var go = new GameObject("NetworkGameManager_AUTO");
        var netObj = go.AddComponent<NetworkObject>();
        go.AddComponent<NetworkGameManager>();
        DontDestroyOnLoad(go);
        netObj.Spawn();
        Debug.Log("[BattleNetworkAutoManager] Spawned NetworkGameManager_AUTO");
    }

    private void EnsurePlacementSystem()
    {
        var existing = FindAnyObjectByType<NetworkCardPlacementSystem>();
        if (existing != null)
        {
            var no = existing.GetComponent<NetworkObject>();
            if (no == null) no = existing.gameObject.AddComponent<NetworkObject>();
            if (!no.IsSpawned)
            {
                DontDestroyOnLoad(existing.gameObject);
                no.Spawn();
                Debug.Log("[BattleNetworkAutoManager] Spawned existing NetworkCardPlacementSystem");
            }
            return;
        }

        var go = new GameObject("NetworkCardPlacementSystem_AUTO");
        var netObj = go.AddComponent<NetworkObject>();
        go.AddComponent<NetworkCardPlacementSystem>();
        DontDestroyOnLoad(go);
        netObj.Spawn();
        Debug.Log("[BattleNetworkAutoManager] Spawned NetworkCardPlacementSystem_AUTO");
    }

    private System.Collections.IEnumerator RetryAutoStartBySide()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) yield break;
        yield return null; // wait one frame
        if (nm.IsListening) yield break;
        // Ensure transport and config again
        if (nm.GetComponent<UnityTransport>() == null)
        {
            nm.gameObject.AddComponent<UnityTransport>();
        }
        try
        {
            if (nm.NetworkConfig.ConnectionApproval && nm.ConnectionApprovalCallback == null)
            {
                nm.NetworkConfig.ConnectionApproval = false;
            }
        }
        catch { }
        int side = 1;
        try { side = PlayerPrefs.GetInt("LocalPlayerIsPlayer1", 1); } catch { side = 1; }
        if (side == 1)
        {
            Debug.Log("[BattleNetworkAutoManager] Retry: StartHost()");
            nm.StartHost();
        }
        else
        {
            Debug.Log("[BattleNetworkAutoManager] Retry: StartClient()");
            nm.StartClient();
        }
    }

    private System.Collections.IEnumerator RestartAsHost()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) yield break;
        // Gracefully stop server-only, wait a frame, then start host
        nm.Shutdown();
        yield return null;
        // Ensure transport and approval settings
        if (nm.GetComponent<UnityTransport>() == null)
        {
            nm.gameObject.AddComponent<UnityTransport>();
        }
        try
        {
            if (nm.NetworkConfig.ConnectionApproval && nm.ConnectionApprovalCallback == null)
            {
                nm.NetworkConfig.ConnectionApproval = false;
            }
        }
        catch { }
        Debug.Log("[BattleNetworkAutoManager] Starting Host after server-only shutdown...");
        nm.StartHost();
    }

    // Convenience context menu for quick testing in Editor
#if UNITY_EDITOR
    [ContextMenu("Start Host (If Not Listening)")]
    private void CtxStartHost()
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.StartHost();
    }

    [ContextMenu("Start Client (If Not Listening)")]
    private void CtxStartClient()
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.StartClient();
    }

    [ContextMenu("Shutdown Network")]
    private void CtxShutdown()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
    }
#endif
}
