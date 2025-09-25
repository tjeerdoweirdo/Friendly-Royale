using UnityEngine;
using System.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class DeckManager : MonoBehaviour
{
    [Header("Selected Cards (current deck)")]
    [Tooltip("The 6 cards currently selected for play. Always 6 slots, can be null if not selected.")]
    public List<Card> selectedCards = new List<Card>(new Card[6]);
    public static DeckManager Instance;

    [Header("Collection")]
    public List<Card> allCards; // full collection

    [Header("Runtime deck")]
    [HideInInspector] public List<Card> deck = new List<Card>(); // draw pile
    [HideInInspector] public List<Card> hand = new List<Card>();

    [Header("Hand settings")]
    public int handSize = 4;
    public float drawDelay = 0.2f;

    [Header("Match context")]
    public Arena selectedArena; // set by DeckSelector when starting a match
    public string selectedArenaID => selectedArena != null ? selectedArena.arenaID : "";

    public HandUI handUI;

    /// <summary>
    /// Event invoked whenever the hand changes (draw / play / reset).
    /// HandUI subscribes to this to refresh visuals.
    /// </summary>
    public Action OnHandChanged;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void Start()
    {
        if (deck.Count == 0)
            BuildDefaultDeck();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
    // Only re-link references, do NOT reset deck or hand here
    FindReferences();
    }

    /// <summary>
    /// Attempts to find hand UI and Arena objects in the currently loaded scene and re-link them.
    /// This is safe to call from other systems if you need to force a re-link (e.g. after async UI creation).
    /// </summary>
    public void FindReferences()
    {
        if (handUI == null)
        {
            handUI = FindFirstObjectByType<HandUI>();
            if (handUI != null)
            {
                handUI.RefreshHand();
            }
            else
            {
                Debug.LogWarning("[DeckManager] No HandUI found in scene.");
            }
        }
        if (selectedArena == null)
        {
            var arenaInScene = FindFirstObjectByType<Arena>();
            if (arenaInScene != null)
            {
                selectedArena = arenaInScene;
            }
            else
            {
                Debug.LogWarning("[DeckManager] No Arena found in scene.");
            }
        }
    }

    void BuildDefaultDeck()
    {
        if (selectedCards != null)
        {
            deck.Clear();
            hand.Clear();
            // Fill deck with selected cards (ignoring nulls)
            foreach (var card in selectedCards)
            {
                if (card != null)
                    deck.Add(card);
            }
            Shuffle(deck);
        }
    }

    void Shuffle<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int j = UnityEngine.Random.Range(i, list.Count);
            T temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }

    #region Draw / Play
    public IEnumerator InitialDraw()
    {
        hand.Clear();
        int maxHand = Mathf.Min(handSize, deck.Count);
        for (int i = 0; i < maxHand; i++)
        {
            yield return new WaitForSeconds(drawDelay);
            DrawCard();
        }
        OnHandChanged?.Invoke();
        handUI?.RefreshHand();
    }

    /// <summary>
    /// Draws the top card from the deck into the hand.
    /// Returns the drawn card or null if deck is empty.
    /// Invokes OnHandChanged if a card was drawn.
    /// </summary>
    public Card DrawCard()
    {
        if (deck.Count == 0)
            return null;
        if (hand.Count >= handSize)
            return null;

        Card c = deck[0];
        deck.RemoveAt(0);
        hand.Add(c);
        OnHandChanged?.Invoke();
        handUI?.RefreshHand();
        return c;
    }

    /// <summary>
    /// Plays a card from the hand. The card is removed from the hand and placed at the
    /// back of the deck (Clash Royale style rotation), then we start a delayed draw to
    /// refill the hand.
    /// </summary>
    public void PlayCard(Card card)
    {
        if (!hand.Contains(card))
            return;

        hand.Remove(card);
        deck.Add(card);
        OnHandChanged?.Invoke();
        handUI?.RefreshHand();
        // Draw a new card after a delay
        Instance.StartCoroutine(DrawWithDelay(drawDelay));
    }

    IEnumerator DrawWithDelay(float t)
    {
        yield return new WaitForSeconds(t);
        DrawCard();
    }
    #endregion

    #region Deck selection API (used on start screen)
    /// <summary>
    /// Set the deck that will be used for the upcoming match. This replaces the draw pile.
    /// Also saves the selected deck persistently in PlayerProgress per arena.
    /// </summary>
    public void SetStartingDeck(List<Card> startingDeck, Arena arena)
    {
        if (startingDeck == null || startingDeck.Count == 0)
        {
            Debug.LogWarning("[DeckManager] Starting deck is empty.");
            return;
        }
        deck.Clear();
        hand.Clear();
        // Always sync selectedCards to the startingDeck, pad with nulls if needed
        if (selectedCards != null)
        {
            for (int i = 0; i < selectedCards.Count; i++)
                selectedCards[i] = (i < startingDeck.Count) ? startingDeck[i] : null;
        }
        // Only add non-null cards to the draw pile
        deck.AddRange(selectedCards.Where(c => c != null));
        selectedArena = arena;
        Shuffle(deck);
    }

    public List<Card> GetSavedDeckForArena(Arena arena)
    {
        // Implement your persistent deck logic here
        return new List<Card>();
    }
    #endregion

    #region Utility
    /// <summary>
    /// Call this at the start of every match to reset the deck, hand, and UI.
    /// </summary>
    public void ResetForNewMatch()
    {
        deck.Clear();
        hand.Clear();
        BuildDefaultDeck();
        FindReferences();
        OnHandChanged?.Invoke();
        handUI?.RefreshHand();
    }
    #endregion
}