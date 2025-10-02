using UnityEngine;
using Unity.Netcode;

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
    public bool enableNetworking = false;
    
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
    
    // Network state
    private bool isNetworkEnabled => enableNetworking && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

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
    
    protected virtual void Start()
    {
        currentHealth = maxHealth;

        if (healthBarPrefab != null)
        {
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                healthBarInstance = Instantiate(healthBarPrefab, canvas.transform);
                bool isEnemy = (faction == Unit.Faction.Enemy);
                healthBarInstance.AttachTo(transform, maxHealth, towerName, isEnemy);
                healthBarInstance.UpdateHealth(currentHealth);
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
    }
    
    public override void OnNetworkSpawn()
    {
        if (isNetworkEnabled)
        {
            // Initialize network variables
            if (IsServer)
            {
                networkCurrentHealth.Value = currentHealth;
                networkIsDestroyed.Value = false;
            }
            
            // Subscribe to network variable changes
            networkCurrentHealth.OnValueChanged += OnNetworkHealthChanged;
            networkIsDestroyed.OnValueChanged += OnNetworkDestroyedChanged;
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
        if (isNetworkEnabled)
        {
            // Network path - only server can attack
            if (IsServer)
            {
                ExecuteAttack(enemy);
            }
            else
            {
                // Request attack from server
                NetworkObject enemyNetworkObject = enemy.GetComponent<NetworkObject>();
                if (enemyNetworkObject != null)
                {
                    AttackServerRpc(enemyNetworkObject.NetworkObjectId);
                }
            }
        }
        else
        {
            // Single-player path
            ExecuteAttack(enemy);
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

        // Try to damage Tower (legacy)
        var targetTower = enemy.GetComponent<Tower>();
        if (targetTower != null)
        {
            if (targetTower.GetComponent<Health>() == null)
            {
                targetTower.TakeDamage(damagePerShot);
            }
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
        
        if (isNetworkEnabled)
        {
            // Network path - only server can modify health
            if (IsServer)
            {
                ApplyDamage(dmg);
            }
            else
            {
                // Request damage from server
                TakeDamageServerRpc(dmg);
            }
        }
        else
        {
            // Single-player path
            ApplyDamage(dmg);
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void TakeDamageServerRpc(int dmg)
    {
        ApplyDamage(dmg);
    }
    
    private void ApplyDamage(int dmg)
    {
        if (isNetworkEnabled)
        {
            if (networkIsDestroyed.Value) return;
            networkCurrentHealth.Value = Mathf.Max(0, networkCurrentHealth.Value - dmg);
            currentHealth = networkCurrentHealth.Value;
            
            if (networkCurrentHealth.Value <= 0)
            {
                networkIsDestroyed.Value = true;
            }
        }
        else
        {
            currentHealth -= dmg;
            if (currentHealth < 0) currentHealth = 0;
            
            if (healthBarInstance != null)
                healthBarInstance.UpdateHealth(currentHealth);

            if (currentHealth <= 0)
                Die();
        }
    }

    /// <summary>
    /// Called when the tower dies. Made virtual so KingTower can override.
    /// </summary>
    protected virtual void Die()
    {
        Debug.Log($"{towerName} destroyed!");
        Destroy(gameObject);
        // Optionally notify GameManager/MatchEnd here or override in KingTower
    }
}
