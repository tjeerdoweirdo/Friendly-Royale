using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;

[System.Serializable]
public class CardLevelEntry
{
    public string cardID;
    public int level;
}

[System.Serializable]
public class CardLevelCollection
{
    public List<CardLevelEntry> entries = new List<CardLevelEntry>();
}

[System.Serializable]
public class CardShardEntry
{
    public string cardID;
    public int shards;
}

[System.Serializable]
public class CardShardCollection
{
    public List<CardShardEntry> entries = new List<CardShardEntry>();
}

public class PlayerProgress : NetworkBehaviour
{
    [Header("Network Configuration")]
    [Tooltip("Enable networking for this component. When disabled, works as single-player component.")]
    public bool enableNetworking = false;
    
    public event System.Action<int> OnGoldChanged;
    public event System.Action<int> OnTrophiesChanged;
    public event System.Action<string, string, int> OnCardLevelChanged; // cardID, arenaID, newLevel
    public static PlayerProgress Instance { get; private set; }

    [Header("Starting values")]
    public int startingGold = 500;
    public int startingTrophies = 0;

    [Header("Account")]
    [Tooltip("Default username shown in inspector if no saved username exists")]
    public string startingUsername = "Player";

    [Header("Runtime (persistent)")]
    public int gold = 0;
    public int trophies = 0;
    
    [Header("Trophy Road")]
    public int currentTrophies = 0;
    public int highestTrophies = 0;
    public int gems = 0;
    public List<int> claimedTrophyRewards = new List<int>();
    public Dictionary<string, int> cardCollection = new Dictionary<string, int>();
    public List<string> unlockedArenas = new List<string>();
    
    [Header("Network Variables (when networking enabled)")]
    private NetworkVariable<int> networkGold = new NetworkVariable<int>(500);
    private NetworkVariable<int> networkTrophies = new NetworkVariable<int>(0);
    private NetworkVariable<int> networkCurrentTrophies = new NetworkVariable<int>(0);
    private NetworkVariable<int> networkHighestTrophies = new NetworkVariable<int>(0);
    private NetworkVariable<int> networkGems = new NetworkVariable<int>(0);
    private NetworkVariable<int> networkLevel = new NetworkVariable<int>(1);
    private NetworkVariable<int> networkExperience = new NetworkVariable<int>(0);
    private NetworkVariable<int> networkWins = new NetworkVariable<int>(0);
    private NetworkVariable<int> networkLosses = new NetworkVariable<int>(0);
    private NetworkVariable<int> networkWinStreak = new NetworkVariable<int>(0);
    private NetworkVariable<int> networkBestWinStreak = new NetworkVariable<int>(0);
    
    // Network player tracking
    private static Dictionary<ulong, PlayerProgress> networkPlayerDict = new Dictionary<ulong, PlayerProgress>();

    // persisted username (runtime)
    [Tooltip("Current player's username (persisted)")]
    public string username = "";

    private const string KEY_GOLD = "PP_GOLD_v2";
    private const string KEY_TROPHIES = "PP_TROPHIES_v2";
    private const string KEY_CURRENT_TROPHIES = "PP_CURRENT_TROPHIES_v1";
    private const string KEY_HIGHEST_TROPHIES = "PP_HIGHEST_TROPHIES_v1";
    private const string KEY_GEMS = "PP_GEMS_v1";
    private const string KEY_CLAIMED_TROPHY_REWARDS = "PP_CLAIMED_TROPHY_REWARDS_v1";
    private const string KEY_CARD_COLLECTION = "PP_CARD_COLLECTION_v1";
    private const string KEY_UNLOCKED_ARENAS = "PP_UNLOCKED_ARENAS_v2";
    private const string KEY_SELECTED_DECK_PREFIX = "PP_SELECTED_DECK_v2_";
    private const string KEY_CARDLEVEL_PREFIX = "PP_CARDLEVELS_v2_"; // + arenaID
    private const string KEY_CARDSHARD_PREFIX = "PP_CARDSHARDS_v2_"; // + arenaID
    private const string KEY_UNLOCKED_CARDS = "PP_UNLOCKED_CARDS_v2"; // global unlocked card list (from chests/shop)

    // username key
    private const string KEY_USERNAME = "PP_USERNAME_v1";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadProgress();
    }
    
    public override void OnNetworkSpawn()
    {
        if (!enableNetworking) return;
        
        // Register this player's progress
        networkPlayerDict[OwnerClientId] = this;
        
        // Subscribe to network variable changes
        networkGold.OnValueChanged += OnNetworkGoldChanged;
        networkTrophies.OnValueChanged += OnNetworkTrophiesChanged;
        networkCurrentTrophies.OnValueChanged += OnNetworkCurrentTrophiesChanged;
        networkHighestTrophies.OnValueChanged += OnNetworkHighestTrophiesChanged;
        networkGems.OnValueChanged += OnNetworkGemsChanged;
        
        // If server, initialize network values from local values
        if (IsServer)
        {
            SyncLocalToNetwork();
        }
        // If client, sync network values to local
        else if (IsOwner)
        {
            SyncNetworkToLocal();
        }
    }
    
    public override void OnNetworkDespawn()
    {
        if (!enableNetworking) return;
        
        // Unsubscribe from network variable changes
        networkGold.OnValueChanged -= OnNetworkGoldChanged;
        networkTrophies.OnValueChanged -= OnNetworkTrophiesChanged;
        networkCurrentTrophies.OnValueChanged -= OnNetworkCurrentTrophiesChanged;
        networkHighestTrophies.OnValueChanged -= OnNetworkHighestTrophiesChanged;
        networkGems.OnValueChanged -= OnNetworkGemsChanged;
        
        // Remove from dictionary
        networkPlayerDict.Remove(OwnerClientId);
    }
    
    #region Network Synchronization Methods
    private void SyncLocalToNetwork()
    {
        if (!IsServer) return;
        
        networkGold.Value = gold;
        networkTrophies.Value = trophies;
        networkCurrentTrophies.Value = currentTrophies;
        networkHighestTrophies.Value = highestTrophies;
        networkGems.Value = gems;
    }
    
    private void SyncNetworkToLocal()
    {
        if (!IsOwner) return;
        
        gold = networkGold.Value;
        trophies = networkTrophies.Value;
        currentTrophies = networkCurrentTrophies.Value;
        highestTrophies = networkHighestTrophies.Value;
        gems = networkGems.Value;
    }
    
    // Network variable change handlers
    private void OnNetworkGoldChanged(int previousValue, int newValue)
    {
        if (IsOwner)
        {
            gold = newValue;
            OnGoldChanged?.Invoke(newValue);
        }
    }
    
    private void OnNetworkTrophiesChanged(int previousValue, int newValue)
    {
        if (IsOwner)
        {
            trophies = newValue;
            OnTrophiesChanged?.Invoke(newValue);
        }
    }
    
    private void OnNetworkCurrentTrophiesChanged(int previousValue, int newValue)
    {
        if (IsOwner)
        {
            currentTrophies = newValue;
        }
    }
    
    private void OnNetworkHighestTrophiesChanged(int previousValue, int newValue)
    {
        if (IsOwner)
        {
            highestTrophies = newValue;
        }
    }
    
    private void OnNetworkGemsChanged(int previousValue, int newValue)
    {
        if (IsOwner)
        {
            gems = newValue;
        }
    }
    #endregion
    
    #region Static Network Methods
    public static PlayerProgress GetPlayerProgress(ulong clientId)
    {
        networkPlayerDict.TryGetValue(clientId, out PlayerProgress progress);
        return progress;
    }
    
    public static PlayerProgress LocalPlayerProgress
    {
        get
        {
            if (NetworkManager.Singleton != null)
            {
                return GetPlayerProgress(NetworkManager.Singleton.LocalClientId);
            }
            return Instance; // Fallback to singleton instance
        }
    }
    #endregion

        public int GetGold()
        {
            return enableNetworking && IsSpawned ? networkGold.Value : gold;
        }

// ...existing code...
    void LoadProgress()
    {
        gold = PlayerPrefs.GetInt(KEY_GOLD, startingGold);
        trophies = PlayerPrefs.GetInt(KEY_TROPHIES, startingTrophies);
        
        // Load trophy road progress
        currentTrophies = PlayerPrefs.GetInt(KEY_CURRENT_TROPHIES, startingTrophies);
        highestTrophies = PlayerPrefs.GetInt(KEY_HIGHEST_TROPHIES, startingTrophies);
        gems = PlayerPrefs.GetInt(KEY_GEMS, 0);
        
        // Load claimed trophy rewards
        string claimedRewardsStr = PlayerPrefs.GetString(KEY_CLAIMED_TROPHY_REWARDS, "");
        claimedTrophyRewards.Clear();
        if (!string.IsNullOrEmpty(claimedRewardsStr))
        {
            string[] rewardStrs = claimedRewardsStr.Split(',');
            foreach (string rewardStr in rewardStrs)
            {
                if (int.TryParse(rewardStr, out int rewardTrophy))
                {
                    claimedTrophyRewards.Add(rewardTrophy);
                }
            }
        }
        
        // Load card collection
        string cardCollectionStr = PlayerPrefs.GetString(KEY_CARD_COLLECTION, "");
        cardCollection.Clear();
        if (!string.IsNullOrEmpty(cardCollectionStr))
        {
            string[] cardEntries = cardCollectionStr.Split(';');
            foreach (string entry in cardEntries)
            {
                string[] parts = entry.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out int amount))
                {
                    cardCollection[parts[0]] = amount;
                }
            }
        }
        
        // Load unlocked arenas
        string unlockedArenasStr = PlayerPrefs.GetString(KEY_UNLOCKED_ARENAS, "");
        unlockedArenas.Clear();
        if (!string.IsNullOrEmpty(unlockedArenasStr))
        {
            unlockedArenas.AddRange(unlockedArenasStr.Split(',').Where(x => !string.IsNullOrEmpty(x)));
        }

        // load username (fallback to startingUsername)
        username = PlayerPrefs.GetString(KEY_USERNAME, startingUsername ?? "Player");

        if (!PlayerPrefs.HasKey(KEY_UNLOCKED_ARENAS))
        {
            PlayerPrefs.SetString(KEY_UNLOCKED_ARENAS, "");
        }
        if (!PlayerPrefs.HasKey(KEY_UNLOCKED_CARDS))
        {
            PlayerPrefs.SetString(KEY_UNLOCKED_CARDS, "");
        }
    }

    public void SaveProgress()
    {
        PlayerPrefs.SetInt(KEY_GOLD, gold);
        PlayerPrefs.SetInt(KEY_TROPHIES, trophies);
        
        // Save trophy road progress
        PlayerPrefs.SetInt(KEY_CURRENT_TROPHIES, currentTrophies);
        PlayerPrefs.SetInt(KEY_HIGHEST_TROPHIES, highestTrophies);
        PlayerPrefs.SetInt(KEY_GEMS, gems);
        
        // Save claimed trophy rewards
        string claimedRewardsStr = string.Join(",", claimedTrophyRewards.Select(x => x.ToString()));
        PlayerPrefs.SetString(KEY_CLAIMED_TROPHY_REWARDS, claimedRewardsStr);
        
        // Save card collection
        List<string> cardEntries = new List<string>();
        foreach (var kvp in cardCollection)
        {
            cardEntries.Add($"{kvp.Key}:{kvp.Value}");
        }
        PlayerPrefs.SetString(KEY_CARD_COLLECTION, string.Join(";", cardEntries));
        
        // Save unlocked arenas
        PlayerPrefs.SetString(KEY_UNLOCKED_ARENAS, string.Join(",", unlockedArenas));

        // save username as well
        if (username == null) username = "";
        PlayerPrefs.SetString(KEY_USERNAME, username);

        PlayerPrefs.Save();
    }

    #region Username helpers
    /// <summary>
    /// Set and persist username immediately.
    /// </summary>
    public void SetUsername(string newUsername)
    {
        if (string.IsNullOrEmpty(newUsername))
        {
            username = "";
        }
        else
        {
            username = newUsername;
        }
        SaveProgress();
    }

    /// <summary>
    /// Get current username (may be empty string).
    /// </summary>
    public string GetUsername()
    {
        return username;
    }

    /// <summary>
    /// Clears stored username and persists the change.
    /// </summary>
    public void ClearUsername()
    {
        username = "";
        SaveProgress();
    }
    #endregion

    #region Gold / Trophies
    public bool SpendGold(int amount)
    {
        if (enableNetworking && IsSpawned)
        {
            if (IsServer)
            {
                if (networkGold.Value < amount) return false;
                networkGold.Value -= amount;
                SaveProgress();
                return true;
            }
            else
            {
                SpendGoldServerRpc(amount);
                return networkGold.Value >= amount; // Optimistic return
            }
        }
        else
        {
            if (gold < amount) return false;
            gold -= amount;
            SaveProgress();
            OnGoldChanged?.Invoke(gold);
            return true;
        }
    }

    public void AddGold(int amount)
    {
        if (enableNetworking && IsSpawned)
        {
            if (IsServer)
            {
                networkGold.Value += amount;
                SaveProgress();
            }
            else
            {
                AddGoldServerRpc(amount);
            }
        }
        else
        {
            gold += amount;
            SaveProgress();
            OnGoldChanged?.Invoke(gold);
        }
    }

    public void AddTrophies(int amount)
    {
        if (enableNetworking && IsSpawned)
        {
            if (IsServer)
            {
                networkTrophies.Value += amount;
                networkCurrentTrophies.Value += amount;
                if (networkCurrentTrophies.Value > networkHighestTrophies.Value)
                {
                    networkHighestTrophies.Value = networkCurrentTrophies.Value;
                }
                SaveProgress();
            }
            else
            {
                AddTrophiesServerRpc(amount);
            }
        }
        else
        {
            trophies += amount;
            currentTrophies += amount;
            if (currentTrophies > highestTrophies)
            {
                highestTrophies = currentTrophies;
            }
            SaveProgress();
            OnTrophiesChanged?.Invoke(trophies);
        }
    }
    #endregion

    #region Arena unlocks
    public List<string> GetUnlockedArenaIDs()
    {
        string s = PlayerPrefs.GetString(KEY_UNLOCKED_ARENAS, "");
        if (string.IsNullOrEmpty(s)) return new List<string>();
        return s.Split(',').Where(x => !string.IsNullOrEmpty(x)).ToList();
    }

    public void UnlockArena(string arenaID)
    {
        var list = GetUnlockedArenaIDs();
        if (!list.Contains(arenaID))
        {
            list.Add(arenaID);
            PlayerPrefs.SetString(KEY_UNLOCKED_ARENAS, string.Join(",", list));
            PlayerPrefs.Save();
        }
    }

    public bool IsArenaUnlocked(string arenaID)
    {
        var list = GetUnlockedArenaIDs();
        return list.Contains(arenaID);
    }
    #endregion

    #region Card unlocks (global)
    public bool IsCardUnlocked(string cardID)
    {
        string s = PlayerPrefs.GetString(KEY_UNLOCKED_CARDS, "");
        if (string.IsNullOrEmpty(s)) return false;
        return s.Split(',').Contains(cardID);
    }

    public void UnlockCard(string cardID)
    {
        if (IsCardUnlocked(cardID)) return;
        string s = PlayerPrefs.GetString(KEY_UNLOCKED_CARDS, "");
        var list = new List<string>();
        if (!string.IsNullOrEmpty(s)) list = s.Split(',').Where(x => !string.IsNullOrEmpty(x)).ToList();
        list.Add(cardID);
        PlayerPrefs.SetString(KEY_UNLOCKED_CARDS, string.Join(",", list));
        PlayerPrefs.Save();
    }
    #endregion

    #region Card levels per arena (persisted)
    public int GetCardLevel(string cardID, string arenaID)
    {
        CardLevelCollection col = LoadCardLevelCollection(arenaID);
        var e = col.entries.Find(x => x.cardID == cardID);
        return e != null ? Mathf.Max(1, e.level) : 1; // default level 1
    }

    public void SetCardLevel(string cardID, string arenaID, int level)
    {
        CardLevelCollection col = LoadCardLevelCollection(arenaID);
        var e = col.entries.Find(x => x.cardID == cardID);
        if (e == null)
        {
            e = new CardLevelEntry { cardID = cardID, level = level };
            col.entries.Add(e);
        }
        else e.level = level;
        SaveCardLevelCollection(arenaID, col);
        
        // Notify listeners that card level changed
        OnCardLevelChanged?.Invoke(cardID, arenaID, level);
    }

    public void IncreaseCardLevel(string cardID, string arenaID, int by = 1)
    {
        int cur = GetCardLevel(cardID, arenaID);
        SetCardLevel(cardID, arenaID, cur + by);
    }

    CardLevelCollection LoadCardLevelCollection(string arenaID)
    {
        string key = KEY_CARDLEVEL_PREFIX + arenaID;
        string json = PlayerPrefs.GetString(key, "");
        if (string.IsNullOrEmpty(json)) return new CardLevelCollection();
        return JsonUtility.FromJson<CardLevelCollection>(json) ?? new CardLevelCollection();
    }

    void SaveCardLevelCollection(string arenaID, CardLevelCollection col)
    {
        string key = KEY_CARDLEVEL_PREFIX + arenaID;
        string json = JsonUtility.ToJson(col);
        PlayerPrefs.SetString(key, json);
        PlayerPrefs.Save();
    }
    #endregion

    #region Card shards per arena (used by chests)
    public int GetCardShards(string cardID, string arenaID)
    {
        CardShardCollection col = LoadCardShardCollection(arenaID);
        var e = col.entries.Find(x => x.cardID == cardID);
        return e != null ? e.shards : 0;
    }

    public void AddCardShards(string cardID, string arenaID, int shardsToAdd)
    {
        if (shardsToAdd <= 0) return;
        CardShardCollection col = LoadCardShardCollection(arenaID);
        var e = col.entries.Find(x => x.cardID == cardID);
        if (e == null)
        {
            e = new CardShardEntry { cardID = cardID, shards = shardsToAdd };
            col.entries.Add(e);
        }
        else e.shards += shardsToAdd;

        SaveCardShardCollection(arenaID, col);

        // Try level up automatically: rule = shardsNeeded = 10 * currentLevel (simple)
        TryLevelUpFromShards(cardID, arenaID);
    }

    void TryLevelUpFromShards(string cardID, string arenaID)
    {
        bool leveled = true;
        while (leveled)
        {
            leveled = false;
            int curLevel = GetCardLevel(cardID, arenaID);
            int shards = GetCardShards(cardID, arenaID);
            int needed = Mathf.Max(1, 10 * curLevel); // e.g., level1->10 shards, level2->20 shards
            if (shards >= needed)
            {
                // consume shards and increase level
                SubtractCardShards(cardID, arenaID, needed);
                SetCardLevel(cardID, arenaID, curLevel + 1);
                leveled = true;
            }
        }
    }

    public void SubtractCardShards(string cardID, string arenaID, int shardsToRemove)
    {
        if (shardsToRemove <= 0) return;
        CardShardCollection col = LoadCardShardCollection(arenaID);
        var e = col.entries.Find(x => x.cardID == cardID);
        if (e == null) return;
        e.shards = Mathf.Max(0, e.shards - shardsToRemove);
        SaveCardShardCollection(arenaID, col);
    }

    CardShardCollection LoadCardShardCollection(string arenaID)
    {
        string key = KEY_CARDSHARD_PREFIX + arenaID;
        string json = PlayerPrefs.GetString(key, "");
        if (string.IsNullOrEmpty(json)) return new CardShardCollection();
        return JsonUtility.FromJson<CardShardCollection>(json) ?? new CardShardCollection();
    }

    void SaveCardShardCollection(string arenaID, CardShardCollection col)
    {
        string key = KEY_CARDSHARD_PREFIX + arenaID;
        string json = JsonUtility.ToJson(col);
        PlayerPrefs.SetString(key, json);
        PlayerPrefs.Save();
    }
    #endregion

    #region Selected deck per arena (persist)
    public void SaveSelectedDeckForArena(string arenaID, List<string> cardIDs)
    {
        string key = KEY_SELECTED_DECK_PREFIX + arenaID;
        string val = string.Join(",", cardIDs);
        PlayerPrefs.SetString(key, val);
        PlayerPrefs.Save();
    }

    public List<string> LoadSelectedDeckForArena(string arenaID)
    {
        string key = KEY_SELECTED_DECK_PREFIX + arenaID;
        string val = PlayerPrefs.GetString(key, "");
        if (string.IsNullOrEmpty(val)) return new List<string>();
        return val.Split(',').Where(x => !string.IsNullOrEmpty(x)).ToList();
    }
    #endregion
    
    #region Network Server RPCs
    [ServerRpc(RequireOwnership = true)]
    private void SpendGoldServerRpc(int amount)
    {
        if (networkGold.Value >= amount)
        {
            networkGold.Value -= amount;
            SaveProgress();
            SpendGoldResultClientRpc(true, networkGold.Value);
        }
        else
        {
            SpendGoldResultClientRpc(false, networkGold.Value);
        }
    }
    
    [ClientRpc]
    private void SpendGoldResultClientRpc(bool success, int remainingGold)
    {
        if (!IsOwner) return;
        
        if (!success)
        {
            Debug.Log("Not enough gold!");
        }
    }
    
    [ServerRpc(RequireOwnership = true)]
    private void AddGoldServerRpc(int amount)
    {
        networkGold.Value += amount;
        SaveProgress();
    }
    
    [ServerRpc(RequireOwnership = true)]
    private void AddTrophiesServerRpc(int amount)
    {
        networkTrophies.Value += amount;
        networkCurrentTrophies.Value += amount;
        if (networkCurrentTrophies.Value > networkHighestTrophies.Value)
        {
            networkHighestTrophies.Value = networkCurrentTrophies.Value;
        }
        SaveProgress();
    }
    
    // Match result methods for networked gameplay
    [ServerRpc(RequireOwnership = true)]
    public void OnMatchWinServerRpc(int goldReward, int trophyReward)
    {
        networkGold.Value += goldReward;
        networkTrophies.Value += trophyReward;
        networkCurrentTrophies.Value += trophyReward;
        networkWins.Value++;
        networkWinStreak.Value++;
        
        if (networkCurrentTrophies.Value > networkHighestTrophies.Value)
        {
            networkHighestTrophies.Value = networkCurrentTrophies.Value;
        }
        
        if (networkWinStreak.Value > networkBestWinStreak.Value)
        {
            networkBestWinStreak.Value = networkWinStreak.Value;
        }
        
        SaveProgress();
        MatchResultClientRpc(true, goldReward, trophyReward);
    }
    
    [ServerRpc(RequireOwnership = true)]
    public void OnMatchLossServerRpc(int goldReward, int trophyPenalty)
    {
        networkGold.Value += goldReward;
        networkTrophies.Value = Mathf.Max(0, networkTrophies.Value - trophyPenalty);
        networkCurrentTrophies.Value = Mathf.Max(0, networkCurrentTrophies.Value - trophyPenalty);
        networkLosses.Value++;
        networkWinStreak.Value = 0; // Reset win streak
        
        SaveProgress();
        MatchResultClientRpc(false, goldReward, -trophyPenalty);
    }
    
    [ClientRpc]
    private void MatchResultClientRpc(bool won, int goldChange, int trophyChange)
    {
        if (!IsOwner) return;
        
        Debug.Log($"Match {(won ? "Won" : "Lost")}! Gold: +{goldChange}, Trophies: {(trophyChange >= 0 ? "+" : "")}{trophyChange}");
    }
    
    // Public getters for network values
    public int GetTrophies() => enableNetworking && IsSpawned ? networkTrophies.Value : trophies;
    public int GetCurrentTrophies() => enableNetworking && IsSpawned ? networkCurrentTrophies.Value : currentTrophies;
    public int GetHighestTrophies() => enableNetworking && IsSpawned ? networkHighestTrophies.Value : highestTrophies;
    public int GetGems() => enableNetworking && IsSpawned ? networkGems.Value : gems;
    public int GetLevel() => enableNetworking && IsSpawned ? networkLevel.Value : 1;
    public int GetExperience() => enableNetworking && IsSpawned ? networkExperience.Value : 0;
    public int GetWins() => enableNetworking && IsSpawned ? networkWins.Value : 0;
    public int GetLosses() => enableNetworking && IsSpawned ? networkLosses.Value : 0;
    public int GetWinStreak() => enableNetworking && IsSpawned ? networkWinStreak.Value : 0;
    public int GetBestWinStreak() => enableNetworking && IsSpawned ? networkBestWinStreak.Value : 0;
    
    public float GetWinRate()
    {
        int totalWins = GetWins();
        int totalLosses = GetLosses();
        int totalMatches = totalWins + totalLosses;
        
        if (totalMatches == 0) return 0f;
        return (float)totalWins / totalMatches;
    }
    
    // Arena/League system helpers
    public string GetArenaName()
    {
        int currentTrophyCount = GetCurrentTrophies();
        
        if (currentTrophyCount < 400) return "Training Camp";
        else if (currentTrophyCount < 800) return "Goblin Stadium";
        else if (currentTrophyCount < 1100) return "Bone Pit";
        else if (currentTrophyCount < 1400) return "Barbarian Bowl";
        else if (currentTrophyCount < 1700) return "P.E.K.K.A's Playhouse";
        else if (currentTrophyCount < 2000) return "Spell Valley";
        else if (currentTrophyCount < 2300) return "Builder's Workshop";
        else if (currentTrophyCount < 2600) return "Royal Arena";
        else if (currentTrophyCount < 3000) return "Frozen Peak";
        else if (currentTrophyCount < 3400) return "Jungle Arena";
        else if (currentTrophyCount < 3800) return "Hog Mountain";
        else if (currentTrophyCount < 4200) return "Electro Valley";
        else if (currentTrophyCount < 4600) return "Spooky Town";
        else if (currentTrophyCount < 5000) return "Rascal's Hideout";
        else if (currentTrophyCount < 5500) return "Serenity Peak";
        else if (currentTrophyCount < 6000) return "Miner's Mine";
        else if (currentTrophyCount < 6500) return "Executioner's Kitchen";
        else if (currentTrophyCount < 7000) return "Royal Championship";
        else return "Champion League";
    }
    #endregion
}
