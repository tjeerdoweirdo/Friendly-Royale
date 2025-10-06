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
        Debug.Log($"[KingTower] {towerName} is dying! isPlayerKing = {isPlayerKing}");
        
        base.Die();

        // Try multiple ways to find GameManager
        var gm = FindFirstObjectByType<GameManager>();
        if (gm == null)
        {
            gm = GameObject.Find("GameManager")?.GetComponent<GameManager>();
        }
        if (gm == null)
        {
            gm = FindAnyObjectByType<GameManager>();
        }
        
        if (gm == null)
        {
            Debug.LogError("[KingTower] GameManager not found in scene! Cannot end match.");
            return;
        }

        Debug.Log($"[KingTower] Found GameManager: {gm.name}");

        if (isPlayerKing)
        {
            Debug.Log("[KingTower] Player lost the match!");
            gm.LoseMatch("Your King was destroyed!");
        }
        else
        {
            Debug.Log("[KingTower] Player won the match!");
            gm.WinMatch("Enemy King was destroyed!");
        }
    }
}