using UnityEngine;
using Unity.Netcode;
using System.Collections;

/// <summary>
/// GameModeManager handles the detection of offline/online status and manages game mode restrictions.
/// When offline, only practice mode is available. When online, both practice and multiplayer modes are available.
/// </summary>
public class GameModeManager : MonoBehaviour
{
    [Header("Game Mode Settings")]
    [Tooltip("Time in seconds to wait for network initialization before considering the game offline")]
    public float networkTimeoutDuration = 5f;
    
    [Tooltip("Should we force offline mode for testing?")]
    public bool forceOfflineMode = false;
    
    [Header("Events")]
    public static System.Action<bool> OnGameModeChanged; // true = online, false = offline
    
    // Current game state
    private bool isOnlineMode = false;
    private bool isInitialized = false;
    
    // Singleton pattern
    public static GameModeManager Instance { get; private set; }
    
    private void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        
        // Start mode detection
        StartCoroutine(DetectGameMode());
    }
    
    /// <summary>
    /// Detects whether the game should run in online or offline mode
    /// </summary>
    private IEnumerator DetectGameMode()
    {
        Debug.Log("GameModeManager: Starting mode detection...");
        
        // If force offline mode is enabled, skip network checks
        if (forceOfflineMode)
        {
            Debug.Log("GameModeManager: Force offline mode enabled");
            SetGameMode(false);
            yield break;
        }
        
        // Check if we have internet connectivity
        bool hasInternet = Application.internetReachability != NetworkReachability.NotReachable;
        
        if (!hasInternet)
        {
            Debug.Log("GameModeManager: No internet connection detected, setting offline mode");
            SetGameMode(false);
            yield break;
        }
        
        // Check if networking components are available and functional
        bool networkingAvailable = IsNetworkingAvailable();
        
        if (!networkingAvailable)
        {
            Debug.Log("GameModeManager: Networking components not available, setting offline mode");
            SetGameMode(false);
            yield break;
        }
        
        // Wait a bit to see if network manager initializes properly
        float timeWaited = 0f;
        while (timeWaited < networkTimeoutDuration)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                Debug.Log("GameModeManager: Network manager is active, enabling online mode");
                SetGameMode(true);
                yield break;
            }
            
            timeWaited += Time.deltaTime;
            yield return null;
        }
        
        // Timeout reached, assume offline mode
        Debug.Log("GameModeManager: Network timeout reached, setting offline mode");
        SetGameMode(false);
    }
    
    /// <summary>
    /// Check if networking components are available
    /// </summary>
    private bool IsNetworkingAvailable()
    {
        // Check if NetworkManager exists in the scene or as a prefab that can be instantiated
        return NetworkManager.Singleton != null || FindFirstObjectByType<NetworkGameManager>() != null;
    }
    
    /// <summary>
    /// Sets the current game mode and notifies listeners
    /// </summary>
    private void SetGameMode(bool online)
    {
        isOnlineMode = online;
        isInitialized = true;
        
        Debug.Log($"GameModeManager: Game mode set to {(online ? "ONLINE" : "OFFLINE")}");
        
        // Notify other systems about the mode change
        OnGameModeChanged?.Invoke(online);
    }
    
    /// <summary>
    /// Returns true if the game is in online mode (multiplayer available)
    /// </summary>
    public bool IsOnlineMode()
    {
        return isInitialized && isOnlineMode;
    }
    
    /// <summary>
    /// Returns true if the game is in offline mode (practice only)
    /// </summary>
    public bool IsOfflineMode()
    {
        return isInitialized && !isOnlineMode;
    }
    
    /// <summary>
    /// Returns true if the mode detection is complete
    /// </summary>
    public bool IsInitialized()
    {
        return isInitialized;
    }
    
    /// <summary>
    /// Force set the game mode (useful for testing)
    /// </summary>
    public void ForceSetGameMode(bool online)
    {
        Debug.Log($"GameModeManager: Force setting game mode to {(online ? "ONLINE" : "OFFLINE")}");
        SetGameMode(online);
    }
    
    /// <summary>
    /// Check current internet connectivity
    /// </summary>
    public bool HasInternetConnection()
    {
        return Application.internetReachability != NetworkReachability.NotReachable;
    }
    
    /// <summary>
    /// Retry mode detection (useful when network conditions change)
    /// </summary>
    public void RetryModeDetection()
    {
        if (!isInitialized)
            return;
            
        Debug.Log("GameModeManager: Retrying mode detection...");
        isInitialized = false;
        StartCoroutine(DetectGameMode());
    }
    
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}