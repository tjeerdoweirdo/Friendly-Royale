using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

/// <summary>
/// UI Controller for multiplayer lobby, connection, and matchmaking functionality.
/// Provides buttons and interface for hosting/joining games.
/// </summary>
public class MultiplayerUI : MonoBehaviour
{
    [Header("Connection UI")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button serverButton;
    [SerializeField] private Button disconnectButton;
    
    [Header("Connection Settings UI")]
    [SerializeField] private TMP_InputField ipAddressInput;
    [SerializeField] private TMP_InputField portInput;
    
    [Header("Status UI")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text playerCountText;
    [SerializeField] private GameObject connectionPanel;
    [SerializeField] private GameObject lobbyPanel;
    
    [Header("Lobby UI")]
    [SerializeField] private TMP_Text waitingText;
    [SerializeField] private Button startGameButton;
    
    private NetworkGameManager networkGameManager;
    
    private void Start()
    {
        SetupUI();
        SetupEvents();
        ShowConnectionPanel();
    }
    
    private void SetupUI()
    {
        // Set default values
        if (ipAddressInput != null)
            ipAddressInput.text = "127.0.0.1";
        
        if (portInput != null)
            portInput.text = "7777";
        
        // Setup button listeners
        if (hostButton != null)
            hostButton.onClick.AddListener(OnHostButtonClicked);
        
        if (joinButton != null)
            joinButton.onClick.AddListener(OnJoinButtonClicked);
        
        if (serverButton != null)
            serverButton.onClick.AddListener(OnServerButtonClicked);
        
        if (disconnectButton != null)
            disconnectButton.onClick.AddListener(OnDisconnectButtonClicked);
        
        if (startGameButton != null)
            startGameButton.onClick.AddListener(OnStartGameButtonClicked);
        
        // Initially hide disconnect button and lobby panel
        if (disconnectButton != null)
            disconnectButton.gameObject.SetActive(false);
        
        if (lobbyPanel != null)
            lobbyPanel.SetActive(false);
    }
    
    private void SetupEvents()
    {
        // Subscribe to network events
        NetworkGameManager.OnPlayerConnected += OnPlayerConnected;
        NetworkGameManager.OnPlayerDisconnected += OnPlayerDisconnected;
        NetworkGameManager.OnGameStarted += OnGameStarted;
        NetworkGameManager.OnGameEnded += OnGameEnded;
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        NetworkGameManager.OnPlayerConnected -= OnPlayerConnected;
        NetworkGameManager.OnPlayerDisconnected -= OnPlayerDisconnected;
        NetworkGameManager.OnGameStarted -= OnGameStarted;
        NetworkGameManager.OnGameEnded -= OnGameEnded;
    }
    
    #region Button Callbacks
    
    private void OnHostButtonClicked()
    {
        UpdateConnectionSettings();
        
        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.StartAsHost();
            UpdateStatus("Starting as Host...");
            ShowLobbyPanel();
        }
        else
        {
            UpdateStatus("NetworkGameManager not found!");
        }
    }
    
    private void OnJoinButtonClicked()
    {
        UpdateConnectionSettings();
        
        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.StartAsClient();
            UpdateStatus("Connecting to server...");
        }
        else
        {
            UpdateStatus("NetworkGameManager not found!");
        }
    }
    
    private void OnServerButtonClicked()
    {
        UpdateConnectionSettings();
        
        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.StartAsServer();
            UpdateStatus("Starting as Server...");
            ShowLobbyPanel();
        }
        else
        {
            UpdateStatus("NetworkGameManager not found!");
        }
    }
    
    private void OnDisconnectButtonClicked()
    {
        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.Disconnect();
            UpdateStatus("Disconnected");
            ShowConnectionPanel();
        }
    }
    
    private void OnStartGameButtonClicked()
    {
        if (NetworkGameManager.Instance != null && NetworkManager.Singleton.IsHost)
        {
            NetworkGameManager.Instance.StartGameServerRpc();
        }
    }
    
    #endregion
    
    #region Network Event Callbacks
    
    private void OnPlayerConnected(int playerId)
    {
        UpdatePlayerCount();
        UpdateStatus($"Player {playerId} connected");
        
        // Show start button if we have enough players and we're the host
        if (NetworkManager.Singleton.IsHost && startGameButton != null)
        {
            startGameButton.gameObject.SetActive(NetworkGameManager.Instance.GetConnectedPlayersCount() >= 2);
        }
    }
    
    private void OnPlayerDisconnected(int playerId)
    {
        UpdatePlayerCount();
        UpdateStatus($"Player {playerId} disconnected");
        
        // Hide start button if we don't have enough players
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(false);
        }
    }
    
    private void OnGameStarted()
    {
        UpdateStatus("Game Starting!");
        // Hide UI during gameplay
        if (connectionPanel != null)
            connectionPanel.SetActive(false);
        if (lobbyPanel != null)
            lobbyPanel.SetActive(false);
    }
    
    private void OnGameEnded()
    {
        UpdateStatus("Game Ended");
        ShowConnectionPanel();
    }
    
    #endregion
    
    #region Private Methods
    
    private void UpdateConnectionSettings()
    {
        if (NetworkGameManager.Instance != null)
        {
            if (ipAddressInput != null && !string.IsNullOrEmpty(ipAddressInput.text))
            {
                NetworkGameManager.Instance.SetIPAddress(ipAddressInput.text);
            }
            
            if (portInput != null && ushort.TryParse(portInput.text, out ushort port))
            {
                NetworkGameManager.Instance.SetPort(port);
            }
        }
    }
    
    private void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        
        Debug.Log($"MultiplayerUI: {message}");
    }
    
    private void UpdatePlayerCount()
    {
        if (playerCountText != null && NetworkGameManager.Instance != null)
        {
            int count = NetworkGameManager.Instance.GetConnectedPlayersCount();
            playerCountText.text = $"Players: {count}/2";
        }
    }
    
    private void ShowConnectionPanel()
    {
        if (connectionPanel != null)
            connectionPanel.SetActive(true);
        
        if (lobbyPanel != null)
            lobbyPanel.SetActive(false);
        
        if (disconnectButton != null)
            disconnectButton.gameObject.SetActive(false);
        
        // Show connection buttons
        if (hostButton != null)
            hostButton.gameObject.SetActive(true);
        if (joinButton != null)
            joinButton.gameObject.SetActive(true);
        if (serverButton != null)
            serverButton.gameObject.SetActive(true);
    }
    
    private void ShowLobbyPanel()
    {
        if (connectionPanel != null)
            connectionPanel.SetActive(false);
        
        if (lobbyPanel != null)
            lobbyPanel.SetActive(true);
        
        if (disconnectButton != null)
            disconnectButton.gameObject.SetActive(true);
        
        UpdatePlayerCount();
        
        // Only show start button for host
        if (startGameButton != null)
        {
            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
            startGameButton.gameObject.SetActive(isHost);
        }
    }
    
    #endregion
}