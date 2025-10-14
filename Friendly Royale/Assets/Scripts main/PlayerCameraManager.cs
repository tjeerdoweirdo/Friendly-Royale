using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

/// <summary>
/// Assigns which Camera belongs to which player side and activates only the local player's camera.
/// Also configures AudioListener and informs CardPlacementSystem of the local side.
/// 
/// Usage:
/// - Drop on a GameObject in the scene.
/// - Assign player1Camera and player2Camera in the inspector.
/// - If using Netcode, leave sideMode = Auto and it will infer by owner/host; otherwise set sideMode = Override and choose overrideSide.
/// </summary>
public class PlayerCameraManager : MonoBehaviour
{
    public enum SideMode { Auto, Override }
    public enum PlayerSide { Player1, Player2 }

    [Header("Cameras")]
    public Camera player1Camera;
    public Camera player2Camera;

    [Header("Audio")]
    [Tooltip("Optional explicit AudioListeners; if null, will search on cameras")]
    public AudioListener player1Audio;
    public AudioListener player2Audio;

    [Header("Mode")] 
    public SideMode sideMode = SideMode.Auto;
    public PlayerSide overrideSide = PlayerSide.Player1;
    [Tooltip("When Auto mode is used: if true, Host => Player1 and Client => Player2. If false, Host => Player2 and Client => Player1.")]
    public bool hostIsPlayer1 = true;

    [Header("Lifecycle")]
    [Tooltip("Call Apply on Start after a short delay to let Netcode initialize")]
    public bool applyOnStart = true;
    [Tooltip("Seconds to wait before first Apply on Start")]
    public float initialApplyDelay = 0.1f;
    [Tooltip("Re-apply camera selection when Netcode events fire (server started / client connected)")]
    public bool reapplyOnNetcodeEvents = true;

    [Tooltip("Re-apply camera selection when a new scene is loaded")]
    public bool reapplyOnSceneLoaded = true;

    [Header("Offline/Practice")]
    [Tooltip("When offline/practice (no Netcode or GameMode offline), always use Player1 camera.")]
    public bool forcePlayer1InOffline = true;

    [Header("Robustness")]
    [Tooltip("If true, disable all other Cameras in the scene except the chosen player's camera")]
    public bool disableOtherCamerasInScene = true;

    [Header("Integration")] 
    [Tooltip("Optional: CardPlacementSystem to set local side on")] 
    public CardPlacementSystem placementSystem;

    private void Awake()
    {
        if (placementSystem == null)
        {
            placementSystem = FindFirstObjectByType<CardPlacementSystem>();
        }
    }

    private void Start()
    {
        if (applyOnStart)
        {
            StartCoroutine(ApplyWhenReady());
        }
    }

    private void OnEnable()
    {
        if (reapplyOnNetcodeEvents && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }

        if (reapplyOnSceneLoaded)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
    }

    private void OnDisable()
    {
        if (reapplyOnNetcodeEvents && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }

        if (reapplyOnSceneLoaded)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }

    public void Apply()
    {
        // Determine local side
        PlayerSide side = DetermineLocalSide();

        // Activate/deactivate cameras
        SetActiveCamera(side);

        // Inform placement system
        if (placementSystem != null)
        {
            placementSystem.SetLocalPlayerSide(side == PlayerSide.Player1 
                ? CardPlacementSystem.PlayerSide.Player1 
                : CardPlacementSystem.PlayerSide.Player2);
        }

        Debug.Log($"[PlayerCameraManager] Applied side: {side} (mode={sideMode}, hostIsP1={hostIsPlayer1})");
    }

    private PlayerSide DetermineLocalSide()
    {
        if (sideMode == SideMode.Override)
            return overrideSide;

        // Prefer GameManager side flags if present (central authority)
        var gm = FindFirstObjectByType<GameManager>();
        if (gm != null)
        {
            try
            {
                if (gm.localIsPlayer1) return PlayerSide.Player1;
                if (gm.localIsPlayer2) return PlayerSide.Player2;
            }
            catch { /* ignore if fields not present */ }
        }

        // Offline/practice mode -> force Player1 if configured
        if (forcePlayer1InOffline)
        {
            // Prefer a central game mode manager if present
            var gmm = FindFirstObjectByType<GameModeManager>();
            if (gmm != null)
            {
                try
                {
                    // Expecting IsOfflineMode()/IsOnlineMode() as in MatchmakingManager references
                    if (gmm.IsOfflineMode())
                        return PlayerSide.Player1;
                }
                catch { /* ignore if API differs */ }
            }

            // If Netcode not running, treat as offline/practice
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                return PlayerSide.Player1;
            }
        }

        // Auto mode: try to infer from Netcode role
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            bool isHost = NetworkManager.Singleton.IsHost;
            if (hostIsPlayer1)
            {
                return isHost ? PlayerSide.Player1 : PlayerSide.Player2;
            }
            else
            {
                return isHost ? PlayerSide.Player2 : PlayerSide.Player1;
            }
        }

        // No networking: default to Player1
        return PlayerSide.Player1;
    }

    private void SetActiveCamera(PlayerSide side)
    {
        bool p1 = side == PlayerSide.Player1;
        if (player1Camera != null) player1Camera.gameObject.SetActive(p1);
        if (player2Camera != null) player2Camera.gameObject.SetActive(!p1);

        // AudioListener: ensure only one enabled
        AudioListener a1 = player1Audio != null ? player1Audio : (player1Camera != null ? player1Camera.GetComponent<AudioListener>() : null);
        AudioListener a2 = player2Audio != null ? player2Audio : (player2Camera != null ? player2Camera.GetComponent<AudioListener>() : null);

        if (a1 != null) a1.enabled = p1;
        if (a2 != null) a2.enabled = !p1;

        if (disableOtherCamerasInScene)
        {
            Camera keep = p1 ? player1Camera : player2Camera;
            if (keep != null)
            {
                var allCams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
                foreach (var cam in allCams)
                {
                    if (cam == null) continue;
                    if (cam == keep) continue;
                    cam.gameObject.SetActive(false);
                    var al = cam.GetComponent<AudioListener>();
                    if (al != null) al.enabled = false;
                }
            }
        }
    }

    private System.Collections.IEnumerator ApplyWhenReady()
    {
        if (initialApplyDelay > 0f)
            yield return new WaitForSeconds(initialApplyDelay);

        // If Netcode exists, wait a short time for it to start listening (with timeout)
        float timeout = 2f;
        float t = 0f;
        while (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening && t < timeout)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        Apply();
    }

    private void OnServerStarted()
    {
        Debug.Log("[PlayerCameraManager] Netcode server started -> re-apply cameras");
        Apply();
    }

    private void OnClientConnected(ulong _)
    {
        Debug.Log("[PlayerCameraManager] Netcode client connected -> re-apply cameras");
        Apply();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"[PlayerCameraManager] Scene loaded ({scene.name}) -> re-apply cameras");
        StartCoroutine(ApplyWhenReady());
    }

    // Allow external scripts to force a side selection
    public void ForceSide(PlayerSide side)
    {
        sideMode = SideMode.Override;
        overrideSide = side;
        Apply();
    }
}
