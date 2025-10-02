using UnityEngine;

/// <summary>
/// Script to create default trophy road rewards and trophy road data
/// Run this in the Unity Editor to generate the trophy road assets
/// </summary>
public class TrophyRoadDataCreator : MonoBehaviour
{
    [Header("Trophy Road Creation")]
    public bool createDefaultTrophyRoad = false;
    
    [ContextMenu("Create Default Trophy Road")]
    public void CreateDefaultTrophyRoad()
    {
        #if UNITY_EDITOR
        CreateTrophyRoadRewards();
        CreateMainTrophyRoad();
        Debug.Log("Default Trophy Road created! Check Assets/TrophyRoad folder");
        #else
        Debug.LogWarning("Trophy Road creation only works in Unity Editor");
        #endif
    }
    
    #if UNITY_EDITOR
    private void CreateTrophyRoadRewards()
    {
        // Create directory if it doesn't exist
        string folderPath = "Assets/TrophyRoad";
        if (!UnityEditor.AssetDatabase.IsValidFolder(folderPath))
        {
            UnityEditor.AssetDatabase.CreateFolder("Assets", "TrophyRoad");
        }
        
        // Define trophy road milestones with rewards
        var milestones = new[]
        {
            new { trophies = 0, name = "Welcome Gift", gold = 100, gems = 0, description = "Welcome to the arena!" },
            new { trophies = 50, name = "First Victory", gold = 50, gems = 5, description = "Your first milestone!" },
            new { trophies = 100, name = "Bronze Chest", gold = 75, gems = 0, description = "Keep climbing!" },
            new { trophies = 200, name = "Silver Reward", gold = 100, gems = 10, description = "You're getting stronger!" },
            new { trophies = 300, name = "Gold Boost", gold = 150, gems = 0, description = "Golden progress!" },
            new { trophies = 400, name = "Arena Master", gold = 200, gems = 15, description = "Arena domination!" },
            new { trophies = 500, name = "Gem Cache", gold = 0, gems = 25, description = "Precious gems!" },
            new { trophies = 750, name = "Big Reward", gold = 300, gems = 20, description = "Major milestone!" },
            new { trophies = 1000, name = "Champion's Bounty", gold = 500, gems = 50, description = "You're a champion!" },
            new { trophies = 1250, name = "Elite Status", gold = 400, gems = 30, description = "Elite level achieved!" },
            new { trophies = 1500, name = "Master's Cache", gold = 600, gems = 40, description = "Master level!" },
            new { trophies = 2000, name = "Legendary Gift", gold = 1000, gems = 100, description = "Legendary achievement!" },
            new { trophies = 2500, name = "Grand Prize", gold = 750, gems = 75, description = "Grand milestone!" },
            new { trophies = 3000, name = "Ultimate Reward", gold = 1500, gems = 150, description = "Ultimate achievement!" },
            new { trophies = 4000, name = "Trophy Road Master", gold = 2000, gems = 200, description = "You've mastered the trophy road!" }
        };
        
        foreach (var milestone in milestones)
        {
            TrophyRoadReward reward = ScriptableObject.CreateInstance<TrophyRoadReward>();
            
            reward.trophyRequirement = milestone.trophies;
            reward.rewardName = milestone.name;
            reward.goldAmount = milestone.gold;
            reward.gemAmount = milestone.gems;
            reward.specialRewardDescription = milestone.description;
            
            // Set reward type based on content
            if (milestone.gold > 0 && milestone.gems > 0)
                reward.rewardType = TrophyRoadReward.RewardType.Mixed;
            else if (milestone.gold > 0)
                reward.rewardType = TrophyRoadReward.RewardType.Gold;
            else if (milestone.gems > 0)
                reward.rewardType = TrophyRoadReward.RewardType.Gems;
            
            // Special rewards for big milestones
            if (milestone.trophies >= 1000)
            {
                reward.isSpecialReward = true;
                reward.backgroundColor = Color.yellow;
            }
            
            string assetPath = $"{folderPath}/TrophyReward_{milestone.trophies}.asset";
            UnityEditor.AssetDatabase.CreateAsset(reward, assetPath);
        }
        
        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.AssetDatabase.Refresh();
    }
    
    private void CreateMainTrophyRoad()
    {
        TrophyRoad trophyRoad = ScriptableObject.CreateInstance<TrophyRoad>();
        
        trophyRoad.roadName = "Main Trophy Road";
        trophyRoad.maxTrophies = 5000;
        trophyRoad.hasSeasonReset = true;
        trophyRoad.seasonResetThreshold = 4000;
        trophyRoad.seasonResetPercentage = 0.5f;
        
        // Load all the reward assets we just created
        string[] rewardGuids = UnityEditor.AssetDatabase.FindAssets("t:TrophyRoadReward", new[] { "Assets/TrophyRoad" });
        
        foreach (string guid in rewardGuids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            TrophyRoadReward reward = UnityEditor.AssetDatabase.LoadAssetAtPath<TrophyRoadReward>(path);
            if (reward != null)
            {
                trophyRoad.rewards.Add(reward);
            }
        }
        
        // Sort rewards by trophy requirement
        trophyRoad.rewards.Sort((a, b) => a.trophyRequirement.CompareTo(b.trophyRequirement));
        
        string trophyRoadPath = "Assets/TrophyRoad/MainTrophyRoad.asset";
        UnityEditor.AssetDatabase.CreateAsset(trophyRoad, trophyRoadPath);
        
        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.AssetDatabase.Refresh();
        
        Debug.Log($"Created trophy road with {trophyRoad.rewards.Count} rewards");
    }
    #endif
    
    private void Update()
    {
        #if UNITY_EDITOR
        if (createDefaultTrophyRoad)
        {
            createDefaultTrophyRoad = false;
            CreateDefaultTrophyRoad();
        }
        #endif
    }
}