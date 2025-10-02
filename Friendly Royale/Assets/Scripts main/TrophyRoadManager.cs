using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;

public class TrophyRoadManager : NetworkBehaviour
{
    [Header("Trophy Road Configuration")]
    public TrophyRoad currentTrophyRoad;
    
    [Header("Events")]
    public System.Action<TrophyRoadReward> OnRewardClaimed;
    public System.Action<int> OnTrophiesChanged;
    public System.Action<TrophyRoadReward> OnNewMilestoneReached;
    public System.Action OnSeasonReset;
    
    private PlayerProgress playerProgress;
    private static TrophyRoadManager instance;
    
    public static TrophyRoadManager Instance
    {
        get
        {
            if (instance == null)
                instance = FindFirstObjectByType<TrophyRoadManager>();
            return instance;
        }
    }
    
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }
    
    private void Start()
    {
        playerProgress = PlayerProgress.Instance;
        if (playerProgress == null)
        {
            Debug.LogError("TrophyRoadManager: PlayerProgress instance not found!");
            return;
        }
        
        // Initialize trophy road if not set
        if (currentTrophyRoad == null)
        {
            Debug.LogWarning("TrophyRoadManager: No trophy road assigned!");
        }
        
        // Check for any unclaimed rewards on startup
        CheckForUnclaimedRewards();
    }
    
    /// <summary>
    /// Add trophies to player and check for new milestones
    /// </summary>
    public void AddTrophies(int amount)
    {
        if (playerProgress == null || currentTrophyRoad == null) return;
        
        int oldTrophies = playerProgress.currentTrophies;
        int newTrophies = oldTrophies + amount;
        
        // Update player progress
        if (IsServer)
        {
            UpdateTrophiesServerRpc(newTrophies);
        }
        else
        {
            RequestTrophyUpdateServerRpc(amount);
        }
        
        // Check for new milestones reached
        CheckForNewMilestones(oldTrophies, newTrophies);
        
        OnTrophiesChanged?.Invoke(newTrophies);
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void RequestTrophyUpdateServerRpc(int amount, ServerRpcParams rpcParams = default)
    {
        // Validate the trophy gain (prevent cheating)
        if (amount <= 0 || amount > 100) // reasonable limits
        {
            Debug.LogWarning($"Invalid trophy amount requested: {amount}");
            return;
        }
        
        var clientId = rpcParams.Receive.SenderClientId;
        UpdateTrophiesClientRpc(amount, new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
            }
        });
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void UpdateTrophiesServerRpc(int newTrophyCount)
    {
        if (playerProgress != null)
        {
            playerProgress.currentTrophies = newTrophyCount;
            playerProgress.highestTrophies = Mathf.Max(playerProgress.highestTrophies, newTrophyCount);
        }
    }
    
    [ClientRpc]
    private void UpdateTrophiesClientRpc(int amount, ClientRpcParams rpcParams = default)
    {
        if (playerProgress != null)
        {
            int oldTrophies = playerProgress.currentTrophies;
            int newTrophies = oldTrophies + amount;
            
            playerProgress.currentTrophies = newTrophies;
            playerProgress.highestTrophies = Mathf.Max(playerProgress.highestTrophies, newTrophies);
            
            CheckForNewMilestones(oldTrophies, newTrophies);
            OnTrophiesChanged?.Invoke(newTrophies);
        }
    }
    
    /// <summary>
    /// Check if player reached any new milestones
    /// </summary>
    private void CheckForNewMilestones(int oldTrophies, int newTrophies)
    {
        if (currentTrophyRoad == null) return;
        
        var newMilestones = currentTrophyRoad.rewards
            .Where(reward => reward.trophyRequirement > oldTrophies && reward.trophyRequirement <= newTrophies)
            .OrderBy(reward => reward.trophyRequirement)
            .ToList();
            
        foreach (var milestone in newMilestones)
        {
            OnNewMilestoneReached?.Invoke(milestone);
            Debug.Log($"New milestone reached: {milestone.trophyRequirement} trophies - {milestone.rewardName}");
        }
    }
    
    /// <summary>
    /// Claim a specific reward
    /// </summary>
    public bool ClaimReward(TrophyRoadReward reward)
    {
        if (playerProgress == null || currentTrophyRoad == null || reward == null)
            return false;
            
        // Check if player has enough trophies
        if (playerProgress.currentTrophies < reward.trophyRequirement)
        {
            Debug.LogWarning($"Not enough trophies to claim reward. Need: {reward.trophyRequirement}, Have: {playerProgress.currentTrophies}");
            return false;
        }
        
        // Check if already claimed
        if (playerProgress.claimedTrophyRewards.Contains(reward.trophyRequirement))
        {
            Debug.LogWarning($"Reward at {reward.trophyRequirement} trophies already claimed");
            return false;
        }
        
        // Give rewards to player
        GiveRewardToPlayer(reward);
        
        // Mark as claimed
        playerProgress.claimedTrophyRewards.Add(reward.trophyRequirement);
        
        OnRewardClaimed?.Invoke(reward);
        return true;
    }
    
    /// <summary>
    /// Claim all available rewards
    /// </summary>
    public int ClaimAllAvailableRewards()
    {
        if (currentTrophyRoad == null) return 0;
        
        var claimableRewards = GetClaimableRewards();
        int claimedCount = 0;
        
        foreach (var reward in claimableRewards)
        {
            if (ClaimReward(reward))
                claimedCount++;
        }
        
        return claimedCount;
    }
    
    /// <summary>
    /// Give the actual reward items to the player
    /// </summary>
    private void GiveRewardToPlayer(TrophyRoadReward reward)
    {
        if (playerProgress == null) return;
        
        // Give gold
        if (reward.goldAmount > 0)
        {
            playerProgress.gold += reward.goldAmount;
            Debug.Log($"Received {reward.goldAmount} gold from trophy road");
        }
        
        // Give gems
        if (reward.gemAmount > 0)
        {
            playerProgress.gems += reward.gemAmount;
            Debug.Log($"Received {reward.gemAmount} gems from trophy road");
        }
        
        // Give cards
        foreach (var cardReward in reward.cardRewards)
        {
            if (cardReward.card != null)
            {
                // Add cards to player's collection
                string cardId = cardReward.card.cardID;
                if (playerProgress.cardCollection.ContainsKey(cardId))
                {
                    playerProgress.cardCollection[cardId] += cardReward.amount;
                }
                else
                {
                    playerProgress.cardCollection[cardId] = cardReward.amount;
                }
                Debug.Log($"Received {cardReward.amount}x {cardReward.card.cardName} from trophy road");
            }
        }
        
        // Handle chests (you might want to add to an inventory system)
        foreach (var chestReward in reward.chestRewards)
        {
            Debug.Log($"Received {chestReward.amount}x {chestReward.chestType} chest from trophy road");
            // Add chest opening logic here
        }
        
        // Handle arena unlocks
        if (reward.isArenaUnlock && reward.unlockedArena != null)
        {
            if (!playerProgress.unlockedArenas.Contains(reward.unlockedArena.arenaID))
            {
                playerProgress.unlockedArenas.Add(reward.unlockedArena.arenaID);
                Debug.Log($"Unlocked new arena: {reward.unlockedArena.displayName}");
            }
        }
        
        // Save progress
        playerProgress.SaveProgress();
    }
    
    /// <summary>
    /// Get all rewards that can be claimed right now
    /// </summary>
    public List<TrophyRoadReward> GetClaimableRewards()
    {
        if (currentTrophyRoad == null || playerProgress == null)
            return new List<TrophyRoadReward>();
            
        return currentTrophyRoad.GetClaimableRewards(
            playerProgress.currentTrophies, 
            playerProgress.claimedTrophyRewards
        );
    }
    
    /// <summary>
    /// Get the next milestone to work towards
    /// </summary>
    public TrophyRoadReward GetNextMilestone()
    {
        if (currentTrophyRoad == null || playerProgress == null)
            return null;
            
        return currentTrophyRoad.GetNextMilestone(playerProgress.currentTrophies);
    }
    
    /// <summary>
    /// Get progress to next milestone (0-1)
    /// </summary>
    public float GetProgressToNextMilestone()
    {
        if (currentTrophyRoad == null || playerProgress == null)
            return 0f;
            
        return currentTrophyRoad.GetProgressToNextMilestone(playerProgress.currentTrophies);
    }
    
    /// <summary>
    /// Get overall trophy road progress (0-1)
    /// </summary>
    public float GetOverallProgress()
    {
        if (currentTrophyRoad == null || playerProgress == null)
            return 0f;
            
        return currentTrophyRoad.GetOverallProgress(playerProgress.currentTrophies);
    }
    
    /// <summary>
    /// Check for unclaimed rewards on startup
    /// </summary>
    private void CheckForUnclaimedRewards()
    {
        var claimableRewards = GetClaimableRewards();
        if (claimableRewards.Count > 0)
        {
            Debug.Log($"Player has {claimableRewards.Count} unclaimed trophy road rewards");
        }
    }
    
    /// <summary>
    /// Perform season reset
    /// </summary>
    public void PerformSeasonReset()
    {
        if (currentTrophyRoad == null || playerProgress == null) return;
        
        int oldTrophies = playerProgress.currentTrophies;
        int newTrophies = currentTrophyRoad.CalculateSeasonResetTrophies(oldTrophies);
        
        playerProgress.currentTrophies = newTrophies;
        
        // Clear claimed rewards for next season (optional)
        // playerProgress.claimedTrophyRewards.Clear();
        
        OnSeasonReset?.Invoke();
        OnTrophiesChanged?.Invoke(newTrophies);
        
        Debug.Log($"Season reset: {oldTrophies} → {newTrophies} trophies");
    }
    
    /// <summary>
    /// Get trophy requirement for next arena unlock
    /// </summary>
    public int GetNextArenaUnlockTrophies()
    {
        if (currentTrophyRoad == null || playerProgress == null)
            return -1;
            
        var nextArenaReward = currentTrophyRoad.rewards
            .Where(r => r.isArenaUnlock && r.trophyRequirement > playerProgress.currentTrophies)
            .OrderBy(r => r.trophyRequirement)
            .FirstOrDefault();
            
        return nextArenaReward?.trophyRequirement ?? -1;
    }
}