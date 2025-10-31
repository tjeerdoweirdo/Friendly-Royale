using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine.EventSystems;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
#if UNITY_RELAY_INSTALLED
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Networking.Transport.Relay;
#endif
using Unity.Netcode.Transports.UTP;

public class MatchmakingManager : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Main matchmaking panel")]
    public GameObject matchmakingPanel;
    
    [Tooltip("Button to start matchmaking")]
    public Button findMatchButton;
    
    [Tooltip("Button to cancel matchmaking")]
    public Button cancelMatchButton;
    
    [Tooltip("Button for practice mode (single player)")]
    public Button practiceButton;
    
    [Tooltip("Button to toggle matchmaking panel visibility")]
    public Button togglePanelButton;
    
    [Tooltip("Text on the toggle button")]
    public TMP_Text toggleButtonText;
    
    [Tooltip("Text showing matchmaking status")]
    public TMP_Text statusText;
    
    [Tooltip("Text showing current trophy count")]
    public TMP_Text trophyCountText;
    
    [Tooltip("Dropdown for arena selection")]
    public TMP_Dropdown arenaDropdown;
    
    [Tooltip("Text showing selected arena")]
    public TMP_Text selectedArenaText;
    
    [Tooltip("Progress bar or loading indicator")]
    public Slider matchmakingProgress;
    
    [Tooltip("Estimated wait time text")]
    public TMP_Text estimatedTimeText;

    [Header("Queue UI")]
    [Tooltip("Text showing how many players are queueing in the selected arena")]
    public TMP_Text arenaQueueCountText;

    [Header("Practice Settings")] 
    [Tooltip("Seconds for practice pre-load before entering scene.")]
    public float practicePreLoadDuration = 4f;

    [Header("Loading Progress Settings")]
    [Tooltip("Approximate seconds for search bar to naturally reach near-complete before a match is found (used only for visual pacing). If a match is found earlier, bar jumps to full.")]
    public float expectedSearchDuration = 12f;
    [Tooltip("Maximum fraction the search bar will auto-fill to before a match is found (prevents it reaching 100% too early).")]
    [Range(0.5f,0.99f)] public float searchBarAutoFillCap = 0.92f;
    [Tooltip("Seconds for the pre-match loading countdown after opponent found before entering the game scene.")]
    public float preMatchLoadDuration = 8f;
    [Tooltip("If true, show a countdown in the status during pre-match load.")]
    public bool showPreMatchCountdownInStatus = true;
    
    [Header("Player Info UI")]
    [Tooltip("Text showing local player's username")]
    public TMP_Text localPlayerUsernameText;
    
    [Tooltip("Text showing local player's trophy count")]
    public TMP_Text localPlayerTrophyText;
    
    [Tooltip("Text showing local player's deck size")]
    public TMP_Text localPlayerDeckSizeText;
    
    [Tooltip("Image showing local player's avatar/profile picture")]
    public Image localPlayerAvatarImage;

    [Header("Arena UI")]
    [Tooltip("Image to display the selected arena's preview sprite in the matchmaking panel")] 
    public Image arenaPreviewImage;
    [Tooltip("Optional fallback sprite if the selected arena has no preview")] 
    public Sprite defaultArenaPreview;

    [Header("Opponent Info UI")]
    [Tooltip("Text showing opponent's username when found")]
    public TMP_Text opponentUsernameText;
    
    [Tooltip("Text showing opponent's trophy count")]
    public TMP_Text opponentTrophyText;
    
    [Tooltip("Text showing opponent's deck size")]
    public TMP_Text opponentDeckSizeText;
    
    [Tooltip("Image showing opponent's avatar/profile picture")]
    public Image opponentAvatarImage;
    
    [Tooltip("Any additional UI elements to show/hide when opponent is found/lost")]
    public GameObject[] opponentUIElements;

    [Header("Lobby Info")]
    [Tooltip("Text showing the current Unity Lobby ID while searching/readying")]
    public TMP_Text lobbyIdText;

    [Header("Ready-Up UI")]
    [Tooltip("Panel shown during ready-up phase")] public GameObject readyPanel;
    [Tooltip("Button for the local player to press Ready")] public Button readyButton;
    [Tooltip("Text that shows local ready state")] public TMP_Text localReadyText;
    [Tooltip("Text that shows opponent ready state")] public TMP_Text opponentReadyText;
    [Tooltip("Label or hint text during ready-up")] public TMP_Text readyHintText;

    [Header("Player Side Selection")]
    [Tooltip("Toggle to decide if local player is Player 1 (left side) or Player 2 (right side) - will be overridden by random assignment in multiplayer")]
    public Toggle playerSideToggle;
    
    [Tooltip("Text showing 'Player 1' label")]
    public TMP_Text player1Label;
    
    [Tooltip("Text showing 'Player 2' label")]
    public TMP_Text player2Label;
    
    [Tooltip("If true, local player will be Player 1 when toggle is ON (practice mode only)")]
    public bool localIsPlayer1WhenToggleOn = true;
    
    [Tooltip("If true, player sides are randomly assigned when opponent is found")]
    public bool randomizePlayerSides = true;

    [Header("Styling")] 
    [Tooltip("Color applied to all trophy count texts (gold style).")]
    public Color trophyTextColor = new Color(1f, 0.843f, 0f); // Approx Gold (#FFD700)

    [Header("Deck Validation")]
    [Tooltip("Minimum cards required in deck")]
    public int minimumDeckSize = 4;
    
    [Tooltip("Maximum cards allowed in deck")]
    public int maximumDeckSize = 8;

    [Header("Matchmaking Settings")]
    [Tooltip("How long to search before expanding trophy range")]
    public float searchTimeBeforeExpansion = 10f;
    
    [Tooltip("Simulate matchmaking time")]
    public float simulatedMatchmakingTime = 15f;

    [Header("Manager References")]
    public DeckManager deckManager;
    public PlayerProgress playerProgress;
    public ArenaManager arenaManager;
    public FullDeckSelector6 deckSelector;

    // Private variables
    private Arena selectedArena;
    private List<Card> currentDeck;
    private bool isSearching = false;
    private float searchStartTime;
    private Coroutine matchmakingCoroutine;
    
    // Lobby polling and heartbeat control
    private Coroutine pollLobbyCoroutine;
    private Coroutine lobbyHeartbeatCoroutine;
    private Coroutine readyPollCoroutine;
    // Adaptive polling delays (helps avoid 429 Too Many Requests)
    private float lobbyPollDelaySeconds = 2f;           // current delay
    private const float LobbyPollMinDelay = 1.5f;       // floor
    private const float LobbyPollMaxDelay = 10f;        // ceiling
    private const float LobbyPollBackoffFactor = 1.8f;  // exponential backoff factor on 429
    private Lobby currentLobby;
    private bool useRealMultiplayer = true;
    private bool matchFound = false;
    private bool preMatchStarted = false;
    private float preMatchStartTime;
    private float searchStartTimeForProgress; // separate from existing searchStartTime logic
    private bool isPracticeStarting = false;
    private float practiceStartTime;
    
    // Debug: allow forcing a match during online search
    private bool debugForceImmediateMatch = false;

    [Header("Debug")]
    [Tooltip("In development builds/editor, prefer hosting when searching (skip joining existing lobbies).")]
    public bool forceHostWhenDebugging = true;
    
    // Relay join code published by host in lobby data
    private string relayJoinCode = string.Empty;
    
    // Host-host stalemate breaker
    private float lastHostSwitchAttemptTime = 0f;
    private const float HostSwitchIntervalSeconds = 4f;
    
    // Queue count refresh control
    private Coroutine queueCountCoroutine;
    private float queueCountRefreshInterval = 10f;
    
    // Arena dropdown tracking
    private List<Arena> arenasInDropdown = new List<Arena>();
    private int lastValidArenaIndex = 0;

    private enum LoadingPhase { None, Searching, PreMatch }
    private LoadingPhase loadingPhase = LoadingPhase.None;
    
    // Local player information
    private string localPlayerUsername = "";
    private int localPlayerTrophies = 0;
    private int localPlayerDeckSize = 0;
    private bool localPlayerIsPlayer1 = true;

    // Ready-up state
    private bool localReady = false;
    private bool opponentReady = false;
    private float readyPollInterval = 1.5f;
    // Track when UI is in ready phase so Cancel button only un-readies instead of leaving lobby
    private bool inReadyPhase = false;

    // Opponent information
    private string opponentUsername = "";
    private string opponentPlayerId = "";
    private int opponentTrophies = 0;
    private int opponentDeckSize = 0;

    // Matchmaking states
    public enum MatchmakingState
    {
        Idle,
        ValidatingDeck,
        SearchingForMatch,
        FoundMatch,
        JoiningMatch,
        Error
    }
    
    private MatchmakingState currentState = MatchmakingState.Idle;

    void Start()
    {
        // Find managers if not assigned
        if (deckManager == null) deckManager = FindFirstObjectByType<DeckManager>();
        if (playerProgress == null) playerProgress = FindFirstObjectByType<PlayerProgress>();
        if (arenaManager == null) arenaManager = FindFirstObjectByType<ArenaManager>();
        if (deckSelector == null) deckSelector = FindFirstObjectByType<FullDeckSelector6>();
        
        // Subscribe to game mode changes
        GameModeManager.OnGameModeChanged += OnGameModeChanged;

        // Setup UI event listeners
        if (findMatchButton != null)
        {
            findMatchButton.onClick.AddListener(StartMatchmaking);
        }
        
        if (cancelMatchButton != null)
        {
            cancelMatchButton.onClick.RemoveAllListeners();
            cancelMatchButton.onClick.AddListener(CancelMatchmaking);
            cancelMatchButton.gameObject.SetActive(false);
        }
        
        if (practiceButton != null)
        {
            practiceButton.onClick.AddListener(StartPracticeMode);
        }
        
        if (togglePanelButton != null)
        {
            togglePanelButton.onClick.AddListener(ToggleMatchmakingPanel);
        }
        
        // Setup arena dropdown
        if (arenaDropdown != null)
        {
            arenaDropdown.onValueChanged.AddListener(OnArenaDropdownChanged);
            InitializeArenaDropdown();
        }
        
        // Setup player side toggle
        if (playerSideToggle != null)
        {
            playerSideToggle.onValueChanged.AddListener(OnPlayerSideToggleChanged);
        }

        // Initialize local player info
        UpdateLocalPlayerInfo();
        
        // Initialize UI
        UpdateUI();
        UpdateToggleButtonText();
        UpdatePlayerSideLabels();
    ApplyTrophyTextStyling();
        // Ready UI initial state
        if (readyPanel != null) readyPanel.SetActive(false);
        if (readyButton != null)
        {
            readyButton.onClick.RemoveAllListeners();
            readyButton.onClick.AddListener(OnReadyClicked);
            readyButton.interactable = true;
        }
    if (localReadyText != null) localReadyText.text = "You: Not";
    if (opponentReadyText != null) opponentReadyText.text = "Opp: Not";
    if (readyHintText != null) readyHintText.text = "Tap Ready";
        // Initial queue count
        StartCoroutine(RefreshArenaQueueCount());
        
        // Ensure matchmaking panel is closed by default
        try { HideMatchmakingPanel(); } catch { }

        // Initialize Unity Services for multiplayer
        InitializeUnityServices();

        // Initialize lobby info UI empty at startup
        UpdateLobbyInfoUI();
    }

    void Update()
    {
        if (isSearching)
        {
            UpdateMatchmakingProgress();
        }
        // Debug hotkey: while searching online, press P to force a match immediately (testing only)
        try
        {
            if (isSearching && (useRealMultiplayer || (GameModeManager.Instance != null && GameModeManager.Instance.IsOnlineMode())) && Input.GetKeyDown(KeyCode.P))
            {
                ForceDebugFindMatch();
            }
        }
        catch { }
        // Practice passive progress (safety in case coroutine paused) using main bar
        if (isPracticeStarting && matchmakingProgress != null)
        {
            float elapsed = Time.time - practiceStartTime;
            float norm = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, practicePreLoadDuration));
            matchmakingProgress.value = norm;
        }
        
        UpdateUI();
    }

    async void InitializeUnityServices()
    {
        try
        {
            await UnityServices.InitializeAsync();
            
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log("Signed in anonymously for matchmaking");
                useRealMultiplayer = true;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to initialize Unity Services: {e.Message}");
            SetStatus("Network services unavailable. Practice mode only.");
            useRealMultiplayer = false;
        }
    }

    public void StartMatchmaking()
    {
        if (isSearching) return;
        // Ensure practice flag is cleared when starting online matchmaking
        try { PlayerPrefs.SetInt("PracticeModeActive", 0); PlayerPrefs.Save(); } catch { }
        
        // Check if online mode is available
        if (GameModeManager.Instance != null && GameModeManager.Instance.IsOfflineMode())
        {
            SetStatus("Multiplayer unavailable - No internet connection. Try Practice Mode!");
            return;
        }
        
        // Validate deck first
        if (!ValidateDeck())
        {
            return;
        }
        
        // Use selected arena from dropdown (already set in OnArenaDropdownChanged)
        if (selectedArena == null)
        {
            SetStatus("Please select an arena first!");
            return;
        }
        
        // Start matchmaking process
        currentState = MatchmakingState.SearchingForMatch;
        isSearching = true;
        searchStartTime = Time.time;
    searchStartTimeForProgress = Time.time;
    matchFound = false;
    preMatchStarted = false;
    loadingPhase = LoadingPhase.Searching;
    if (matchmakingProgress != null) matchmakingProgress.value = 0f;
        
        // Update UI
        if (findMatchButton != null) findMatchButton.gameObject.SetActive(false);
        if (cancelMatchButton != null) cancelMatchButton.gameObject.SetActive(true);
        
        // Hide any previous opponent info
        HideOpponentInfo();
        
        // Start matchmaking coroutine
        if (useRealMultiplayer)
        {
            matchmakingCoroutine = StartCoroutine(RealMatchmakingProcess());
        }
        else
        {
            matchmakingCoroutine = StartCoroutine(SimulateMatchmaking());
        }
        
    SetStatus("Searching...");
        Debug.Log("Started matchmaking for arena: " + selectedArena.arenaID);
        // Start periodic queue count refresh while searching
        if (queueCountCoroutine != null)
        {
            StopCoroutine(queueCountCoroutine);
            queueCountCoroutine = null;
        }
        queueCountCoroutine = StartCoroutine(ArenaQueueLoop());
    }

    public void CancelMatchmaking()
    {
        // Guard: if this was (mis)wired to the Ready button, ignore to prevent accidental cancel
        var es = EventSystem.current;
        if (es != null && readyButton != null)
        {
            var src = es.currentSelectedGameObject;
            if (src == readyButton.gameObject)
            {
                Debug.LogWarning("CancelMatchmaking invoked from Ready button; ignoring.");
                return;
            }
        }
        if (!isSearching) return;
        // If we're in ready phase, cancel should stop matchmaking completely and inform both sides via lobby leave
        if (inReadyPhase && currentLobby != null)
        {
            DoCancelMatchmaking("Canceled");
            return;
        }

        // Default: cancel active search
    DoCancelMatchmaking("Canceled");
    }

    // Centralized cancellation routine so we can use specific messages (e.g., "Match canceled")
    void DoCancelMatchmaking(string statusMessage)
    {
        isSearching = false;
        currentState = MatchmakingState.Idle;
        loadingPhase = LoadingPhase.None;
        preMatchStarted = false;
        inReadyPhase = false;

        // Stop matchmaking coroutine
        if (matchmakingCoroutine != null)
        {
            StopCoroutine(matchmakingCoroutine);
            matchmakingCoroutine = null;
        }
        // Stop polling and heartbeat coroutines
        if (pollLobbyCoroutine != null)
        {
            StopCoroutine(pollLobbyCoroutine);
            pollLobbyCoroutine = null;
        }
        if (lobbyHeartbeatCoroutine != null)
        {
            StopCoroutine(lobbyHeartbeatCoroutine);
            lobbyHeartbeatCoroutine = null;
        }
        if (readyPollCoroutine != null)
        {
            StopCoroutine(readyPollCoroutine);
            readyPollCoroutine = null;
        }
        // Stop queue count loop
        if (queueCountCoroutine != null)
        {
            StopCoroutine(queueCountCoroutine);
            queueCountCoroutine = null;
        }

        // Leave lobby if we're in one (this notifies the other player and ends the ready-up)
        if (currentLobby != null)
        {
            LeaveLobby();
        }

        // Clear lobby info UI when cancelling
        UpdateLobbyInfoUI();

        // Update UI
        if (findMatchButton != null) findMatchButton.gameObject.SetActive(true);
        if (cancelMatchButton != null) cancelMatchButton.gameObject.SetActive(false);

        // Hide opponent info
        HideOpponentInfo();
        // Reset ready UI/state
        localReady = false;
        opponentReady = false;
        if (readyPanel != null) readyPanel.SetActive(false);
        if (readyButton != null) readyButton.interactable = true;
    if (localReadyText != null) localReadyText.text = "You: Not";
    if (opponentReadyText != null) opponentReadyText.text = "Opp: Not";

        SetStatus(statusMessage);
        Debug.Log(statusMessage);
    }

    public void StartPracticeMode()
    {
        if (!ValidateDeck())
        {
            return;
        }
        
        // Use selected arena from dropdown (already set in OnArenaDropdownChanged)
        if (selectedArena == null)
        {
            SetStatus("Please select an arena first!");
            return;
        }
        
        // Set up single player mode
    SetStatus("Practice...");
        // Explicitly disable any auto network start and mark practice active
        try { PlayerPrefs.SetInt("AutoNetworkStart", 0); PlayerPrefs.SetInt("PracticeModeActive", 1); PlayerPrefs.Save(); } catch { }
        
        // Save current deck
        if (deckManager != null && currentDeck != null)
        {
            deckManager.SetStartingDeck(currentDeck, selectedArena);
            playerProgress?.SaveSelectedDeckForArena("global", currentDeck.Select(c => c.cardID).ToList());
        }
        
        // Save player side for GameManager / PlayerCameraManager
        bool finalLocalIsP1 = localPlayerIsPlayer1;
        if (useRealMultiplayer && currentLobby != null)
        {
            try { finalLocalIsP1 = (currentLobby.HostId == AuthenticationService.Instance.PlayerId); } catch { }
        }
        PlayerPrefs.SetInt("LocalPlayerIsPlayer1", finalLocalIsP1 ? 1 : 0);
        PlayerPrefs.SetString("LocalPlayerUsername", localPlayerUsername);
        PlayerPrefs.SetString("OpponentUsername", "AI Bot");
        PlayerPrefs.Save();
        
        // Show practice overlay and delay scene load
        BeginPracticeOverlayAndLoad();
    }

    void BeginPracticeOverlayAndLoad()
    {
        // Use existing UI instead of a separate overlay panel
        ShowMatchmakingPanel();
    if (matchmakingProgress != null) matchmakingProgress.value = 0f;
    if (statusText != null) statusText.text = "Preparing...";
    if (estimatedTimeText != null) estimatedTimeText.text = "Starting...";
        isPracticeStarting = true;
        practiceStartTime = Time.time;
        StartCoroutine(PracticeLoadCountdown());

        // Stop network stack for local practice (no Netcode/Relay running), but keep NetworkManager object active for later online sessions
        try
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }
        catch { }

        // Hide opponent / player2 UI during practice start
        HideOpponentInfo();
        if (player2Label != null) player2Label.text = ""; // clear player2 text
        if (player1Label != null) player1Label.text = "Player 1 (You)";
    // Ensure ready UI is hidden/cleared for practice overlay
    if (readyPanel != null) readyPanel.SetActive(false);
    if (localReadyText != null) localReadyText.text = "";
    if (opponentReadyText != null) opponentReadyText.text = "";
    if (readyHintText != null) readyHintText.text = "";
    }

    IEnumerator PracticeLoadCountdown()
    {
        while (isPracticeStarting)
        {
            float elapsed = Time.time - practiceStartTime;
            float norm = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, practicePreLoadDuration));
            if (matchmakingProgress != null) matchmakingProgress.value = norm;
            float remaining = Mathf.Max(0f, practicePreLoadDuration - elapsed);
            if (estimatedTimeText != null) estimatedTimeText.text = $"Loading... {Mathf.CeilToInt(remaining)}s";
            if (statusText != null) statusText.text = "Preparing...";
            if (elapsed >= practicePreLoadDuration)
            {
                isPracticeStarting = false;
                break;
            }
            yield return null;
        }

        if (!string.IsNullOrEmpty(selectedArena.sceneName))
        {
            // Practice is offline, so a local scene load is correct here
            SceneManager.LoadScene(selectedArena.sceneName);
        }
        else
        {
            Debug.LogError("Selected arena has no scene name assigned!");
            SetStatus("Error: Arena scene not configured");
        }
    }

    bool ValidateDeck()
    {
        currentState = MatchmakingState.ValidatingDeck;
        
        // Get current deck from deck manager
        if (deckManager == null || deckManager.selectedCards == null)
        {
            SetStatus("Deck manager not available!");
            currentState = MatchmakingState.Error;
            return false;
        }
        
        currentDeck = deckManager.selectedCards.Where(c => c != null).ToList();
        
        // Check minimum deck size
        if (currentDeck.Count < minimumDeckSize)
        {
            SetStatus($"You need at least {minimumDeckSize} cards in your deck!");
            currentState = MatchmakingState.Error;
            return false;
        }
        
        // Check maximum deck size
        if (currentDeck.Count > maximumDeckSize)
        {
            SetStatus($"Your deck cannot have more than {maximumDeckSize} cards!");
            currentState = MatchmakingState.Error;
            return false;
        }
        
        // Validate each card in the deck
        foreach (Card card in currentDeck)
        {
            if (card == null)
            {
                SetStatus("Invalid card detected in deck!");
                currentState = MatchmakingState.Error;
                return false;
            }
            
            // Check if player has unlocked this card
            if (playerProgress != null && !playerProgress.IsCardUnlocked(card.cardID))
            {
                // Auto-unlock cards that are being used (temporary fix)
                Debug.Log($"Auto-unlocking card: {card.cardName}");
                playerProgress.UnlockCard(card.cardID);
                
                // Still show warning but don't block
                Debug.LogWarning($"Card '{card.cardName}' was not unlocked but has been auto-unlocked for gameplay");
            }
        }
        
        Debug.Log($"Deck validated successfully: {currentDeck.Count} cards");
        return true;
    }

    IEnumerator SimulateMatchmaking()
    {
        // Just keep searching until we decide to "find" a match (simulate timing)
        while (isSearching && !matchFound)
        {
            if ((Time.time - searchStartTimeForProgress) > searchTimeBeforeExpansion)
            {
                SetStatus("Widening...");
            }
            else
            {
                SetStatus("Searching...");
            }
            // Random chance to find opponent after minimum half of simulated time
            if ((Time.time - searchStartTimeForProgress) > simulatedMatchmakingTime * 0.5f && Random.value < 0.15f)
            {
                // Found an opponent
                currentState = MatchmakingState.FoundMatch;
                GenerateSimulatedOpponent();
                ShowOpponentFound();
                break;
            }
            yield return new WaitForSeconds(0.5f);
        }
        // Pre-match countdown handled by ShowOpponentFound -> PreMatch coroutine
    }

    void StartMultiplayerMatch()
    {
        // Save current deck
        if (deckManager != null && currentDeck != null)
        {
            deckManager.SetStartingDeck(currentDeck, selectedArena);
            playerProgress?.SaveSelectedDeckForArena("global", currentDeck.Select(c => c.cardID).ToList());
        }
        
        // Save player side preference for GameManager to use
        PlayerPrefs.SetInt("LocalPlayerIsPlayer1", localPlayerIsPlayer1 ? 1 : 0);
        PlayerPrefs.SetString("LocalPlayerUsername", localPlayerUsername);
        PlayerPrefs.SetString("OpponentUsername", opponentUsername);
        // Signal battle scene auto-manager to start networking based on saved side
        PlayerPrefs.SetInt("AutoNetworkStart", 1);
        PlayerPrefs.Save();
        
    SetStatus("Joining...");

        // Load the arena scene in a network-aware way
        LoadArenaSceneNetworkAware();
    }

    // Host triggers synchronized scene load; clients are moved by server automatically
    void LoadArenaSceneNetworkAware()
    {
        if (string.IsNullOrEmpty(selectedArena?.sceneName))
        {
            Debug.LogError("Selected arena has no scene name assigned!");
            SetStatus("Error: Arena scene not configured");
            return;
        }

        // In debug-forced matches, always load locally (bypass netcode sync)
        if (debugForceImmediateMatch)
        {
            SceneManager.LoadScene(selectedArena.sceneName);
            return;
        }

        if (useRealMultiplayer && NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.IsServer)
            {
                NetworkManager.Singleton.SceneManager.LoadScene(selectedArena.sceneName, LoadSceneMode.Single);
            }
            // Clients do not load locally; they will follow the server
        }
        else
        {
            // Offline or simulation: load locally
            SceneManager.LoadScene(selectedArena.sceneName);
        }
    }

    // Testing helper: force a match immediately when searching online
    private void ForceDebugFindMatch()
    {
        // Stop any ongoing lobby polling/creation but keep UI state
        if (matchmakingCoroutine != null)
        {
            StopCoroutine(matchmakingCoroutine);
            matchmakingCoroutine = null;
        }
        if (pollLobbyCoroutine != null)
        {
            StopCoroutine(pollLobbyCoroutine);
            pollLobbyCoroutine = null;
        }
        if (lobbyHeartbeatCoroutine != null)
        {
            StopCoroutine(lobbyHeartbeatCoroutine);
            lobbyHeartbeatCoroutine = null;
        }

        debugForceImmediateMatch = true;
        // For debug matches, make local player Player 1 (Host) and persist immediately so battle scene auto-starts as Host
        localPlayerIsPlayer1 = true;
        try
        {
            PlayerPrefs.SetInt("LocalPlayerIsPlayer1", 1);
            if (!string.IsNullOrEmpty(localPlayerUsername)) PlayerPrefs.SetString("LocalPlayerUsername", localPlayerUsername);
            string oppName = string.IsNullOrEmpty(opponentUsername) ? "DebugOpponent" : opponentUsername;
            PlayerPrefs.SetString("OpponentUsername", oppName);
            PlayerPrefs.SetInt("AutoNetworkStart", 1);
            PlayerPrefs.Save();
        }
        catch { }
        currentState = MatchmakingState.FoundMatch;
        matchFound = true;
        loadingPhase = LoadingPhase.PreMatch;
        if (matchmakingProgress != null) matchmakingProgress.value = 1f;
        // Provide a simple fake opponent for UI
        opponentUsername = string.IsNullOrEmpty(opponentUsername) ? "DebugOpponent" : opponentUsername;
        opponentTrophies = opponentTrophies == 0 ? (playerProgress?.GetCurrentTrophies() ?? 0) : opponentTrophies;
        opponentDeckSize = opponentDeckSize == 0 ? Mathf.Clamp(currentDeck != null ? currentDeck.Count : 8, 4, 8) : opponentDeckSize;

        // Hide ready-up and jump straight to countdown
        inReadyPhase = false;
        if (readyPanel != null) readyPanel.SetActive(false);
        SetStatus("Debug: Match forced (P)");

        if (!preMatchStarted)
        {
            StartCoroutine(PreMatchCountdown());
        }
    }

    IEnumerator RealMatchmakingProcess()
    {
        while (isSearching)
        {
            // If we already created or joined a lobby, don't spam find/create.
            if (currentLobby == null)
            {
                // Try to find or create a lobby (handles its own rate limiting)
                yield return StartCoroutine(FindOrCreateLobby());
            }

            // If we found a match, proceed
            if (currentState == MatchmakingState.FoundMatch)
                break;

            // Otherwise, wait a bit and loop (polling coroutine will be running if we created a lobby)
            yield return new WaitForSeconds(0.5f);
        }

        // When a match is found, we now wait for the PreMatchCountdown to complete
    }

    IEnumerator FindOrCreateLobby()
    {
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            SetStatus("Not authenticated. Please restart the game.");
            yield break;
        }

        // If we already have a lobby, ensure polling is active and return.
        if (currentLobby != null)
        {
            if (pollLobbyCoroutine == null)
            {
                pollLobbyCoroutine = StartCoroutine(PollLobby());
            }
            yield break;
        }
        
        int playerTrophies = playerProgress?.trophies ?? 0;
        
        // Optionally skip joining existing lobbies in development to ensure we host for debugging
        bool skipJoinForDebug = forceHostWhenDebugging && Debug.isDebugBuild;
        bool rateLimited = false;
        if (!skipJoinForDebug)
        {
            // Try to find existing lobbies first
            var queryOptions = new QueryLobbiesOptions
            {
                Count = 10,
                Filters = new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "1", QueryFilter.OpOptions.GE)
                }
            };
            
            var response = LobbyService.Instance.QueryLobbiesAsync(queryOptions);
            yield return new WaitUntil(() => response.IsCompleted);
            rateLimited = response.Exception != null &&
                               (response.Exception.Message != null &&
                                (response.Exception.Message.Contains("Too Many Requests") || response.Exception.Message.Contains("429")));

            if (response.Exception == null)
            {
                // Filter to lobbies for the selected arena and with available slots
                var all = response.Result.Results;
                var sameArena = all.Where(l => l != null && l.Data != null && l.Data.ContainsKey("arena") && l.Data["arena"].Value == selectedArena.arenaID).ToList();

                // Update queue count for this arena (sum players in open lobbies)
                int queueCount = 0;
                foreach (var l in sameArena)
                {
                    // Consider only lobbies with at least one open slot to represent "queueing"
                    try
                    {
                        if ((l.MaxPlayers - (l.Players?.Count ?? 0)) > 0)
                        {
                            queueCount += (l.Players != null ? l.Players.Count : 0);
                        }
                    }
                    catch { /* ignore */ }
                }
                UpdateArenaQueueCount(queueCount);

                if (sameArena.Count > 0)
                {
                    // Found existing lobby for this arena, try to join it
                    var lobby = sameArena[0];
                
                string currentPlayerUsername = GetPlayerUsername();
                
                var joinOptions = new JoinLobbyByIdOptions
                {
                    Player = new Unity.Services.Lobbies.Models.Player
                    {
                        Data = new Dictionary<string, PlayerDataObject>
                        {
                            {"username", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, currentPlayerUsername)},
                            {"trophies", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, playerTrophies.ToString())},
                            {"deckSize", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, currentDeck.Count.ToString())},
                            {"ready", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, "0")}
                        }
                    }
                };
                
                    var joinResponse = LobbyService.Instance.JoinLobbyByIdAsync(lobby.Id, joinOptions);
                yield return new WaitUntil(() => joinResponse.IsCompleted);
                bool joinRateLimited = joinResponse.Exception != null &&
                                       (joinResponse.Exception.Message != null &&
                                        (joinResponse.Exception.Message.Contains("Too Many Requests") || joinResponse.Exception.Message.Contains("429")));
                    if (joinResponse.Exception == null)
                    {
                        currentLobby = joinResponse.Result;
                        UpdateLobbyInfoUI();

                        // Verify lobby arena matches selection
                        string lobbyArena = (currentLobby.Data != null && currentLobby.Data.ContainsKey("arena")) ? currentLobby.Data["arena"].Value : null;
                        if (string.IsNullOrEmpty(lobbyArena) || lobbyArena != selectedArena.arenaID)
                        {
                            Debug.LogWarning("Joined lobby arena mismatch; leaving and continuing search.");
                            LeaveLobby();
                        }
                        else
                        {
                            // Extract opponent information (the host who created the lobby)
                            ExtractOpponentFromLobby();
                            ShowOpponentFound();
                            
                            currentState = MatchmakingState.FoundMatch;
                            SetStatus("Match found! Joining...");
                            yield break;
                        }
                    }
                    else if (joinRateLimited)
                    {
                        // Rate limited when joining, back off and retry logic will be handled by outer loop
                        lobbyPollDelaySeconds = Mathf.Min(LobbyPollMaxDelay, lobbyPollDelaySeconds * LobbyPollBackoffFactor);
                        SetStatus("Rate limited. Slowing down join attempts...");
                        yield return new WaitForSeconds(lobbyPollDelaySeconds);
                    }
                }
            }
            else if (rateLimited)
            {
                // Rate limited on query, back off and retry later
                lobbyPollDelaySeconds = Mathf.Min(LobbyPollMaxDelay, lobbyPollDelaySeconds * LobbyPollBackoffFactor);
                SetStatus("Rate limited. Slowing down search...");
                yield return new WaitForSeconds(lobbyPollDelaySeconds);
            }
        }
        
        // No suitable lobby found, wait a short randomized delay to reduce simultaneous lobby creation races, then create one
        {
            float jitterDelay = Random.Range(0.2f, 0.8f);
            yield return new WaitForSeconds(jitterDelay);
        }
        // Create a new lobby for this arena
        string playerUsername = GetPlayerUsername();
        
        var createOptions = new CreateLobbyOptions
        {
            IsPrivate = false,
            Data = new Dictionary<string, DataObject>
            {
                {"arena", new DataObject(DataObject.VisibilityOptions.Public, selectedArena.arenaID)},
                {"trophies", new DataObject(DataObject.VisibilityOptions.Public, playerTrophies.ToString())},
                {"deckSize", new DataObject(DataObject.VisibilityOptions.Public, currentDeck.Count.ToString())},
                {"username", new DataObject(DataObject.VisibilityOptions.Public, playerUsername)}
            },
            Player = new Unity.Services.Lobbies.Models.Player
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    {"username", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, playerUsername)},
                    {"trophies", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, playerTrophies.ToString())},
                    {"deckSize", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, currentDeck.Count.ToString())},
                    {"ready", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, "0")}
                }
            }
        };
        
        var createResponse = LobbyService.Instance.CreateLobbyAsync($"Match_{selectedArena.arenaID}", 2, createOptions);
        yield return new WaitUntil(() => createResponse.IsCompleted);
        bool createRateLimited = createResponse.Exception != null &&
                                 (createResponse.Exception.Message != null &&
                                  (createResponse.Exception.Message.Contains("Too Many Requests") || createResponse.Exception.Message.Contains("429")));

        if (createResponse.Exception == null)
        {
            currentLobby = createResponse.Result;
            SetStatus("Waiting for opponent...");
            UpdateLobbyInfoUI();
            // Our newly created lobby represents 1 player queued in this arena
            UpdateArenaQueueCount(1);
            
            // Start polling for players joining
            if (pollLobbyCoroutine == null)
            {
                pollLobbyCoroutine = StartCoroutine(PollLobby());
            }
            // Start lobby heartbeat if we are the host to keep lobby alive
            if (lobbyHeartbeatCoroutine == null)
            {
                lobbyHeartbeatCoroutine = StartCoroutine(LobbyHeartbeat());
            }
        }
        else
        {
            Debug.LogError($"Failed to create lobby: {createResponse.Exception?.Message}");
            if (createRateLimited)
            {
                lobbyPollDelaySeconds = Mathf.Min(LobbyPollMaxDelay, lobbyPollDelaySeconds * LobbyPollBackoffFactor);
                SetStatus("Rate limited creating lobby. Retrying slower...");
                yield return new WaitForSeconds(lobbyPollDelaySeconds);
            }
            else
            {
                SetStatus("Failed to create match. Retrying...");
                yield return new WaitForSeconds(2f);
            }
        }
    }

    IEnumerator PollLobby()
    {
        while (currentLobby != null && isSearching)
        {
            var response = LobbyService.Instance.GetLobbyAsync(currentLobby.Id);
            yield return new WaitUntil(() => response.IsCompleted);

            bool rateLimited = response.Exception != null &&
                               (response.Exception.Message != null &&
                                (response.Exception.Message.Contains("Too Many Requests") || response.Exception.Message.Contains("429")));

            if (response.Exception == null)
            {
                currentLobby = response.Result;
                UpdateLobbyInfoUI();
                
                // Check if lobby is full (2 players)
                if (currentLobby.Players.Count >= 2)
                {
                    // Ensure lobby arena matches our selection before starting
                    string lobbyArena = (currentLobby.Data != null && currentLobby.Data.ContainsKey("arena")) ? currentLobby.Data["arena"].Value : null;
                    if (!string.IsNullOrEmpty(lobbyArena) && selectedArena != null && lobbyArena == selectedArena.arenaID)
                    {
                        currentState = MatchmakingState.FoundMatch;
                        
                        // Extract opponent information
                        ExtractOpponentFromLobby();
                        ShowOpponentFound();
                        
                        SetStatus("Opponent found!");
                        break;
                    }
                    else
                    {
                        Debug.LogWarning("Lobby full but arena mismatch; waiting/leaving.");
                        // Leave and reset to continue proper search
                        LeaveLobby();
                        currentLobby = null;
                        // brief pause to avoid tight loop
                        yield return new WaitForSeconds(0.5f);
                        continue;
                    }
                }

                // Successful poll - gently reduce delay toward minimum
                lobbyPollDelaySeconds = Mathf.Max(LobbyPollMinDelay, lobbyPollDelaySeconds * 0.9f);

                // If we are hosting our own lobby and it's still not full, occasionally look for another
                // same-arena lobby to join. This breaks the "both players created a lobby" deadlock.
                bool weAreHost = false;
                try { weAreHost = currentLobby.HostId == AuthenticationService.Instance.PlayerId; } catch { }
                bool skipJoinForDebug = forceHostWhenDebugging && Debug.isDebugBuild;
                if (!skipJoinForDebug && weAreHost && (Time.time - lastHostSwitchAttemptTime) > HostSwitchIntervalSeconds)
                {
                    lastHostSwitchAttemptTime = Time.time;
                    yield return StartCoroutine(ConsiderSwitchToExistingLobby());
                }
            }
            else
            {
                if (rateLimited)
                {
                    // Exponential backoff on 429
                    lobbyPollDelaySeconds = Mathf.Min(LobbyPollMaxDelay, lobbyPollDelaySeconds * LobbyPollBackoffFactor);
                    SetStatus("Server busy. Slowing polling...");
                }
                else
                {
                    Debug.LogError($"Failed to poll lobby: {response.Exception.Message}");
                    // For transient errors, wait a bit and continue; break only if lobby no longer valid
                }
            }

            // Add small jitter to avoid thundering herd
            float jitter = Random.Range(0.9f, 1.1f);
            yield return new WaitForSeconds(lobbyPollDelaySeconds * jitter);
        }

        // Clear handle when loop ends
        pollLobbyCoroutine = null;
    }

    IEnumerator ConsiderSwitchToExistingLobby()
    {
        // Query for open lobbies in the same arena; if found one hosted by someone else with a free slot, leave ours and join theirs.
        var queryOptions = new QueryLobbiesOptions
        {
            Count = 20,
            Filters = new List<QueryFilter>
            {
                new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "1", QueryFilter.OpOptions.GE)
            }
        };

        var response = LobbyService.Instance.QueryLobbiesAsync(queryOptions);
        yield return new WaitUntil(() => response.IsCompleted);
        if (response.Exception != null || response.Result == null)
        {
            yield break; // silent fail
        }

        string myPlayerId = string.Empty;
        try { myPlayerId = AuthenticationService.Instance.PlayerId; } catch { }
        string arenaId = selectedArena != null ? selectedArena.arenaID : null;
        if (string.IsNullOrEmpty(arenaId)) yield break;

        var candidates = response.Result.Results
            .Where(l => l != null && l.Id != (currentLobby?.Id ?? "") && l.Data != null && l.Data.ContainsKey("arena")
                        && l.Data["arena"].Value == arenaId && (l.MaxPlayers - (l.Players?.Count ?? 0) > 0))
            .ToList();

        if (candidates.Count == 0) yield break;

        var target = candidates[0];

        // Prepare join options
        string currentPlayerUsername = GetPlayerUsername();
        int playerTrophies = playerProgress?.trophies ?? 0;
        var joinOptions = new JoinLobbyByIdOptions
        {
            Player = new Unity.Services.Lobbies.Models.Player
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    {"username", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, currentPlayerUsername)},
                    {"trophies", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, playerTrophies.ToString())},
                    {"deckSize", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, currentDeck.Count.ToString())},
                    {"ready", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, "0")}
                }
            }
        };

        // Leave our own lobby first
        if (currentLobby != null)
        {
            var leaveTask = LobbyService.Instance.RemovePlayerAsync(currentLobby.Id, myPlayerId);
            yield return new WaitUntil(() => leaveTask.IsCompleted);
            // swallow any exception silently
            currentLobby = null;
        }

        var joinResp = LobbyService.Instance.JoinLobbyByIdAsync(target.Id, joinOptions);
        yield return new WaitUntil(() => joinResp.IsCompleted);
        if (joinResp.Exception == null)
        {
            currentLobby = joinResp.Result;
            SetStatus("Switched to existing lobby to pair faster...");
            UpdateLobbyInfoUI();
        }
        else
        {
            // If join failed, let the outer loop re-create a lobby on next iteration
            Debug.LogWarning($"[Matchmaking] Switch-to-existing join failed: {joinResp.Exception?.Message}");
        }
    }

    IEnumerator ArenaQueueLoop()
    {
        while (isSearching)
        {
            yield return RefreshArenaQueueCount();
            // Small jitter to avoid synchronized spikes
            float jitter = Random.Range(0.9f, 1.1f);
            yield return new WaitForSeconds(queueCountRefreshInterval * jitter);
        }
        queueCountCoroutine = null;
    }

    IEnumerator RefreshArenaQueueCount()
    {
        if (!useRealMultiplayer || selectedArena == null)
        {
            UpdateArenaQueueCount(0);
            yield break;
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            UpdateArenaQueueCount(0);
            yield break;
        }

        var queryOptions = new QueryLobbiesOptions
        {
            Count = 25,
            Filters = new List<QueryFilter>
            {
                new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "1", QueryFilter.OpOptions.GE)
            }
        };

        var response = LobbyService.Instance.QueryLobbiesAsync(queryOptions);
        yield return new WaitUntil(() => response.IsCompleted);
        if (response.Exception != null)
        {
            // Keep previous text; optionally show unknown
            yield break;
        }

        var all = response.Result.Results;
        int queueCount = 0;
        foreach (var l in all)
        {
            try
            {
                if (l != null && l.Data != null && l.Data.ContainsKey("arena") && l.Data["arena"].Value == selectedArena.arenaID)
                {
                    if ((l.MaxPlayers - (l.Players?.Count ?? 0)) > 0)
                    {
                        queueCount += (l.Players != null ? l.Players.Count : 0);
                    }
                }
            }
            catch { /* ignore */ }
        }
        UpdateArenaQueueCount(queueCount);
    }

    void StartArenaQueueCountLoop(bool continuous)
    {
        if (queueCountCoroutine != null)
        {
            StopCoroutine(queueCountCoroutine);
            queueCountCoroutine = null;
        }
        if (continuous)
        {
            queueCountCoroutine = StartCoroutine(ArenaQueueLoop());
        }
        else
        {
            // Trigger a single refresh
            StartCoroutine(RefreshArenaQueueCount());
        }
    }

    void UpdateArenaQueueCount(int count)
    {
        if (arenaQueueCountText != null)
        {
            string arenaName = selectedArena != null ? selectedArena.displayName : "Arena";
            arenaQueueCountText.text = $"{arenaName} queue: {count}";
        }
    }

    // Update lobby metadata shown in the panel (e.g., Lobby ID)
    private void UpdateLobbyInfoUI()
    {
        if (lobbyIdText != null)
        {
            string id = (currentLobby != null && !string.IsNullOrEmpty(currentLobby.Id)) ? currentLobby.Id : string.Empty;
#if UNITY_RELAY_INSTALLED
            string code = null;
            try
            {
                if (currentLobby != null && currentLobby.Data != null && currentLobby.Data.ContainsKey("joinCode"))
                {
                    code = currentLobby.Data["joinCode"].Value;
                }
            }
            catch { }
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(code))
            {
                lobbyIdText.text = $"Lobby: {id}  |  Code: {code}";
            }
            else if (!string.IsNullOrEmpty(id))
            {
                lobbyIdText.text = $"Lobby: {id}";
            }
            else
            {
                lobbyIdText.text = string.Empty;
            }
#else
            lobbyIdText.text = string.IsNullOrEmpty(id) ? string.Empty : $"Lobby: {id}";
#endif
        }
    }

    IEnumerator LobbyHeartbeat()
    {
        while (currentLobby != null && isSearching)
        {
            // Only the host should send heartbeat pings
            bool isHost = false;
            try
            {
                isHost = currentLobby != null && currentLobby.HostId == AuthenticationService.Instance.PlayerId;
            }
            catch { /* ignore */ }

            if (isHost)
            {
                var ping = LobbyService.Instance.SendHeartbeatPingAsync(currentLobby.Id);
                yield return new WaitUntil(() => ping.IsCompleted);
            }

            // Heartbeat recommended interval ~15s
            yield return new WaitForSeconds(15f);
        }
        lobbyHeartbeatCoroutine = null;
    }

    IEnumerator JoinMatch()
    {
        currentState = MatchmakingState.JoiningMatch;
        SetStatus("Joining match...");
        
        // Save current deck
        if (deckManager != null && currentDeck != null)
        {
            deckManager.SetStartingDeck(currentDeck, selectedArena);
            playerProgress?.SaveSelectedDeckForArena("global", currentDeck.Select(c => c.cardID).ToList());
        }
        
        // Small delay for dramatic effect
        yield return new WaitForSeconds(1f);
        
        // Start networking (Relay-backed) and transition
        _ = StartNetworkingAndEnterSceneAsync();
    }

    private async System.Threading.Tasks.Task StartNetworkingAndEnterSceneAsync()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager not found; cannot start networking.");
            return;
        }

        // Ensure all card prefabs are registered on this client before starting networking
        try { RegisterNetworkPrefabs(); } catch { }

        bool isHostRole = currentLobby != null && currentLobby.HostId == (AuthenticationService.Instance?.PlayerId ?? "");

        try
        {
            if (isHostRole)
            {
                bool ok = await SetupRelayAndStartHostAsync();
                if (!ok)
                {
                    Debug.LogWarning("Relay host setup failed; falling back to direct host.");
                    NetworkManager.Singleton.StartHost();
                }
            }
            else
            {
                bool ok = await SetupRelayAndStartClientAsync();
                if (!ok)
                {
                    Debug.LogWarning("Relay client setup failed; falling back to direct client.");
                    NetworkManager.Singleton.StartClient();
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to start networking: {ex.Message}");
            return;
        }

        // Load scene (host) or let server synchronize clients
        LoadArenaSceneNetworkAware();
    }

    private void RegisterNetworkPrefabs()
    {
        if (NetworkManager.Singleton == null) return;
        var dm = deckManager != null ? deckManager : FindFirstObjectByType<DeckManager>();
        if (dm == null || dm.allCards == null) return;
        foreach (var c in dm.allCards)
        {
            if (c == null) continue;
            if (c.unitPrefab != null)
            {
                try { NetworkManager.Singleton.AddNetworkPrefab(c.unitPrefab); } catch { }
            }
            if (c.spawnUnitPrefab != null)
            {
                try { NetworkManager.Singleton.AddNetworkPrefab(c.spawnUnitPrefab); } catch { }
            }
        }
    }

    private async System.Threading.Tasks.Task<bool> SetupRelayAndStartHostAsync()
    {
#if UNITY_RELAY_INSTALLED
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("UnityTransport missing on NetworkManager; cannot configure Relay.");
            return false;
        }
        try
        {
            // Choose protocol suitable for restrictive networks (default wss over 443)
            string protocol = PlayerPrefs.GetString("RelayProtocol", "wss"); // "wss" or "dtls"
            // Allocate for 1 client (2 total players)
            Allocation alloc = await RelayService.Instance.CreateAllocationAsync(1);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
            relayJoinCode = joinCode;

            if (currentLobby != null)
            {
                var updateTask = LobbyService.Instance.UpdateLobbyAsync(currentLobby.Id, new UpdateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject>
                    {
                        {"joinCode", new DataObject(DataObject.VisibilityOptions.Public, joinCode)}
                    }
                });
                await updateTask;
                var refreshed = await LobbyService.Instance.GetLobbyAsync(currentLobby.Id);
                currentLobby = refreshed;
            }

            bool started = false;
            System.Exception lastEx = null;
            foreach (var proto in new[] { protocol, protocol == "wss" ? "dtls" : "wss" })
            {
                try
                {
                    var serverData = new RelayServerData(alloc, proto);
                    transport.SetRelayServerData(serverData);
                    NetworkManager.Singleton.StartHost();
                    Debug.Log($"Relay host started. Protocol={proto} JoinCode: {joinCode}");
                    started = true;
                    break;
                }
                catch (System.Exception ex)
                {
                    lastEx = ex;
                    Debug.LogWarning($"Relay host start failed with protocol {proto}: {ex.Message}");
                }
            }
            return started;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Relay host setup failed: {ex.Message}");
            return false;
        }
#else
        // Fallback: start host directly when Relay package is not installed
        NetworkManager.Singleton.StartHost();
        return true;
#endif
    }

    private async System.Threading.Tasks.Task<bool> SetupRelayAndStartClientAsync()
    {
#if UNITY_RELAY_INSTALLED
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("UnityTransport missing on NetworkManager; cannot configure Relay.");
            return false;
        }
        try
        {
            string protocol = PlayerPrefs.GetString("RelayProtocol", "wss");
            string joinCode = relayJoinCode;
            // Robustly poll lobby data for joinCode published by host
            System.DateTime startWait = System.DateTime.UtcNow;
            System.TimeSpan maxWait = System.TimeSpan.FromSeconds(12);
            bool statusSet = false;
            while (string.IsNullOrEmpty(joinCode) && (currentLobby != null) && (System.DateTime.UtcNow - startWait) < maxWait)
            {
                try
                {
                    var refreshed = await LobbyService.Instance.GetLobbyAsync(currentLobby.Id);
                    currentLobby = refreshed;
                    if (currentLobby.Data != null && currentLobby.Data.ContainsKey("joinCode"))
                    {
                        joinCode = currentLobby.Data["joinCode"].Value;
                        break;
                    }
                }
                catch { }
                if (!statusSet)
                {
                    try { SetStatus("Waiting for host to publish code..."); } catch { }
                    statusSet = true;
                }
                await System.Threading.Tasks.Task.Delay(600);
            }
            if (string.IsNullOrEmpty(joinCode))
            {
                Debug.LogError("Relay join code missing; cannot join host.");
                return false;
            }
            JoinAllocation joinAlloc = await RelayService.Instance.JoinAllocationAsync(joinCode);
            bool started = false;
            System.Exception lastEx = null;
            foreach (var proto in new[] { protocol, protocol == "wss" ? "dtls" : "wss" })
            {
                try
                {
                    var serverData = new RelayServerData(joinAlloc, proto);
                    transport.SetRelayServerData(serverData);
                    NetworkManager.Singleton.StartClient();
                    Debug.Log($"Relay client started. Protocol={proto}");
                    started = true;
                    break;
                }
                catch (System.Exception ex)
                {
                    lastEx = ex;
                    Debug.LogWarning($"Relay client start failed with protocol {proto}: {ex.Message}");
                }
            }
            return started;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Relay client setup failed: {ex.Message}");
            return false;
        }
#else
        // Fallback: start client directly when Relay package is not installed
        NetworkManager.Singleton.StartClient();
        return true;
#endif
    }

    async void LeaveLobby()
    {
        if (currentLobby != null)
        {
            try
            {
                await LobbyService.Instance.RemovePlayerAsync(currentLobby.Id, AuthenticationService.Instance.PlayerId);
                currentLobby = null;
                UpdateLobbyInfoUI();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to leave lobby: {e.Message}");
            }
        }
    }
    
    void GenerateSimulatedOpponent()
    {
        // Generate random opponent data for simulation
        string[] possibleNames = {
            "DragonSlayer", "KnightRider", "WizardMaster", "ArcherQueen",
            "GoblinKing", "SteelWarrior", "FireMage", "IceWizard",
            "ThunderBolt", "ShadowNinja", "GoldenKnight", "CrystalMage",
            "IronFist", "StormBreaker", "FrostBite", "BlazeFury"
        };
        
        // Select random name
        opponentUsername = possibleNames[Random.Range(0, possibleNames.Length)];
        
        // Generate random player ID
        opponentPlayerId = "sim_" + Random.Range(1000, 9999).ToString();
        
        // Generate opponent trophies close to player's trophies (±200)
        int playerTrophies = playerProgress?.GetCurrentTrophies() ?? 0;
        opponentTrophies = playerTrophies + Random.Range(-200, 201);
        opponentTrophies = Mathf.Max(0, opponentTrophies); // Don't go below 0
        
        // Generate random deck size (usually 4-8 cards)
        opponentDeckSize = Random.Range(4, 9);
        
        Debug.Log($"Generated simulated opponent: {opponentUsername} ({opponentTrophies} trophies, {opponentDeckSize} cards)");
    }
    
    void ShowOpponentFound()
    {
        matchFound = true;
        // Complete the search bar visually
        if (matchmakingProgress != null)
        {
            matchmakingProgress.value = 1f;
        }

        // Update opponent username
        if (opponentUsernameText != null)
        {
            opponentUsernameText.text = $"vs {opponentUsername}";
        }
        
        // Update opponent trophy count
        if (opponentTrophyText != null)
        {
            opponentTrophyText.text = opponentTrophies.ToString();
        }
        
        // Update opponent deck size
        if (opponentDeckSizeText != null)
        {
            opponentDeckSizeText.text = $"Deck: {opponentDeckSize} cards";
        }
        
        // Show opponent UI elements
        if (opponentUIElements != null)
        {
            foreach (var element in opponentUIElements)
            {
                if (element != null)
                {
                    element.SetActive(true);
                }
            }
        }
        
        // Assign deterministic sides for multiplayer: Host = Player1, Client = Player2
        if (useRealMultiplayer && currentLobby != null)
        {
            bool iAmHost = false;
            try { iAmHost = (currentLobby.HostId == AuthenticationService.Instance.PlayerId); } catch { }
            localPlayerIsPlayer1 = iAmHost; // Host gets Player1, Client gets Player2
            string playerSide = localPlayerIsPlayer1 ? "Player 1" : "Player 2";
            Debug.Log($"Assigned (deterministic): Local player is {playerSide} (Host={iAmHost})");
            SetStatus($"Assigned {playerSide}. Starting...");
            if (playerSideToggle != null)
            {
                playerSideToggle.SetIsOnWithoutNotify(localPlayerIsPlayer1 == localIsPlayer1WhenToggleOn);
            }
            // Persist side early so battle scene auto-start logic has it even if host loads scene before StartMultiplayerMatch writes prefs
            try
            {
                PlayerPrefs.SetInt("LocalPlayerIsPlayer1", localPlayerIsPlayer1 ? 1 : 0);
                if (!string.IsNullOrEmpty(localPlayerUsername)) PlayerPrefs.SetString("LocalPlayerUsername", localPlayerUsername);
                if (!string.IsNullOrEmpty(opponentUsername)) PlayerPrefs.SetString("OpponentUsername", opponentUsername);
                PlayerPrefs.Save();
            }
            catch { }
        }
        
        // Update player side labels to show opponent names
        UpdatePlayerSideLabels();
        
        // Update status with opponent info
    SetStatus($"Found: {opponentUsername} ({opponentTrophies})");

        // Start the ready-up phase (both players must press Ready)
        StartReadyPhase();
    }
    
    void HideOpponentInfo()
    {
        // Clear opponent username
        if (opponentUsernameText != null)
        {
            opponentUsernameText.text = "";
        }
        
        // Clear opponent trophy count
        if (opponentTrophyText != null)
        {
            opponentTrophyText.text = "";
        }
        
        // Clear opponent deck size
        if (opponentDeckSizeText != null)
        {
            opponentDeckSizeText.text = "";
        }
        
        // Hide opponent UI elements
        if (opponentUIElements != null)
        {
            foreach (var element in opponentUIElements)
            {
                if (element != null)
                {
                    element.SetActive(false);
                }
            }
        }
        
        // Update player side labels to remove opponent names
        // Only update labels if not currently in practice starting sequence (to keep custom text)
        if (!isPracticeStarting)
        {
            UpdatePlayerSideLabels();
        }
        
        // Clear opponent data
        opponentUsername = "";
        opponentPlayerId = "";
        opponentTrophies = 0;
        opponentDeckSize = 0;
    }
    
    void ExtractOpponentFromLobby()
    {
        if (currentLobby == null || currentLobby.Players.Count < 2)
        {
            Debug.LogWarning("Cannot extract opponent - lobby not ready");
            return;
        }
        
        // Find the opponent (player who is not us)
        string myPlayerId = AuthenticationService.Instance.PlayerId;
        
        foreach (var player in currentLobby.Players)
        {
            if (player.Id != myPlayerId)
            {
                // This is our opponent
                opponentPlayerId = player.Id;
                
                // Extract username from player data
                if (player.Data != null && player.Data.ContainsKey("username"))
                {
                    opponentUsername = player.Data["username"].Value;
                }
                else
                {
                    // Fallback to player ID or generate name
                    opponentUsername = $"Player_{player.Id.Substring(0, 4)}";
                }
                
                // Extract trophies from player data
                if (player.Data != null && player.Data.ContainsKey("trophies"))
                {
                    if (int.TryParse(player.Data["trophies"].Value, out int trophies))
                    {
                        opponentTrophies = trophies;
                    }
                }
                
                // Extract deck size from player data
                if (player.Data != null && player.Data.ContainsKey("deckSize"))
                {
                    if (int.TryParse(player.Data["deckSize"].Value, out int deckSize))
                    {
                        opponentDeckSize = deckSize;
                    }
                }
                
                Debug.Log($"Found opponent: {opponentUsername} (ID: {opponentPlayerId}, Trophies: {opponentTrophies}, Deck: {opponentDeckSize})");
                break;
            }
        }
    }
    
    string GetPlayerUsername()
    {
        string username = playerProgress?.GetUsername() ?? "Anonymous";
        if (string.IsNullOrEmpty(username))
        {
            username = "Player_" + Random.Range(1000, 9999);
        }
        return username;
    }


    void UpdateMatchmakingProgress()
    {
        if (matchmakingProgress == null) return;

        switch (loadingPhase)
        {
            case LoadingPhase.Searching:
                if (!matchFound)
                {
                    // Auto-fill up to cap based on expected duration
                    float elapsed = Time.time - searchStartTimeForProgress;
                    if (expectedSearchDuration <= 0f) expectedSearchDuration = 10f;
                    float target = Mathf.Clamp01(elapsed / expectedSearchDuration);
                    target = Mathf.Min(target, searchBarAutoFillCap);
                    // Smooth step towards target
                    matchmakingProgress.value = Mathf.MoveTowards(matchmakingProgress.value, target, Time.deltaTime * 0.25f);
                }
                else
                {
                    matchmakingProgress.value = 1f;
                }
                // Estimated time display (time searching)
                if (estimatedTimeText != null)
                {
                    float searchElapsed = Time.time - searchStartTimeForProgress;
                    estimatedTimeText.text = $"Searching {searchElapsed:0}s";
                }
                break;
            case LoadingPhase.PreMatch:
                float preElapsed = Time.time - preMatchStartTime;
                float preNorm = Mathf.Clamp01(preElapsed / preMatchLoadDuration);
                matchmakingProgress.value = preNorm;
                if (estimatedTimeText != null)
                {
                    float remaining = Mathf.Max(0f, preMatchLoadDuration - preElapsed);
                    estimatedTimeText.text = $"Starting in {Mathf.CeilToInt(remaining)}s";
                }
                break;
            default:
                break;
        }
    }

    void UpdateLocalPlayerInfo()
    {
        // Update local player data
        localPlayerUsername = GetPlayerUsername();
        localPlayerTrophies = playerProgress?.trophies ?? 0;
        localPlayerDeckSize = ValidateDeckSilently() ? deckManager.selectedCards.Where(c => c != null).Count() : 0;
        
        // Update UI
        if (localPlayerUsernameText != null)
        {
            localPlayerUsernameText.text = localPlayerUsername;
        }
        
        if (localPlayerTrophyText != null)
        {
            localPlayerTrophyText.text = localPlayerTrophies.ToString();
        }
        
        if (localPlayerDeckSizeText != null)
        {
            // Show King Tower level instead of deck size
            int localKingLevel = 1;
            bool isOfflineMode = GameModeManager.Instance != null && GameModeManager.Instance.IsOfflineMode();
            if (isOfflineMode)
            {
                localKingLevel = ComputeAverageDeckLevelForSelectedDeck();
            }
            else
            {
                // Online default shown in lobby
                localKingLevel = 1;
            }
            localPlayerDeckSizeText.text = $"King Lv: {localKingLevel}";
        }
        
        // Update player side based on toggle (only for practice mode)
        if (!isSearching)
        {
            localPlayerIsPlayer1 = playerSideToggle != null ? 
                (playerSideToggle.isOn == localIsPlayer1WhenToggleOn) : true;
        }
    }

    void OnPlayerSideToggleChanged(bool toggleValue)
    {
        // Only allow manual toggle changes when not searching (practice mode)
        if (!isSearching)
        {
            localPlayerIsPlayer1 = toggleValue == localIsPlayer1WhenToggleOn;
            UpdatePlayerSideLabels();
            Debug.Log($"Player side changed: Local player is now {(localPlayerIsPlayer1 ? "Player 1" : "Player 2")}");
        }
        else
        {
            // During matchmaking, sides are randomly assigned - inform user
            Debug.Log("Player sides are randomly assigned during matchmaking");
        }
    }
    
    void UpdatePlayerSideLabels()
    {
        string randomIndicator = (isSearching && randomizePlayerSides) ? " [Random]" : "";
        if (isPracticeStarting)
        {
            if (player1Label != null) player1Label.text = "Player 1 (You)";
            if (player2Label != null) player2Label.text = "";
            return;
        }
        
        if (player1Label != null)
        {
            if (localPlayerIsPlayer1)
            {
                player1Label.text = $"Player 1 (You){randomIndicator}";
            }
            else
            {
                string opponentName = isSearching && !string.IsNullOrEmpty(opponentUsername) ? opponentUsername : "Opponent";
                player1Label.text = isSearching && !string.IsNullOrEmpty(opponentUsername) ? 
                    $"Player 1 ({opponentName}){randomIndicator}" : "Player 1";
            }
        }
        
        if (player2Label != null)
        {
            if (!localPlayerIsPlayer1)
            {
                player2Label.text = $"Player 2 (You){randomIndicator}";
            }
            else
            {
                string opponentName = isSearching && !string.IsNullOrEmpty(opponentUsername) ? opponentUsername : "Opponent";
                player2Label.text = isSearching && !string.IsNullOrEmpty(opponentUsername) ? 
                    $"Player 2 ({opponentName}){randomIndicator}" : "Player 2";
            }
        }
    }

    void UpdateUI()
    {
        // Update local player info
        UpdateLocalPlayerInfo();
        
        // Update trophy count
        if (trophyCountText != null && playerProgress != null)
        {
            trophyCountText.text = playerProgress.trophies.ToString();
        }
        
        // Update selected arena (already handled by dropdown selection)
        if (selectedArenaText != null)
        {
            if (selectedArena != null)
            {
                selectedArenaText.text = selectedArena.displayName;
            }
            else
            {
                selectedArenaText.text = "No Arena Selected";
            }
        }
        
        // Update arena preview sprite
        if (arenaPreviewImage != null)
        {
            if (selectedArena != null && selectedArena.preview != null)
            {
                arenaPreviewImage.sprite = selectedArena.preview;
                arenaPreviewImage.enabled = true;
            }
            else if (defaultArenaPreview != null)
            {
                arenaPreviewImage.sprite = defaultArenaPreview;
                arenaPreviewImage.enabled = true;
            }
            else
            {
                arenaPreviewImage.enabled = false;
            }
        }
        
        // Update button states based on deck completeness and game mode
        bool isDeckValid = ValidateDeckSilently();
        bool isOnlineMode = GameModeManager.Instance == null || GameModeManager.Instance.IsOnlineMode();
        
        if (findMatchButton != null)
        {
            // Disable multiplayer button if offline or deck invalid or searching
            findMatchButton.interactable = isDeckValid && !isSearching && isOnlineMode;
            var btnImage = findMatchButton.GetComponent<Image>();
            if (btnImage != null)
            {
                // Red if deck invalid, gray if offline mode, white if ready
                if (!isDeckValid)
                    btnImage.color = Color.red;
                else if (!isOnlineMode)
                    btnImage.color = Color.gray;
                else
                    btnImage.color = Color.white;
            }
        }
        
        if (practiceButton != null)
        {
            // Practice mode is always available when deck is valid
            practiceButton.interactable = isDeckValid && !isSearching;
        }

        // Display opponent's King Tower level (reusing opponentDeckSizeText field)
        if (opponentDeckSizeText != null)
        {
            int oppLevel = 1;
            bool isOffline = GameModeManager.Instance != null && GameModeManager.Instance.IsOfflineMode();
            if (isOffline)
            {
                // Prefer arena-configured bot level if available, else average deck level
                var dm = deckManager != null ? deckManager : FindFirstObjectByType<DeckManager>();
                var arena = dm != null ? dm.selectedArena : null;
                if (arena != null && arena.botKingLevel > 0)
                {
                    oppLevel = Mathf.Max(1, arena.botKingLevel);
                }
                else
                {
                    oppLevel = ComputeAverageDeckLevelForSelectedDeck();
                }
                opponentDeckSizeText.text = $"AI King Lv: {oppLevel}";
            }
            else
            {
                oppLevel = 1;
                opponentDeckSizeText.text = $"King Lv: {oppLevel}";
            }
        }
    }

    bool ValidateDeckSilently()
    {
        if (deckManager == null || deckManager.selectedCards == null) return false;
        
        var deck = deckManager.selectedCards.Where(c => c != null).ToList();
        return deck.Count >= minimumDeckSize && deck.Count <= maximumDeckSize;
    }

    // Compute average deck level for the selected arena; returns 1 if unavailable
    private int ComputeAverageDeckLevelForSelectedDeck()
    {
        try
        {
            if (deckManager == null || playerProgress == null) return 1;
            var cards = deckManager.selectedCards != null && deckManager.selectedCards.Count > 0
                ? deckManager.selectedCards
                : deckManager.deck;
            if (cards == null || cards.Count == 0) return 1;
            string arenaID = deckManager.selectedArena != null ? deckManager.selectedArena.arenaID : (deckManager.selectedArenaID ?? "default");
            int sum = 0; int count = 0;
            foreach (var c in cards)
            {
                if (c == null || string.IsNullOrEmpty(c.cardID)) continue;
                int lvl = playerProgress.GetCardLevel(c.cardID, arenaID);
                sum += Mathf.Max(1, lvl);
                count++;
            }
            if (count == 0) return 1;
            return Mathf.Max(1, Mathf.RoundToInt(sum / (float)count));
        }
        catch { return 1; }
    }
    
    void InitializeArenaDropdown()
    {
        if (arenaDropdown == null || arenaManager == null) return;
        // Make sure unlocked list reflects current trophies
        if (ArenaManager.Instance != null)
        {
            ArenaManager.Instance.EnsureArenasUnlockedByTrophies();
        }
        
        // Clear existing options
        arenaDropdown.ClearOptions();
        
        // Build options from all arenas; grey out locked ones, remove trophy icon
        var availableArenas = new List<TMP_Dropdown.OptionData>();
        arenasInDropdown = arenaManager.GetAllArenas();

        int trophies = playerProgress != null ? playerProgress.trophies : 0;
        for (int i = 0; i < arenasInDropdown.Count; i++)
        {
            var arena = arenasInDropdown[i];
            bool unlockedByTrophies = trophies >= arena.trophyRequirement;
            string label;
            if (!unlockedByTrophies)
            {
                // Grey out locked entries; show required trophies plainly (no emoji)
                label = $"<color=#999999>{arena.displayName} (Requires {arena.trophyRequirement} trophies)</color>";
            }
            else
            {
                // Show requirement for unlocked arenas too (informational)
                label = $"{arena.displayName} ({arena.trophyRequirement} trophies)";
            }
            availableArenas.Add(new TMP_Dropdown.OptionData(label));
        }

        arenaDropdown.AddOptions(availableArenas);

        // Default selection: highest index that is unlocked by trophies; else 0
        int defaultIndex = 0;
        for (int i = 0; i < arenasInDropdown.Count; i++)
        {
            if (trophies >= arenasInDropdown[i].trophyRequirement)
            {
                defaultIndex = i;
            }
        }

        lastValidArenaIndex = defaultIndex;
        arenaDropdown.SetValueWithoutNotify(defaultIndex);
        OnArenaDropdownChanged(defaultIndex);
    }
    
    void OnArenaDropdownChanged(int index)
    {
        if (arenaManager == null) return;
        if (arenasInDropdown == null || arenasInDropdown.Count == 0) return;
        if (index < 0 || index >= arenasInDropdown.Count) return;

        var arena = arenasInDropdown[index];
        int trophies = playerProgress != null ? playerProgress.trophies : 0;
        bool unlockedByTrophies = trophies >= arena.trophyRequirement;

        if (!unlockedByTrophies)
        {
            // Block selection; revert to last valid index
            arenaDropdown.SetValueWithoutNotify(lastValidArenaIndex);
            // Inform the user
            SetStatus($"Locked: requires {arena.trophyRequirement} trophies");
            // Refresh selectedArenaText to current valid selection
            var current = arenasInDropdown[lastValidArenaIndex];
            selectedArena = current;
            if (selectedArenaText != null)
            {
                selectedArenaText.text = current.displayName;
            }
            return;
        }

        // Accept selection
        selectedArena = arena;
        lastValidArenaIndex = index;

        // Update the selected arena text
        if (selectedArenaText != null)
        {
            selectedArenaText.text = selectedArena.displayName;
        }

        Debug.Log($"Selected arena: {selectedArena.displayName}");
        // Refresh queue count when selection changes
        StartArenaQueueCountLoop(isSearching);
    }

    /// <summary>
    /// Called when game mode changes between online and offline
    /// </summary>
    private void OnGameModeChanged(bool isOnline)
    {
        Debug.Log($"MatchmakingManager: Game mode changed to {(isOnline ? "ONLINE" : "OFFLINE")}");
        
        // Update useRealMultiplayer based on game mode
        useRealMultiplayer = isOnline;
        
        // Update UI to reflect current capabilities
        UpdateUI();
        
        // Update status message
        if (isOnline)
        {
            SetStatus("Online mode: Multiplayer and Practice available");
        }
        else
        {
            SetStatus("Offline mode: Practice mode only");
            
            // Cancel any ongoing matchmaking if we go offline
            if (isSearching)
            {
                CancelMatchmaking();
            }
        }
    }
    
    void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log($"Matchmaking Status: {message}");
    }

    IEnumerator PreMatchCountdown()
    {
        preMatchStarted = true;
        loadingPhase = LoadingPhase.PreMatch;
        preMatchStartTime = Time.time;
        if (matchmakingProgress != null) matchmakingProgress.value = 0f;
        while (isSearching && (Time.time - preMatchStartTime) < preMatchLoadDuration)
        {
            if (showPreMatchCountdownInStatus)
            {
                float remaining = Mathf.Max(0f, preMatchLoadDuration - (Time.time - preMatchStartTime));
                SetStatus($"Preparing... {Mathf.CeilToInt(remaining)}s");
            }
            yield return null;
        }
        if (!isSearching) yield break; // cancelled mid-countdown

        // Proceed to match start (different path for real vs simulated)
        if (useRealMultiplayer)
        {
            if (currentLobby != null && NetworkManager.Singleton != null && !NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
            {
                _ = StartNetworkingAndEnterSceneAsync();
            }
        }

        // After full countdown, perform final transition
        StartMultiplayerMatch();
    }

    void ApplyTrophyTextStyling()
    {
        // Set color of all trophy texts to gold
        if (trophyCountText != null) trophyCountText.color = trophyTextColor;
        if (localPlayerTrophyText != null) localPlayerTrophyText.color = trophyTextColor;
        if (opponentTrophyText != null) opponentTrophyText.color = trophyTextColor;
    }

    // --- Ready-up phase ---
    void StartReadyPhase()
    {
        inReadyPhase = true;
        // Reset states/UI
        localReady = false;
        opponentReady = false;
    if (readyPanel != null) readyPanel.SetActive(true);
    if (readyButton != null) readyButton.interactable = true;
    if (localReadyText != null) localReadyText.text = "You: Not";
    if (opponentReadyText != null) opponentReadyText.text = "Opp: Not";
    if (readyHintText != null) readyHintText.text = "Tap Ready";
    SetStatus("Waiting for Ready...");

        // Start polling lobby for ready flags
        if (readyPollCoroutine != null)
        {
            StopCoroutine(readyPollCoroutine);
            readyPollCoroutine = null;
        }
        readyPollCoroutine = StartCoroutine(ReadyPollLoop());
    }

    void OnReadyClicked()
    {
        // Toggle ready/unready unless pre-match already started
        if (preMatchStarted) return;
        bool target = !localReady;
        if (target)
        {
            SetStatus("Ready. Waiting...");
        }
        else
        {
            SetStatus("Not Ready. Waiting...");
        }
        StartCoroutine(UpdatePlayerReadyAsync(target));
    }

    IEnumerator UpdatePlayerReadyAsync(bool ready)
    {
        if (currentLobby == null)
            yield break;
        string myId = string.Empty;
        try { myId = AuthenticationService.Instance.PlayerId; } catch { }
        var update = LobbyService.Instance.UpdatePlayerAsync(currentLobby.Id, myId, new UpdatePlayerOptions
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                {"ready", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, ready ? "1" : "0")}
            }
        });
        yield return new WaitUntil(() => update.IsCompleted);
        if (update.Exception != null)
        {
            Debug.LogWarning($"[Matchmaking] Failed to set ready: {update.Exception?.Message}");
            if (readyButton != null) readyButton.interactable = true;
            yield break;
        }
        localReady = ready;
        UpdateReadyUI();
    }

    IEnumerator ReadyPollLoop()
    {
        while (isSearching && currentLobby != null && !(localReady && opponentReady))
        {
            var resp = LobbyService.Instance.GetLobbyAsync(currentLobby.Id);
            yield return new WaitUntil(() => resp.IsCompleted);
            if (resp.Exception == null && resp.Result != null)
            {
                currentLobby = resp.Result;
                // If opponent left during ready-up, stop matchmaking completely and show message
                if (inReadyPhase && (currentLobby.Players == null || currentLobby.Players.Count < 2))
                {
                    DoCancelMatchmaking("Match canceled");
                    yield break;
                }
                string myId = string.Empty;
                try { myId = AuthenticationService.Instance.PlayerId; } catch { }
                bool localR = localReady;
                bool oppR = opponentReady;
                foreach (var p in currentLobby.Players)
                {
                    if (p == null) continue;
                    string flag = (p.Data != null && p.Data.ContainsKey("ready")) ? p.Data["ready"].Value : "0";
                    bool isReady = flag == "1" || flag.ToLower() == "true";
                    if (p.Id == myId)
                    {
                        localR = isReady;
                    }
                    else
                    {
                        oppR = isReady;
                    }
                }
                localReady = localR;
                opponentReady = oppR;
                UpdateReadyUI();

                if (localReady && opponentReady)
                {
                    break;
                }
            }
            // wait interval
            yield return new WaitForSeconds(readyPollInterval);
        }

        readyPollCoroutine = null;

        if (isSearching && localReady && opponentReady)
        {
            if (readyPanel != null) readyPanel.SetActive(false);
            if (!preMatchStarted)
            {
                StartCoroutine(PreMatchCountdown());
            }
            // Ready phase is over once countdown begins
            inReadyPhase = false;
        }
    }

    void UpdateReadyUI()
    {
        if (localReadyText != null) localReadyText.text = localReady ? "You: Ready" : "You: Not";
        if (opponentReadyText != null) opponentReadyText.text = opponentReady ? "Opp: Ready" : "Opp: Not";
        // Keep button interactable to allow toggling ready on/off during ready phase
        if (readyButton != null) readyButton.interactable = true;
    }

    public void ShowMatchmakingPanel()
    {
        if (matchmakingPanel != null)
        {
            matchmakingPanel.SetActive(true);
        }
        
        UpdateToggleButtonText();
    }

    public void HideMatchmakingPanel()
    {
        if (matchmakingPanel != null)
        {
            matchmakingPanel.SetActive(false);
        }
        
        // Cancel matchmaking if we're hiding the panel
        if (isSearching)
        {
            CancelMatchmaking();
        }
        
        UpdateToggleButtonText();
    }

    public void ToggleMatchmakingPanel()
    {
        if (matchmakingPanel != null)
        {
            bool isActive = matchmakingPanel.activeSelf;
            
            if (isActive)
            {
                HideMatchmakingPanel();
            }
            else
            {
                ShowMatchmakingPanel();
            }
            
            UpdateToggleButtonText();
        }
    }
    
    void UpdateToggleButtonText()
    {
        if (toggleButtonText != null && matchmakingPanel != null)
        {
            bool isPanelActive = matchmakingPanel.activeSelf;
            toggleButtonText.text = isPanelActive ? "Close Panel" : "Open Panel";
        }
    }

    void OnDestroy()
    {
        // Unsubscribe from GameModeManager events
        GameModeManager.OnGameModeChanged -= OnGameModeChanged;
        
        // Clean up event listeners
        if (findMatchButton != null) findMatchButton.onClick.RemoveAllListeners();
        if (cancelMatchButton != null) cancelMatchButton.onClick.RemoveAllListeners();
        if (practiceButton != null) practiceButton.onClick.RemoveAllListeners();
        if (togglePanelButton != null) togglePanelButton.onClick.RemoveAllListeners();
    if (readyButton != null) readyButton.onClick.RemoveAllListeners();
        if (arenaDropdown != null) arenaDropdown.onValueChanged.RemoveAllListeners();
        if (playerSideToggle != null) playerSideToggle.onValueChanged.RemoveAllListeners();
        
        // Leave lobby if we're in one
        if (currentLobby != null)
        {
            LeaveLobby();
        }
        
        // Stop any running coroutines
        if (matchmakingCoroutine != null)
        {
            StopCoroutine(matchmakingCoroutine);
        }
        if (pollLobbyCoroutine != null)
        {
            StopCoroutine(pollLobbyCoroutine);
            pollLobbyCoroutine = null;
        }
        if (lobbyHeartbeatCoroutine != null)
        {
            StopCoroutine(lobbyHeartbeatCoroutine);
            lobbyHeartbeatCoroutine = null;
        }
        if (queueCountCoroutine != null)
        {
            StopCoroutine(queueCountCoroutine);
            queueCountCoroutine = null;
        }
    }
}