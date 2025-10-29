using UnityEngine;
using System.Linq;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class HandUI : MonoBehaviour
{
    [Header("UI slots")]
    public List<Button> cardSlots; // UI buttons mapped to slots
    public List<Image> cardIcons;
    public TMP_Text[] costTexts;

    // Optional inspector reference (not required because we use CoinSystem.Instance)
    public CoinSystem coin;

    // Reference to the CardSpawner (drag in inspector or it will auto-find)
    [Header("Spawner (auto-find if empty)")]
    public CardSpawner cardSpawner;
    
    [Header("Placement Mode")]
    [Tooltip("CardPlacementSystem for click-to-place functionality")]
    public CardPlacementSystem placementSystem;
    
    [Header("Placement Backend (optional)")]
    [Tooltip("Explicit NetworkCardPlacementSystem to use. If assigned, HandUI will link the placement system to this object.")]
    public NetworkCardPlacementSystem networkPlacementOverride;
    
    [Header("Auto-link Network Placement")]
    [Tooltip("Automatically link to a spawned NetworkCardPlacementSystem when networking starts.")]
    public bool autoLinkNetworkPlacement = true;
    [Tooltip("Seconds between auto-link retries while networking is starting up.")]
    public float autoLinkRetryInterval = 2f;
    private float _nextAutoLinkTime = 0f;
    
    // Placement mode state
    private bool isInPlacementMode = false;
    private Card selectedCard = null;
    private int selectedCardIndex = -1;
    private Camera mainCamera;
    [Header("Debug")]
    [Tooltip("When true, bypass network and force local spawn for debugging placement issues")]
    public bool debugForceLocalSpawn = false;

    private bool subscribedToDeck = false;
    private bool subscribedToCoin = false;

    void Awake()
    {
        // try quick singleton fallback for coin system if not assigned
        if (coin == null && CoinSystem.Instance != null) coin = CoinSystem.Instance;

        // try to auto-find CardSpawner if not assigned
        if (cardSpawner == null)
        {
            cardSpawner = FindFirstObjectByType<CardSpawner>();
            if (cardSpawner == null)
                Debug.LogWarning("[HandUI] No CardSpawner found in scene.");
        }
        
        // try to auto-find CardPlacementSystem if not assigned
        if (placementSystem == null)
        {
            placementSystem = FindFirstObjectByType<CardPlacementSystem>();
            if (placementSystem == null)
                Debug.LogWarning("[HandUI] No CardPlacementSystem found in scene. Click-to-place will not work.");
        }
        // Ensure the placement system is linked to a (preferably spawned) NetworkCardPlacementSystem
        EnsureNetworkPlacementLinked();
        
        // get main camera reference
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            mainCamera = FindFirstObjectByType<Camera>();
        }
    }

    void OnEnable()
    {
        // Link ourselves to the persistent DeckManager if it exists.
        if (DeckManager.Instance != null)
        {
            DeckManager.Instance.handUI = this;
            Debug.Log("[HandUI] Linked to DeckManager.");
            RefreshHand();
            SubscribeToDeckEvents();
        }

        // Subscribe to coin changes if coin system is present
        if (coin == null && CoinSystem.Instance != null) coin = CoinSystem.Instance;
        SubscribeToCoinEvents();

        // Also watch for future scene loads so we re-link if necessary.
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        // unsubscribe from deck events
        UnsubscribeFromDeckEvents();

        // unsubscribe from coin events
        UnsubscribeFromCoinEvents();

        // If we were the linked HandUI on the DeckManager, clear the reference to avoid dangling refs.
        if (DeckManager.Instance != null && DeckManager.Instance.handUI == this)
        {
            DeckManager.Instance.handUI = null;
        }
    }

    void Start()
    {
        // extra safety: refresh when component starts
        RefreshHand();
    }
    
    void Update()
    {
        // Handle placement mode input
        if (isInPlacementMode)
        {
            HandlePlacementModeInput();
        }

        // Periodically attempt to auto-link to spawned NetworkCardPlacementSystem once networking is live
        if (autoLinkNetworkPlacement && Time.time >= _nextAutoLinkTime)
        {
            _nextAutoLinkTime = Time.time + Mathf.Max(0.2f, autoLinkRetryInterval);
            EnsureNetworkPlacementLinked();
        }
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // After a new scene loads, ensure DeckManager points at the HandUI in that scene.
        if (DeckManager.Instance != null)
        {
            DeckManager.Instance.handUI = this;
            Debug.Log($"[HandUI] Scene loaded '{scene.name}' - linked to DeckManager.");
            RefreshHand();
            SubscribeToDeckEvents();
        }

        // re-find spawner if needed
        if (cardSpawner == null)
        {
            cardSpawner = FindFirstObjectByType<CardSpawner>();
            if (cardSpawner == null)
                Debug.LogWarning("[HandUI] No CardSpawner found in scene after scene load.");
        }

        // re-find coin system and subscribe
        if (coin == null && CoinSystem.Instance != null)
        {
            coin = CoinSystem.Instance;
            SubscribeToCoinEvents();
        }

        // refresh UI baseline
        RefreshHand();
    }

    void SubscribeToDeckEvents()
    {
        if (subscribedToDeck) return;
        if (DeckManager.Instance != null)
        {
            DeckManager.Instance.OnHandChanged += RefreshHand;
            subscribedToDeck = true;
        }
    }

    void UnsubscribeFromDeckEvents()
    {
        if (!subscribedToDeck) return;
        if (DeckManager.Instance != null)
        {
            DeckManager.Instance.OnHandChanged -= RefreshHand;
        }
        subscribedToDeck = false;
    }

    void SubscribeToCoinEvents()
    {
        if (subscribedToCoin) return;
        if (coin != null)
        {
            coin.OnCoinsChanged += OnCoinsChanged;
            subscribedToCoin = true;
        }
    }

    void UnsubscribeFromCoinEvents()
    {
        if (!subscribedToCoin) return;
        if (coin != null)
        {
            coin.OnCoinsChanged -= OnCoinsChanged;
        }
        subscribedToCoin = false;
    }

    void OnCoinsChanged(int newCount)
    {
        // whenever coins change, refresh the UI so affordabilities are updated instantly
        RefreshHand();
    }

    Color GetRarityColor(CardRarity rarity)
    {
        switch (rarity)
        {
            case CardRarity.Common: return new Color(0.85f, 0.85f, 0.85f); // light gray
            case CardRarity.Rare: return new Color(0.2f, 0.6f, 1f); // blue
            case CardRarity.Epic: return new Color(0.7f, 0.2f, 0.9f); // purple
            case CardRarity.Legendary: return new Color(1f, 0.7f, 0.2f); // orange/gold
            default: return Color.white;
        }
    }

    Color GetCostTextColor(bool canAfford)
    {
        return canAfford ? Color.yellow : Color.red;
    }

    public void RefreshHand()
    {
        // safe-guard: if DeckManager isn't present yet, bail
        if (DeckManager.Instance == null) return;

        var hand = DeckManager.Instance.hand;

        for (int i = 0; i < cardSlots.Count; i++)
        {
            var btn = cardSlots[i];

            if (i < hand.Count)
            {
                // capture locals so callbacks use the correct values
                var localIndex = i;
                var localBtn = btn;
                Card c = hand[i];

                localBtn.gameObject.SetActive(true);

                // Coin check: only require enough coins for this card
                bool canAfford = false;
                if (CoinSystem.Instance != null)
                {
                    canAfford = CoinSystem.Instance.currentCoins >= c.coinCost;
                }

                // Set icon sprite and color
                if (cardIcons != null && i < cardIcons.Count)
                {
                    var localIcon = cardIcons[i];
                    localIcon.sprite = c.icon;
                    localIcon.color = canAfford ? GetRarityColor(c.rarity) : new Color(0.5f, 0.5f, 0.5f, 0.7f);
                }

                // Set cost text and color
                if (costTexts != null && i < costTexts.Length && costTexts[i] != null)
                {
                    costTexts[i].text = c.coinCost.ToString();
                    costTexts[i].color = GetCostTextColor(canAfford);
                }

                // Set scale
                localBtn.transform.localScale = Vector3.one * (canAfford ? 1.0f : 0.9f);

                // Set interactable state: allow play if you have enough coins for this card
                localBtn.interactable = canAfford;

                // Set up DraggableCard component for drag-and-drop functionality
                DraggableCard draggableCard = localBtn.GetComponent<DraggableCard>();
                if (draggableCard == null)
                {
                    draggableCard = localBtn.gameObject.AddComponent<DraggableCard>();
                }
                
                // Initialize the draggable card with current data
                draggableCard.Initialize(c, localIndex, this);
                
                // Remove old listeners and add the correct one with captured index
                localBtn.onClick.RemoveAllListeners();
                // Note: DraggableCard will handle clicks through its drag system
                // We keep this as fallback for non-draggable interactions
                localBtn.onClick.AddListener(() => OnCardClicked(localIndex));

                // Remove or add EventTrigger based on affordability
                var trigger = localBtn.GetComponent<UnityEngine.EventSystems.EventTrigger>();
                if (canAfford)
                {
                    if (trigger == null)
                        trigger = localBtn.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                    trigger.triggers = new List<UnityEngine.EventSystems.EventTrigger.Entry>();

                    // PointerEnter: scale to 1.1
                    var entryEnter = new UnityEngine.EventSystems.EventTrigger.Entry
                    {
                        eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter
                    };
                    entryEnter.callback.AddListener((data) => {
                        localBtn.transform.localScale = Vector3.one * 1.1f;
                    });
                    trigger.triggers.Add(entryEnter);

                    // PointerExit: scale back to 1.0
                    var entryExit = new UnityEngine.EventSystems.EventTrigger.Entry
                    {
                        eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit
                    };
                    entryExit.callback.AddListener((data) => {
                        localBtn.transform.localScale = Vector3.one * 1.0f;
                    });
                    trigger.triggers.Add(entryExit);
                }
                else
                {
                    // Remove EventTrigger if present
                    if (trigger != null)
                        DestroyImmediate(trigger);
                }
            }
            else
            {
                // no card for this slot — hide + remove listeners
                btn.onClick.RemoveAllListeners();
                btn.gameObject.SetActive(false);
                var trigger = btn.GetComponent<UnityEngine.EventSystems.EventTrigger>();
                if (trigger != null) trigger.triggers = new List<UnityEngine.EventSystems.EventTrigger.Entry>();
                
                // Remove DraggableCard component if present
                DraggableCard draggableCard = btn.GetComponent<DraggableCard>();
                if (draggableCard != null)
                {
                    DestroyImmediate(draggableCard);
                }
            }
        }

        // If we're currently in placement mode, reapply highlight to preserve user feedback.
        // This avoids the selected card looking deselected when coins regenerate (RefreshHand is called on coin gain).
        if (isInPlacementMode && selectedCardIndex >= 0 && selectedCardIndex < cardSlots.Count)
        {
            HighlightSelectedCard(selectedCardIndex, true);
        }
    }

    /// <summary>
    /// Public method for handling card clicks - enters placement mode
    /// </summary>
    public void OnCardClicked(int slotIndex)
    {
        if (DeckManager.Instance == null) return;
        if (slotIndex >= DeckManager.Instance.hand.Count) return;

        Card c = DeckManager.Instance.hand[slotIndex];
        Debug.Log($"[HandUI] OnCardClicked: Card '{c.cardName}' - entering placement mode");
        
        // Check if systems are available
        if (CoinSystem.Instance == null)
        {
            Debug.LogWarning("CoinSystem.Instance is null. Make sure a CoinSystem exists in the scene.");
            return;
        }
        
        // Ensure a CardPlacementSystem exists; if missing, try to create and link one automatically
        if (placementSystem == null)
        {
            TryEnsurePlacementSystem();
            if (placementSystem == null)
            {
                Debug.LogWarning("[HandUI] No CardPlacementSystem found or created. Falling back to legacy spawn.");
                OnCardClickedLegacy(slotIndex);
                return;
            }
        }

        // Check cost before entering placement mode
        if (CoinSystem.Instance.currentCoins < c.coinCost)
        {
            Debug.Log("[HandUI] Not enough coins to play card.");
            ShowElixirError();
            return;
        }
        
        // Enter placement mode
        EnterPlacementMode(c, slotIndex);
    }

    /// <summary>
    /// Attempts to auto-create and link a CardPlacementSystem at runtime so click-to-place works without manual setup.
    /// </summary>
    private void TryEnsurePlacementSystem()
    {
        // Try to find an existing backend first
        if (placementSystem == null)
        {
            placementSystem = FindFirstObjectByType<CardPlacementSystem>();
        }
        if (placementSystem != null)
        {
            // Make sure it's linked to the Network backend
            if (placementSystem.networkPlacement == null)
            {
                placementSystem.networkPlacement = networkPlacementOverride ?? (FindSpawnedPlacementSystem() ?? NetworkCardPlacementSystem.Instance ?? FindFirstObjectByType<NetworkCardPlacementSystem>());
            }
            return;
        }

        // Create a new one if not found
        var cpsGO = new GameObject("CardPlacementSystem");
        placementSystem = cpsGO.AddComponent<CardPlacementSystem>();
        // Link to backend
        placementSystem.networkPlacement = networkPlacementOverride ?? (FindSpawnedPlacementSystem() ?? NetworkCardPlacementSystem.Instance ?? FindFirstObjectByType<NetworkCardPlacementSystem>());
        if (placementSystem.networkPlacement == null)
        {
            Debug.LogWarning("[HandUI] NetworkCardPlacementSystem not found. Placement preview will be limited.");
        }
        else
        {
            Debug.Log("[HandUI] Auto-created CardPlacementSystem and linked backend: " + placementSystem.networkPlacement.name);
        }
        Debug.Log("[HandUI] Auto-created CardPlacementSystem and linked backend.");
    }

    // Ensure our placement system is linked to a viable NetworkCardPlacementSystem; prefer spawned for RPCs
    private void EnsureNetworkPlacementLinked()
    {
        if (placementSystem == null) return;
        // If already linked to a spawned NCPS, nothing to do
        if (placementSystem.networkPlacement != null)
        {
            var no = placementSystem.networkPlacement.GetComponent<Unity.Netcode.NetworkObject>();
            if (no != null && no.IsSpawned)
            {
                return;
            }
        }

        // If an explicit override is set, prefer that
        if (networkPlacementOverride != null)
        {
            placementSystem.networkPlacement = networkPlacementOverride;
            // Warn if networking is active and the override is not spawned, as RPCs must originate from spawned objects
            bool netActive = Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening;
            var ono = networkPlacementOverride.GetComponent<Unity.Netcode.NetworkObject>();
            if (netActive && (ono == null || !ono.IsSpawned))
            {
                Debug.LogWarning("[HandUI] Using inspector-assigned NetworkCardPlacementSystem that is not a spawned NetworkObject. Preview is fine, but RPCs will be routed via a spawned instance.");
            }
            return;
        }

        var spawned = FindSpawnedPlacementSystem();
        if (spawned != null)
        {
            placementSystem.networkPlacement = spawned;
            Debug.Log("[HandUI] Linked to spawned NetworkCardPlacementSystem: " + spawned.name);
            return;
        }

        // Fallbacks
        if (placementSystem.networkPlacement == null)
        {
            var inst = NetworkCardPlacementSystem.Instance;
            if (inst != null)
            {
                placementSystem.networkPlacement = inst;
                Debug.Log("[HandUI] Linked to NetworkCardPlacementSystem.Instance: " + inst.name);
                return;
            }
            var any = FindFirstObjectByType<NetworkCardPlacementSystem>();
            if (any != null)
            {
                placementSystem.networkPlacement = any;
                Debug.Log("[HandUI] Linked to first NetworkCardPlacementSystem found: " + any.name);
            }
        }
    }

    private NetworkCardPlacementSystem FindSpawnedPlacementSystem()
    {
        var all = FindObjectsByType<NetworkCardPlacementSystem>(FindObjectsSortMode.None);
        foreach (var sys in all)
        {
            if (sys == null) continue;
            var no = sys.GetComponent<Unity.Netcode.NetworkObject>();
            if (no != null && no.IsSpawned) return sys;
        }
        return null;
    }

    /// <summary>
    /// Enter placement mode for the selected card
    /// </summary>
    private void EnterPlacementMode(Card card, int cardIndex)
    {
        if (isInPlacementMode)
        {
            ExitPlacementMode(); // Exit current placement mode first
        }
        
        isInPlacementMode = true;
        selectedCard = card;
        selectedCardIndex = cardIndex;
        
        // Start placement system
        if (placementSystem != null)
        {
            placementSystem.BeginCardPlacement(card);
        }
        
        // Visual feedback - highlight the selected card
        HighlightSelectedCard(cardIndex, true);
        
        Debug.Log($"[HandUI] Entered placement mode for {card.cardName}. Click on battlefield to place.");
    }
    
    /// <summary>
    /// Exit placement mode
    /// </summary>
    private void ExitPlacementMode()
    {
        if (!isInPlacementMode) return;
        
        // End placement system
        if (placementSystem != null)
        {
            placementSystem.EndCardPlacement();
        }

        // Hide any network placement preview
        var netPreview = NetworkCardPlacementSystem.Instance;
        if (netPreview != null)
        {
            netPreview.HidePlacementPreview();
        }
        
        // Remove visual feedback
        if (selectedCardIndex >= 0)
        {
            HighlightSelectedCard(selectedCardIndex, false);
        }
        
        isInPlacementMode = false;
        selectedCard = null;
        selectedCardIndex = -1;
        
        Debug.Log("[HandUI] Exited placement mode.");
    }
    
    /// <summary>
    /// Handle input during placement mode
    /// </summary>
    private void HandlePlacementModeInput()
    {
        if (!isInPlacementMode || selectedCard == null || mainCamera == null) return;
        
        // Handle mouse/touch input
        if (Input.GetMouseButtonDown(0))
        {
            Vector2 screenPos = Input.mousePosition;
            Ray ray = mainCamera.ScreenPointToRay(screenPos);
            
            // Check if we clicked on UI (ignore placement if so)
            if (UnityEngine.EventSystems.EventSystem.current != null && 
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                return; // Clicked on UI, ignore
            }
            
            // Prefer unified wrapper so range indicator and preview stay in sync
            Vector3 worldPos = Vector3.zero;
            bool isValid = false;
            bool hasPos = false;
            if (placementSystem != null)
            {
                hasPos = placementSystem.TryGetValidPlacementPosition(ray, selectedCard, out worldPos, out isValid);
            }
            else
            {
                // Last resort: use network system directly
                var net = NetworkCardPlacementSystem.Instance;
                if (net != null)
                {
                    worldPos = net.GetWorldPositionFromScreen(screenPos);
                    if (worldPos != Vector3.zero)
                    {
                        ulong cid = Unity.Netcode.NetworkManager.Singleton != null ? Unity.Netcode.NetworkManager.Singleton.LocalClientId : 0UL;
                        isValid = net.IsValidPlacementPosition(worldPos, selectedCard, Unit.Faction.Player, cid);
                        hasPos = true;
                    }
                }
            }

            if (hasPos && isValid)
            {
                PlaceSelectedCard(worldPos);
            }
            else
            {
                Debug.Log("[HandUI] Invalid placement position.");
                // Could add audio/visual feedback here for invalid placement
            }
        }
        
        // Handle cancellation (right click or escape)
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
        {
            ExitPlacementMode();
        }
        
        // Update placement preview (prefer network-aware indicator so Player 2 sees correct feedback)
        {
            Vector2 mousePos = Input.mousePosition;
            Ray ray = mainCamera.ScreenPointToRay(mousePos);

            // Always go through placementSystem so both preview indicator and range circle update
            if (placementSystem != null)
            {
                Vector3 worldPos;
                bool isValid = false;
                bool hasPos = placementSystem.TryGetValidPlacementPosition(ray, selectedCard, out worldPos, out isValid);
                if (hasPos)
                {
                    placementSystem.UpdatePlacementPreview(worldPos, isValid);
                }
            }
            else
            {
                // Fallback to network-only preview
                var net = NetworkCardPlacementSystem.Instance;
                if (net != null)
                {
                    Vector3 worldPos = net.GetWorldPositionFromScreen(mousePos);
                    bool isValid = worldPos != Vector3.zero;
                    if (isValid)
                    {
                        ulong cid = Unity.Netcode.NetworkManager.Singleton != null ? Unity.Netcode.NetworkManager.Singleton.LocalClientId : 0UL;
                        isValid = net.IsValidPlacementPosition(worldPos, selectedCard, Unit.Faction.Player, cid);
                    }
                    net.ShowPlacementPreview(worldPos, selectedCard, Unit.Faction.Player, isValid);
                }
            }
        }
    }
    
    /// <summary>
    /// Place the selected card at the specified world position
    /// </summary>
    private void PlaceSelectedCard(Vector3 worldPosition)
    {
        if (!isInPlacementMode || selectedCard == null) return;
        
        // Check and spend coins
        if (CoinSystem.Instance != null && CoinSystem.Instance.currentCoins >= selectedCard.coinCost)
        {
            bool paid = CoinSystem.Instance.SpendCoins(selectedCard.coinCost);
            if (paid)
            {
                // Play the card through DeckManager
                if (DeckManager.Instance != null)
                {
                    DeckManager.Instance.PlayCard(selectedCard);
                }
                
                // Use NetworkCardPlacementSystem for proper multiplayer support
                NetworkCardPlacementSystem networkPlacement = NetworkCardPlacementSystem.Instance;
                if (networkPlacement != null && !debugForceLocalSpawn)
                {
                    Debug.Log("[HandUI] Using NetworkCardPlacementSystem.RequestCardPlacement (multiplayer path)");
                    networkPlacement.RequestCardPlacement(worldPosition, selectedCard, Unit.Faction.Player);
                }
                else if (cardSpawner != null)
                {
                    // Fallback: if networking is running, use spawner ServerRpc; else local spawn
                    bool netReady = Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening;
                    if (debugForceLocalSpawn)
                    {
                        Debug.Log("[HandUI] debugForceLocalSpawn: forcing local spawn via CardSpawner.SpawnUnitAtPosition");
                        StartCoroutine(cardSpawner.SpawnUnitAtPosition(selectedCard, worldPosition, Unit.Faction.Player));
                    }
                    else if (netReady)
                    {
                        var nm = Unity.Netcode.NetworkManager.Singleton;
                        bool isHost = nm.IsServer; // server/host side
                        ulong clientId = nm.LocalClientId;
                        if (isHost)
                        {
                            Debug.Log("[HandUI] Host detected: spawning directly via CardSpawner.SpawnAuthoritative");
                            cardSpawner.SpawnAuthoritative(selectedCard.cardID, worldPosition, Unit.Faction.Player, clientId);
                        }
                        else
                        {
                            // Client path: ensure spawner has a spawned NetworkObject before calling ServerRpc
                            var no = cardSpawner.GetComponent<Unity.Netcode.NetworkObject>();
                            if (no == null || !no.IsSpawned)
                            {
                                Debug.LogError("[HandUI] CardSpawner has no spawned NetworkObject on client; cannot send ServerRpc. Ensure your arena scene is loaded via NGO and CardSpawner has a NetworkObject component.");
                                // Try network placement system as a more robust route (server will find spawner)
                                var net = NetworkCardPlacementSystem.Instance;
                                if (net != null)
                                {
                                    Debug.Log("[HandUI] Redirecting to NetworkCardPlacementSystem.RequestCardPlacement (client -> server)");
                                    net.RequestCardPlacement(worldPosition, selectedCard, Unit.Faction.Player);
                                }
                                else
                                {
                                    Debug.LogWarning("[HandUI] No NetworkCardPlacementSystem in scene; falling back to local spawn (client-only, not authoritative)");
                                    StartCoroutine(cardSpawner.SpawnUnitAtPosition(selectedCard, worldPosition, Unit.Faction.Player));
                                }
                            }
                            else
                            {
                                Debug.Log("[HandUI] Network active: sending RequestSpawnServerRpc to CardSpawner");
                                cardSpawner.RequestSpawnServerRpc(worldPosition, selectedCard.cardID, Unit.Faction.Player, clientId);
                            }
                        }
                    }
                    else
                    {
                        Debug.Log("[HandUI] Network not active: local spawn via CardSpawner.SpawnUnitAtPosition");
                        StartCoroutine(cardSpawner.SpawnUnitAtPosition(selectedCard, worldPosition, Unit.Faction.Player));
                    }
                }
                else
                {
                    Debug.LogWarning("[HandUI] No CardSpawner or NetworkCardPlacementSystem found for placement!");
                }
                
                Debug.Log($"[HandUI] Placed card {selectedCard.cardName} at {worldPosition}");
                
                // Exit placement mode
                ExitPlacementMode();
                
                // Refresh hand UI
                RefreshHand();
            }
            else
            {
                Debug.LogWarning("[HandUI] Failed to spend coins for card placement.");
                ExitPlacementMode();
            }
        }
        else
        {
            Debug.LogWarning("[HandUI] Not enough coins for card placement.");
            ShowElixirError();
            ExitPlacementMode();
        }
    }
    
    /// <summary>
    /// Highlight or unhighlight a card slot during placement mode
    /// </summary>
    private void HighlightSelectedCard(int cardIndex, bool highlight)
    {
        if (cardIndex < 0 || cardIndex >= cardSlots.Count) return;
        
        Button cardButton = cardSlots[cardIndex];
    if (cardButton == null) return;
        
        if (highlight)
        {
            // Add visual feedback - scale up and change color
            cardButton.transform.localScale = Vector3.one * 1.15f;
            
            // Add a subtle color tint
            if (cardIcons != null && cardIndex < cardIcons.Count)
            {
                Image icon = cardIcons[cardIndex];
                if (icon != null)
                {
                    // Recompute a stable base color (prevents cumulative brightening on repeated RefreshHand calls)
                    Color baseColor = icon.color;
                    // If we can access the card data, derive the intended base color directly
                    if (DeckManager.Instance != null && cardIndex < DeckManager.Instance.hand.Count)
                    {
                        var card = DeckManager.Instance.hand[cardIndex];
                        bool canAfford = CoinSystem.Instance != null && CoinSystem.Instance.currentCoins >= card.coinCost;
                        baseColor = canAfford ? GetRarityColor(card.rarity) : new Color(0.5f, 0.5f, 0.5f, 0.7f);
                    }
                    // Apply highlight multiplier once
                    var highlighted = baseColor * 1.2f;
                    highlighted.a = 1f; // ensure fully opaque
                    icon.color = highlighted;
                }
            }
        }
        else
        {
            // Remove visual feedback - restore normal appearance
            // RefreshHand will restore the proper scale and colors
            RefreshHand();
        }
    }
    
    /// <summary>
    /// Legacy card click method for fallback when placement system is not available
    /// </summary>
    private void OnCardClickedLegacy(int slotIndex)
    {
        if (DeckManager.Instance == null) return;

    Card c = DeckManager.Instance.hand[slotIndex];
        if (CoinSystem.Instance.currentCoins < c.coinCost)
        {
            ShowElixirError();
            return;
        }

        // Spend coins and play the card
        bool paid = CoinSystem.Instance.SpendCoins(c.coinCost);
        if (!paid) return;

        DeckManager.Instance.PlayCard(c);

        // Legacy click-to-play: spawn on left side
        if (cardSpawner != null)
        {
            cardSpawner.SpawnOnSideImmediate(true, c, Unit.Faction.Player);
        }
        else
        {
            Vector3 world = new Vector3(-2f, 0f, 0f);
            StartCoroutine(FindSpawnerAndSpawnFallback(c, world));
        }

        RefreshHand();
    }
    
    // Simple error notification for not enough elixir/coins
    void ShowElixirError()
    {
        // TODO: Replace with your own UI popup/animation if desired
        Debug.LogWarning("Not enough elixir/coins to play this card!");
        // Example: You could trigger a UI animation or sound here
    }

    IEnumerator FindSpawnerAndSpawnFallback(Card c, Vector3 worldPos)
    {
        // wait a frame so scene objects have a chance to exist
        yield return null;
        CardSpawner sp = FindFirstObjectByType<CardSpawner>();
        if (sp != null)
        {
            StartCoroutine(sp.SpawnUnitAtPosition(c, worldPos, Unit.Faction.Player));
        }
        else
        {
            Debug.LogWarning("[HandUI] No CardSpawner found for fallback spawn.");
        }
    }
}