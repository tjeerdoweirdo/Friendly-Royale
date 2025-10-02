using UnityEngine;
using Unity.Netcode;
using System;
using System.Collections.Generic;

/// <summary>
/// Networked PlayerProgress system that synchronizes player statistics, trophies, and rewards
/// across all clients while maintaining server authority for anti-cheat protection.
/// </summary>
public class NetworkPlayerProgress : NetworkBehaviour
{
    [Header("Player Stats")]
    [SerializeField] private NetworkVariable<int> networkGold = new NetworkVariable<int>(1000);
    [SerializeField] private NetworkVariable<int> networkTrophies = new NetworkVariable<int>(0);
    [SerializeField] private NetworkVariable<int> networkLevel = new NetworkVariable<int>(1);
    [SerializeField] private NetworkVariable<int> networkExperience = new NetworkVariable<int>(0);
    [SerializeField] private NetworkVariable<int> networkWins = new NetworkVariable<int>(0);
    [SerializeField] private NetworkVariable<int> networkLosses = new NetworkVariable<int>(0);
    
    [Header("Match Statistics")]
    [SerializeField] private NetworkVariable<int> networkTotalMatches = new NetworkVariable<int>(0);
    [SerializeField] private NetworkVariable<int> networkWinStreak = new NetworkVariable<int>(0);
    [SerializeField] private NetworkVariable<int> networkBestWinStreak = new NetworkVariable<int>(0);
    
    // Local cache for UI updates
    private int lastGold, lastTrophies, lastLevel, lastExperience, lastWins, lastLosses;
    
    // Events for UI updates
    public static System.Action<int> OnGoldChanged;
    public static System.Action<int> OnTrophiesChanged;
    public static System.Action<int> OnLevelChanged;
    public static System.Action<int> OnExperienceChanged;
    public static System.Action<int, int> OnWinLossChanged; // wins, losses
    
    // Player identification
    private ulong playerId;
    private string playerName;
    
    // Static references for easy access
    private static Dictionary<ulong, NetworkPlayerProgress> playerProgressDict = new Dictionary<ulong, NetworkPlayerProgress>();
    
    public static NetworkPlayerProgress GetPlayerProgress(ulong clientId)
    {
        playerProgressDict.TryGetValue(clientId, out NetworkPlayerProgress progress);
        return progress;
    }
    
    public static NetworkPlayerProgress LocalPlayerProgress
    {
        get
        {
            if (NetworkManager.Singleton != null)
            {
                return GetPlayerProgress(NetworkManager.Singleton.LocalClientId);
            }
            return null;
        }
    }

    public override void OnNetworkSpawn()
    {
        playerId = OwnerClientId;
        playerProgressDict[playerId] = this;
        
        // Subscribe to network variable changes
        networkGold.OnValueChanged += OnGoldValueChanged;
        networkTrophies.OnValueChanged += OnTrophiesValueChanged;
        networkLevel.OnValueChanged += OnLevelValueChanged;
        networkExperience.OnValueChanged += OnExperienceValueChanged;
        networkWins.OnValueChanged += OnWinsValueChanged;
        networkLosses.OnValueChanged += OnLossesValueChanged;
        
        // Initialize local cache
        UpdateLocalCache();
        
        // Load player data if this is the server
        if (IsServer)
        {
            LoadPlayerData();
        }
    }

    public override void OnNetworkDespawn()
    {
        // Unsubscribe from events
        networkGold.OnValueChanged -= OnGoldValueChanged;
        networkTrophies.OnValueChanged -= OnTrophiesValueChanged;
        networkLevel.OnValueChanged -= OnLevelValueChanged;
        networkExperience.OnValueChanged -= OnExperienceValueChanged;
        networkWins.OnValueChanged -= OnWinsValueChanged;
        networkLosses.OnValueChanged -= OnLossesValueChanged;
        
        // Remove from dictionary
        playerProgressDict.Remove(playerId);
        
        // Save player data if this is the server
        if (IsServer)
        {
            SavePlayerData();
        }
    }
    
    private void LoadPlayerData()
    {
        // Load from PlayerPrefs (in a real game, this would be from a database)
        string playerKey = $"Player_{playerId}";
        
        networkGold.Value = PlayerPrefs.GetInt($"{playerKey}_Gold", 1000);
        networkTrophies.Value = PlayerPrefs.GetInt($"{playerKey}_Trophies", 0);
        networkLevel.Value = PlayerPrefs.GetInt($"{playerKey}_Level", 1);
        networkExperience.Value = PlayerPrefs.GetInt($"{playerKey}_Experience", 0);
        networkWins.Value = PlayerPrefs.GetInt($"{playerKey}_Wins", 0);
        networkLosses.Value = PlayerPrefs.GetInt($"{playerKey}_Losses", 0);
        networkTotalMatches.Value = PlayerPrefs.GetInt($"{playerKey}_TotalMatches", 0);
        networkWinStreak.Value = PlayerPrefs.GetInt($"{playerKey}_WinStreak", 0);
        networkBestWinStreak.Value = PlayerPrefs.GetInt($"{playerKey}_BestWinStreak", 0);
        
        playerName = PlayerPrefs.GetString($"{playerKey}_Name", $"Player_{playerId}");
    }
    
    private void SavePlayerData()
    {
        // Save to PlayerPrefs (in a real game, this would be to a database)
        string playerKey = $"Player_{playerId}";
        
        PlayerPrefs.SetInt($"{playerKey}_Gold", networkGold.Value);
        PlayerPrefs.SetInt($"{playerKey}_Trophies", networkTrophies.Value);
        PlayerPrefs.SetInt($"{playerKey}_Level", networkLevel.Value);
        PlayerPrefs.SetInt($"{playerKey}_Experience", networkExperience.Value);
        PlayerPrefs.SetInt($"{playerKey}_Wins", networkWins.Value);
        PlayerPrefs.SetInt($"{playerKey}_Losses", networkLosses.Value);
        PlayerPrefs.SetInt($"{playerKey}_TotalMatches", networkTotalMatches.Value);
        PlayerPrefs.SetInt($"{playerKey}_WinStreak", networkWinStreak.Value);
        PlayerPrefs.SetInt($"{playerKey}_BestWinStreak", networkBestWinStreak.Value);
        PlayerPrefs.SetString($"{playerKey}_Name", playerName);
        
        PlayerPrefs.Save();
    }
    
    private void UpdateLocalCache()
    {
        lastGold = networkGold.Value;
        lastTrophies = networkTrophies.Value;
        lastLevel = networkLevel.Value;
        lastExperience = networkExperience.Value;
        lastWins = networkWins.Value;
        lastLosses = networkLosses.Value;
    }
    
    // Network variable change handlers
    private void OnGoldValueChanged(int previousValue, int newValue)
    {
        if (IsOwner)
        {
            OnGoldChanged?.Invoke(newValue);
        }
    }
    
    private void OnTrophiesValueChanged(int previousValue, int newValue)
    {
        if (IsOwner)
        {
            OnTrophiesChanged?.Invoke(newValue);
        }
    }
    
    private void OnLevelValueChanged(int previousValue, int newValue)
    {
        if (IsOwner)
        {
            OnLevelChanged?.Invoke(newValue);
        }
    }
    
    private void OnExperienceValueChanged(int previousValue, int newValue)
    {
        if (IsOwner)
        {
            OnExperienceChanged?.Invoke(newValue);
        }
    }
    
    private void OnWinsValueChanged(int previousValue, int newValue)
    {
        if (IsOwner)
        {
            OnWinLossChanged?.Invoke(newValue, networkLosses.Value);
        }
    }
    
    private void OnLossesValueChanged(int previousValue, int newValue)
    {
        if (IsOwner)
        {
            OnWinLossChanged?.Invoke(networkWins.Value, newValue);
        }
    }
    
    // Public methods for match results
    public void OnMatchWin(int goldReward, int trophyReward, int experienceReward)
    {
        if (!IsServer) return;
        
        networkGold.Value += goldReward;
        networkTrophies.Value += trophyReward;
        networkExperience.Value += experienceReward;
        networkWins.Value++;
        networkTotalMatches.Value++;
        networkWinStreak.Value++;
        
        if (networkWinStreak.Value > networkBestWinStreak.Value)
        {
            networkBestWinStreak.Value = networkWinStreak.Value;
        }
        
        CheckLevelUp();
        SavePlayerData();
    }
    
    public void OnMatchLoss(int goldReward, int trophyPenalty, int experienceReward)
    {
        if (!IsServer) return;
        
        networkGold.Value += goldReward;
        networkTrophies.Value = Mathf.Max(0, networkTrophies.Value - trophyPenalty);
        networkExperience.Value += experienceReward;
        networkLosses.Value++;
        networkTotalMatches.Value++;
        networkWinStreak.Value = 0; // Reset win streak
        
        CheckLevelUp();
        SavePlayerData();
    }
    
    public void OnMatchDraw(int goldReward, int trophyReward, int experienceReward)
    {
        if (!IsServer) return;
        
        networkGold.Value += goldReward;
        networkTrophies.Value += trophyReward;
        networkExperience.Value += experienceReward;
        networkTotalMatches.Value++;
        // Win streak is not affected by draws
        
        CheckLevelUp();
        SavePlayerData();
    }
    
    private void CheckLevelUp()
    {
        int experienceNeeded = GetExperienceNeededForLevel(networkLevel.Value + 1);
        
        while (networkExperience.Value >= experienceNeeded)
        {
            networkLevel.Value++;
            experienceNeeded = GetExperienceNeededForLevel(networkLevel.Value + 1);
            
            // Award level up rewards
            OnLevelUpClientRpc(networkLevel.Value);
        }
    }
    
    [ClientRpc]
    private void OnLevelUpClientRpc(int newLevel)
    {
        if (IsOwner)
        {
            Debug.Log($"Level Up! Now level {newLevel}");
            // You can add level up effects, sounds, etc. here
        }
    }
    
    private int GetExperienceNeededForLevel(int level)
    {
        // Simple exponential progression
        return level * 100 + (level - 1) * 50;
    }
    
    // Public methods for spending/earning resources
    [ServerRpc(RequireOwnership = false)]
    public void SpendGoldServerRpc(int amount, ulong clientId)
    {
        // Validate that the requesting client owns this progress
        if (clientId != OwnerClientId) return;
        
        if (networkGold.Value >= amount)
        {
            networkGold.Value -= amount;
            SavePlayerData();
            
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
            // Show insufficient funds UI
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    public void EarnGoldServerRpc(int amount, ulong clientId)
    {
        // Validate that the requesting client owns this progress
        if (clientId != OwnerClientId) return;
        
        networkGold.Value += amount;
        SavePlayerData();
    }
    
    // Public getters
    public int GetGold() => networkGold.Value;
    public int GetTrophies() => networkTrophies.Value;
    public int GetLevel() => networkLevel.Value;
    public int GetExperience() => networkExperience.Value;
    public int GetWins() => networkWins.Value;
    public int GetLosses() => networkLosses.Value;
    public int GetTotalMatches() => networkTotalMatches.Value;
    public int GetWinStreak() => networkWinStreak.Value;
    public int GetBestWinStreak() => networkBestWinStreak.Value;
    public string GetPlayerName() => playerName;
    public ulong GetPlayerId() => playerId;
    
    public float GetWinRate()
    {
        if (networkTotalMatches.Value == 0) return 0f;
        return (float)networkWins.Value / networkTotalMatches.Value;
    }
    
    public int GetExperienceNeededForNextLevel()
    {
        return GetExperienceNeededForLevel(networkLevel.Value + 1) - networkExperience.Value;
    }
    
    public float GetLevelProgress()
    {
        int currentLevelExp = GetExperienceNeededForLevel(networkLevel.Value);
        int nextLevelExp = GetExperienceNeededForLevel(networkLevel.Value + 1);
        int expInCurrentLevel = networkExperience.Value - currentLevelExp;
        int expNeededForLevel = nextLevelExp - currentLevelExp;
        
        return (float)expInCurrentLevel / expNeededForLevel;
    }
    
    // Arena/League system helpers
    public string GetArenaName()
    {
        int trophies = networkTrophies.Value;
        
        if (trophies < 400) return "Training Camp";
        else if (trophies < 800) return "Goblin Stadium";
        else if (trophies < 1100) return "Bone Pit";
        else if (trophies < 1400) return "Barbarian Bowl";
        else if (trophies < 1700) return "P.E.K.K.A's Playhouse";
        else if (trophies < 2000) return "Spell Valley";
        else if (trophies < 2300) return "Builder's Workshop";
        else if (trophies < 2600) return "Royal Arena";
        else if (trophies < 3000) return "Frozen Peak";
        else if (trophies < 3400) return "Jungle Arena";
        else if (trophies < 3800) return "Hog Mountain";
        else if (trophies < 4200) return "Electro Valley";
        else if (trophies < 4600) return "Spooky Town";
        else if (trophies < 5000) return "Rascal's Hideout";
        else if (trophies < 5500) return "Serenity Peak";
        else if (trophies < 6000) return "Miner's Mine";
        else if (trophies < 6500) return "Executioner's Kitchen";
        else if (trophies < 7000) return "Royal Championship";
        else return "Champion League";
    }
    
    // Public method to set player name
    [ServerRpc(RequireOwnership = false)]
    public void SetPlayerNameServerRpc(string newName, ulong clientId)
    {
        if (clientId != OwnerClientId) return;
        
        playerName = newName;
        SavePlayerData();
    }
}