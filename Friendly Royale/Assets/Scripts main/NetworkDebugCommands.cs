using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
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

    private static UnityTransport EnsureTransport()
    {
        if (NetworkManager.Singleton == null)
        {
            return null;
        }
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            transport = NetworkManager.Singleton.gameObject.AddComponent<UnityTransport>();
        }
        return transport;
    }

    private static void ConfigureForHostOrServer(UnityTransport transport)
    {
        if (transport == null) return;
        // Bind to any interface on default port (or override by PlayerPrefs)
        ushort port = (ushort)PlayerPrefs.GetInt("Net_Port", 7777);
        transport.SetConnectionData("0.0.0.0", port);
        Debug.Log($"[NetDebug] Host/Server bind 0.0.0.0:{port}");
    }

    private static void ConfigureForClient(UnityTransport transport)
    {
        if (transport == null) return;
        string ip = PlayerPrefs.GetString("Net_IP", "127.0.0.1");
        ushort port = (ushort)PlayerPrefs.GetInt("Net_Port", 7777);
        transport.SetConnectionData(ip, port);
        Debug.Log($"[NetDebug] Client connect {ip}:{port}");
    }

    public static void StartServer()
    {
        if (NetworkManager.Singleton != null)
        {
            var transport = EnsureTransport();
            ConfigureForHostOrServer(transport);
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
            var transport = EnsureTransport();
            ConfigureForHostOrServer(transport);
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
            var transport = EnsureTransport();
            ConfigureForClient(transport);
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
        Debug.Log($"   Connected Clients: {NetworkManager.Singleton.ConnectedClientsIds?.Count}");
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
        {
            Debug.Log($"   Transport: UnityTransport");
            Debug.Log($"   Addr/Port: {transport.ConnectionData.Address}:{transport.ConnectionData.Port}");
        }
        Debug.Log($"   Unity Services: {UnityServices.State}");
        Debug.Log($"   Authentication: {(AuthenticationService.Instance?.IsSignedIn ?? false)}");
    }
}