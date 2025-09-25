using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Singleton deck/hand manager for enemy AI.
/// - Clones ScriptableObject cards when populating draw pile so drawn cards are unique runtime instances.
/// - BuildDeck(...) builds the draw pile from an inspector-provided aiDeck or from DeckManager.allCards (with unlock filtering).
/// - Supports duplicates, shuffle, discard, draw, initial hand filling.
/// - Exposes `hand` so bots can query playable cards.
/// </summary>
[DisallowMultipleComponent]
public class EnemyDeckManager : MonoBehaviour
{
    public static EnemyDeckManager Instance;

    [Header("Pool / build settings")]
    [Tooltip("Optional: default AI card pool. If empty, the manager will fall back to DeckManager.allCards (filtered by unlocks).")]
    public List<Card> aiAllCards = new List<Card>();

    [Tooltip("If true and an explicit deck isn't provided, use DeckManager's saved deck for the selected arena.")]
    public bool useSavedDeckIfEmpty = true;

    [Tooltip("When building random decks from pool, how many cards to pick.")]
    public int defaultDeckSize = 12;

    [Tooltip("Allow duplicates when picking randomly from the pool.")]
    public bool allowDuplicatesInRandomDeck = false;

    [Header("Runtime state (inspect if needed)")]
    [HideInInspector] public List<Card> drawPile = new List<Card>();
    [HideInInspector] public List<Card> discardPile = new List<Card>();
    [HideInInspector] public List<Card> hand = new List<Card>();

    [Header("References (auto-find)")]
    public DeckManager deckManager;
    public PlayerProgress playerProgress;

    // simple RNG (kept as System.Random so you can seed externally if desired)
    private System.Random rng = new System.Random();

    // event for UI / listeners
    public event Action OnHandChanged;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (deckManager == null) deckManager = DeckManager.Instance;
        if (playerProgress == null) playerProgress = PlayerProgress.Instance;
    }

    /// <summary>
    /// Build the draw pile. If startingDeck is non-null/has items, it will be used (cloned).
    /// Otherwise the manager will attempt to use aiPool (parameter), then DeckManager saved / allCards.
    /// </summary>
    public void BuildDeck(List<Card> startingDeck = null, List<Card> aiPoolOverride = null, Arena arenaForSavedDeck = null, int deckSize = -1)
    {
        drawPile.Clear();
        discardPile.Clear();
        hand.Clear();

        // explicit starting deck -> clone each entry
        if (startingDeck != null && startingDeck.Count > 0)
        {
            foreach (var c in startingDeck)
            {
                if (c == null) continue;
                drawPile.Add(ScriptableObject.Instantiate(c));
            }
            ShuffleDrawPile();
            OnHandChanged?.Invoke();
            return;
        }

        // determine pool: explicit override -> aiAllCards -> deckManager saved / allCards
        List<Card> pool = new List<Card>();
        if (aiPoolOverride != null && aiPoolOverride.Count > 0) pool.AddRange(aiPoolOverride);
        else if (aiAllCards != null && aiAllCards.Count > 0) pool.AddRange(aiAllCards);
        else if (useSavedDeckIfEmpty && deckManager != null && arenaForSavedDeck != null)
        {
            var saved = deckManager.GetSavedDeckForArena(arenaForSavedDeck);
            if (saved != null && saved.Count > 0) pool.AddRange(saved);
        }

        // final fallback to DeckManager.allCards (with unlock filtering)
        if (pool.Count == 0 && deckManager != null && deckManager.allCards != null)
        {
            pool.AddRange(deckManager.allCards.Where(c =>
                c != null &&
                (string.IsNullOrEmpty(c.unlockArenaID) ||
                 playerProgress == null ||
                 playerProgress.IsArenaUnlocked(c.unlockArenaID) ||
                 playerProgress.IsCardUnlocked(c.cardID))
            ));
        }

        if (pool.Count == 0)
        {
            Debug.LogWarning("EnemyDeckManager: No cards available to build deck.");
            return;
        }

        int toPick = deckSize > 0 ? deckSize : (defaultDeckSize > 0 ? defaultDeckSize : 12);

        // create local modifiable candidate pool (these are asset references, we will clone when adding to drawPile)
        var candidates = new List<Card>(pool);

        for (int i = 0; i < toPick; i++)
        {
            if (candidates.Count == 0)
            {
                if (allowDuplicatesInRandomDeck)
                {
                    candidates.AddRange(pool); // refill references
                }
                else break;
            }

            int idx = rng.Next(candidates.Count);
            var assetCard = candidates[idx];
            if (assetCard != null)
            {
                // clone into runtime instance so each draw is unique
                drawPile.Add(ScriptableObject.Instantiate(assetCard));
            }

            if (!allowDuplicatesInRandomDeck)
            {
                candidates.RemoveAt(idx);
            }
            else
            {
                // keep duplicates possible but reduce immediate repeats
                candidates.RemoveAt(idx);
                if (candidates.Count == 0) candidates.AddRange(pool);
            }
        }

        ShuffleDrawPile();
        OnHandChanged?.Invoke();
    }

    public void ShuffleDrawPile()
    {
        for (int i = 0; i < drawPile.Count; i++)
        {
            int r = rng.Next(i, drawPile.Count);
            var tmp = drawPile[i];
            drawPile[i] = drawPile[r];
            drawPile[r] = tmp;
        }
    }

    /// <summary>
    /// Fill up the hand to handSize. If draw pile empties, reshuffles discard into draw pile.
    /// </summary>
    public void FillInitialHand(int handSize)
    {
        while (hand.Count < handSize && drawPile.Count > 0)
        {
            DrawOne();
        }

        if (hand.Count < handSize && discardPile.Count > 0)
        {
            drawPile.AddRange(discardPile);
            discardPile.Clear();
            ShuffleDrawPile();
            while (hand.Count < handSize && drawPile.Count > 0) DrawOne();
        }

        OnHandChanged?.Invoke();
    }

    /// <summary>
    /// Draw a single card into the hand (returns the card or null).
    /// Handles reshuffle from discard.
    /// </summary>
    public Card DrawOne()
    {
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
        hand.Add(card); // card is already an instantiated runtime clone
        OnHandChanged?.Invoke();
        return card;
    }

    /// <summary>
    /// Remove card from hand (without discarding). Useful if external logic wants to remove it.
    /// </summary>
    public bool RemoveFromHand(Card c)
    {
        if (c == null) return false;
        bool removed = hand.Remove(c);
        if (removed) OnHandChanged?.Invoke();
        return removed;
    }

    /// <summary>
    /// Place a card into discard pile (usually after playing).
    /// </summary>
    public void Discard(Card c)
    {
        if (c == null) return;
        discardPile.Add(c); // runtime instance moved to discard
    }

    /// <summary>
    /// Fully rebuild a random deck using the manager settings or supplied parameters.
    /// </summary>
    public void RebuildRandomDeck(List<Card> aiPoolOverride = null, Arena arenaForSavedDeck = null, int deckSize = -1)
    {
        BuildDeck(null, aiPoolOverride, arenaForSavedDeck, deckSize);
        ShuffleDrawPile();
        hand.Clear();
        FillInitialHand(defaultDeckSize > 0 ? Mathf.Max(1, defaultDeckSize / 3) : 4); // draw ~1/3 of deck as initial hand
    }

    #region Helpers / Info
    public int GetDrawCount() => drawPile.Count;
    public int GetDiscardCount() => discardPile.Count;
    public int GetHandCount() => hand.Count;
    #endregion
}
