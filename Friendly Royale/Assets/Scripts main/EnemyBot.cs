using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// EnemyBot with internal coin system and inbuilt randomized deck + hand system.
/// - No dependency on PlayerProgress or DeckManager for internal build.
/// - Clones card assets when building internal deck so drawn entries are unique runtime instances.
/// - Atomic play: check -> deduct -> remove -> spawn. Refund on failure.
/// - Stable card key fallback to instance ID to avoid empty cardID collisions.
/// </summary>
[DisallowMultipleComponent]
public class EnemyBot : MonoBehaviour
{
    [Header("Deck / hand settings")]
    [Tooltip("Optional: assign a specific AI deck. If empty the bot will build a random deck from aiPool or EnemyDeckManager.aiAllCards.")]
    public List<Card> aiDeck = new List<Card>();

    [Tooltip("If no explicit aiDeck is provided, the bot will pick from this pool (inspector).")]
    public List<Card> aiPool = new List<Card>();

    [Tooltip("Size of the bot's deck (only used when building a random deck).")]
    public int deckSize = 12;

    [Tooltip("Hand size (how many cards the bot holds at once).")]
    public int handSize = 4;

    [Tooltip("Allow duplicates when building random deck.")]
    public bool allowDuplicatesInRandomDeck = false;

    [Header("Arena context (informational only)")]
    [Tooltip("Optional arena id for your own bookkeeping (not used for player progress).")]
    public string arenaID = "default";

    [Header("In-built coin (elixir) system")]
    public int maxCoins = 10;
    public int startCoins = 4;
    public float regenTimePerCoin = 1.2f; // seconds per coin
    private int currentCoins;
    private float regenTimer = 0f;

    [Header("Thinking / pacing")]
    public float minThinkInterval = 0.6f;
    public float maxThinkInterval = 2.0f;
    private float nextThink = 0f;

    [Header("Card play cooldowns")]
    [Tooltip("Base cooldown (seconds) per coin cost if card has no explicit cooldown.")]
    public float cooldownPerCoin = 0.9f;
    [Tooltip("Minimum cooldown for any card.")]
    public float minCardCooldown = 0.4f;
    // Optional manual card cooldown overrides (cardID -> seconds)
    public List<CardCooldownOverride> cooldownOverrides = new List<CardCooldownOverride>();

    [Header("Selection weights")]
    [Range(0f, 5f)] public float rarityWeight = 1.0f;
    [Range(0f, 5f)] public float levelWeight = 1.0f;
    [Range(0f, 5f)] public float buildingBias = 0.6f; // bonus for buildings

    [Header("Enemy Leveling System")]
    [Tooltip("Level of the enemy bot (affects card stats).")]
    [Range(1, 20)]
    public int enemyLevel = 1;
    [Tooltip("Maximum level the enemy bot can reach.")]
    public int maxEnemyLevel = 20;

    [Header("Per-Unit Level Randomization")]
    [Tooltip("Minimum level for any unit the bot spawns.")]
    [Range(1, 20)]
    public int minUnitLevel = 1;
    [Tooltip("Maximum level for any unit the bot spawns.")]
    [Range(1, 20)]
    public int maxUnitLevel = 5;

    [Header("References (auto-find if empty)")]
    public CardSpawner spawner;

    [Header("EnemyDeckManager (optional)")]
    [Tooltip("If assigned, EnemyBot will use this EnemyDeckManager for draw/hand/discard. If left empty, it will try EnemyDeckManager.Instance. If none found, falls back to internal deck.")]
    public EnemyDeckManager enemyDeckManager;

    // internal deck/hand state (fallback if no EnemyDeckManager assigned)
    private List<Card> drawPile = new List<Card>();
    private List<Card> discardPile = new List<Card>();
    private List<Card> hand = new List<Card>();

    // per-card cooldown dictionary: cardKey -> seconds remaining
    private Dictionary<string, float> cardCooldowns = new Dictionary<string, float>();

    // quick lookup for overrides (by cardID)
    private Dictionary<string, float> cooldownOverridesDict = new Dictionary<string, float>();

    // RNG
    private System.Random rng = new System.Random();

    // convenience property
    private bool UsingManager => enemyDeckManager != null;

    // Per-card level mapping: Card instance -> assigned level
    private Dictionary<Card, int> cardLevels = new Dictionary<Card, int>();

    void Awake()
    {
        if (spawner == null) spawner = FindObjectOfType<CardSpawner>();

        // try to auto-assign manager if field left empty
        if (enemyDeckManager == null)
            enemyDeckManager = EnemyDeckManager.Instance;

        currentCoins = startCoins;
        nextThink = UnityEngine.Random.Range(minThinkInterval, maxThinkInterval);

        // prepare override lookup
        cooldownOverridesDict = cooldownOverrides.ToDictionary(x => x.cardID, x => x.cooldownSeconds);

        // build deck & initial hand (delegates to manager if present)
    BuildInitialDeck();
    ShuffleDrawPile();
    FillInitialHand();
    }

    void Update()
    {
        // coin regen
        if (currentCoins < maxCoins)
        {
            regenTimer += Time.deltaTime;
            if (regenTimer >= regenTimePerCoin)
            {
                regenTimer -= regenTimePerCoin;
                currentCoins = Mathf.Min(maxCoins, currentCoins + 1);
            }
        }

        // reduce card cooldowns
        if (cardCooldowns.Count > 0)
        {
            var keys = cardCooldowns.Keys.ToList();
            foreach (var k in keys)
            {
                cardCooldowns[k] -= Time.deltaTime;
                if (cardCooldowns[k] <= 0f) cardCooldowns.Remove(k);
            }
        }

        // thinking timer
        nextThink -= Time.deltaTime;
        if (nextThink <= 0f)
        {
            ThinkAndPlay();
            nextThink = UnityEngine.Random.Range(minThinkInterval, maxThinkInterval);
        }
    }

    #region Deck / Hand management (delegates to EnemyDeckManager if available)
    void BuildInitialDeck()
    {
        if (UsingManager)
        {
            // Give the manager an explicit starting deck if aiDeck supplied,
            // otherwise manager will build from its own pools.
            enemyDeckManager.BuildDeck(
                startingDeck: aiDeck != null && aiDeck.Count > 0 ? new List<Card>(aiDeck) : null,
                aiPoolOverride: aiPool != null && aiPool.Count > 0 ? new List<Card>(aiPool) : null,
                arenaForSavedDeck: null,
                deckSize: deckSize
            );
            return;
        }

        // --- internal build logic (fallback) ---
        drawPile.Clear();
        discardPile.Clear();
        hand.Clear();

        cardLevels.Clear();

        // If aiDeck is provided, clone and use that
        if (aiDeck != null && aiDeck.Count > 0)
        {
            foreach (var c in aiDeck)
            {
                if (c == null) continue;
                var cardInstance = ScriptableObject.Instantiate(c);
                drawPile.Add(cardInstance);
                cardLevels[cardInstance] = rng.Next(minUnitLevel, maxUnitLevel + 1);
            }
        }
        else
        {
            // determine source: aiPool -> EnemyDeckManager.Instance.aiAllCards
            List<Card> source = new List<Card>();
            if (aiPool != null && aiPool.Count > 0)
                source.AddRange(aiPool);
            else if (EnemyDeckManager.Instance != null && EnemyDeckManager.Instance.aiAllCards != null && EnemyDeckManager.Instance.aiAllCards.Count > 0)
                source.AddRange(EnemyDeckManager.Instance.aiAllCards);

            if (source.Count == 0)
            {
                Debug.LogWarning("EnemyBot: No cards available to build a deck (aiDeck, aiPool and EnemyDeckManager.aiAllCards empty).");
                return;
            }

            // pick deckSize cards randomly (respect duplicates setting)
            var pool = new List<Card>(source);
            for (int i = 0; i < deckSize; i++)
            {
                if (pool.Count == 0)
                {
                    // refill pool if duplicates allowed, otherwise stop
                    if (allowDuplicatesInRandomDeck) pool.AddRange(source);
                    else break;
                }

                int idx = rng.Next(pool.Count);
                var assetCard = pool[idx];
                if (assetCard != null)
                {
                    var cardInstance = ScriptableObject.Instantiate(assetCard);
                    drawPile.Add(cardInstance);
                    cardLevels[cardInstance] = rng.Next(minUnitLevel, maxUnitLevel + 1);
                }

                pool.RemoveAt(idx);
                if (pool.Count == 0 && allowDuplicatesInRandomDeck)
                    pool.AddRange(source);
            }
        }

        if (drawPile.Count == 0)
        {
            Debug.LogWarning("EnemyBot: drawPile ended up empty after deck build.");
        }
    }

    void ShuffleDrawPile()
    {
        if (UsingManager)
        {
            enemyDeckManager.ShuffleDrawPile();
            return;
        }

        for (int i = 0; i < drawPile.Count; i++)
        {
            int r = rng.Next(i, drawPile.Count);
            var tmp = drawPile[i];
            drawPile[i] = drawPile[r];
            drawPile[r] = tmp;
        }
    }

    void FillInitialHand()
    {
        if (UsingManager)
        {
            enemyDeckManager.FillInitialHand(handSize);
            return;
        }

        while (hand.Count < handSize && drawPile.Count > 0)
        {
            DrawOne();
        }
        // If hand is still short and discard has cards, reshuffle discard into draw pile
        if (hand.Count < handSize && discardPile.Count > 0)
        {
            drawPile.AddRange(discardPile);
            discardPile.Clear();
            ShuffleDrawPile();
            while (hand.Count < handSize && drawPile.Count > 0) DrawOne();
        }
    }

    Card DrawOne()
    {
        if (UsingManager)
        {
            return enemyDeckManager.DrawOne();
        }

        if (drawPile.Count == 0)
        {
            if (discardPile.Count > 0)
            {
                drawPile.AddRange(discardPile);
                discardPile.Clear();
                ShuffleDrawPile();
            }
            else return null;
        }

        var card = drawPile[0];
        drawPile.RemoveAt(0);
        hand.Add(card);
        return card;
    }

    void Discard(Card c)
    {
        if (UsingManager)
        {
            enemyDeckManager.Discard(c);
            return;
        }

        if (c == null) return;
        discardPile.Add(c);
    }
    #endregion

    #region Decision & play
    // Track how many turns the bot has been unable to play
    private int stuckTurns = 0;
    private const int maxStuckTurns = 3;

    void ThinkAndPlay()
    {
        List<Card> handRef = UsingManager ? enemyDeckManager.hand : hand;
        if (handRef == null || handRef.Count == 0) return;

        // Build list of playable candidates from hand:
        var playable = handRef.Where(c => c != null && c.coinCost <= currentCoins && !cardCooldowns.ContainsKey(GetCardKey(c))).ToList();
        if (playable.Count == 0)
        {
            stuckTurns++;
            if (stuckTurns >= maxStuckTurns)
            {
                // Discard and redraw hand to avoid getting stuck
                if (UsingManager)
                {
                    foreach (var c in handRef.ToList())
                        enemyDeckManager.Discard(c);
                    enemyDeckManager.hand.Clear();
                    enemyDeckManager.FillInitialHand(handSize);
                }
                else
                {
                    foreach (var c in hand.ToList())
                        Discard(c);
                    hand.Clear();
                    FillInitialHand();
                }
                stuckTurns = 0;
            }
            return;
        }
        else
        {
            stuckTurns = 0;
        }

        // Smarter selection: prefer to avoid too many of the same type in a row
        // Count types in hand
        int troopCount = playable.Count(c => c.cardType == CardType.Troop);
        int buildingCount = playable.Count(c => c.cardType == CardType.Building);
        int spellCount = playable.Count(c => c.cardType == CardType.Spell);

        // Score each playable card and pick one by weighted random
        var scored = playable.Select(c => new { card = c, score = ScoreCardSmart(c, troopCount, buildingCount, spellCount) }).Where(x => x.score > 0f).ToList();
        if (scored.Count == 0) return;

        float total = scored.Sum(x => x.score);
        float pick = (float)(rng.NextDouble() * total);
        float acc = 0f;
        Card chosen = scored.Last().card;
        foreach (var s in scored)
        {
            acc += s.score;
            if (pick <= acc)
            {
                chosen = s.card;
                break;
            }
        }

        if (chosen != null)
        {
            PlayCardFromHand(chosen);
        }
    }

    float ScoreCardSmart(Card c, int troopCount, int buildingCount, int spellCount)
    {
        if (c == null) return 0f;
        float score = 1f;

        // rarity effect
        float rarFactor = 1f;
        switch (c.rarity)
        {
            case CardRarity.Common: rarFactor = 1f; break;
            case CardRarity.Rare: rarFactor = 1.4f; break;
            case CardRarity.Epic: rarFactor = 2.0f; break;
            case CardRarity.Legendary: rarFactor = 3.0f; break;
        }
        score *= Mathf.Pow(rarFactor, rarityWeight);

        // level bonus: use per-card level if available
        int lvl = cardLevels.ContainsKey(c) ? cardLevels[c] : enemyLevel;
        score *= Mathf.Pow(1f + (lvl - 1) * 0.08f, levelWeight);

        // building bias (some bots prefer defensive buildings)
        if (c.cardType == CardType.Building) score += buildingBias;

        // Prefer to balance card types (avoid too many of one type)
        if (c.cardType == CardType.Troop && troopCount > buildingCount + spellCount) score *= 0.8f;
        if (c.cardType == CardType.Building && buildingCount > troopCount + spellCount) score *= 0.8f;
        if (c.cardType == CardType.Spell && spellCount > troopCount + buildingCount) score *= 0.8f;

        // cheap cards get slight preference to keep bot active
        float costPenalty = (float)c.coinCost / Mathf.Max(1, maxCoins);
        score *= (1f - 0.25f * costPenalty);

        // small randomness
        score *= 0.8f + (float)rng.NextDouble() * 0.4f;

        return Mathf.Max(0.01f, score);
    }

    void PlayCardFromHand(Card c)
    {
        if (c == null) return;
        if (currentCoins < c.coinCost) return;

        string key = GetCardKey(c);

        // deduct coins
        currentCoins -= c.coinCost;

        // compute cooldown for this card (override -> use override, else based on cost)
        float cd = minCardCooldown;
        if (!string.IsNullOrEmpty(c.cardID) && cooldownOverridesDict.TryGetValue(c.cardID, out float overrideSec))
            cd = Mathf.Max(minCardCooldown, overrideSec);
        else
            cd = Mathf.Max(minCardCooldown, c.coinCost * cooldownPerCoin);

        cardCooldowns[key] = cd;

        // Remove from hand (manager or local)
        bool removed = false;
        if (UsingManager)
        {
            removed = enemyDeckManager.RemoveFromHand(c);
            if (removed) enemyDeckManager.Discard(c);
        }
        else
        {
            removed = hand.Remove(c);
            if (removed) Discard(c);
        }

        if (!removed)
        {
            currentCoins += c.coinCost;
            if (cardCooldowns.ContainsKey(key)) cardCooldowns.Remove(key);
            return;
        }

        // Spawn the card using its assigned level (if available)
        int cardLevel = cardLevels.ContainsKey(c) ? cardLevels[c] : enemyLevel;
        if (spawner != null)
        {
            spawner.SpawnOnSideImmediate(UnityEngine.Random.value < 0.5f, c, Unit.Faction.Enemy, cardLevel);
        }
        else
        {
            Debug.LogWarning("EnemyBot: No CardSpawner assigned.");
        }

        // Draw a new card to maintain hand size (cycle like player)
        if (UsingManager)
        {
            enemyDeckManager.DrawOne();
        }
        else
        {
            DrawOne();
        }
        // Optional trace:
        // Debug.Log($"{name} played {c.cardName} (cost {c.coinCost}, level {cardLevel}). Remaining coins: {currentCoins}. Cooldown: {cd}s");
    }
    #endregion

    #region Public API
    public int GetCurrentCoins() => currentCoins;
    public void AddCoins(int amount) => currentCoins = Mathf.Min(maxCoins, currentCoins + amount);

    /// <summary>
    /// Force the bot to rebuild a random deck (useful for difficulty change / new arena).
    /// If an EnemyDeckManager is assigned, it will rebuild that manager's deck.
    /// </summary>
    public void RebuildRandomDeck()
    {
        if (UsingManager)
        {
            enemyDeckManager.RebuildRandomDeck(aiPoolOverride: aiDeck != null && aiDeck.Count > 0 ? aiDeck : (aiPool != null && aiPool.Count > 0 ? aiPool : null), arenaForSavedDeck: null, deckSize: deckSize);
            enemyDeckManager.ShuffleDrawPile();
            enemyDeckManager.hand.Clear();
            enemyDeckManager.FillInitialHand(handSize);
            return;
        }

        BuildInitialDeck();
        ShuffleDrawPile();
        hand.Clear();
        FillInitialHand();
    }
    #endregion

    #region Helpers
    // Stable card key: prefer real cardID, fall back to instance ID to avoid empty-string collisions
    private string GetCardKey(Card c)
    {
        if (c == null) return "null_card";
        if (!string.IsNullOrEmpty(c.cardID)) return c.cardID;
        return $"inst_{c.GetInstanceID()}";
    }
    #endregion
}

/// <summary>
/// Helper to set custom cooldowns via inspector.
/// </summary>
[Serializable]
public class CardCooldownOverride
{
    public string cardID;
    public float cooldownSeconds = 1f;
}
