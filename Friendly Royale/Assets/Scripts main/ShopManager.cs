using UnityEngine;
using System;
using System.Collections;
using System.Linq;
using System.Collections.Generic;

using TMPro;

public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance { get; private set; }

    [Header("Upgrade Panel UI Elements")]
    public CanvasGroup shopCanvasGroup;
    public RectTransform shopPanelRectTransform;
    public float slideDuration = 0.5f;
    public Vector2 hiddenPosition = new Vector2(-600, 0); // Off-screen left
    public Vector2 shownPosition = new Vector2(0, 0); // On-screen
    public UnityEngine.UI.Button closeButton; // Assign in Inspector
    private bool isShopVisible = false;
    private Coroutine slideCoroutine;
    private Settingspanel settingsPanel;

    [Header("Shop Upgrade Slots")]
    public int shopSlotCount = 5;
    [System.Serializable]
    public class ShopUpgradeSlot
    {
        public Card card;
        public int upgradeLimit;
        public int upgradesBought;
    }
    public ShopUpgradeSlot[] shopSlots;
    [Tooltip("Prefab for a single shop slot (should have a TMP_Text for name, limit, and a Button)")]
    public GameObject shopSlotPrefab;
    [Tooltip("Parent transform for each shop slot UI element (size must match shopSlotCount)")]
    public Transform[] shopSlotParents;
    [Tooltip("Button to refresh the shop manually")]
    public UnityEngine.UI.Button refreshShopButton;
    private GameObject[] shopSlotUIs;

    [Header("Upgrade pricing")]
    public int goldCostPerLevel = 100;

    // Event for UI to listen to upgrades
    public event Action<Card, Arena, int> OnCardUpgraded;
    public event Action<Card, Arena, string> OnUpgradeFailed; // Optional: for UI feedback

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // Initialize shop slots
        shopSlots = new ShopUpgradeSlot[shopSlotCount];
        for (int i = 0; i < shopSlotCount; i++)
        {
            shopSlots[i] = new ShopUpgradeSlot();
        }
        // Setup refresh button
        if (refreshShopButton != null)
            refreshShopButton.onClick.AddListener(RefreshShopSlots);

        // Error checks for prefab and parents
        if (shopSlotPrefab == null)
            Debug.LogWarning("ShopManager: shopSlotPrefab is not assigned!");
        if (shopSlotParents == null || shopSlotParents.Length != shopSlotCount)
            Debug.LogWarning($"ShopManager: shopSlotParents must be assigned and have {shopSlotCount} elements!");

        // Create UI for each slot using EntryShopUI
        if (shopSlotPrefab != null && shopSlotParents != null && shopSlotParents.Length == shopSlotCount)
        {
            shopSlotUIs = new GameObject[shopSlotCount];
            for (int i = 0; i < shopSlotCount; i++)
            {
                var go = Instantiate(shopSlotPrefab, shopSlotParents[i]);
                shopSlotUIs[i] = go;
            }
        }

        RefreshShopSlots();
        if (shopCanvasGroup != null)
        {
            HideShopImmediate();
        }
        if (shopPanelRectTransform != null)
        {
            shopPanelRectTransform.anchoredPosition = hiddenPosition;
        }
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(CloseShop);
        }
        // Find Settingspanel in scene (or assign via Inspector for best practice)
    settingsPanel = FindFirstObjectByType<Settingspanel>();
    }

    void Update()
    {
        // (Timer and UI update logic will be added in later steps)
        // Optional: Toggle shop with a key (e.g., S)
        if (shopCanvasGroup != null && Input.GetKeyDown(KeyCode.S))
        {
            ToggleShopSlide();
        }
    }

    // Call this to randomize shop slots and their upgrade limits
    public void RefreshShopSlots()
    {
        // Find all Card assets in Resources or a known folder
        // Try to get all cards from DeckManager in the scene
        List<Card> allCards = null;
        DeckManager deckManager = FindFirstObjectByType<DeckManager>();
        if (deckManager != null && deckManager.allCards != null && deckManager.allCards.Count > 0)
        {
            allCards = deckManager.allCards;
            Debug.Log($"ShopManager: Loaded {allCards.Count} Card assets from DeckManager.");
        }
        else
        {
            Card[] resourcesCards = Resources.LoadAll<Card>("");
            allCards = resourcesCards.ToList();
            Debug.Log($"ShopManager: Loaded {allCards.Count} Card assets from Resources as fallback.");
        }
        // Only include cards that can be upgraded (not at max level)
        var upgradable = allCards.Where(card => card != null && card.maxLevel > 1).ToList();
        Debug.Log($"ShopManager: Found {upgradable.Count} upgradable cards.");
        if (upgradable.Count < shopSlotCount)
        {
            Debug.LogWarning($"Not enough upgradable cards to fill all shop slots! ({upgradable.Count} found)");
        }
        System.Random rng = new System.Random();
        // Shuffle and pick up to 5 unique upgradable cards
        upgradable = upgradable.OrderBy(x => rng.Next()).ToList();
        for (int i = 0; i < shopSlotCount; i++)
        {
            if (i < upgradable.Count)
            {
                shopSlots[i].card = upgradable[i];
                Debug.Log($"ShopManager: Assigned card '{upgradable[i].cardName}' to slot {i}.");
            }
            else
            {
                shopSlots[i].card = null;
                Debug.LogWarning($"ShopManager: Slot {i} left empty (not enough cards).");
            }
            shopSlots[i].upgradeLimit = rng.Next(1, 6); // Random limit between 1 and 5
            shopSlots[i].upgradesBought = 0;
        }
        UpdateShopSlotUI();
    }

    public void ToggleShopSlide()
    {
        if (slideCoroutine != null)
            StopCoroutine(slideCoroutine);
        if (isShopVisible)
            slideCoroutine = StartCoroutine(SlideShop(hiddenPosition, false));
        else
        {
            // Close settings if open
            if (settingsPanel != null)
                settingsPanel.HidePanelImmediate();
            slideCoroutine = StartCoroutine(SlideShop(shownPosition, true));
        }
    }

    private IEnumerator SlideShop(Vector2 targetPosition, bool show)
    {
        if (shopPanelRectTransform == null || shopCanvasGroup == null)
            yield break;
        float elapsed = 0f;
        Vector2 startPos = shopPanelRectTransform.anchoredPosition;
        if (show)
        {
            shopCanvasGroup.alpha = 1f;
            shopCanvasGroup.interactable = true;
            shopCanvasGroup.blocksRaycasts = true;
        }
        while (elapsed < slideDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            shopPanelRectTransform.anchoredPosition = Vector2.Lerp(startPos, targetPosition, elapsed / slideDuration);
            yield return null;
        }
        shopPanelRectTransform.anchoredPosition = targetPosition;
        isShopVisible = show;
        if (!show)
        {
            shopCanvasGroup.alpha = 0f;
            shopCanvasGroup.interactable = false;
            shopCanvasGroup.blocksRaycasts = false;
        }
    }

    private void UpdateShopSlotUI()
    {
        if (shopSlotUIs == null) {
            Debug.LogWarning("ShopManager: shopSlotUIs is null in UpdateShopSlotUI");
            return;
        }
        for (int i = 0; i < shopSlotCount; i++)
        {
            var slot = shopSlots[i];
            var go = shopSlotUIs[i];
            if (go == null) {
                Debug.LogWarning($"ShopManager: shopSlotUIs[{i}] is null");
                continue;
            }
            var entry = go.GetComponent<EntryShopUI>();
            if (entry != null && slot.card != null)
            {
                int idx = i;
                int cost = GetRarityPrice(slot.card.rarity);
                string arenaID = "";
                if (DeckManager.Instance != null && DeckManager.Instance.selectedArena != null)
                    arenaID = DeckManager.Instance.selectedArena.arenaID;
                int currentLevel = PlayerProgress.Instance.GetCardLevel(slot.card.cardID, arenaID);
                Debug.Log($"ShopManager: Updating UI for slot {i} with card '{slot.card.cardName}', level {currentLevel}, and cost {cost}.");
                entry.Setup(
                    slot.card,
                    slot.upgradesBought,
                    slot.upgradeLimit,
                    cost,
                    () => OnUpgradeButtonClicked(idx),
                    currentLevel
                );
            }
            else if (entry == null)
            {
                Debug.LogWarning($"ShopManager: EntryShopUI component missing on slot prefab instance {i}.");
            }
            else if (slot.card == null)
            {
                Debug.Log($"ShopManager: Slot {i} has no card assigned.");
            }
        }

    }

    // Returns the upgrade price for a card based on its rarity
    private int GetRarityPrice(CardRarity rarity)
    {
        switch (rarity)
        {
            case CardRarity.Common: return goldCostPerLevel;
            case CardRarity.Rare: return goldCostPerLevel * 2;
            case CardRarity.Epic: return goldCostPerLevel * 4;
            case CardRarity.Legendary: return goldCostPerLevel * 8;
            default: return goldCostPerLevel;
        }
    }

    private void OnUpgradeButtonClicked(int slotIndex)
    {
        var slot = shopSlots[slotIndex];
        if (slot.card == null || slot.upgradesBought >= slot.upgradeLimit)
            return;

        // Get price based on rarity
        int price = GetRarityPrice(slot.card.rarity);
        // Get current arena (if needed, fallback to empty string)
        string arenaID = "";
        if (DeckManager.Instance != null && DeckManager.Instance.selectedArena != null)
            arenaID = DeckManager.Instance.selectedArena.arenaID;

        // Check if card is at max level
        int currentLevel = PlayerProgress.Instance.GetCardLevel(slot.card.cardID, arenaID);
        if (currentLevel >= slot.card.maxLevel)
        {
            Debug.Log($"ShopManager: {slot.card.cardName} is already at max level.");
            return;
        }

        // Check if player has enough gold
        if (!PlayerProgress.Instance.SpendGold(price))
        {
            Debug.Log($"ShopManager: Not enough gold to upgrade {slot.card.cardName}.");
            return;
        }

        // Actually upgrade the card
        PlayerProgress.Instance.SetCardLevel(slot.card.cardID, arenaID, currentLevel + 1);
        slot.upgradesBought++;
        Debug.Log($"ShopManager: Upgraded {slot.card.cardName} to level {currentLevel + 1} for {price} gold.");
        UpdateShopSlotUI();
    }

    public void HideShopImmediate()
    {
        if (shopCanvasGroup == null || shopPanelRectTransform == null) return;
        shopCanvasGroup.alpha = 0f;
        shopCanvasGroup.interactable = false;
        shopCanvasGroup.blocksRaycasts = false;
        shopPanelRectTransform.anchoredPosition = hiddenPosition;
        isShopVisible = false;
    }

    public void CloseShop()
    {
        if (slideCoroutine != null)
            StopCoroutine(slideCoroutine);
        slideCoroutine = StartCoroutine(SlideShop(hiddenPosition, false));
    }

    public bool IsShopOpen()
    {
        return isShopVisible;
    }

    // Call this from Settingspanel before opening settings
    public void CloseShopIfOpen()
    {
        if (isShopVisible)
            CloseShop();
    }

    /// <summary>
    /// Returns the gold cost to upgrade a card from its current level.
    /// </summary>
    public int GetUpgradeCost(Card card, Arena arena)
    {
        int curLevel = PlayerProgress.Instance.GetCardLevel(card.cardID, arena.arenaID);
        return goldCostPerLevel * curLevel;
    }

    /// <summary>
    /// Returns true if the card can be upgraded in the given arena.
    /// </summary>
    public bool CanUpgradeCard(Card card, Arena arena)
    {
        if (card == null || arena == null) return false;
        int curLevel = PlayerProgress.Instance.GetCardLevel(card.cardID, arena.arenaID);
        if (curLevel >= card.maxLevel) return false;
        int cost = GetUpgradeCost(card, arena);
        return PlayerProgress.Instance.GetGold() >= cost;
    }

    /// <summary>
    /// Attempts to upgrade a card by 1 level for a specific arena. Returns true if purchase successful.
    /// </summary>
    public bool PurchaseCardUpgrade(Card card, Arena arena)
    {
        if (!CanUpgradeCard(card, arena))
        {
            OnUpgradeFailed?.Invoke(card, arena, "Not enough gold or max level reached.");
            return false;
        }

        int curLevel = PlayerProgress.Instance.GetCardLevel(card.cardID, arena.arenaID);
        int cost = GetUpgradeCost(card, arena);
        if (!PlayerProgress.Instance.SpendGold(cost))
        {
            OnUpgradeFailed?.Invoke(card, arena, "Failed to spend gold.");
            return false;
        }

        PlayerProgress.Instance.SetCardLevel(card.cardID, arena.arenaID, curLevel + 1);
        Debug.Log($"Upgraded {card.cardName} to level {curLevel + 1} in arena {arena.displayName}");

        // Notify UI
        OnCardUpgraded?.Invoke(card, arena, curLevel + 1);
        return true;
    }

    /// <summary>
    /// UI/shop calls this to try to upgrade a card.
    /// </summary>
    public void TryUpgradeCardFromShop(Card card, Arena arena)
    {
        if (!PurchaseCardUpgrade(card, arena))
        {
            Debug.LogWarning("Upgrade failed: Not enough gold or max level reached.");
            // UI can listen to OnUpgradeFailed for feedback
        }
    }

}