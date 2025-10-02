using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// NetworkSecurityManager handles server-side validation for all game actions
/// to prevent cheating and ensure fair gameplay in multiplayer matches.
/// </summary>
public class NetworkSecurityManager : NetworkBehaviour
{
    [Header("Validation Settings")]
    [SerializeField] private float maxCardPlayRate = 0.5f; // Minimum seconds between card plays
    [SerializeField] private float maxActionRate = 0.1f; // Minimum seconds between any action
    [SerializeField] private int maxActionsPerSecond = 20; // Maximum actions per second
    [SerializeField] private float suspiciousActionThreshold = 50f; // Actions per second that trigger suspicion
    
    [Header("Resource Validation")]
    [SerializeField] private int maxElixirCapacity = 10;
    [SerializeField] private float elixirRegenRate = 1.2f; // Seconds per elixir point
    [SerializeField] private int startingElixir = 4;
    
    // Player action tracking
    private Dictionary<ulong, PlayerActionTracker> playerTrackers = new Dictionary<ulong, PlayerActionTracker>();
    
    // Card database for validation
    private Dictionary<string, Card> cardDatabase = new Dictionary<string, Card>();
    
    // Events for security violations
    public static System.Action<ulong, string> OnSecurityViolation;
    public static System.Action<ulong> OnPlayerSuspended;
    
    private static NetworkSecurityManager instance;
    public static NetworkSecurityManager Instance => instance;
    
    private class PlayerActionTracker
    {
        public ulong clientId;
        public float lastCardPlayTime;
        public float lastActionTime;
        public Queue<float> recentActions = new Queue<float>();
        public int violationCount;
        public bool isSuspended;
        public float suspensionEndTime;
        
        // Resource tracking
        public float currentElixir;
        public float lastElixirUpdateTime;
        public List<CardPlay> recentCardPlays = new List<CardPlay>();
        
        public PlayerActionTracker(ulong id)
        {
            clientId = id;
            currentElixir = 4f; // Starting elixir
            lastElixirUpdateTime = Time.time;
        }
        
        public void UpdateElixir()
        {
            float currentTime = Time.time;
            float deltaTime = currentTime - lastElixirUpdateTime;
            
            // Regenerate elixir
            currentElixir = Mathf.Min(10f, currentElixir + (deltaTime / 1.2f)); // 1.2 seconds per elixir
            lastElixirUpdateTime = currentTime;
        }
        
        public bool CanAffordCard(int cost)
        {
            UpdateElixir();
            return currentElixir >= cost;
        }
        
        public void SpendElixir(int cost)
        {
            UpdateElixir();
            currentElixir = Mathf.Max(0, currentElixir - cost);
        }
    }
    
    private class CardPlay
    {
        public string cardId;
        public Vector3 position;
        public float timestamp;
        public int cost;
        
        public CardPlay(string id, Vector3 pos, int cardCost)
        {
            cardId = id;
            position = pos;
            timestamp = Time.time;
            cost = cardCost;
        }
    }
    
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        
        // Initialize card database
        InitializeCardDatabase();
        
        // Subscribe to network events
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }
    
    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
    
    private void Update()
    {
        if (!IsServer) return;
        
        UpdatePlayerTrackers();
        CheckForSuspiciousActivity();
    }
    
    private void InitializeCardDatabase()
    {
        // Load all cards and store them in the database
        Card[] allCards = Resources.LoadAll<Card>("Cards");
        cardDatabase.Clear();
        
        foreach (Card card in allCards)
        {
            if (!string.IsNullOrEmpty(card.cardID))
            {
                cardDatabase[card.cardID] = card;
            }
        }
        
        // Fallback: find cards in scene
        if (cardDatabase.Count == 0)
        {
            Card[] sceneCards = FindObjectsOfType<Card>();
            foreach (Card card in sceneCards)
            {
                if (!string.IsNullOrEmpty(card.cardID))
                {
                    cardDatabase[card.cardID] = card;
                }
            }
        }
        
        Debug.Log($"NetworkSecurityManager: Loaded {cardDatabase.Count} cards into database");
    }
    
    private void OnClientConnected(ulong clientId)
    {
        playerTrackers[clientId] = new PlayerActionTracker(clientId);
        Debug.Log($"NetworkSecurityManager: Tracking player {clientId}");
    }
    
    private void OnClientDisconnected(ulong clientId)
    {
        playerTrackers.Remove(clientId);
        Debug.Log($"NetworkSecurityManager: Stopped tracking player {clientId}");
    }
    
    private void UpdatePlayerTrackers()
    {
        float currentTime = Time.time;
        
        foreach (var tracker in playerTrackers.Values)
        {
            // Update elixir
            tracker.UpdateElixir();
            
            // Clean old actions (keep only last 1 second)
            while (tracker.recentActions.Count > 0 && 
                   currentTime - tracker.recentActions.Peek() > 1f)
            {
                tracker.recentActions.Dequeue();
            }
            
            // Clean old card plays (keep only last 10 seconds for analysis)
            tracker.recentCardPlays.RemoveAll(play => currentTime - play.timestamp > 10f);
            
            // Check if suspension has expired
            if (tracker.isSuspended && currentTime > tracker.suspensionEndTime)
            {
                tracker.isSuspended = false;
                Debug.Log($"Player {tracker.clientId} suspension expired");
            }
        }
    }
    
    private void CheckForSuspiciousActivity()
    {
        foreach (var tracker in playerTrackers.Values)
        {
            if (tracker.isSuspended) continue;
            
            // Check action rate
            int actionsPerSecond = tracker.recentActions.Count;
            
            if (actionsPerSecond > suspiciousActionThreshold)
            {
                ReportViolation(tracker.clientId, $"Suspicious action rate: {actionsPerSecond} actions/second");
            }
            else if (actionsPerSecond > maxActionsPerSecond)
            {
                ReportViolation(tracker.clientId, $"Exceeded max action rate: {actionsPerSecond} actions/second");
            }
        }
    }
    
    /// <summary>
    /// Validate a card play request from a client
    /// </summary>
    public bool ValidateCardPlay(ulong clientId, string cardId, Vector3 position, Unit.Faction faction)
    {
        if (!IsServer) return false;
        
        // Check if player is suspended
        if (!playerTrackers.TryGetValue(clientId, out PlayerActionTracker tracker))
        {
            ReportViolation(clientId, "Player tracker not found");
            return false;
        }
        
        if (tracker.isSuspended)
        {
            return false;
        }
        
        // Check card play rate limiting
        float currentTime = Time.time;
        if (currentTime - tracker.lastCardPlayTime < maxCardPlayRate)
        {
            ReportViolation(clientId, $"Card play rate too fast: {currentTime - tracker.lastCardPlayTime}s");
            return false;
        }
        
        // Validate card exists
        if (!cardDatabase.TryGetValue(cardId, out Card card))
        {
            ReportViolation(clientId, $"Invalid card ID: {cardId}");
            return false;
        }
        
        // Check elixir cost
        int cardCost = GetCardCost(card);
        if (!tracker.CanAffordCard(cardCost))
        {
            ReportViolation(clientId, $"Insufficient elixir for card {cardId} (cost: {cardCost}, available: {tracker.currentElixir})");
            return false;
        }
        
        // Validate position (basic bounds checking)
        if (!IsValidPosition(position, faction, clientId))
        {
            ReportViolation(clientId, $"Invalid position for card play: {position}");
            return false;
        }
        
        // Check for duplicate rapid plays (spam prevention)
        var recentSimilarPlays = tracker.recentCardPlays
            .Where(play => play.cardId == cardId && 
                          Vector3.Distance(play.position, position) < 2f &&
                          currentTime - play.timestamp < 1f)
            .Count();
            
        if (recentSimilarPlays > 0)
        {
            ReportViolation(clientId, $"Duplicate card play detected: {cardId} at {position}");
            return false;
        }
        
        // All validations passed
        tracker.lastCardPlayTime = currentTime;
        tracker.lastActionTime = currentTime;
        tracker.recentActions.Enqueue(currentTime);
        tracker.SpendElixir(cardCost);
        tracker.recentCardPlays.Add(new CardPlay(cardId, position, cardCost));
        
        return true;
    }
    
    /// <summary>
    /// Validate any general action from a client
    /// </summary>
    public bool ValidateAction(ulong clientId, string actionType)
    {
        if (!IsServer) return false;
        
        if (!playerTrackers.TryGetValue(clientId, out PlayerActionTracker tracker))
        {
            return false;
        }
        
        if (tracker.isSuspended)
        {
            return false;
        }
        
        float currentTime = Time.time;
        
        // Check general action rate
        if (currentTime - tracker.lastActionTime < maxActionRate)
        {
            ReportViolation(clientId, $"Action rate too fast for {actionType}");
            return false;
        }
        
        tracker.lastActionTime = currentTime;
        tracker.recentActions.Enqueue(currentTime);
        
        return true;
    }
    
    private int GetCardCost(Card card)
    {
        // Try to get cost from card using reflection (matching original game logic)
        if (TryGetCardValue(card, "cost", out int cost))
        {
            return cost;
        }
        
        if (TryGetCardValue(card, "elixirCost", out int elixirCost))
        {
            return elixirCost;
        }
        
        // Default cost if not found
        return 1;
    }
    
    private bool TryGetCardValue<T>(object card, string name, out T value)
    {
        value = default;
        if (card == null) return false;

        System.Type t = card.GetType();

        System.Reflection.FieldInfo f = t.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (f != null)
        {
            try
            {
                object val = f.GetValue(card);
                if (val is T)
                {
                    value = (T)val;
                    return true;
                }
                else if (val != null)
                {
                    value = (T)System.Convert.ChangeType(val, typeof(T));
                    return true;
                }
            }
            catch { }
        }

        System.Reflection.PropertyInfo p = t.GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (p != null && p.CanRead)
        {
            try
            {
                object val = p.GetValue(card, null);
                if (val is T)
                {
                    value = (T)val;
                    return true;
                }
                else if (val != null)
                {
                    value = (T)System.Convert.ChangeType(val, typeof(T));
                    return true;
                }
            }
            catch { }
        }

        return false;
    }
    
    private bool IsValidPosition(Vector3 position, Unit.Faction faction, ulong clientId)
    {
        // Basic position validation
        // In a real implementation, you'd check against valid placement areas
        
        // Check if position is within reasonable bounds
        if (Mathf.Abs(position.x) > 50f || Mathf.Abs(position.z) > 50f)
        {
            return false;
        }
        
        // Check if position is on the correct side for the faction
        // This is simplified - you'd want more sophisticated area checking
        if (faction == Unit.Faction.Player && position.z > 0)
        {
            // Player cards should generally be played on player side (negative Z)
            // But allow some flexibility for bridge plays
            return position.z < 10f;
        }
        else if (faction == Unit.Faction.Enemy && position.z < 0)
        {
            // Enemy cards should generally be played on enemy side (positive Z)
            return position.z > -10f;
        }
        
        return true;
    }
    
    private void ReportViolation(ulong clientId, string reason)
    {
        if (!playerTrackers.TryGetValue(clientId, out PlayerActionTracker tracker))
        {
            return;
        }
        
        tracker.violationCount++;
        
        Debug.LogWarning($"Security violation from player {clientId}: {reason} (violation #{tracker.violationCount})");
        OnSecurityViolation?.Invoke(clientId, reason);
        
        // Progressive punishment
        if (tracker.violationCount >= 5)
        {
            SuspendPlayer(clientId, 60f); // 1 minute suspension
        }
        else if (tracker.violationCount >= 10)
        {
            SuspendPlayer(clientId, 300f); // 5 minute suspension
        }
        else if (tracker.violationCount >= 15)
        {
            // Kick player
            KickPlayer(clientId, "Too many security violations");
        }
    }
    
    private void SuspendPlayer(ulong clientId, float duration)
    {
        if (!playerTrackers.TryGetValue(clientId, out PlayerActionTracker tracker))
        {
            return;
        }
        
        tracker.isSuspended = true;
        tracker.suspensionEndTime = Time.time + duration;
        
        Debug.Log($"Player {clientId} suspended for {duration} seconds");
        OnPlayerSuspended?.Invoke(clientId);
        
        // Notify client
        NotifyPlayerSuspendedClientRpc(duration, NetworkManager.Singleton.ConnectedClients[clientId].ClientId);
    }
    
    [ClientRpc]
    private void NotifyPlayerSuspendedClientRpc(float duration, ulong targetClientId)
    {
        if (NetworkManager.Singleton.LocalClientId != targetClientId) return;
        
        Debug.Log($"You have been suspended for {duration} seconds due to suspicious activity");
        // Show suspension UI to player
    }
    
    private void KickPlayer(ulong clientId, string reason)
    {
        Debug.Log($"Kicking player {clientId}: {reason}");
        
        // Notify other clients
        NotifyPlayerKickedClientRpc(clientId, reason);
        
        // Disconnect the player
        NetworkManager.Singleton.DisconnectClient(clientId);
    }
    
    [ClientRpc]
    private void NotifyPlayerKickedClientRpc(ulong kickedClientId, string reason)
    {
        if (NetworkManager.Singleton.LocalClientId == kickedClientId)
        {
            Debug.Log($"You have been kicked from the game: {reason}");
            // Show kick message and return to main menu
        }
        else
        {
            Debug.Log($"Player {kickedClientId} was kicked: {reason}");
        }
    }
    
    // Public methods for external systems
    public float GetPlayerElixir(ulong clientId)
    {
        if (playerTrackers.TryGetValue(clientId, out PlayerActionTracker tracker))
        {
            tracker.UpdateElixir();
            return tracker.currentElixir;
        }
        return 0f;
    }
    
    public bool IsPlayerSuspended(ulong clientId)
    {
        if (playerTrackers.TryGetValue(clientId, out PlayerActionTracker tracker))
        {
            return tracker.isSuspended;
        }
        return false;
    }
    
    public int GetPlayerViolationCount(ulong clientId)
    {
        if (playerTrackers.TryGetValue(clientId, out PlayerActionTracker tracker))
        {
            return tracker.violationCount;
        }
        return 0;
    }
    
    // Debug methods
    [ServerRpc(RequireOwnership = false)]
    public void RequestPlayerStatsServerRpc(ulong requestingClientId)
    {
        if (!playerTrackers.TryGetValue(requestingClientId, out PlayerActionTracker tracker))
        {
            return;
        }
        
        tracker.UpdateElixir();
        SendPlayerStatsClientRpc(tracker.currentElixir, tracker.violationCount, tracker.isSuspended, requestingClientId);
    }
    
    [ClientRpc]
    private void SendPlayerStatsClientRpc(float elixir, int violations, bool suspended, ulong targetClientId)
    {
        if (NetworkManager.Singleton.LocalClientId != targetClientId) return;
        
        Debug.Log($"Player Stats - Elixir: {elixir:F1}, Violations: {violations}, Suspended: {suspended}");
    }
}