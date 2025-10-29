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

    [Header("Dev HUD (optional)")]
    [Tooltip("Create a lightweight dev HUD in Editor/Development builds for start/stop.")]
    public bool addDevHudInDevBuilds = true;

    void Awake()
    {
        // Do nothing for practice/offline mode
        try
        {
            if (GameModeManager.Instance != null && GameModeManager.Instance.IsOfflineMode())
            {
                return;
            }
        }
        catch { }

        if (ensureNetworkManager)
        {
            EnsureNetworkManager();
        }
    }

    void Start()
    {
        // After Awake so any existing managers created elsewhere can initialize first
        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            return;
        }

        if (addDevHudInDevBuilds)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (FindAnyObjectByType<NetworkHUDDev>() == null)
            {
                var hudGo = new GameObject("NetworkHUD_DEV");
                hudGo.AddComponent<NetworkHUDDev>();
                DontDestroyOnLoad(hudGo);
            }
#endif
        }

        if (autoStartIfNotListening && !nm.IsListening && startMode != StartMode.None)
        {
            TryAutoStart(startMode);
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
