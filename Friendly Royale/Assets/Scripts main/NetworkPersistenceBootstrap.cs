using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

/// <summary>
/// Ensures a NetworkManager exists and persists across scene loads.
/// If missing in an online session, creates a minimal NetworkManager with UnityTransport.
/// No-op in offline/practice mode.
/// </summary>
public static class NetworkPersistenceBootstrap
{
    private static bool s_initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureNetworkManager()
    {
        if (s_initialized) return;
        s_initialized = true;

        // Skip in offline/practice mode
        try
        {
            if (GameModeManager.Instance != null && GameModeManager.Instance.IsOfflineMode())
            {
                return;
            }
        }
        catch { }

        if (NetworkManager.Singleton != null)
        {
            Object.DontDestroyOnLoad(NetworkManager.Singleton.gameObject);
            return;
        }

        // Create a minimal NetworkManager if we're online but it's missing
        try
        {
            var go = new GameObject("NetworkManager_AUTO");
            var nm = go.AddComponent<NetworkManager>();
            var utp = go.AddComponent<UnityTransport>();
            // Optionally tweak default transport settings here
            Object.DontDestroyOnLoad(go);
            Debug.Log("[NetworkPersistenceBootstrap] Created minimal NetworkManager (AUTO)");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[NetworkPersistenceBootstrap] Failed to create NetworkManager: {ex.Message}");
        }
    }
}
