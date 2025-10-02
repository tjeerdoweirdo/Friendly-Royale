using UnityEngine;
using Unity.Netcode;
using Unity.Services.Core;
using Unity.Services.Authentication;

public static class NetworkDebugCommands
{
    [RuntimeInitializeOnLoadMethod]
    static void Initialize()
    {
        Debug.Log("Network Debug Commands Available:");
        Debug.Log("- NetworkDebugCommands.StartServer()");
        Debug.Log("- NetworkDebugCommands.StartHost()");
        Debug.Log("- NetworkDebugCommands.StartClient()");
        Debug.Log("- NetworkDebugCommands.StopNetwork()");
        Debug.Log("- NetworkDebugCommands.GetStatus()");
    }

    public static void StartServer()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.StartServer();
            Debug.Log("✅ Server Started!");
        }
        else
        {
            Debug.LogError("❌ NetworkManager not found!");
        }
    }

    public static void StartHost()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.StartHost();
            Debug.Log("✅ Host Started!");
        }
        else
        {
            Debug.LogError("❌ NetworkManager not found!");
        }
    }

    public static void StartClient()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.StartClient();
            Debug.Log("✅ Client Started!");
        }
        else
        {
            Debug.LogError("❌ NetworkManager not found!");
        }
    }

    public static void StopNetwork()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
            Debug.Log("🛑 Network Stopped!");
        }
    }

    public static void GetStatus()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.Log("❌ NetworkManager: Not Found");
            return;
        }

        Debug.Log("📊 Network Status:");
        Debug.Log($"   Server: {NetworkManager.Singleton.IsServer}");
        Debug.Log($"   Client: {NetworkManager.Singleton.IsClient}");
        Debug.Log($"   Host: {NetworkManager.Singleton.IsHost}");
        Debug.Log($"   Listening: {NetworkManager.Singleton.IsListening}");
        Debug.Log($"   Connected Clients: {NetworkManager.Singleton.ConnectedClients.Count}");
        Debug.Log($"   Unity Services: {UnityServices.State}");
        Debug.Log($"   Authentication: {(AuthenticationService.Instance?.IsSignedIn ?? false)}");
    }
}