using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class TrophyRoadUI : MonoBehaviour
{
    [Header("UI References")]
    public ScrollRect scrollRect;
    public Transform rewardContainer;
    public GameObject rewardItemPrefab;
    
    [Header("Progress Bar")]
    public Slider overallProgressBar;
    public Slider nextMilestoneProgressBar;
    public TextMeshProUGUI currentTrophiesText;
    public TextMeshProUGUI nextMilestoneText;
    public TextMeshProUGUI nextMilestoneRequirementText;
    
    [Header("Header Info")]
    public TextMeshProUGUI roadNameText;
    public TextMeshProUGUI claimableRewardsText;
    public Button claimAllButton;
    
    [Header("Reward Item Components")]
    public Image rewardIcon;
    public TextMeshProUGUI rewardNameText;
    public TextMeshProUGUI trophyRequirementText;
    public TextMeshProUGUI rewardDescriptionText;
    public Button claimButton;
    public GameObject claimedIndicator;
    public GameObject lockedIndicator;
    
    [Header("Colors")]
    public Color claimableColor = Color.green;
    public Color claimedColor = Color.gray;
    public Color lockedColor = Color.red;
    public Color specialRewardColor = Color.yellow;
    
    private TrophyRoadManager trophyRoadManager;
    private PlayerProgress playerProgress;
    private List<TrophyRoadRewardItem> rewardItems = new List<TrophyRoadRewardItem>();
    
    private void Start()
    {
        trophyRoadManager = TrophyRoadManager.Instance;
        playerProgress = PlayerProgress.Instance;
        
        if (trophyRoadManager == null)
        {
            Debug.LogError("TrophyRoadUI: TrophyRoadManager not found!");
            return;
        }
        
        if (playerProgress == null)
        {
            Debug.LogError("TrophyRoadUI: PlayerProgress not found!");
            return;
        }
        
        // Subscribe to events
        trophyRoadManager.OnTrophiesChanged += UpdateUI;
        trophyRoadManager.OnRewardClaimed += OnRewardClaimed;
        trophyRoadManager.OnNewMilestoneReached += OnNewMilestoneReached;
        
        // Setup UI
        SetupClaimAllButton();
        CreateRewardItems();
        UpdateUI(playerProgress.currentTrophies);
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (trophyRoadManager != null)
        {
            trophyRoadManager.OnTrophiesChanged -= UpdateUI;
            trophyRoadManager.OnRewardClaimed -= OnRewardClaimed;
            trophyRoadManager.OnNewMilestoneReached -= OnNewMilestoneReached;
        }
    }
    
    /// <summary>
    /// Setup the claim all button
    /// </summary>
    private void SetupClaimAllButton()
    {
        if (claimAllButton != null)
        {
            claimAllButton.onClick.AddListener(ClaimAllRewards);
        }
    }
    
    /// <summary>
    /// Create UI items for all rewards
    /// </summary>
    private void CreateRewardItems()
    {
        if (trophyRoadManager.currentTrophyRoad == null || rewardContainer == null || rewardItemPrefab == null)
            return;
            
        // Clear existing items
        foreach (Transform child in rewardContainer)
        {
            Destroy(child.gameObject);
        }
        rewardItems.Clear();
        
        // Create items for each reward
        foreach (var reward in trophyRoadManager.currentTrophyRoad.rewards)
        {
            GameObject itemGO = Instantiate(rewardItemPrefab, rewardContainer);
            TrophyRoadRewardItem item = itemGO.GetComponent<TrophyRoadRewardItem>();
            
            if (item == null)
            {
                item = itemGO.AddComponent<TrophyRoadRewardItem>();
            }
            
            item.SetupReward(reward, this);
            rewardItems.Add(item);
        }
    }
    
    /// <summary>
    /// Update the entire UI
    /// </summary>
    private void UpdateUI(int currentTrophies)
    {
        UpdateProgressBars();
        UpdateHeaderInfo();
        UpdateRewardItems();
    }
    
    /// <summary>
    /// Update progress bars
    /// </summary>
    private void UpdateProgressBars()
    {
        if (trophyRoadManager == null) return;
        
        // Overall progress
        if (overallProgressBar != null)
        {
            overallProgressBar.value = trophyRoadManager.GetOverallProgress();
        }
        
        // Next milestone progress
        if (nextMilestoneProgressBar != null)
        {
            nextMilestoneProgressBar.value = trophyRoadManager.GetProgressToNextMilestone();
        }
        
        // Trophy count text
        if (currentTrophiesText != null)
        {
            currentTrophiesText.text = $"{playerProgress.currentTrophies}";
        }
        
        // Next milestone info
        var nextMilestone = trophyRoadManager.GetNextMilestone();
        if (nextMilestone != null)
        {
            if (nextMilestoneText != null)
                nextMilestoneText.text = nextMilestone.rewardName;
                
            if (nextMilestoneRequirementText != null)
                nextMilestoneRequirementText.text = $"{nextMilestone.trophyRequirement}";
        }
        else
        {
            if (nextMilestoneText != null)
                nextMilestoneText.text = "Max Level!";
                
            if (nextMilestoneRequirementText != null)
                nextMilestoneRequirementText.text = "";
        }
    }
    
    /// <summary>
    /// Update header information
    /// </summary>
    private void UpdateHeaderInfo()
    {
        if (trophyRoadManager.currentTrophyRoad != null && roadNameText != null)
        {
            roadNameText.text = trophyRoadManager.currentTrophyRoad.roadName;
        }
        
        var claimableRewards = trophyRoadManager.GetClaimableRewards();
        if (claimableRewardsText != null)
        {
            claimableRewardsText.text = $"Claimable: {claimableRewards.Count}";
        }
        
        if (claimAllButton != null)
        {
            claimAllButton.interactable = claimableRewards.Count > 0;
        }
    }
    
    /// <summary>
    /// Update all reward items
    /// </summary>
    private void UpdateRewardItems()
    {
        foreach (var item in rewardItems)
        {
            item.UpdateDisplay();
        }
    }
    
    /// <summary>
    /// Claim all available rewards
    /// </summary>
    private void ClaimAllRewards()
    {
        int claimedCount = trophyRoadManager.ClaimAllAvailableRewards();
        
        if (claimedCount > 0)
        {
            Debug.Log($"Claimed {claimedCount} trophy road rewards!");
            // Show reward popup or animation here
        }
    }
    
    /// <summary>
    /// Called when a reward is claimed
    /// </summary>
    private void OnRewardClaimed(TrophyRoadReward reward)
    {
        // Find and update the specific reward item
        var item = rewardItems.FirstOrDefault(r => r.reward == reward);
        if (item != null)
        {
            item.UpdateDisplay();
        }
        
        // Show reward popup or animation
        ShowRewardClaimedEffect(reward);
    }
    
    /// <summary>
    /// Called when a new milestone is reached
    /// </summary>
    private void OnNewMilestoneReached(TrophyRoadReward milestone)
    {
        // Scroll to the new milestone
        ScrollToReward(milestone);
        
        // Show milestone reached effect
        ShowMilestoneReachedEffect(milestone);
    }
    
    /// <summary>
    /// Scroll to a specific reward
    /// </summary>
    public void ScrollToReward(TrophyRoadReward reward)
    {
        if (scrollRect == null) return;
        
        var item = rewardItems.FirstOrDefault(r => r.reward == reward);
        if (item != null)
        {
            // Calculate scroll position
            float itemPosition = item.transform.localPosition.x;
            float containerWidth = rewardContainer.GetComponent<RectTransform>().rect.width;
            float viewportWidth = scrollRect.viewport.rect.width;
            
            float normalizedPosition = Mathf.Clamp01(itemPosition / (containerWidth - viewportWidth));
            scrollRect.horizontalNormalizedPosition = normalizedPosition;
        }
    }
    
    /// <summary>
    /// Show reward claimed visual effect
    /// </summary>
    private void ShowRewardClaimedEffect(TrophyRoadReward reward)
    {
        // Implement particle effects, animations, or popups here
        Debug.Log($"Reward claimed effect for: {reward.rewardName}");
    }
    
    /// <summary>
    /// Show milestone reached visual effect
    /// </summary>
    private void ShowMilestoneReachedEffect(TrophyRoadReward milestone)
    {
        // Implement celebration effects here
        Debug.Log($"Milestone reached effect for: {milestone.rewardName}");
    }
}

/// <summary>
/// Individual reward item component
/// </summary>
public class TrophyRoadRewardItem : MonoBehaviour
{
    [Header("UI Components")]
    public Image rewardIcon;
    public TextMeshProUGUI rewardNameText;
    public TextMeshProUGUI trophyRequirementText;
    public TextMeshProUGUI rewardDescriptionText;
    public Button claimButton;
    public GameObject claimedIndicator;
    public GameObject lockedIndicator;
    public Image backgroundImage;
    
    [HideInInspector]
    public TrophyRoadReward reward;
    private TrophyRoadUI parentUI;
    
    /// <summary>
    /// Setup this reward item
    /// </summary>
    public void SetupReward(TrophyRoadReward rewardData, TrophyRoadUI ui)
    {
        reward = rewardData;
        parentUI = ui;
        
        // Setup UI elements
        if (rewardIcon != null && reward.rewardIcon != null)
            rewardIcon.sprite = reward.rewardIcon;
            
        if (rewardNameText != null)
            rewardNameText.text = reward.rewardName;
            
        if (trophyRequirementText != null)
            trophyRequirementText.text = reward.trophyRequirement.ToString();
            
        if (rewardDescriptionText != null)
            rewardDescriptionText.text = reward.GetRewardDescription();
        
        // Setup claim button
        if (claimButton != null)
        {
            claimButton.onClick.AddListener(ClaimReward);
        }
        
        UpdateDisplay();
    }
    
    /// <summary>
    /// Update the visual display based on current state
    /// </summary>
    public void UpdateDisplay()
    {
        if (reward == null) return;
        
        var playerProgress = PlayerProgress.Instance;
        if (playerProgress == null) return;
        
        bool canClaim = playerProgress.currentTrophies >= reward.trophyRequirement;
        bool alreadyClaimed = playerProgress.claimedTrophyRewards.Contains(reward.trophyRequirement);
        
        // Update button state
        if (claimButton != null)
        {
            claimButton.interactable = canClaim && !alreadyClaimed;
        }
        
        // Update indicators
        if (claimedIndicator != null)
            claimedIndicator.SetActive(alreadyClaimed);
            
        if (lockedIndicator != null)
            lockedIndicator.SetActive(!canClaim);
        
        // Update background color
        if (backgroundImage != null)
        {
            if (alreadyClaimed)
                backgroundImage.color = parentUI.claimedColor;
            else if (canClaim)
                backgroundImage.color = reward.isSpecialReward ? parentUI.specialRewardColor : parentUI.claimableColor;
            else
                backgroundImage.color = parentUI.lockedColor;
        }
    }
    
    /// <summary>
    /// Claim this reward
    /// </summary>
    private void ClaimReward()
    {
        var trophyRoadManager = TrophyRoadManager.Instance;
        if (trophyRoadManager != null)
        {
            trophyRoadManager.ClaimReward(reward);
        }
    }
}