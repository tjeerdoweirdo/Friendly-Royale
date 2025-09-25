    // ...existing code...
// ...existing code...
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class FullDeckSelector6 : MonoBehaviour
{
    // Called when a card is upgraded in the shop
    private Card lastInfoCard = null;
    private void OnCardUpgradedFromShop(Card card, Arena arena, int newLevel)
    {
        // Only update if the upgraded card is in the current deck
        bool inDeck = currentDeck != null && currentDeck.Any(c => c != null && c.cardID == card.cardID);
        if (inDeck)
        {
            UpdateCurrentDeckDisplay();
        }
        // If info panel is open and showing this card, refresh it
        if (infoPanel != null && infoPanel.activeSelf && lastInfoCard != null && lastInfoCard.cardID == card.cardID)
        {
            ShowInfoForCard(card);
        }
    }
    [Header("Info Panel Stat Texts")]
    [Tooltip("TMP_Text to display card health. Optional.")]
    public TMP_Text infoHealthText;
    [Tooltip("TMP_Text to display card damage. Optional.")]
    public TMP_Text infoDamageText;
    [Tooltip("TMP_Text to display card speed. Optional.")]
    public TMP_Text infoSpeedText;
    [Tooltip("TMP_Text to display card range. Optional.")]
    public TMP_Text infoRangeText;
    [Tooltip("TMP_Text to display card attack cooldown. Optional.")]
    public TMP_Text infoAttackCooldownText;
    [Header("Info Panel Extra")]
    [Tooltip("TMP_Text to display the card type. Optional. If not assigned, type will be shown in the description panel.")]
    public TMP_Text infoTypeText;
    [Header("Panel (optional)")]
    public GameObject panelRoot;
    [Tooltip("Total number of slots; default 6.")]
    public int deckSize = 6;

    [Header("Managers (auto-find if null)")]
    public DeckManager deckManager;
    public PlayerProgress playerProgress;
    public ArenaManager arenaManager;

    [Header("Card list (inputs)")]
    public ScrollRect cardListScrollRect; // optional
    public Transform cardListContainer;    // content transform (or assigned)
    public GameObject cardEntryPrefab;     // must have CardEntryEquipUI

    [Header("Current deck placeholders (assign exact positions in inspector)")]
    [Tooltip("Assign exactly deckSize placeholders (RectTransform/Transforms) in the order you want the slots to map.")]
    public List<Transform> deckSlotPlaceholders = new List<Transform>();

    [Tooltip("Prefab used to show a card currently in the deck. Must have CurrentDeckCardUI attached.")]
    public GameObject currentDeckCardPrefab;

    [Header("Optional: preset deck in inspector (drag Card assets into these slots).")]
    [Tooltip("Assign up to deckSize cards here to pre-fill the deck slots at Start.")]
    public List<Card> inspectorDeck = new List<Card>();

    [Header("Controls")]
    public TMP_Dropdown arenaDropdown; // filter dropdown
    public TMP_Dropdown selectableArenaDropdown; // NEW: for deck save/load/start
    public TMP_InputField searchInput;
    public TMP_Dropdown rarityFilterDropdown;
    public TMP_Dropdown sortDropdown;
    public Button quickFillButton;
    public Button clearDeckButton;
        // Removed save/load deck buttons: deck is always auto-saved per arena
    public Button startBattleButton;



    [Header("Info Panel (moved here)")]
    [Tooltip("Root GameObject of the info panel (will be activated when showing info).")]
    public GameObject infoPanel;
    public TMP_Text infoNameText;
    public TMP_Text infoRarityText;
    public TMP_Text infoLevelText;
    public TMP_Text infoRoleText; // NEW: displays card role
    public TMP_Text infoDescriptionText;
    public Image infoRarityBackground; // optional image to tint by rarity

    [Tooltip("Close button on the info panel (will be wired automatically if assigned).")]
    public Button infoCloseButton;

    [Tooltip("Sprite image on the info panel to display the card icon.")]
    public Image infoSpriteImage;

    [Header("Rarity Colors")]
    public Color commonColor = new Color(0.8f, 0.8f, 0.8f);
    public Color rareColor = new Color(0.3f, 0.6f, 1f);
    public Color epicColor = new Color(0.7f, 0.3f, 1f);
    public Color legendaryColor = new Color(1f, 0.6f, 0.1f);

    // runtime state
    public Arena selectedArena; // for deck save/load/start (from selectableArenaDropdown)
    private Arena filterArena; // for card filtering (from arenaDropdown)
    private List<Card> availableCards = new List<Card>();

    // fixed-size deck: index -> Card or null
    private List<Card> currentDeck;

    // currently spawned visual GameObjects per slot (null if none)
    private GameObject[] spawnedCurrentCards;

    void Awake()
    {
        // Listen for card upgrades from the shop to update deck display
        var shop = FindFirstObjectByType<ShopManager>();
        if (shop != null)
        {
            shop.OnCardUpgraded += OnCardUpgradedFromShop;
        }

        StartCoroutine(TryFindManagersCoroutine());
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

        if (cardListScrollRect != null && cardListScrollRect.content != null)
            cardListContainer = cardListScrollRect.content;

        if (quickFillButton != null) quickFillButton.onClick.AddListener(OnQuickFill);
        if (clearDeckButton != null) clearDeckButton.onClick.AddListener(OnClearDeck);
            // Removed save/load deck button listeners: deck is always auto-saved per arena
        if (startBattleButton != null) startBattleButton.onClick.AddListener(OnStartBattle);

        if (searchInput != null) searchInput.onValueChanged.AddListener((s) => PopulateCardList());
        if (rarityFilterDropdown != null) rarityFilterDropdown.onValueChanged.AddListener((i) => PopulateCardList());
        if (sortDropdown != null) sortDropdown.onValueChanged.AddListener((i) => PopulateCardList());
        if (arenaDropdown != null) arenaDropdown.onValueChanged.AddListener((i) => OnArenaFilterChanged(i));
        if (selectableArenaDropdown != null) selectableArenaDropdown.onValueChanged.AddListener((i) => OnSelectableArenaChanged(i));

        if (infoPanel != null) infoPanel.SetActive(false);

        if (infoCloseButton != null)
        {
            infoCloseButton.onClick.RemoveAllListeners();
            infoCloseButton.onClick.AddListener(HideInfoPanel);
        }
    }

    void OnDestroy()
    {
        // Unsubscribe from shop upgrade event
        var shop = FindFirstObjectByType<ShopManager>();
        if (shop != null)
        {
            shop.OnCardUpgraded -= OnCardUpgradedFromShop;
        }

        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        if (quickFillButton != null) quickFillButton.onClick.RemoveAllListeners();
        if (clearDeckButton != null) clearDeckButton.onClick.RemoveAllListeners();
            // Removed save/load deck button listeners
        if (startBattleButton != null) startBattleButton.onClick.RemoveAllListeners();

        if (searchInput != null) searchInput.onValueChanged.RemoveAllListeners();
        if (rarityFilterDropdown != null) rarityFilterDropdown.onValueChanged.RemoveAllListeners();
        if (sortDropdown != null) sortDropdown.onValueChanged.RemoveAllListeners();
        if (arenaDropdown != null) arenaDropdown.onValueChanged.RemoveAllListeners();
        if (selectableArenaDropdown != null) selectableArenaDropdown.onValueChanged.RemoveAllListeners();

        if (infoCloseButton != null) infoCloseButton.onClick.RemoveAllListeners();
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        StartCoroutine(TryFindManagersCoroutine());
    }

    private System.Collections.IEnumerator TryFindManagersCoroutine()
    {
        float delay = 0.2f;
        bool warned = false;
        int tries = 0;
        bool deckManagerFound = false, playerProgressFound = false, arenaManagerFound = false;
        while (deckManager == null || playerProgress == null || arenaManager == null)
        {
            if (deckManager == null) {
                deckManager = DeckManager.Instance;
                if (deckManager == null) {
                    var all = Object.FindObjectsByType<DeckManager>(FindObjectsSortMode.None);
                    if (all != null && all.Length > 0) deckManager = all[0];
                }
                if (deckManager != null && !deckManagerFound) {
                    Debug.Log("DeckSelectorUI: deckManager found after " + tries + " tries.");
                    deckManagerFound = true;
                }
            }
            if (playerProgress == null) {
                playerProgress = PlayerProgress.Instance;
                if (playerProgress == null) {
                    var all = Object.FindObjectsByType<PlayerProgress>(FindObjectsSortMode.None);
                    if (all != null && all.Length > 0) playerProgress = all[0];
                }
                if (playerProgress != null && !playerProgressFound) {
                    Debug.Log("DeckSelectorUI: playerProgress found after " + tries + " tries.");
                    playerProgressFound = true;
                }
            }
            if (arenaManager == null) {
                arenaManager = ArenaManager.Instance;
                if (arenaManager == null) {
                    var all = Object.FindObjectsByType<ArenaManager>(FindObjectsSortMode.None);
                    if (all != null && all.Length > 0) arenaManager = all[0];
                }
                if (arenaManager != null && !arenaManagerFound) {
                    Debug.Log("DeckSelectorUI: arenaManager found after " + tries + " tries.");
                    arenaManagerFound = true;
                }
            }
            if (deckManager != null && playerProgress != null && arenaManager != null) break;
            tries++;
            if (tries == 25 && !warned) {
                Debug.LogWarning("DeckSelectorUI: Still waiting for managers after 5 seconds. Will keep retrying.");
                warned = true;
            }
            yield return new WaitForSeconds(delay);
        }
    }

    void Start()
    {
        if (deckSize <= 0) deckSize = 6;

        currentDeck = new List<Card>(new Card[deckSize]);
        spawnedCurrentCards = new GameObject[deckSize];

        // Try to load the last used deck from PlayerProgress (always uses "global" key)
        if (playerProgress != null && deckManager != null && deckManager.allCards != null)
        {
            var savedIds = playerProgress.LoadSelectedDeckForArena("global");
            if (savedIds != null && savedIds.Count > 0)
            {
                for (int i = 0; i < deckSize; i++)
                {
                    string id = (i < savedIds.Count) ? savedIds[i] : null;
                    Card card = null;
                    if (!string.IsNullOrEmpty(id))
                        card = deckManager.allCards.FirstOrDefault(c => c.cardID == id);
                    currentDeck[i] = card;
                }
            }
            else
            {
                // fallback to inspectorDeck if no saved deck
                for (int i = 0; i < inspectorDeck.Count && i < deckSize; i++)
                    currentDeck[i] = inspectorDeck[i];
            }
        }
        else
        {
            // fallback to inspectorDeck if managers/cards not ready
            for (int i = 0; i < inspectorDeck.Count && i < deckSize; i++)
                currentDeck[i] = inspectorDeck[i];
        }

        // Sync to DeckManager's selectedCards
        if (deckManager != null && deckManager.selectedCards != null && deckManager.selectedCards.Count == deckSize)
        {
            for (int i = 0; i < deckSize; i++)
                deckManager.selectedCards[i] = currentDeck[i];
        }

        if (deckSlotPlaceholders.Count < deckSize)
        {
            Debug.LogWarning($"FullDeckSelector6: deckSlotPlaceholders.Count ({deckSlotPlaceholders.Count}) < deckSize ({deckSize}). Only the first {deckSlotPlaceholders.Count} slots will be displayed.");
        }

        PopulateArenaDropdown();
        PopulateSelectableArenaDropdown();
        PopulateRarityDropdown();
        PopulateSortDropdown();
        OnSelectableArenaChanged(selectableArenaDropdown != null ? selectableArenaDropdown.value : 0);
        PopulateCardList();
        UpdateCurrentDeckDisplay();
    // ...existing code...
    }


    // ---------- panel control ----------
    public void Show()
    {
        if (panelRoot != null) panelRoot.SetActive(true);
        RefreshAll();
    }

    public void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void TogglePanel()
    {
        if (panelRoot == null) return;
        panelRoot.SetActive(!panelRoot.activeSelf);
        if (panelRoot.activeSelf) RefreshAll();
    }

    public void RefreshAll()
    {
        PopulateArenaDropdown();
        PopulateSelectableArenaDropdown();
        PopulateRarityDropdown();
        PopulateSortDropdown();
        PopulateCardList();
        UpdateCurrentDeckDisplay();
    // ...existing code...
    }

    // ---------- arena filter dropdown (card filter only) ----------
    void PopulateArenaDropdown()
    {
        if (arenaDropdown == null || arenaManager == null) return;
        var all = arenaManager.GetAllArenas();
        if (all == null) return;
        arenaDropdown.ClearOptions();

        List<string> options = new List<string> { "All Arenas" };
        options.AddRange(all.Select(a => a.displayName));
        arenaDropdown.AddOptions(options);

        arenaDropdown.value = 0;
        OnArenaFilterChanged(0);
    }

    void OnArenaFilterChanged(int idx)
    {
        var allArenas = arenaManager.GetAllArenas();
        if (allArenas == null || allArenas.Count == 0)
        {
            filterArena = null;
            return;
        }
        if (idx == 0)
        {
            filterArena = null; // All Arenas
        }
        else
        {
            int arenaIdx = idx - 1;
            filterArena = allArenas[Mathf.Clamp(arenaIdx, 0, allArenas.Count - 1)];
        }
        PopulateCardList();
    }

    // ---------- selectable arena dropdown (for deck save/load/start) ----------
    void PopulateSelectableArenaDropdown()
    {
        if (selectableArenaDropdown == null || arenaManager == null) return;
        var all = arenaManager.GetAllArenas();
        if (all == null) return;
        selectableArenaDropdown.ClearOptions();

        List<string> options = all.Select(a => a.displayName).ToList();
        selectableArenaDropdown.AddOptions(options);

        selectableArenaDropdown.value = 0;
        OnSelectableArenaChanged(0);
    }

    void OnSelectableArenaChanged(int idx)
    {
        // Clear the current deck when a new arena is selected

        var allArenas = arenaManager.GetAllArenas();
        if (allArenas == null || allArenas.Count == 0)
        {
            selectedArena = null;
            UpdateCurrentDeckDisplay();
            return;
        }
        selectedArena = allArenas[Mathf.Clamp(idx, 0, allArenas.Count - 1)];
        UpdateCurrentDeckDisplay();
            UpdateCurrentDeckDisplay();
    }

    // ---------- rarity dropdown ----------
    void PopulateRarityDropdown()
    {
        if (rarityFilterDropdown == null) return;
        rarityFilterDropdown.ClearOptions();
        List<string> options = new List<string> { "All" };
        foreach (CardRarity rarity in System.Enum.GetValues(typeof(CardRarity)))
        {
            options.Add(rarity.ToString());
        }
        rarityFilterDropdown.AddOptions(options);
        rarityFilterDropdown.value = 0;
    }

    // ---------- sort dropdown ----------
    void PopulateSortDropdown()
    {
        if (sortDropdown == null) return;
        sortDropdown.ClearOptions();
        List<string> options = new List<string>
        {
            "Default",
            "Name",
            "Cost",
            "Rarity",
            "Level"
        };
        sortDropdown.AddOptions(options);
        sortDropdown.value = 0;
    }

    // ---------- card list ----------
    public void PopulateCardList()
    {
        if (cardListContainer == null || cardEntryPrefab == null || deckManager == null) return;

        // Arena filter (use filterArena, not selectedArena)
        if (filterArena == null)
        {
            // All Arenas: show all unlocked cards
            availableCards = deckManager.allCards.Where(c =>
                string.IsNullOrEmpty(c.unlockArenaID) ||
                playerProgress.IsArenaUnlocked(c.unlockArenaID) ||
                playerProgress.IsCardUnlocked(c.cardID)
            ).ToList();
        }
        else
        {
            // Only cards for this arena
            availableCards = deckManager.allCards.Where(c =>
                string.IsNullOrEmpty(c.unlockArenaID) ||
                c.unlockArenaID == filterArena.arenaID ||
                playerProgress.IsArenaUnlocked(c.unlockArenaID) ||
                playerProgress.IsCardUnlocked(c.cardID)
            ).ToList();
        }

        // search
        string q = searchInput != null ? searchInput.text?.Trim().ToLowerInvariant() ?? "" : "";
        if (!string.IsNullOrEmpty(q))
            availableCards.RemoveAll(c => !(c.cardName.ToLowerInvariant().Contains(q) || c.cardID.ToLowerInvariant().Contains(q)));

        // rarity
        int ridx = rarityFilterDropdown != null ? rarityFilterDropdown.value : 0;
        if (ridx > 0)
        {
            CardRarity target = (CardRarity)(ridx - 1);
            availableCards.RemoveAll(c => c.rarity != target);
        }

        // sorting
        int sidx = sortDropdown != null ? sortDropdown.value : 0;
        switch (sidx)
        {
            case 1: availableCards.Sort((a, b) => string.Compare(a.cardName, b.cardName)); break;
            case 2: availableCards.Sort((a, b) => a.coinCost.CompareTo(b.coinCost)); break;
            case 3: availableCards.Sort((a, b) => a.rarity.CompareTo(b.rarity)); break;
            case 4:
                availableCards.Sort((a, b) => playerProgress.GetCardLevel(b.cardID, selectedArena != null ? selectedArena.arenaID : "default")
                    .CompareTo(playerProgress.GetCardLevel(a.cardID, selectedArena != null ? selectedArena.arenaID : "default")));
                break;
            default: break;
        }

        ClearChildrenSafe(cardListContainer);

        foreach (var card in availableCards)
        {
            var go = Instantiate(cardEntryPrefab, cardListContainer, false);
            var ui = go.GetComponent<CardEntryEquipUI>();
            if (ui == null) ui = go.AddComponent<CardEntryEquipUI>();
            ui.Setup(card, this, selectedArena);
        }

        if (cardListScrollRect != null) cardListScrollRect.verticalNormalizedPosition = 1f;
    }

    // ---------- current deck display ----------
    void UpdateCurrentDeckDisplay()
    {
        if (infoPanel != null) infoPanel.SetActive(false);

        for (int i = 0; i < spawnedCurrentCards.Length; i++)
        {
            if (spawnedCurrentCards[i] != null)
            {
                Destroy(spawnedCurrentCards[i]);
                spawnedCurrentCards[i] = null;
            }
        }

        int displayCount = Mathf.Min(deckSize, deckSlotPlaceholders.Count);

        for (int i = 0; i < displayCount; i++)
        {
            var card = (i < currentDeck.Count) ? currentDeck[i] : null;
            if (card != null)
            {
                Transform parent = deckSlotPlaceholders[i];
                if (parent == null) continue;
                var go = Instantiate(currentDeckCardPrefab, parent, false);
                var ui = go.GetComponent<CurrentDeckCardUI>();
                if (ui == null) ui = go.AddComponent<CurrentDeckCardUI>();

                // Get the card level for the selected arena (or default)
                int lvl = 1;
                if (PlayerProgress.Instance != null)
                {
                    string arenaId = (selectedArena != null) ? selectedArena.arenaID : "default";
                    lvl = PlayerProgress.Instance.GetCardLevel(card.cardID, arenaId);
                }

                // If CurrentDeckCardUI has a TMP_Text for level, update it
                var levelText = go.GetComponentInChildren<TMPro.TMP_Text>(true);
                if (levelText != null)
                {
                    // Try to find a TMP_Text named "LevelText" or similar, else just set the first one
                    if (levelText.gameObject.name.ToLower().Contains("level"))
                        levelText.text = $"Level {lvl}";
                    else
                        levelText.text = $"Lvl {lvl}";
                }

                ui.Setup(card, this, i);

                if (ui.infoButton != null)
                {
                    ui.infoButton.onClick.RemoveAllListeners();
                    var capturedCard = card;
                    ui.infoButton.onClick.AddListener(() => ShowInfoForCard(capturedCard));
                }

                spawnedCurrentCards[i] = go;
            }
        }

        // Auto-save deck for the current arena whenever the deck changes
        if (currentDeck != null)
        {
            var ids = currentDeck.Where(c => c != null).Select(c => c.cardID).ToList();
            playerProgress.SaveSelectedDeckForArena("global", ids);
        }

        // Enable/disable startBattleButton and set color based on deck completeness
        if (startBattleButton != null)
        {
            bool isFull = currentDeck.All(c => c != null);
            var btnImage = startBattleButton.GetComponent<Image>();
            if (!isFull)
            {
                startBattleButton.interactable = false;
                if (btnImage != null) btnImage.color = Color.red;
            }
            else
            {
                startBattleButton.interactable = true;
                if (btnImage != null) btnImage.color = Color.white;
            }
        }
    }

    // ---------- info panel methods ----------
    public void ShowInfoForCard(Card card)
    {
    lastInfoCard = card;
    // Clear all stat TMPs first
        if (infoHealthText != null) infoHealthText.text = "";
        if (infoDamageText != null) infoDamageText.text = "";
        if (infoSpeedText != null) infoSpeedText.text = "";
        if (infoRangeText != null) infoRangeText.text = "";
        if (infoAttackCooldownText != null) infoAttackCooldownText.text = "";

        // Show only relevant stats for each card type, with labels
        // Get the current level for this card in the selected arena
        int lvl = 1;
        if (PlayerProgress.Instance != null)
        {
            string arenaId = (selectedArena != null) ? selectedArena.arenaID : "default";
            lvl = PlayerProgress.Instance.GetCardLevel(card.cardID, arenaId);
        }
        switch (card.cardType)
        {
            case CardType.Troop:
                if (infoHealthText != null && card.baseHealth > 0f) infoHealthText.text = $"Health: {card.GetHealthForLevel(lvl):0}";
                if (infoDamageText != null && card.baseDamage > 0f) infoDamageText.text = $"Damage: {card.GetDamageForLevel(lvl):0}";
                if (infoSpeedText != null && card.baseSpeed > 0f) infoSpeedText.text = $"Speed: {card.GetSpeedForLevel(lvl):0.##}";
                if (infoRangeText != null && card.baseRange > 0f) infoRangeText.text = $"Range: {card.GetRangeForLevel(lvl):0.##}";
                if (infoAttackCooldownText != null && card.baseAttackCooldown > 0f) infoAttackCooldownText.text = $"Attack Cooldown: {card.GetAttackCooldownForLevel(lvl):0.##}";
                break;
            case CardType.Building:
                if (infoHealthText != null && card.baseHealth > 0f) infoHealthText.text = $"Health: {card.GetHealthForLevel(lvl):0}";
                if (infoDamageText != null && card.baseDamage > 0f) infoDamageText.text = $"Damage: {card.GetDamageForLevel(lvl):0}";
                // Only show speed/range/attack cooldown for defense buildings
                if (card.buildingType == Building.BuildingType.Defense) {
                    if (infoRangeText != null && card.defenseAttackRange > 0f) infoRangeText.text = $"Range: {card.defenseAttackRange:0.##}";
                    if (infoAttackCooldownText != null && card.defenseAttackCooldown > 0f) infoAttackCooldownText.text = $"Attack Cooldown: {card.defenseAttackCooldown:0.##}";
                }
                break;
            case CardType.Spell:
                if (infoRangeText != null && card.baseRange > 0f) infoRangeText.text = $"Range: {card.GetRangeForLevel(lvl):0.##}";
                break;
            case CardType.Targeted:
                if (infoRangeText != null && card.baseRange > 0f) infoRangeText.text = $"Range: {card.GetRangeForLevel(lvl):0.##}";
                break;
        }
        if (infoPanel == null)
        {
            if (card != null) Debug.Log($"Card Info: {card.cardName} (Rarity: {card.rarity}, ID: {card.cardID})");
            return;
        }

        if (card == null)
        {
            infoPanel.SetActive(false);
            return;
        }


        if (infoNameText != null) infoNameText.text = card.cardName;
        if (infoRarityText != null) infoRarityText.text = card.rarity.ToString();

        // Level already calculated above for stat display, so just set the text
        if (infoLevelText != null) infoLevelText.text = $"Level {lvl}";

        // Show card role if field is assigned
        if (infoRoleText != null)
        {
            // Show role only if not None or Unknown
            string roleStr = (card.unitRole.ToString() != "None" && card.unitRole.ToString() != "Unknown")
                ? card.unitRole.ToString()
                : "";
            infoRoleText.text = !string.IsNullOrEmpty(roleStr) ? $"Role: {roleStr}" : "";
        }

        var field = card.GetType().GetField("description");
        string desc = (field != null) ? (field.GetValue(card) as string ?? "") : "";

        // If infoTypeText is assigned, show type there; otherwise, prepend to description
        if (infoTypeText != null)
        {
            infoTypeText.text = card.cardType.ToString();
        }

        if (infoDescriptionText != null)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (infoTypeText == null)
                sb.AppendLine($"Type: {card.cardType}");
            if (!string.IsNullOrEmpty(desc)) sb.AppendLine(desc);

            // Only add extra info that is NOT already shown in stat fields
            if (card.cardType == CardType.Building)
            {
                sb.AppendLine("--- Building Info ---");
                sb.AppendLine($"Lifetime: {(card.buildingLifetime > 0 ? card.buildingLifetime + "s" : "Infinite")}");
                if (card.buildingHpDecayPerSecond > 0f) sb.AppendLine($"HP Decay: {card.buildingHpDecayPerSecond}/s");
                sb.AppendLine($"Building Type: {card.buildingType}");
                if (card.buildingType == Building.BuildingType.Spawner)
                {
                    sb.AppendLine($"Spawns: {card.spawnUnitPrefab?.name ?? "?"}");
                    sb.AppendLine($"Interval: {card.spawnInterval}s, Max Active: {card.maxActive}, Max Total: {card.maxTotalSpawns}");
                }
                if (card.buildingType == Building.BuildingType.Defense)
                {
                    // Only show defenseAttackDamage/Range/Cooldown if not already shown in stat fields
                    // (Assume stat fields show baseDamage, baseRange, baseAttackCooldown)
                    // If defenseAttackDamage != baseDamage, show it
                    if (card.defenseAttackDamage != card.baseDamage && card.defenseAttackDamage > 0f)
                        sb.AppendLine($"Attack Damage: {card.defenseAttackDamage}");
                    if (card.defenseAttackRange != card.baseRange && card.defenseAttackRange > 0f)
                        sb.AppendLine($"Attack Range: {card.defenseAttackRange}");
                    if (card.defenseAttackCooldown != card.baseAttackCooldown && card.defenseAttackCooldown > 0f)
                        sb.AppendLine($"Attack Cooldown: {card.defenseAttackCooldown}");
                }
            }
            if (card.unitRole == CardUnitRole.Buffer || card.unitRole == CardUnitRole.Debuffer || card.unitRole == CardUnitRole.Healer)
            {
                sb.AppendLine($"--- {card.unitRole} Info ---");
                sb.AppendLine($"Effect: {card.effectStat} {(card.effectAmount > 0 ? "+" : "")}{card.effectAmount} for {card.effectDuration}s");
                sb.AppendLine($"Mode: {card.effectMode}");
                if (card.effectMode == CardEffectMode.Aura)
                {
                    sb.AppendLine($"Aura Radius: {card.auraRadius}, Interval: {card.auraInterval}s");
                }
            }
            if (card.cardType == CardType.Spell)
            {
                sb.AppendLine("--- Spell Info ---");
                // Add more spell-specific info here if needed
            }
            infoDescriptionText.text = sb.ToString().TrimEnd();
        }

        if (infoSpriteImage != null)
        {
            if (card.icon != null)
            {
                infoSpriteImage.sprite = card.icon;
                infoSpriteImage.gameObject.SetActive(true);
            }
            else
            {
                infoSpriteImage.sprite = null;
                infoSpriteImage.gameObject.SetActive(false);
            }
        }

        if (infoRarityBackground != null)
            infoRarityBackground.color = GetColorForRarity(card.rarity);

        infoPanel.SetActive(true);
    }

    public void HideInfoPanel()
    {
        if (infoPanel != null) infoPanel.SetActive(false);
    }

    private Color GetColorForRarity(CardRarity r)
    {
        switch (r)
        {
            case CardRarity.Common: return commonColor;
            case CardRarity.Rare: return rareColor;
            case CardRarity.Epic: return epicColor;
            case CardRarity.Legendary: return legendaryColor;
            default: return Color.white;
        }
    }

    // ---------- equip / unequip ----------
    public bool EquipCard(Card card)
    {
        if (card == null) return false;
        if (currentDeck.Contains(card)) return false;

        int slot = -1;
        for (int i = 0; i < deckSize; i++)
        {
            if (currentDeck[i] == null)
            {
                slot = i;
                break;
            }
        }
        if (slot == -1) return false;

        currentDeck[slot] = card;
        if (deckManager != null && deckManager.selectedCards != null && deckManager.selectedCards.Count == deckSize)
            deckManager.selectedCards[slot] = card;
        UpdateCurrentDeckDisplay();
        return true;
    }

    public bool UnequipSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= deckSize) return false;
        if (currentDeck[slotIndex] == null) return false;
        currentDeck[slotIndex] = null;
        if (deckManager != null && deckManager.selectedCards != null && deckManager.selectedCards.Count == deckSize)
            deckManager.selectedCards[slotIndex] = null;
        UpdateCurrentDeckDisplay();
        return true;
    }

    // ---------- quick / clear / save / load / start ----------
    void OnQuickFill()
    {
        var ordered = availableCards.OrderByDescending(c => playerProgress.GetCardLevel(c.cardID, selectedArena != null ? selectedArena.arenaID : "default"))
                                     .ThenBy(c => c.coinCost).ToList();

        for (int i = 0; i < deckSize; i++)
        {
            if (currentDeck[i] == null)
            {
                var candidate = ordered.FirstOrDefault(c => !currentDeck.Contains(c));
                if (candidate != null) currentDeck[i] = candidate;
            }
        }
        // Sync to DeckManager's selectedCards
        if (deckManager != null && deckManager.selectedCards != null && deckManager.selectedCards.Count == deckSize)
        {
            for (int i = 0; i < deckSize; i++)
                deckManager.selectedCards[i] = currentDeck[i];
        }
        UpdateCurrentDeckDisplay();
    }

    void OnClearDeck()
    {
        for (int i = 0; i < deckSize; i++) currentDeck[i] = null;
        // Sync to DeckManager's selectedCards
        if (deckManager != null && deckManager.selectedCards != null && deckManager.selectedCards.Count == deckSize)
        {
            for (int i = 0; i < deckSize; i++)
                deckManager.selectedCards[i] = null;
        }
        UpdateCurrentDeckDisplay();
    }

    void OnSaveDeck()
    {
    var ids = currentDeck.Where(c => c != null).Select(c => c.cardID).ToList();
    playerProgress.SaveSelectedDeckForArena("global", ids);
    Debug.Log("Deck saved.");
    }

    void OnLoadDeck()
    {
    UpdateCurrentDeckDisplay();
    }


    void OnStartBattle()
    {
        if (selectedArena == null) { Debug.LogError("No arena."); return; }
        // Use deckManager.selectedCards as the source of truth
        var deckToUse = deckManager.selectedCards != null ? deckManager.selectedCards.Where(c => c != null).ToList() : new List<Card>();
        if (deckToUse.Count < 4)
        {
            Debug.LogWarning("You must select at least 4 cards to start a match.");
            // Optionally, show a UI warning here
            return;
        }
        deckManager.SetStartingDeck(deckToUse, selectedArena);
    playerProgress.SaveSelectedDeckForArena("global", deckToUse.Select(c => c.cardID).ToList());
        if (!string.IsNullOrEmpty(selectedArena.sceneName)) SceneManager.LoadScene(selectedArena.sceneName);
    }

    // Gold/trophy UI is now handled by GoldTrophyUI.cs

    private void ClearChildrenSafe(Transform parent)
    {
        if (parent == null) return;

        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            GameObject child = parent.GetChild(i).gameObject;
            if (Application.isPlaying)
            {
                Destroy(child);
            }
            else
            {
#if UNITY_EDITOR
                Undo.DestroyObjectImmediate(child);
#else
                DestroyImmediate(child);
#endif
            }
        }
    }
}