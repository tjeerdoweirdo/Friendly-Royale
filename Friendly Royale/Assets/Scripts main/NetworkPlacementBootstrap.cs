using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Ensures a networked NetworkCardPlacementSystem exists at runtime so clients always have a valid RPC target.
/// Host/Server creates and spawns it once on startup if not present. Marked DontDestroyOnLoad.
/// </summary>
public static class NetworkPlacementBootstrap
{
    private static bool subscribedToServerStarted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsurePlacementSystem()
    {
        // Only the server/host should create and spawn the networked object
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        // If server not started yet, subscribe to spawn when it does
        if (!NetworkManager.Singleton.IsListening)
        {
            if (!subscribedToServerStarted)
            {
                NetworkManager.Singleton.OnServerStarted += OnServerStarted;
                subscribedToServerStarted = true;
            }
            return;
        }

        if (!NetworkManager.Singleton.IsServer)
        {
            return;
        }

        // If an instance already exists, nothing to do
        if (Object.FindFirstObjectByType<NetworkCardPlacementSystem>() != null)
        {
            return;
        }

        // Create a new GameObject with NetworkObject and the placement system
        var go = new GameObject("NetworkCardPlacementSystem_AUTO");
        var no = go.AddComponent<NetworkObject>();
        go.AddComponent<NetworkCardPlacementSystem>();

        Object.DontDestroyOnLoad(go);

        // Spawn it so clients get the object and can route RPCs reliably
        no.Spawn();
        Debug.Log("[NetworkPlacementBootstrap] Spawned NetworkCardPlacementSystem_AUTO as a networked object.");
    }

    private static void OnServerStarted()
    {
        // Server has started; ensure a spawned placement system exists
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        var existing = Object.FindFirstObjectByType<NetworkCardPlacementSystem>();
        if (existing != null)
        {
            var no = existing.GetComponent<NetworkObject>();
            if (no != null && !no.IsSpawned)
            {
                Object.DontDestroyOnLoad(existing.gameObject);
                no.Spawn();
                Debug.Log("[NetworkPlacementBootstrap] Spawned existing NetworkCardPlacementSystem on server start.");
            }
            return;
        }

        var go = new GameObject("NetworkCardPlacementSystem_AUTO");
        var netObj = go.AddComponent<NetworkObject>();
        go.AddComponent<NetworkCardPlacementSystem>();
        Object.DontDestroyOnLoad(go);
        netObj.Spawn();
        Debug.Log("[NetworkPlacementBootstrap] Spawned NetworkCardPlacementSystem_AUTO on server start.");
    }
}
