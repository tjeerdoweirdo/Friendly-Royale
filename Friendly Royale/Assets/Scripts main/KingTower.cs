using UnityEngine;

/// <summary>
/// Represents a King Tower. Notifies the GameManager when destroyed.
/// </summary>
public class KingTower : Tower
{
    [Tooltip("Set to true for player's king, false for enemy king.")]
    public bool isPlayerKing = true;

    [Header("Level & Health Scaling")]
    [Tooltip("Base max health for King Tower at level 1")] public int baseMaxHealth = 4000;
    [Tooltip("Additional health gained per King Tower level (linear)")] public int healthPerLevel = 400;
    [Tooltip("Optional override per level. If non-empty and contains index for the level (1-based), that value is used for max health.")]
    public int[] levelHealthOverrides;

    [Header("Death Cleanup (King)")]
    [Tooltip("When the king dies, destroy all Units in the scene")] public bool destroyAllUnitsOnDeath = true;
    [Tooltip("When the king dies, also destroy all other Towers in the scene (besides this)")] public bool destroyAllTowersOnDeath = false;
    [Tooltip("Extra objects to destroy when the king dies (e.g., effects, barriers)")] public GameObject[] extraKingObjectsToDestroyOnDeath;

    protected override void Start()
    {
        // Ensure ownerTag and faction are correctly set so damage/isEnemy checks work
        ownerTag = isPlayerKing ? "Player" : "Enemy";
        faction = isPlayerKing ? Unit.Faction.Player : Unit.Faction.Enemy;

        // Compute max health from level before base.Start() initializes currentHealth/health bar
        int level = 1;
        if (PlayerProgress.Instance != null)
        {
            level = PlayerProgress.Instance.GetKingTowerLevel();
        }
        int computedMax = baseMaxHealth + (Mathf.Max(1, level) - 1) * Mathf.Max(0, healthPerLevel);
        if (levelHealthOverrides != null && levelHealthOverrides.Length >= level && level > 0)
        {
            int idx = level - 1; // convert 1-based level to 0-based index
            if (idx >= 0 && idx < levelHealthOverrides.Length && levelHealthOverrides[idx] > 0)
            {
                computedMax = levelHealthOverrides[idx];
            }
        }
        maxHealth = Mathf.Max(1, computedMax);
        base.Start();
        Debug.Log($"[KingTower] Start -> {towerName}: isPlayerKing={isPlayerKing}, ownerTag={ownerTag}, faction={faction}, level={level}, maxHealth={maxHealth}");
    }

    protected override void Die()
    {
        Debug.Log($"[KingTower] {towerName} is dying! isPlayerKing = {isPlayerKing}");
        
        base.Die();

        // Clear battlefield if configured
        if (destroyAllUnitsOnDeath)
        {
            var units = FindObjectsByType<Unit>(FindObjectsSortMode.None);
            foreach (var u in units)
            {
                if (u != null && u.gameObject != null)
                {
                    Destroy(u.gameObject);
                }
            }
        }
        if (destroyAllTowersOnDeath)
        {
            var towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
            foreach (var t in towers)
            {
                if (t == this) continue;
                if (t != null && t.gameObject != null)
                {
                    Destroy(t.gameObject);
                }
            }
        }
        if (extraKingObjectsToDestroyOnDeath != null)
        {
            foreach (var go in extraKingObjectsToDestroyOnDeath)
            {
                if (go != null) Destroy(go);
            }
        }

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