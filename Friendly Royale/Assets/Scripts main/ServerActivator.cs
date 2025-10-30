using UnityEngine;
using Unity.Netcode;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;

public class ServerActivator : MonoBehaviour
{
    [Header("Server Settings")]
    public bool autoStartAsServer = false;
    public bool autoStartAsHost = false;
    public int maxPlayers = 2;
    public ushort port = 7777;
    public string ipAddress = "127.0.0.1";

    [Header("Debug")]
    public bool showDebugUI = true;

    async void Start()
    {
        await InitializeUnityServices();
        // If matchmaking requested an auto network start, defer to BattleNetworkAutoManager's logic
        int autoFlag = 0;
        try { autoFlag = PlayerPrefs.GetInt("AutoNetworkStart", 0); } catch { autoFlag = 0; }
        // If practice mode is active or offline mode is enabled, do not auto-start any networking
        bool practiceActive = false;
        try { practiceActive = PlayerPrefs.GetInt("PracticeModeActive", 0) == 1; } catch { practiceActive = false; }
        bool offlineMode = false;
        try { offlineMode = (GameModeManager.Instance != null && GameModeManager.Instance.IsOfflineMode()); } catch { offlineMode = false; }
        if (autoFlag == 1 || practiceActive || offlineMode)
        {
            return;
        }

        if (autoStartAsServer)
        {
            StartServer();
        }
        else if (autoStartAsHost)
        {
            StartHost();
        }
    }

    async System.Threading.Tasks.Task InitializeUnityServices()
    {
        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
                Debug.Log("Unity Services initialized successfully");
            }
            else
            {
                Debug.Log("Unity Services already initialized");
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log($"Signed in as: {AuthenticationService.Instance.PlayerId}");
            }
            else
            {
                Debug.Log($"Already signed in as: {AuthenticationService.Instance.PlayerId}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to initialize Unity Services: {e.Message}");
        }
    }

    public void StartServer()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager not found! Please add NetworkManager to the scene.");
            return;
        }

        // Configure transport
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null)
        {
            // Allow PlayerPrefs overrides
            string bindIp = PlayerPrefs.GetString("Net_IP_Server", "0.0.0.0");
            ushort p = (ushort)PlayerPrefs.GetInt("Net_Port", port);
            transport.SetConnectionData(bindIp, p);
        }

        NetworkManager.Singleton.StartServer();
        Debug.Log($"Server started on port {port}");
    }

    public void StartHost()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager not found! Please add NetworkManager to the scene.");
            return;
        }
        var utp = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (utp != null)
        {
            // Bind like server for host
            string bindIp = PlayerPrefs.GetString("Net_IP_Server", "0.0.0.0");
            ushort p = (ushort)PlayerPrefs.GetInt("Net_Port", port);
            utp.SetConnectionData(bindIp, p);
        }
        NetworkManager.Singleton.StartHost();
        Debug.Log("Started as Host (Server + Client)");
    }

    public void StartClient()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager not found! Please add NetworkManager to the scene.");
            return;
        }
        var utp = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (utp != null)
        {
            // Default to loopback; change to target IP for LAN testing
            string addr = PlayerPrefs.GetString("Net_IP_Client", ipAddress);
            ushort p = (ushort)PlayerPrefs.GetInt("Net_Port", port);
            utp.SetConnectionData(addr, p);
        }
        NetworkManager.Singleton.StartClient();
        Debug.Log("Started as Client");
    }

    public void StopNetworking()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
            Debug.Log("Networking stopped");
        }
    }

    void OnGUI()
    {
        if (!showDebugUI) return;
        GUILayout.BeginArea(new Rect(10, 10, 300, 300));
        try
        {
            GUILayout.Label($"Unity Services: {(UnityServices.State == ServicesInitializationState.Initialized ? "✅ Ready" : "❌ Not Ready")}");
            GUILayout.Label($"Authentication: {(AuthenticationService.Instance.IsSignedIn ? "✅ Signed In" : "❌ Not Signed In")}");
            
            if (NetworkManager.Singleton != null)
            {
                GUILayout.Label($"Network Status: {NetworkManager.Singleton.IsServer} | {NetworkManager.Singleton.IsClient} | {NetworkManager.Singleton.IsHost}");
                GUILayout.Label($"Connected Clients: {(NetworkManager.Singleton.IsServer ? NetworkManager.Singleton.ConnectedClients.Count : 0)}");
            }

            GUILayout.Space(10);

            if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening)
            {
                try { if (GUILayout.Button("Start as Server")) StartServer(); } catch { }
                try { if (GUILayout.Button("Start as Host")) StartHost(); } catch { }
                try { if (GUILayout.Button("Start as Client")) StartClient(); } catch { }
            }
            else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                try { if (GUILayout.Button("Stop Networking")) StopNetworking(); } catch { }
            }
        }
        finally
        {
            GUILayout.EndArea();
        }
    }
}