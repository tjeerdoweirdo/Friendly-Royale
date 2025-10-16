using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

/// <summary>
/// Basic tower behavior with optional network support:
/// - HP, attack loop, finds nearest enemy by tag "Enemy"
/// - Applies instant melee damage (no projectile)
/// - Shows a TowerHealthBar UI if assigned
/// </summary>
[RequireComponent(typeof(Collider))]
public class Tower : NetworkBehaviour
{
    [Header("Tower Stats")]
    public string towerName = "Princess Tower";
    public int maxHealth = 2000;
    public float attackRange = 8f;
    [Tooltip("Cooldown in seconds between attacks.")]
    public float attackCooldown = 1f;
    public int damagePerShot = 15;

    [Header("Owner")]
    [Tooltip("Tag used to mark the owner (e.g. 'Player' or 'Enemy'). Projectiles will be configured to ignore this tag.")]
    public string ownerTag = "Player";


    [Header("Network Settings")]
    [Tooltip("Enable networking for this tower")]  
    public bool enableNetworking = true;
    
    [Header("Faction")]
    public Unit.Faction faction;
    
    // Network Variables
    private NetworkVariable<int> networkCurrentHealth = new NetworkVariable<int>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    
    private NetworkVariable<bool> networkIsDestroyed = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    
    // Network state (only treat as network-enabled if transport is listening AND we are actually a server or client)
    private bool isNetworkEnabled => enableNetworking
        && NetworkManager.Singleton != null
        && NetworkManager.Singleton.IsListening
        && (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient);

    [Header("King Tower Settings")]
    [Tooltip("Check if this tower is the enemy king tower.")]
    public bool isEnemyKingTower = false;

    [Header("Health UI")]
    [Tooltip("Prefab with TowerHealthBar script (screen-space canvas).")]
    public TowerHealthBar healthBarPrefab;

    [Header("Audio")]
    [Tooltip("AudioSource for playing attack sounds. If not assigned, one will be added at runtime.")]
    public AudioSource audioSource;
    [Tooltip("Sound to play when the tower attacks.")]
    public AudioClip attackSound;

    // internal
    protected int currentHealth;
    private float lastAttackTime = 0f;
    protected TowerHealthBar healthBarInstance;

    /// <summary>
    /// Public accessor for current health (for UI and debugging)
    /// </summary>
    public int CurrentHealth => currentHealth;

    [Header("Death Cleanup")]
    [Tooltip("Destroy the spawned health bar UI when this tower dies")] public bool destroyHealthBarOnDeath = true;
    [Tooltip("Any additional scene objects to destroy when this tower dies (e.g., auxiliary visuals, markers)")] public List<GameObject> extraObjectsToDestroyOnDeath = new List<GameObject>();
    
    protected virtual void Awake()
    {
        // If Netcode is active and we have a NetworkObject, auto-enable networking to keep health in sync across clients
        if (!enableNetworking && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            var no = GetComponent<NetworkObject>();
            if (no != null)
            {
                enableNetworking = true;
                Debug.Log($"[Tower] Awake auto-enable networking for {towerName} (NetworkObject present)");
            }
            else
            {
                Debug.LogWarning($"[Tower] Netcode running but {towerName} has no NetworkObject. Health will not sync to clients.");
            }
        }

        // If game is explicitly offline / practice, force disable networking to avoid RPC path doing nothing.
        var gmMode = GameModeManager.Instance;
        if (gmMode != null && gmMode.IsOfflineMode())
        {
            if (enableNetworking)
            {
                Debug.Log($"[Tower] Disabling networking for {towerName} because game is offline/practice.");
            }
            enableNetworking = false;
        }

        // Perform early faction assignment here (was only in Start) so any early spawns / unit target scans post-Awake see correct faction
        if (string.Equals(ownerTag, "Player", System.StringComparison.OrdinalIgnoreCase))
        {
            faction = Unit.Faction.Player;
        }
        else if (string.Equals(ownerTag, "Enemy", System.StringComparison.OrdinalIgnoreCase))
        {
            faction = Unit.Faction.Enemy;
        }
        // Do NOT set currentHealth here yet because derived classes (KingTower) may adjust maxHealth before calling base.Start().
    }
    
    protected virtual void Start()
    {
        currentHealth = maxHealth;
        // Faction already set in Awake (kept here previously) – removed duplication.

        if (healthBarPrefab != null)
        {
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                healthBarInstance = Instantiate(healthBarPrefab, canvas.transform);
                bool isEnemy = (faction == Unit.Faction.Enemy);
                healthBarInstance.AttachTo(transform, maxHealth, towerName, isEnemy);
                healthBarInstance.UpdateHealth(currentHealth);
                if (healthBarInstance.slider == null)
                {
                    Debug.LogWarning($"[Tower] {towerName} health bar Slider is not assigned on the prefab - only text will update.");
                }
            }
            else
            {
                Debug.LogWarning("[Tower] No Canvas found in scene for TowerHealthBar; assign a Canvas in the scene.");
            }
        }

        // Ensure audio source exists
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        Debug.Log($"[Tower] Start -> {towerName}: ownerTag={ownerTag}, faction={faction}, maxHealth={maxHealth}");
    }
    
    public override void OnNetworkSpawn()
    {
        // If this object is part of a spawned network session, force-enable networking
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            enableNetworking = true;
        }

        if (!isNetworkEnabled) return;

        // Initialize network variables (server authoritative)
        if (IsServer)
        {
            networkCurrentHealth.Value = currentHealth;
            networkIsDestroyed.Value = false;
        }
        
        // Subscribe to network variable changes
        networkCurrentHealth.OnValueChanged += OnNetworkHealthChanged;
        networkIsDestroyed.OnValueChanged += OnNetworkDestroyedChanged;

        // Immediately sync current value on spawn (clients won't get an event unless value changes)
        currentHealth = networkCurrentHealth.Value;
        if (healthBarInstance != null)
        {
            healthBarInstance.UpdateHealth(currentHealth);
        }
    }
    
    public override void OnNetworkDespawn()
    {
        if (isNetworkEnabled)
        {
            // Unsubscribe from events
            networkCurrentHealth.OnValueChanged -= OnNetworkHealthChanged;
            networkIsDestroyed.OnValueChanged -= OnNetworkDestroyedChanged;
        }
    }
    
    private void OnNetworkHealthChanged(int previousValue, int newValue)
    {
        currentHealth = newValue;
        if (healthBarInstance != null)
            healthBarInstance.UpdateHealth(currentHealth);
    }
    
    private void OnNetworkDestroyedChanged(bool previousValue, bool newValue)
    {
        if (newValue && !previousValue)
        {
            // Tower was just destroyed
            Die();
        }
    }

    protected virtual void Update()
    {
        GameObject target = FindNearestEnemy();
        if (target != null)
        {
            // Only attack if the target is a living unit
            var unit = target.GetComponent<Unit>();
            bool isAlive = true;
            if (unit != null && unit.health != null)
                isAlive = unit.health.IsAlive;
            var hp = target.GetComponent<UnitHealth>();
            if (hp != null)
                isAlive = isAlive && hp.IsAlive;
            if (isAlive)
            {
                float dist = Vector3.Distance(transform.position, target.transform.position);
                if (dist <= attackRange + 0.1f && (Time.time - lastAttackTime >= attackCooldown))
                {
                    Attack(target);
                    lastAttackTime = Time.time;
                }
            }
        }

        if (healthBarInstance != null)
        {
            healthBarInstance.UpdateHealth(currentHealth);
        }
    }

    protected virtual void Attack(GameObject enemy)
    {
        if (!isNetworkEnabled)
        {
            ExecuteAttack(enemy); // local fallback
            return;
        }

        // Network path
        if (IsServer)
        {
            ExecuteAttack(enemy);
        }
        else
        {
            NetworkObject enemyNetworkObject = enemy.GetComponent<NetworkObject>();
            if (enemyNetworkObject != null)
            {
                AttackServerRpc(enemyNetworkObject.NetworkObjectId);
            }
            else
            {
                // Fallback: if server object missing or not networked properly, execute locally to avoid silent failure in practice
                Debug.LogWarning($"[Tower] {towerName} could not find enemy NetworkObject for RPC attack; executing locally fallback.");
                ExecuteAttack(enemy);
            }
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void AttackServerRpc(ulong enemyNetworkId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(enemyNetworkId, out NetworkObject enemyNetworkObject))
        {
            ExecuteAttack(enemyNetworkObject.gameObject);
        }
    }
    
    private void ExecuteAttack(GameObject enemy)
    {
        // Play attack sound if assigned
        if (attackSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(attackSound);
        }

        // Prefer damaging Tower if present (handles child colliders)
        var targetTower = enemy.GetComponentInParent<Tower>();
        if (targetTower != null && targetTower != this)
        {
            // Only damage enemies
            if (targetTower.faction != this.faction)
            {
                targetTower.TakeDamage(damagePerShot);
                return;
            }
        }

        // Melee/direct damage (like non-ranged Unit)
        var targetHealth = enemy.GetComponent<UnitHealth>();
        if (targetHealth != null && targetHealth.IsAlive)
        {
            targetHealth.TakeDamage(damagePerShot, gameObject);
            return;
        }

        // Try to damage Health (for buildings or towers)
        var healthComp = enemy.GetComponent<Health>();
        if (healthComp != null && !healthComp.isDead)
        {
            healthComp.TakeDamage(damagePerShot);
            return;
        }
    }

    protected GameObject FindNearestEnemy()
    {
        float closest = float.MaxValue;
        GameObject nearest = null;

        // 1. Try to find enemy Units by faction
        var allUnits = UnityEngine.Object.FindObjectsByType<Unit>(UnityEngine.FindObjectsSortMode.None);
        foreach (var unit in allUnits)
        {
            // Only attack units with a different faction
            if (unit.faction != this.faction)
            {
                if (unit.health != null && !unit.health.IsAlive) continue;
                float dist = Vector3.Distance(transform.position, unit.transform.position);
                if (dist < attackRange && dist < closest)
                {
                    closest = dist;
                    nearest = unit.gameObject;
                }
            }
        }

        // Only target Units
        return nearest;
    }

    /// <summary>
    /// Called by other code to apply damage to this tower.
    /// </summary>
    public virtual void TakeDamage(int dmg)
    {
        if (dmg <= 0) return;

        // Global arm check: avoid applying damage before scene auto configuration finalizes factions.
        if (!TowerSceneAutoConfigurator.DamageArmed)
        {
            // Failsafe: if damage is coming in but damage isn't armed within a short time window, force arm so gameplay isn't stuck.
            _firstUnarmedDamageTime ??= Time.time;
            float elapsed = Time.time - _firstUnarmedDamageTime.Value;
            if (elapsed > 2f)
            {
                TowerSceneAutoConfigurator.ForceArmDamage($"Failsafe after {elapsed:F2}s (tower {towerName} receiving live damage attempts)");
            }
            else
            {
                Debug.Log($"[Tower] {towerName} received damage {dmg} before DamageArmed (elapsed {elapsed:F2}s); temporarily ignoring until armed.");
                return; // skip without applying until armed or failsafe triggers
            }
        }
    Debug.Log($"[Tower] {towerName} taking {dmg} damage. Current health: {currentHealth}/{maxHealth} (net={isNetworkEnabled}, server={IsServer}, listening={ (NetworkManager.Singleton!=null && NetworkManager.Singleton.IsListening)} )");

        if (!isNetworkEnabled)
        {
            ApplyDamage(dmg);
            return;
        }

        // Network-enabled path
        if (IsServer)
        {
            ApplyDamage(dmg);
        }
        else
        {
            bool hasNet = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            bool serverUp = hasNet && NetworkManager.Singleton.IsServer;
            if (!hasNet || !serverUp)
            {
                Debug.LogWarning($"[Tower] {towerName} net flag ON but no authoritative server -> local damage fallback.");
                ApplyDamage(dmg);
                return;
            }
            TakeDamageServerRpc(dmg);
        }
    }

    // Static timestamp for first unarmed damage observation
    private static float? _firstUnarmedDamageTime = null;
    
    [ServerRpc(RequireOwnership = false)]
    private void TakeDamageServerRpc(int dmg)
    {
        ApplyDamage(dmg);
    }
    
    private void ApplyDamage(int dmg)
    {
        Debug.Log($"[Tower] ApplyDamage called on {towerName}. Damage: {dmg}, isNetworkEnabled: {isNetworkEnabled}, currentHealth: {currentHealth}");
        
        if (isNetworkEnabled)
        {
            Debug.Log($"[Tower] Network path - IsServer: {IsServer}");
            if (networkIsDestroyed.Value) 
            {
                Debug.Log($"[Tower] {towerName} already destroyed, ignoring damage");
                return;
            }
            int oldHealth = networkCurrentHealth.Value;
            networkCurrentHealth.Value = Mathf.Max(0, networkCurrentHealth.Value - dmg);
            currentHealth = networkCurrentHealth.Value;
            Debug.Log($"[Tower] {towerName} network health: {oldHealth} -> {networkCurrentHealth.Value}");
            
            // Update health bar for network mode too
            if (healthBarInstance != null)
            {
                Debug.Log($"[Tower] Updating health bar (network) from {oldHealth} to {currentHealth}");
                healthBarInstance.UpdateHealth(currentHealth);
            }
            
            if (networkCurrentHealth.Value <= 0)
            {
                networkIsDestroyed.Value = true;
            }
        }
        else
        {
            Debug.Log($"[Tower] Single-player path for {towerName}");
            int oldHealth = currentHealth;
            currentHealth -= dmg;
            if (currentHealth < 0) currentHealth = 0;
            Debug.Log($"[Tower] {towerName} health: {oldHealth} -> {currentHealth}");
            
            // Force health bar update
            if (healthBarInstance != null)
            {
                Debug.Log($"[Tower] Updating health bar from {oldHealth} to {currentHealth}");
                healthBarInstance.UpdateHealth(currentHealth);
            }
            else
            {
                Debug.LogWarning($"[Tower] {towerName} has no health bar instance!");
            }

            if (currentHealth <= 0)
            {
                Debug.Log($"[Tower] {towerName} health reached 0, calling Die()");
                Die();
            }
        }
    }

    /// <summary>
    /// Called when the tower dies. Made virtual so KingTower can override.
    /// </summary>
    protected virtual void Die()
    {
        Debug.Log($"{towerName} destroyed!");
        // Cleanup UI and extras
        if (destroyHealthBarOnDeath && healthBarInstance != null)
        {
            Destroy(healthBarInstance.gameObject);
            healthBarInstance = null;
        }
        if (extraObjectsToDestroyOnDeath != null)
        {
            foreach (var go in extraObjectsToDestroyOnDeath)
            {
                if (go != null) Destroy(go);
            }
            extraObjectsToDestroyOnDeath.Clear();
        }
        Destroy(gameObject);
        // Optionally notify GameManager/MatchEnd here or override in KingTower
    }
}
