using UnityEngine;

/// <summary>
/// Represents a King Tower. Notifies the GameManager when destroyed.
/// </summary>
public class KingTower : Tower
{
    public enum KingTowerType
    {
        PlayerKing,
        EnemyKing
    }

    [Header("King Tower Type")]
    [Tooltip("Select whether this is a Player King Tower or Enemy King Tower")]
    public KingTowerType kingTowerType = KingTowerType.PlayerKing;
    
    // Backward compatibility property
    public bool isPlayerKing => kingTowerType == KingTowerType.PlayerKing;

    [Header("Level & Health Scaling")]
    [Tooltip("Base max health for King Tower at level 1")] public int baseMaxHealth = 4000;
    [Tooltip("Additional health gained per King Tower level (linear)")] public int healthPerLevel = 400;
    [Tooltip("Optional override per level. If non-empty and contains index for the level (1-based), that value is used for max health.")]
    public int[] levelHealthOverrides;

    [Header("Death Cleanup (King)")]
    [Tooltip("When the king dies, destroy all Units in the scene")] public bool destroyAllUnitsOnDeath = true;
    [Tooltip("When the king dies, also destroy all other Towers in the scene (besides this)")] public bool destroyAllTowersOnDeath = false;
    [Tooltip("Extra objects to destroy when the king dies (e.g., effects, barriers)")] public GameObject[] extraKingObjectsToDestroyOnDeath;

    [Header("Auto-Sync Settings")]
    [Tooltip("If enabled, automatically sets ownerTag and faction based on King Tower Type selection")]
    public bool autoSyncSettings = true;

    [Header("Networking Override")]
    [Tooltip("Force this KingTower to operate purely locally (no Netcode sync) even if a NetworkManager exists.")]
    public bool forceLocal = true;
    
#if UNITY_EDITOR
    [Space]
    [Tooltip("Click to manually sync ownerTag and faction to match King Tower Type")]
    public bool syncNow = false;
#endif

    // Readiness flag to guard early damage attempts during scene load
    private bool initialized = false;
    private int bufferedPreInitDamage = 0;

    protected override void Awake()
    {
        // Run Tower base Awake first for networking + early faction from ownerTag
        base.Awake();
        // If auto sync is enabled, ensure ownerTag/faction are correct ASAP (before other Units start scanning)
        if (autoSyncSettings)
        {
            ownerTag = isPlayerKing ? "Player" : "Enemy";
            faction = isPlayerKing ? Unit.Faction.Player : Unit.Faction.Enemy;
        }
        if (forceLocal && enableNetworking)
        {
            Debug.Log("[KingTower] forceLocal active -> disabling networking for this KingTower instance.");
            enableNetworking = false; // ensures damage + health are handled immediately and locally
        }
        // Provide an early debug trace
        Debug.Log($"[KingTower] Awake -> {towerName}: ownerTag={ownerTag} faction={faction} autoSync={autoSyncSettings}");
    }

    protected override void Start()
    {
        // Only auto-set ownerTag and faction if autoSyncSettings is enabled
        if (autoSyncSettings)
        {
            ownerTag = isPlayerKing ? "Player" : "Enemy";
            faction = isPlayerKing ? Unit.Faction.Player : Unit.Faction.Enemy;
        }

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
        Debug.Log($"[KingTower] Start -> {towerName}: kingTowerType={kingTowerType}, ownerTag={ownerTag}, faction={faction}, level={level}, maxHealth={maxHealth}");

        initialized = true;
        if (bufferedPreInitDamage > 0)
        {
            Debug.Log($"[KingTower] Applying buffered damage {bufferedPreInitDamage} after initialization.");
            base.TakeDamage(bufferedPreInitDamage);
            bufferedPreInitDamage = 0;
        }
    }

#if UNITY_EDITOR
    private KingTowerType lastKingTowerType;
    private bool hasInitialized = false;

    /// <summary>
    /// Validates settings in the editor to ensure consistency
    /// </summary>
    void OnValidate()
    {
        // Initialize on first run
        if (!hasInitialized)
        {
            lastKingTowerType = kingTowerType;
            hasInitialized = true;
            
            // Only auto-sync on first initialization if enabled
            if (autoSyncSettings)
            {
                SyncSettingsToKingTowerType();
            }
            return;
        }

        // Only auto-sync if the King Tower Type dropdown was changed AND auto-sync is enabled
        if (autoSyncSettings && lastKingTowerType != kingTowerType)
        {
            SyncSettingsToKingTowerType();
        }
        
        // Manual sync button
        if (syncNow)
        {
            syncNow = false; // Reset the button
            SyncSettingsToKingTowerType();
        }
        
        // Update the last known type
        lastKingTowerType = kingTowerType;
    }
    
    private void SyncSettingsToKingTowerType()
    {
        if (kingTowerType == KingTowerType.PlayerKing)
        {
            ownerTag = "Player";
            faction = Unit.Faction.Player;
            if (string.IsNullOrEmpty(towerName)) towerName = "Player King Tower";
        }
        else if (kingTowerType == KingTowerType.EnemyKing)
        {
            ownerTag = "Enemy";
            faction = Unit.Faction.Enemy;
            if (string.IsNullOrEmpty(towerName)) towerName = "Enemy King Tower";
        }
    }
#endif

    /// <summary>
    /// Override TakeDamage to add logging for debugging
    /// </summary>
    public override void TakeDamage(int dmg)
    {
        if (!initialized)
        {
            // Buffer damage until Start completes to prevent "invulnerable on scene load" race
            bufferedPreInitDamage += dmg;
            Debug.Log($"[KingTower] Received {dmg} damage before initialization complete. Buffering total={bufferedPreInitDamage}.");
            return;
        }
        Debug.Log($"[KingTower] {towerName} taking {dmg} damage! Current health BEFORE: {currentHealth}/{maxHealth}, Faction: {faction}");
        base.TakeDamage(dmg);
        Debug.Log($"[KingTower] {towerName} health AFTER damage: {currentHealth}/{maxHealth}");
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