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

    [Header("Debug")]
    public bool showDebugUI = true;

    async void Start()
    {
        await InitializeUnityServices();
        // If matchmaking requested an auto network start, defer to BattleNetworkAutoManager's logic
        int autoFlag = 0;
        try { autoFlag = PlayerPrefs.GetInt("AutoNetworkStart", 0); } catch { autoFlag = 0; }
        if (autoFlag == 1)
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
            transport.SetConnectionData("0.0.0.0", port);
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
            if (GUILayout.Button("Start as Server"))
            {
                StartServer();
            }
            if (GUILayout.Button("Start as Host"))
            {
                StartHost();
            }
            if (GUILayout.Button("Start as Client"))
            {
                StartClient();
            }
        }
        else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (GUILayout.Button("Stop Networking"))
            {
                StopNetworking();
            }
        }

        GUILayout.EndArea();
    }
}