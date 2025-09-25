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
                Debug.LogWarning("[HandUI] No CardSpawner found in scene. Spawn choice UI will not appear.");
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

                // Remove old listeners and add the correct one with captured index
                localBtn.onClick.RemoveAllListeners();
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
            }
        }
    }

    void OnCardClicked(int slotIndex)
    {
        if (DeckManager.Instance == null) return;
        if (slotIndex >= DeckManager.Instance.hand.Count) return;

        Card c = DeckManager.Instance.hand[slotIndex];
        if (CoinSystem.Instance == null)
        {
            Debug.LogWarning("CoinSystem.Instance is null. Make sure a CoinSystem exists in the scene.");
            return;
        }

        // Check cost before proceeding
        if (CoinSystem.Instance.currentCoins < c.coinCost)
        {
            Debug.Log("[HandUI] Not enough coins to play card.");
            ShowElixirError();
            return;
        }

        // Special building spawn logic
        if (c.cardType == CardType.Building && cardSpawner != null)
        {
            int freeSpot = -1;
            for (int i = 0; i < cardSpawner.specialBuildingOccupants.Length; i++)
            {
                if (cardSpawner.specialBuildingOccupants[i] == null)
                {
                    freeSpot = i;
                    break;
                }
            }
            if (freeSpot == -1)
            {
                Debug.Log("Cannot play: Both special building spots are occupied. No coins spent.");
                return;
            }
        }

        // Spend coins and play the card
        bool paid = CoinSystem.Instance.SpendCoins(c.coinCost);
        if (!paid) return;

        DeckManager.Instance.PlayCard(c);

        // Show spawn choice
        if (cardSpawner != null)
        {
            cardSpawner.ShowSpawnChoice(c, Unit.Faction.Player);
        }
        else
        {
            Vector3 world = (slotIndex % 2 == 0) ? new Vector3(-2f, 0f, 0f) : new Vector3(2f, 0f, 0f);
            StartCoroutine(FindSpawnerAndSpawnFallback(c, world));
        }

        // Refresh UI immediately so this slot updates
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
            StartCoroutine(sp.SpawnUnitFromCard(c, worldPos, Unit.Faction.Player));
        }
        else
        {
            Debug.LogWarning("[HandUI] No CardSpawner found for fallback spawn.");
        }
    }
}
