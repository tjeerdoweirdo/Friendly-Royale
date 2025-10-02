using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[CreateAssetMenu(menuName = "CR/Trophy Road/Trophy Road")]
public class TrophyRoad : ScriptableObject
{
    [Header("Trophy Road Configuration")]
    public string roadName = "Trophy Road";
    public int maxTrophies = 5000; // maximum trophies for this road
    public List<TrophyRoadReward> rewards = new List<TrophyRoadReward>();
    
    [Header("Season Settings")]
    public bool hasSeasonReset = true;
    public int seasonResetThreshold = 4000; // trophies reset point
    public float seasonResetPercentage = 0.5f; // how much to reset (50% = half)
    
    [Header("Visual Settings")]
    public Sprite roadBackground;
    public Color roadColor = new Color(0.2f, 0.4f, 0.8f);
    public AnimationCurve progressCurve = AnimationCurve.Linear(0, 0, 1, 1);
    
    private void OnValidate()
    {
        // Sort rewards by trophy requirement when edited in inspector
        if (rewards != null && rewards.Count > 0)
        {
            rewards = rewards.Where(r => r != null).OrderBy(r => r.trophyRequirement).ToList();
        }
    }
    
    /// <summary>
    /// Get all rewards up to a specific trophy count
    /// </summary>
    public List<TrophyRoadReward> GetRewardsUpToTrophies(int trophyCount)
    {
        return rewards.Where(reward => reward.trophyRequirement <= trophyCount).ToList();
    }
    
    /// <summary>
    /// Get the next unclaimed reward based on current trophies and claimed rewards
    /// </summary>
    public TrophyRoadReward GetNextUnclaimedReward(int currentTrophies, List<int> claimedRewardTrophies)
    {
        return rewards
            .Where(reward => reward.trophyRequirement <= currentTrophies)
            .Where(reward => !claimedRewardTrophies.Contains(reward.trophyRequirement))
            .OrderBy(reward => reward.trophyRequirement)
            .FirstOrDefault();
    }
    
    /// <summary>
    /// Get all claimable rewards (earned but not claimed)
    /// </summary>
    public List<TrophyRoadReward> GetClaimableRewards(int currentTrophies, List<int> claimedRewardTrophies)
    {
        return rewards
            .Where(reward => reward.trophyRequirement <= currentTrophies)
            .Where(reward => !claimedRewardTrophies.Contains(reward.trophyRequirement))
            .OrderBy(reward => reward.trophyRequirement)
            .ToList();
    }
    
    /// <summary>
    /// Get the next reward milestone to work towards
    /// </summary>
    public TrophyRoadReward GetNextMilestone(int currentTrophies)
    {
        return rewards
            .Where(reward => reward.trophyRequirement > currentTrophies)
            .OrderBy(reward => reward.trophyRequirement)
            .FirstOrDefault();
    }
    
    /// <summary>
    /// Get progress percentage towards next milestone
    /// </summary>
    public float GetProgressToNextMilestone(int currentTrophies)
    {
        var nextMilestone = GetNextMilestone(currentTrophies);
        if (nextMilestone == null) return 1f; // maxed out
        
        var previousMilestone = rewards
            .Where(r => r.trophyRequirement <= currentTrophies)
            .OrderByDescending(r => r.trophyRequirement)
            .FirstOrDefault();
            
        int startTrophies = previousMilestone?.trophyRequirement ?? 0;
        int endTrophies = nextMilestone.trophyRequirement;
        
        if (endTrophies <= startTrophies) return 1f;
        
        float progress = (float)(currentTrophies - startTrophies) / (endTrophies - startTrophies);
        return Mathf.Clamp01(progress);
    }
    
    /// <summary>
    /// Get total progress percentage through the entire trophy road
    /// </summary>
    public float GetOverallProgress(int currentTrophies)
    {
        if (rewards.Count == 0) return 0f;
        
        int maxTrophyReward = rewards.Max(r => r.trophyRequirement);
        return Mathf.Clamp01((float)currentTrophies / maxTrophyReward);
    }
    
    /// <summary>
    /// Calculate season reset trophies
    /// </summary>
    public int CalculateSeasonResetTrophies(int currentTrophies)
    {
        if (!hasSeasonReset || currentTrophies < seasonResetThreshold)
            return currentTrophies;
            
        int excessTrophies = currentTrophies - seasonResetThreshold;
        int resetTrophies = Mathf.RoundToInt(excessTrophies * seasonResetPercentage);
        
        return seasonResetThreshold + resetTrophies;
    }
    
    /// <summary>
    /// Get rewards in a specific trophy range
    /// </summary>
    public List<TrophyRoadReward> GetRewardsInRange(int minTrophies, int maxTrophies)
    {
        return rewards
            .Where(r => r.trophyRequirement >= minTrophies && r.trophyRequirement <= maxTrophies)
            .OrderBy(r => r.trophyRequirement)
            .ToList();
    }
    
    /// <summary>
    /// Check if a specific trophy milestone exists
    /// </summary>
    public bool HasMilestone(int trophyAmount)
    {
        return rewards.Any(r => r.trophyRequirement == trophyAmount);
    }
    
    /// <summary>
    /// Get reward at specific trophy milestone
    /// </summary>
    public TrophyRoadReward GetRewardAtMilestone(int trophyAmount)
    {
        return rewards.FirstOrDefault(r => r.trophyRequirement == trophyAmount);
    }
}