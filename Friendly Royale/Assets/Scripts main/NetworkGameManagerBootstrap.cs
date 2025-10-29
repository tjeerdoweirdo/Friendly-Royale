using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Ensures a networked NetworkGameManager exists during online play.
/// Server-only spawn; persists across scenes. No-op in offline/practice.
/// </summary>
public static class NetworkGameManagerBootstrap
{
    private static bool s_subscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Ensure()
    {
        // Skip in offline/practice mode
        try
        {
            if (GameModeManager.Instance != null && GameModeManager.Instance.IsOfflineMode())
                return;
        }
        catch { }

        if (NetworkManager.Singleton == null)
            return;

        if (!NetworkManager.Singleton.IsListening)
        {
            if (!s_subscribed)
            {
                NetworkManager.Singleton.OnServerStarted += OnServerStarted;
                s_subscribed = true;
            }
            return;
        }

        if (!NetworkManager.Singleton.IsServer)
            return;

        EnsureSpawned();
    }

    private static void OnServerStarted()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        EnsureSpawned();
    }

    private static void EnsureSpawned()
    {
        var existing = Object.FindFirstObjectByType<NetworkGameManager>();
        if (existing != null)
        {
            var no = existing.GetComponent<NetworkObject>();
            if (no == null)
            {
                no = existing.gameObject.AddComponent<NetworkObject>();
            }
            if (!no.IsSpawned)
            {
                Object.DontDestroyOnLoad(existing.gameObject);
                no.Spawn();
                Debug.Log("[NetworkGameManagerBootstrap] Spawned existing NetworkGameManager.");
            }
            return;
        }

        var go = new GameObject("NetworkGameManager_AUTO");
        var netObj = go.AddComponent<NetworkObject>();
        go.AddComponent<NetworkGameManager>();
        Object.DontDestroyOnLoad(go);
        netObj.Spawn();
        Debug.Log("[NetworkGameManagerBootstrap] Spawned NetworkGameManager_AUTO");
    }
}
