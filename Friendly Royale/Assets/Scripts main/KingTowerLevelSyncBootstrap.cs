using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Ensures a KingTowerLevelSync networked object exists so we can exchange king levels between host and client.
/// </summary>
public static class KingTowerLevelSyncBootstrap
{
    private static bool subscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Ensure()
    {
        // Skip entirely in offline/practice mode
        try
        {
            if (GameModeManager.Instance != null && GameModeManager.Instance.IsOfflineMode())
            {
                return;
            }
        }
        catch { /* ignore */ }

        if (NetworkManager.Singleton == null)
            return;

        if (!NetworkManager.Singleton.IsListening)
        {
            if (!subscribed)
            {
                NetworkManager.Singleton.OnServerStarted += OnServerStarted;
                subscribed = true;
            }
            return;
        }

        if (!NetworkManager.Singleton.IsServer)
            return;

        if (Object.FindFirstObjectByType<KingTowerLevelSync>() != null)
            return;

        var go = new GameObject("KingTowerLevelSync_AUTO");
        var no = go.AddComponent<NetworkObject>();
        go.AddComponent<KingTowerLevelSync>();
        Object.DontDestroyOnLoad(go);
        no.Spawn();
        Debug.Log("[KingTowerLevelSyncBootstrap] Spawned KingTowerLevelSync_AUTO");
    }

    private static void OnServerStarted()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        var existing = Object.FindFirstObjectByType<KingTowerLevelSync>();
        if (existing != null)
        {
            var no = existing.GetComponent<NetworkObject>();
            if (no != null && !no.IsSpawned)
            {
                Object.DontDestroyOnLoad(existing.gameObject);
                no.Spawn();
                Debug.Log("[KingTowerLevelSyncBootstrap] Spawned existing KingTowerLevelSync on server start.");
            }
            return;
        }

        var go = new GameObject("KingTowerLevelSync_AUTO");
        var netObj = go.AddComponent<NetworkObject>();
        go.AddComponent<KingTowerLevelSync>();
        Object.DontDestroyOnLoad(go);
        netObj.Spawn();
        Debug.Log("[KingTowerLevelSyncBootstrap] Spawned KingTowerLevelSync_AUTO on server start.");
    }
}
