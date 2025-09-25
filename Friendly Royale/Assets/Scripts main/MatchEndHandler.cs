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
    /// <param name="playerWon">True if player won, false if lost.</param>
    public void OnMatchEnd(bool playerWon)
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
        if (playerWon)
        {
            playerProgress.AddTrophies(winTrophies);
            playerProgress.AddGold(winGold);
        }
        else
        {
            playerProgress.AddGold(loseGold);
        }
    }
}
