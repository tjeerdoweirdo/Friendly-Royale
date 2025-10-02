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
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

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
    private Lobby currentLobby;
    private bool useRealMultiplayer = true;

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

        // Setup UI event listeners
        if (findMatchButton != null)
        {
            findMatchButton.onClick.AddListener(StartMatchmaking);
        }
        
        if (cancelMatchButton != null)
        {
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

        // Initialize UI
        UpdateUI();
        UpdateToggleButtonText();
        
        // Initialize Unity Services for multiplayer
        InitializeUnityServices();
    }

    void Update()
    {
        if (isSearching)
        {
            UpdateMatchmakingProgress();
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
        
        // Update UI
        if (findMatchButton != null) findMatchButton.gameObject.SetActive(false);
        if (cancelMatchButton != null) cancelMatchButton.gameObject.SetActive(true);
        
        // Start matchmaking coroutine
        if (useRealMultiplayer)
        {
            matchmakingCoroutine = StartCoroutine(RealMatchmakingProcess());
        }
        else
        {
            matchmakingCoroutine = StartCoroutine(SimulateMatchmaking());
        }
        
        SetStatus("Searching for opponent...");
        Debug.Log("Started matchmaking for arena: " + selectedArena.arenaID);
    }

    public void CancelMatchmaking()
    {
        if (!isSearching) return;
        
        isSearching = false;
        currentState = MatchmakingState.Idle;
        
        // Stop matchmaking coroutine
        if (matchmakingCoroutine != null)
        {
            StopCoroutine(matchmakingCoroutine);
            matchmakingCoroutine = null;
        }
        
        // Leave lobby if we're in one
        if (currentLobby != null)
        {
            LeaveLobby();
        }
        
        // Update UI
        if (findMatchButton != null) findMatchButton.gameObject.SetActive(true);
        if (cancelMatchButton != null) cancelMatchButton.gameObject.SetActive(false);
        
        SetStatus("Matchmaking cancelled");
        Debug.Log("Matchmaking cancelled by player");
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
        SetStatus("Starting practice match...");
        
        // Save current deck
        if (deckManager != null && currentDeck != null)
        {
            deckManager.SetStartingDeck(currentDeck, selectedArena);
            playerProgress?.SaveSelectedDeckForArena("global", currentDeck.Select(c => c.cardID).ToList());
        }
        
        // Load the arena scene
        if (!string.IsNullOrEmpty(selectedArena.sceneName))
        {
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
        float phase1Time = simulatedMatchmakingTime * 0.4f; // 40% of time searching
        float phase2Time = simulatedMatchmakingTime * 0.3f; // 30% of time "found match"
        float phase3Time = simulatedMatchmakingTime * 0.3f; // 30% of time "joining"
        
        // Phase 1: Searching for opponent
        float phase1Start = Time.time;
        while (isSearching && (Time.time - phase1Start) < phase1Time)
        {
            SetStatus("Searching for opponent...");
            yield return new WaitForSeconds(0.5f);
            
            if ((Time.time - phase1Start) > searchTimeBeforeExpansion)
            {
                SetStatus("Expanding search range...");
            }
        }
        
        if (!isSearching) yield break;
        
        // Phase 2: Found opponent
        currentState = MatchmakingState.FoundMatch;
        SetStatus("Opponent found! Preparing match...");
        yield return new WaitForSeconds(phase2Time);
        
        if (!isSearching) yield break;
        
        // Phase 3: Joining match
        currentState = MatchmakingState.JoiningMatch;
        SetStatus("Starting multiplayer match...");
        yield return new WaitForSeconds(phase3Time);
        
        if (!isSearching) yield break;
        
        // Start the multiplayer match
        StartMultiplayerMatch();
    }

    void StartMultiplayerMatch()
    {
        // Save current deck
        if (deckManager != null && currentDeck != null)
        {
            deckManager.SetStartingDeck(currentDeck, selectedArena);
            playerProgress?.SaveSelectedDeckForArena("global", currentDeck.Select(c => c.cardID).ToList());
        }
        
        // For now, just start the same arena scene - in the future this would connect to Unity Netcode
        SetStatus("Joining multiplayer match...");
        
        // Load the arena scene
        if (!string.IsNullOrEmpty(selectedArena.sceneName))
        {
            SceneManager.LoadScene(selectedArena.sceneName);
        }
        else
        {
            Debug.LogError("Selected arena has no scene name assigned!");
            SetStatus("Error: Arena scene not configured");
        }
    }

    IEnumerator RealMatchmakingProcess()
    {
        while (isSearching)
        {
            // Try to find or create a lobby
            yield return StartCoroutine(FindOrCreateLobby());
            
            // If we found a match, break out of the loop
            if (currentState == MatchmakingState.FoundMatch)
            {
                break;
            }
            
            // Wait before trying again
            yield return new WaitForSeconds(2f);
        }
        
        if (currentState == MatchmakingState.FoundMatch)
        {
            yield return StartCoroutine(JoinMatch());
        }
    }

    IEnumerator FindOrCreateLobby()
    {
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            SetStatus("Not authenticated. Please restart the game.");
            yield break;
        }
        
        int playerTrophies = playerProgress?.trophies ?? 0;
        
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
        
        if (response.Exception == null && response.Result.Results.Count > 0)
        {
            // Found existing lobby, try to join it
            var lobby = response.Result.Results[0];
            var joinResponse = LobbyService.Instance.JoinLobbyByIdAsync(lobby.Id);
            yield return new WaitUntil(() => joinResponse.IsCompleted);
            
            if (joinResponse.Exception == null)
            {
                currentLobby = joinResponse.Result;
                currentState = MatchmakingState.FoundMatch;
                SetStatus("Match found! Joining...");
                yield break;
            }
        }
        
        // No suitable lobby found, create one
        var createOptions = new CreateLobbyOptions
        {
            IsPrivate = false,
            Data = new Dictionary<string, DataObject>
            {
                {"arena", new DataObject(DataObject.VisibilityOptions.Public, selectedArena.arenaID)},
                {"trophies", new DataObject(DataObject.VisibilityOptions.Public, playerTrophies.ToString())},
                {"deckSize", new DataObject(DataObject.VisibilityOptions.Public, currentDeck.Count.ToString())}
            }
        };
        
        var createResponse = LobbyService.Instance.CreateLobbyAsync($"Match_{selectedArena.arenaID}", 2, createOptions);
        yield return new WaitUntil(() => createResponse.IsCompleted);
        
        if (createResponse.Exception == null)
        {
            currentLobby = createResponse.Result;
            SetStatus("Waiting for opponent...");
            
            // Start polling for players joining
            StartCoroutine(PollLobby());
        }
        else
        {
            Debug.LogError($"Failed to create lobby: {createResponse.Exception?.Message}");
            SetStatus("Failed to create match. Retrying...");
        }
    }

    IEnumerator PollLobby()
    {
        while (currentLobby != null && isSearching)
        {
            var response = LobbyService.Instance.GetLobbyAsync(currentLobby.Id);
            yield return new WaitUntil(() => response.IsCompleted);
            
            if (response.Exception == null)
            {
                currentLobby = response.Result;
                
                // Check if lobby is full (2 players)
                if (currentLobby.Players.Count >= 2)
                {
                    currentState = MatchmakingState.FoundMatch;
                    SetStatus("Opponent found! Starting match...");
                    break;
                }
            }
            else
            {
                Debug.LogError($"Failed to poll lobby: {response.Exception.Message}");
                break;
            }
            
            yield return new WaitForSeconds(1f);
        }
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
        
        // Start the networking and load the scene
        if (NetworkManager.Singleton != null)
        {
            // Start as host if we created the lobby, client if we joined
            bool isHost = currentLobby.HostId == AuthenticationService.Instance.PlayerId;
            
            if (isHost)
            {
                NetworkManager.Singleton.StartHost();
            }
            else
            {
                NetworkManager.Singleton.StartClient();
            }
        }
        
        // Load the arena scene
        if (!string.IsNullOrEmpty(selectedArena.sceneName))
        {
            SceneManager.LoadScene(selectedArena.sceneName);
        }
        else
        {
            Debug.LogError("Selected arena has no scene name assigned!");
            SetStatus("Error: Arena scene not configured");
        }
    }

    async void LeaveLobby()
    {
        if (currentLobby != null)
        {
            try
            {
                await LobbyService.Instance.RemovePlayerAsync(currentLobby.Id, AuthenticationService.Instance.PlayerId);
                currentLobby = null;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to leave lobby: {e.Message}");
            }
        }
    }



    void UpdateMatchmakingProgress()
    {
        if (matchmakingProgress != null)
        {
            float searchDuration = Time.time - searchStartTime;
            float normalizedProgress = Mathf.PingPong(searchDuration * 0.5f, 1f);
            matchmakingProgress.value = normalizedProgress;
        }
        
        // Update estimated time
        if (estimatedTimeText != null)
        {
            float searchTime = Time.time - searchStartTime;
            int estimatedSeconds = Mathf.Max(0, (int)simulatedMatchmakingTime - (int)searchTime);
            estimatedTimeText.text = $"Est. {estimatedSeconds}s";
        }
    }

    void UpdateUI()
    {
        // Update trophy count
        if (trophyCountText != null && playerProgress != null)
        {
            trophyCountText.text = $"🏆 {playerProgress.trophies}";
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
        
        // Update button states based on deck completeness
        bool isDeckValid = ValidateDeckSilently();
        
        if (findMatchButton != null)
        {
            findMatchButton.interactable = isDeckValid && !isSearching;
            var btnImage = findMatchButton.GetComponent<Image>();
            if (btnImage != null)
            {
                btnImage.color = isDeckValid ? Color.white : Color.red;
            }
        }
        
        if (practiceButton != null)
        {
            practiceButton.interactable = isDeckValid && !isSearching;
        }
    }

    bool ValidateDeckSilently()
    {
        if (deckManager == null || deckManager.selectedCards == null) return false;
        
        var deck = deckManager.selectedCards.Where(c => c != null).ToList();
        return deck.Count >= minimumDeckSize && deck.Count <= maximumDeckSize;
    }
    
    void InitializeArenaDropdown()
    {
        if (arenaDropdown == null || arenaManager == null) return;
        
        // Clear existing options
        arenaDropdown.ClearOptions();
        
        // Get available arenas
        var availableArenas = new List<TMP_Dropdown.OptionData>();
        var arenas = arenaManager.GetUnlockedArenas();
        
        for (int i = 0; i < arenas.Count; i++)
        {
            var arena = arenas[i];
            string displayText = arena.displayName;
            
            // Add trophy requirement if available
            if (arena.trophyRequirement > 0)
            {
                displayText += $" ({arena.trophyRequirement}🏆)";
            }
            
            availableArenas.Add(new TMP_Dropdown.OptionData(displayText));
        }
        
        arenaDropdown.AddOptions(availableArenas);
        
        // Set default selection (first unlocked arena or first arena)
        int defaultIndex = 0;
        if (playerProgress != null)
        {
            for (int i = 0; i < arenas.Count; i++)
            {
                if (playerProgress.trophies >= arenas[i].trophyRequirement)
                {
                    defaultIndex = i;
                }
            }
        }
        
        arenaDropdown.value = defaultIndex;
        OnArenaDropdownChanged(defaultIndex);
    }
    
    void OnArenaDropdownChanged(int index)
    {
        if (arenaManager == null) return;
        
        var arenas = arenaManager.GetUnlockedArenas();
        if (index >= 0 && index < arenas.Count)
        {
            selectedArena = arenas[index];
            
            // Update the selected arena text
            if (selectedArenaText != null)
            {
                selectedArenaText.text = selectedArena.displayName;
            }
            
            Debug.Log($"Selected arena: {selectedArena.displayName}");
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
        // Clean up event listeners
        if (findMatchButton != null) findMatchButton.onClick.RemoveAllListeners();
        if (cancelMatchButton != null) cancelMatchButton.onClick.RemoveAllListeners();
        if (practiceButton != null) practiceButton.onClick.RemoveAllListeners();
        if (togglePanelButton != null) togglePanelButton.onClick.RemoveAllListeners();
        if (arenaDropdown != null) arenaDropdown.onValueChanged.RemoveAllListeners();
        
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
    }
}