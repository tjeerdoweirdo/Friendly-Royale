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

    [Header("Enemy Level Override (optional)")]
    [Tooltip("If set (>0), Enemy King Tower will use this level when opponent level is not provided by matchmaking.")]
    public int enemyLevelOverride = 0;

    // Static opponent king level broadcast (set by matchmaking when known)
    private static int? s_OpponentKingLevel = null;
    public static void SetOpponentKingLevel(int level)
    {
        s_OpponentKingLevel = Mathf.Max(1, level);
    }

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
        int level = ComputeDesiredLevel();
        maxHealth = Mathf.Max(1, ComputeMaxHealthForLevel(level));
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

    // Computes the level this tower should use based on role and any known opponent level.
    public int ComputeDesiredLevel()
    {
        // Practice/offline: Player king uses average deck level; Enemy king can use arena.botKingLevel when configured
        if (GameModeManager.Instance != null && GameModeManager.Instance.IsOfflineMode())
        {
            if (isPlayerKing)
            {
                return Mathf.Max(1, ComputeAverageDeckLevel());
            }
            else
            {
                int arenaBot = GetArenaBotKingLevel();
                if (arenaBot > 0) return Mathf.Max(1, arenaBot);
                return Mathf.Max(1, ComputeAverageDeckLevel());
            }
        }

        // Online/default: Player king defaults to level 1 (do not mirror local progress)
        if (isPlayerKing)
        {
            return 1;
        }

        // Enemy king (online): use opponent level if known; else optional override; else default 1
        if (s_OpponentKingLevel.HasValue)
        {
            return Mathf.Max(1, s_OpponentKingLevel.Value);
        }
        if (enemyLevelOverride > 0)
        {
            return Mathf.Max(1, enemyLevelOverride);
        }
        return 1;
    }

    // Computes the average level of cards in the current deck for the selected arena; falls back to 1
    private int ComputeAverageDeckLevel()
    {
        try
        {
            var dm = DeckManager.Instance;
            var pp = PlayerProgress.Instance;
            if (dm == null || pp == null) return 1;
            string arenaID = dm.selectedArena != null ? dm.selectedArena.arenaID : (dm.selectedArenaID ?? "default");
            var cards = dm.selectedCards != null && dm.selectedCards.Count > 0 ? dm.selectedCards : dm.deck;
            if (cards == null || cards.Count == 0) return 1;
            int sum = 0; int count = 0;
            foreach (var c in cards)
            {
                if (c == null || string.IsNullOrEmpty(c.cardID)) continue;
                int lvl = pp.GetCardLevel(c.cardID, arenaID);
                sum += Mathf.Max(1, lvl);
                count++;
            }
            if (count == 0) return 1;
            return Mathf.Max(1, Mathf.RoundToInt(sum / (float)count));
        }
        catch { return 1; }
    }

    private int GetArenaBotKingLevel()
    {
        try
        {
            var dm = DeckManager.Instance;
            var arena = dm != null ? dm.selectedArena : null;
            if (arena != null && arena.botKingLevel > 0) return arena.botKingLevel;
        }
        catch { }
        return 0;
    }

    // Compute max health stat for a given level using base/step or overrides.
    public int ComputeMaxHealthForLevel(int level)
    {
        int computedMax = baseMaxHealth + (Mathf.Max(1, level) - 1) * Mathf.Max(0, healthPerLevel);
        if (levelHealthOverrides != null && level > 0 && level <= levelHealthOverrides.Length)
        {
            int idx = level - 1;
            if (idx >= 0 && idx < levelHealthOverrides.Length && levelHealthOverrides[idx] > 0)
            {
                computedMax = levelHealthOverrides[idx];
            }
        }
        return Mathf.Max(1, computedMax);
    }

    // Public API to apply a level post-initialization and update health appropriately.
    public void ApplyLevel(int level, bool keepCurrentHealthPercent = true)
    {
        level = Mathf.Max(1, level);
        int newMax = ComputeMaxHealthForLevel(level);
        if (keepCurrentHealthPercent && maxHealth > 0)
        {
            float pct = Mathf.Clamp01(currentHealth / (float)maxHealth);
            maxHealth = newMax;
            currentHealth = Mathf.RoundToInt(pct * maxHealth);
        }
        else
        {
            maxHealth = newMax;
            currentHealth = maxHealth;
        }

        Debug.Log($"[KingTower] {towerName} ApplyLevel -> level={level} maxHealth={maxHealth} current={currentHealth}");
    }

    // Convenience: Recompute both kings in the current scene using known PlayerProgress and s_OpponentKingLevel.
    public static void RecomputeAllKingsFromKnownLevels()
    {
        var kings = FindObjectsByType<KingTower>(FindObjectsSortMode.None);
        foreach (var kt in kings)
        {
            if (kt == null) continue;
            int level = kt.ComputeDesiredLevel();
            kt.ApplyLevel(level, keepCurrentHealthPercent: false);
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