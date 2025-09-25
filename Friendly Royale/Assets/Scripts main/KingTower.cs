using UnityEngine;

/// <summary>
/// Represents a King Tower. Notifies the GameManager when destroyed.
/// </summary>
public class KingTower : Tower
{
    [Tooltip("Set to true for player's king, false for enemy king.")]
    public bool isPlayerKing = true;

    protected override void Die()
    {
        base.Die();

        var gm = FindObjectOfType<GameManager>();
        if (gm == null)
        {
            Debug.LogWarning("[KingTower] GameManager not found in scene.");
            return;
        }

        if (isPlayerKing)
        {
            Debug.Log("Player lost the match!");
            gm.LoseMatch("Your King was destroyed!");
        }
        else
        {
            Debug.Log("Player won the match!");
            gm.WinMatch("Enemy King was destroyed!");
        }
    }
}