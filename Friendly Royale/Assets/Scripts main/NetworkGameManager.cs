using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine.SceneManagement;

/// <summary>
/// NetworkGameManager handles multiplayer connection, player joining, and game session management.
/// This is the central hub for multiplayer functionality in Friendly Royale.
/// </summary>
public class NetworkGameManager : NetworkBehaviour
{
    [Header("Network Settings")]
    [SerializeField] private int maxPlayers = 2;
    [SerializeField] private string gameplaySceneName = "GameplayScene";
    [SerializeField] private string lobbySceneName = "LobbyScene";
    
    [Header("Connection Settings")]
    [SerializeField] private ushort port = 7777;
    [SerializeField] private string ipAddress = "127.0.0.1";
    
    // Network variables for game state
    private NetworkVariable<int> connectedPlayers = new NetworkVariable<int>(0);
    private NetworkVariable<bool> gameStarted = new NetworkVariable<bool>(false);
    private NetworkVariable<float> matchTimeRemaining = new NetworkVariable<float>(180f);
    
    // Events
    public static System.Action<int> OnPlayerConnected;
    public static System.Action<int> OnPlayerDisconnected;
    public static System.Action OnGameStarted;
    public static System.Action OnGameEnded;
    
    private static NetworkGameManager instance;
    public static NetworkGameManager Instance => instance;
    
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        instance = this;
        DontDestroyOnLoad(gameObject);
    }
    
    private void Start()
    {
        // Set up network callbacks
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
    }
    
    private new void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
        }
    }
    
    #region Network Callbacks
    
    private void OnServerStarted()
    {
        Debug.Log("Server started successfully");
    }
    
    private void OnClientConnected(ulong clientId)
    {
        if (IsServer)
        {
            connectedPlayers.Value++;
            Debug.Log($"Client {clientId} connected. Total players: {connectedPlayers.Value}");
            
            OnPlayerConnected?.Invoke((int)clientId);
            
            // Start game when we have enough players
            if (connectedPlayers.Value >= maxPlayers && !gameStarted.Value)
            {
                StartGameServerRpc();
            }
        }
    }
    
    private void OnClientDisconnected(ulong clientId)
    {
        if (IsServer)
        {
            connectedPlayers.Value--;
            Debug.Log($"Client {clientId} disconnected. Total players: {connectedPlayers.Value}");
            
            OnPlayerDisconnected?.Invoke((int)clientId);
            
            // Handle game interruption if game was in progress
            if (gameStarted.Value)
            {
                EndGameServerRpc(GameEndReason.PlayerDisconnected);
            }
        }
    }
    
    #endregion
    
    #region Public Methods
    
    public void StartAsHost()
    {
        SetConnectionData();
        NetworkManager.Singleton.StartHost();
    }
    
    public void StartAsServer()
    {
        SetConnectionData();
        NetworkManager.Singleton.StartServer();
    }
    
    public void StartAsClient()
    {
        SetConnectionData();
        NetworkManager.Singleton.StartClient();
    }
    
    public void Disconnect()
    {
        if (NetworkManager.Singleton.IsHost)
        {
            NetworkManager.Singleton.Shutdown();
        }
        else if (NetworkManager.Singleton.IsClient)
        {
            NetworkManager.Singleton.Shutdown();
        }
    }
    
    public void SetIPAddress(string ip)
    {
        ipAddress = ip;
    }
    
    public void SetPort(ushort newPort)
    {
        port = newPort;
    }
    
    public int GetConnectedPlayersCount()
    {
        return connectedPlayers.Value;
    }
    
    public bool IsGameStarted()
    {
        return gameStarted.Value;
    }
    
    public float GetMatchTimeRemaining()
    {
        return matchTimeRemaining.Value;
    }
    
    #endregion
    
    #region Server RPCs
    
    [ServerRpc(RequireOwnership = false)]
    public void StartGameServerRpc()
    {
        if (gameStarted.Value) return;
        
        gameStarted.Value = true;
        matchTimeRemaining.Value = 180f; // 3 minutes default
        
        Debug.Log("Game started!");
        OnGameStarted?.Invoke();
        
        // Load gameplay scene for all clients
        NetworkManager.Singleton.SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
    }
    
    [ServerRpc(RequireOwnership = false)]
    public void EndGameServerRpc(GameEndReason reason)
    {
        if (!gameStarted.Value) return;
        
        gameStarted.Value = false;
        
        Debug.Log($"Game ended. Reason: {reason}");
        OnGameEnded?.Invoke();
        
        // Handle post-game logic
        HandleGameEndClientRpc(reason);
    }
    
    [ServerRpc(RequireOwnership = false)]
    public void UpdateMatchTimeServerRpc(float timeRemaining)
    {
        matchTimeRemaining.Value = timeRemaining;
    }
    
    #endregion
    
    #region Client RPCs
    
    [ClientRpc]
    private void HandleGameEndClientRpc(GameEndReason reason)
    {
        // Handle game end on client side
        switch (reason)
        {
            case GameEndReason.PlayerDisconnected:
                // Show disconnection message
                break;
            case GameEndReason.TimeExpired:
                // Handle timeout
                break;
            case GameEndReason.PlayerWon:
                // Handle victory/defeat
                break;
        }
    }
    
    #endregion
    
    #region Private Methods
    
    private void SetConnectionData()
    {
        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
        {
            // Allow PlayerPrefs overrides for quick LAN testing
            string addr = PlayerPrefs.GetString("Net_IP_Client", ipAddress);
            ushort p = (ushort)PlayerPrefs.GetInt("Net_Port", port);
            transport.ConnectionData.Address = addr;
            transport.ConnectionData.Port = p;
        }
    }
    
    #endregion
}

public enum GameEndReason
{
    PlayerWon,
    PlayerDisconnected,
    TimeExpired,
    ServerError
}