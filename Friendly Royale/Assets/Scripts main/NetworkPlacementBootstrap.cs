using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Ensures a networked NetworkCardPlacementSystem exists at runtime so clients always have a valid RPC target.
/// Host/Server creates and spawns it once on startup if not present. Marked DontDestroyOnLoad.
/// </summary>
public static class NetworkPlacementBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsurePlacementSystem()
    {
        // Only the server/host should create and spawn the networked object
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || !NetworkManager.Singleton.IsServer)
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
}
