using UnityEngine;
using System;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "CR/Trophy Road/Trophy Road Reward")]
public class TrophyRoadReward : ScriptableObject
{
    [Header("Reward Info")]
    public int trophyRequirement;
    public string rewardName;
    public Sprite rewardIcon;
    public RewardType rewardType;
    
    [Header("Reward Content")]
    public int goldAmount;
    public int gemAmount;
    public List<CardReward> cardRewards = new List<CardReward>();
    public List<ChestReward> chestRewards = new List<ChestReward>();
    
    [Header("Special Rewards")]
    public bool isArenaUnlock;
    public Arena unlockedArena;
    public bool isSpecialReward; // for big milestone rewards
    public string specialRewardDescription;
    
    [System.Serializable]
    public class CardReward
    {
        public Card card;
        public int amount;
    }
    
    [System.Serializable]
    public class ChestReward
    {
        public string chestType; // "wooden", "silver", "gold", "magical"
        public int amount;
    }
    
    public enum RewardType
    {
        Gold,
        Gems,
        Cards,
        Chest,
        Arena,
        Mixed // combination of multiple reward types
    }
    
    [Header("UI Display")]
    public Color backgroundColor = Color.white;
    public bool isFreeTier = true; // for potential premium pass system
    
    /// <summary>
    /// Get a formatted description of all rewards in this milestone
    /// </summary>
    public string GetRewardDescription()
    {
        List<string> descriptions = new List<string>();
        
        if (goldAmount > 0)
            descriptions.Add($"{goldAmount} Gold");
            
        if (gemAmount > 0)
            descriptions.Add($"{gemAmount} Gems");
            
        foreach (var cardReward in cardRewards)
        {
            descriptions.Add($"{cardReward.amount}x {cardReward.card.cardName}");
        }
        
        foreach (var chestReward in chestRewards)
        {
            descriptions.Add($"{chestReward.amount}x {chestReward.chestType} Chest");
        }
        
        if (isArenaUnlock && unlockedArena != null)
            descriptions.Add($"Unlock {unlockedArena.displayName}");
            
        if (isSpecialReward && !string.IsNullOrEmpty(specialRewardDescription))
            descriptions.Add(specialRewardDescription);
        
        return string.Join(", ", descriptions);
    }
    
    /// <summary>
    /// Check if this reward has any content to give
    /// </summary>
    public bool HasRewards()
    {
        return goldAmount > 0 || 
               gemAmount > 0 || 
               cardRewards.Count > 0 || 
               chestRewards.Count > 0 || 
               isArenaUnlock || 
               isSpecialReward;
    }
}