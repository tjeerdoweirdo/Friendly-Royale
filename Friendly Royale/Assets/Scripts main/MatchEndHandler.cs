using UnityEngine;

/// <summary>
/// Handles awarding coins and trophies at the end of a match.
/// </summary>
public class MatchEndHandler : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Assign the GameManager for match end callbacks.")]
    public GameManager gameManager;

    [Header("Rewards")]
    public int winGold = 100;
    public int winTrophies = 30;
    public int loseGold = 25;
    public int loseTrophies = -15;
    public int drawGold = 50;
    public int drawTrophies = 10;

    private PlayerProgress playerProgress;

    void Awake()
    {
        if (gameManager == null)
        {
            gameManager = FindFirstObjectByType<GameManager>();
        }
        if (PlayerProgress.Instance != null)
        {
            playerProgress = PlayerProgress.Instance;
        }
        else
        {
            playerProgress = FindFirstObjectByType<PlayerProgress>();
        }
    }

    /// <summary>
    /// Call this when the match ends.
    /// </summary>
    /// <param name="result">The match result (Win, Loss, or Draw).</param>
    public void OnMatchEnd(MatchResult result)
    {
        if (playerProgress == null)
        {
            playerProgress = PlayerProgress.Instance ?? FindFirstObjectByType<PlayerProgress>();
            if (playerProgress == null)
            {
                Debug.LogError("PlayerProgress not found in scene!");
                return;
            }
        }
        
        switch (result)
        {
            case MatchResult.Win:
                playerProgress.AddTrophies(winTrophies);
                playerProgress.AddGold(winGold);
                break;
            case MatchResult.Loss:
                playerProgress.AddTrophies(loseTrophies); // Subtract trophies on loss
                playerProgress.AddGold(loseGold);
                break;
            case MatchResult.Draw:
                playerProgress.AddTrophies(drawTrophies);
                playerProgress.AddGold(drawGold);
                break;
        }
    }
}
