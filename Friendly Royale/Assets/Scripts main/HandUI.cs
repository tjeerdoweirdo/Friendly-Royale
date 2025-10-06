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
    
    // Placement mode state
    private bool isInPlacementMode = false;
    private Card selectedCard = null;
    private int selectedCardIndex = -1;
    private Camera mainCamera;

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
        
        if (placementSystem == null)
        {
            Debug.LogWarning("[HandUI] No CardPlacementSystem found. Falling back to legacy spawn.");
            OnCardClickedLegacy(slotIndex);
            return;
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
            
            // Try to place the card
            Vector3 worldPos;
            if (placementSystem != null && placementSystem.TryGetPlacementPosition(ray, selectedCard, out worldPos))
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
        
        // Update placement preview
        if (placementSystem != null)
        {
            Vector2 mousePos = Input.mousePosition;
            Ray ray = mainCamera.ScreenPointToRay(mousePos);
            Vector3 worldPos;
            bool isValid = placementSystem.TryGetPlacementPosition(ray, selectedCard, out worldPos);
            placementSystem.UpdatePlacementPreview(worldPos, isValid);
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
                if (networkPlacement != null)
                {
                    networkPlacement.RequestCardPlacement(worldPosition, selectedCard, Unit.Faction.Player);
                }
                else if (cardSpawner != null)
                {
                    // Fallback to direct spawner for offline mode
                    StartCoroutine(cardSpawner.SpawnUnitAtPosition(selectedCard, worldPosition, Unit.Faction.Player));
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
                    Color originalColor = icon.color;
                    icon.color = new Color(originalColor.r, originalColor.g, originalColor.b, 1f) * 1.2f;
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
        if (slotIndex >= DeckManager.Instance.hand.Count) return;

        Card c = DeckManager.Instance.hand[slotIndex];
        
        // Check cost before proceeding
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
