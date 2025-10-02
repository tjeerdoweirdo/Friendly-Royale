using UnityEngine;
using Unity.Netcode;
using System.Collections;

/// <summary>
/// Networked Tower that synchronizes health, attacks, and destruction across all clients.
/// Only the server has authority over tower logic to prevent cheating.
/// </summary>
[RequireComponent(typeof(Collider))]
public class NetworkTower : NetworkBehaviour
{
    [Header("Tower Stats")]
    public string towerName = "Princess Tower";
    public int maxHealth = 2000;
    public float attackRange = 8f;
    [Tooltip("Cooldown in seconds between attacks.")]
    public float attackCooldown = 1f;
    public int damagePerShot = 15;

    [Header("Owner")]
    [Tooltip("Tag used to mark the owner (e.g. 'Player' or 'Enemy').")]
    public string ownerTag = "Player";

    [Header("Faction")]
    public Unit.Faction faction;

    [Header("King Tower Settings")]
    [Tooltip("Check if this tower is the enemy king tower.")]
    public bool isEnemyKingTower = false;
    [Tooltip("Check if this tower is the player king tower.")]
    public bool isPlayerKingTower = false;

    [Header("Health UI")]
    [Tooltip("Prefab with TowerHealthBar script.")]
    public TowerHealthBar healthBarPrefab;

    [Header("Audio")]
    [Tooltip("AudioSource for playing attack sounds.")]
    public AudioSource audioSource;
    [Tooltip("Sound to play when the tower attacks.")]
    public AudioClip attackSound;
    [Tooltip("Sound to play when the tower is destroyed.")]
    public AudioClip destructionSound;

    [Header("Visual Effects")]
    [Tooltip("Particle effect to spawn when tower is destroyed.")]
    public GameObject destructionEffect;
    [Tooltip("Particle effect to spawn when tower attacks.")]
    public GameObject attackEffect;

    // Network Variables
    private NetworkVariable<int> networkCurrentHealth = new NetworkVariable<int>();
    private NetworkVariable<bool> networkIsDestroyed = new NetworkVariable<bool>();
    private NetworkVariable<bool> networkIsAttacking = new NetworkVariable<bool>();
    
    // Local variables
    protected TowerHealthBar healthBarInstance;
    private Transform currentTarget;
    private float lastAttackTime = 0f;
    private bool isInitialized = false;

    // Events
    public static System.Action<NetworkTower> OnTowerDestroyed;
    public static System.Action<NetworkTower> OnKingTowerDestroyed;

    public override void OnNetworkSpawn()
    {
        // Initialize network variables
        if (IsServer)
        {
            networkCurrentHealth.Value = maxHealth;
            networkIsDestroyed.Value = false;
            networkIsAttacking.Value = false;
        }

        // Subscribe to network variable changes
        networkCurrentHealth.OnValueChanged += OnHealthChanged;
        networkIsDestroyed.OnValueChanged += OnDestroyedStateChanged;
        networkIsAttacking.OnValueChanged += OnAttackingStateChanged;

        // Setup components
        SetupComponents();
        
        isInitialized = true;
    }

    public override void OnNetworkDespawn()
    {
        // Unsubscribe from events
        networkCurrentHealth.OnValueChanged -= OnHealthChanged;
        networkIsDestroyed.OnValueChanged -= OnDestroyedStateChanged;
        networkIsAttacking.OnValueChanged -= OnAttackingStateChanged;
    }

    private void SetupComponents()
    {
        // Setup audio source
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        // Create health bar UI
        if (healthBarPrefab != null && healthBarInstance == null)
        {
            healthBarInstance = Instantiate(healthBarPrefab);
            if (healthBarInstance != null)
            {
                healthBarInstance.AttachTo(transform, maxHealth, towerName, faction == Unit.Faction.Enemy);
                healthBarInstance.UpdateHealth(GetCurrentHealth());
            }
        }
    }

    private void Update()
    {
        // Only server controls tower logic
        if (!IsServer || !isInitialized) return;
        
        // Don't do anything if destroyed
        if (networkIsDestroyed.Value) return;

        UpdateTargeting();
        UpdateCombat();
    }

    private void UpdateTargeting()
    {
        // Find the closest enemy within range
        Collider[] enemies = Physics.OverlapSphere(transform.position, attackRange);
        Transform closestEnemy = null;
        float closestDistance = Mathf.Infinity;

        foreach (Collider enemyCollider in enemies)
        {
            // Check for enemy units
            NetworkUnit enemyUnit = enemyCollider.GetComponent<NetworkUnit>();
            if (enemyUnit != null && (int)enemyUnit.faction != (int)faction)
            {
                float distance = Vector3.Distance(transform.position, enemyCollider.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestEnemy = enemyCollider.transform;
                }
            }

            // Check for legacy units (backwards compatibility)
            Unit legacyUnit = enemyCollider.GetComponent<Unit>();
            if (legacyUnit != null && legacyUnit.faction != faction)
            {
                float distance = Vector3.Distance(transform.position, enemyCollider.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestEnemy = enemyCollider.transform;
                }
            }
        }

        currentTarget = closestEnemy;
    }

    private void UpdateCombat()
    {
        if (currentTarget == null)
        {
            networkIsAttacking.Value = false;
            return;
        }

        // Check if target is still in range
        float distanceToTarget = Vector3.Distance(transform.position, currentTarget.position);
        if (distanceToTarget > attackRange)
        {
            currentTarget = null;
            networkIsAttacking.Value = false;
            return;
        }

        // Attack if cooldown is ready
        if (Time.time - lastAttackTime >= attackCooldown)
        {
            AttackTarget();
            lastAttackTime = Time.time;
            networkIsAttacking.Value = true;
        }
    }

    private void AttackTarget()
    {
        if (currentTarget == null) return;

        // Get the network object ID if available
        NetworkObject targetNetObj = currentTarget.GetComponent<NetworkObject>();
        if (targetNetObj != null)
        {
            ExecuteAttackClientRpc(targetNetObj.NetworkObjectId);
        }
        else
        {
            // Fallback for non-networked targets
            ExecuteAttackClientRpc(0); // Use 0 as invalid ID
        }
    }

    [ClientRpc]
    private void ExecuteAttackClientRpc(ulong targetNetworkId)
    {
        Transform target = currentTarget;
        
        // Try to find target by network ID if valid
        if (targetNetworkId != 0 && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkId, out NetworkObject targetNetObj))
        {
            target = targetNetObj.transform;
        }

        if (target == null) return;

        // Play attack sound
        if (attackSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(attackSound);
        }

        // Spawn attack effect
        if (attackEffect != null)
        {
            Instantiate(attackEffect, transform.position, transform.rotation);
        }

        // Deal damage (only on server)
        if (IsServer)
        {
            DealDamageToTarget(target);
        }
    }

    private void DealDamageToTarget(Transform target)
    {
        // Try NetworkUnit first
        NetworkUnit networkUnit = target.GetComponent<NetworkUnit>();
        if (networkUnit != null)
        {
            UnitHealth unitHealth = networkUnit.GetComponent<UnitHealth>();
            if (unitHealth != null)
            {
                unitHealth.TakeDamage(damagePerShot);
            }
            return;
        }

        // Fallback to legacy Unit
        Unit legacyUnit = target.GetComponent<Unit>();
        if (legacyUnit != null)
        {
            UnitHealth unitHealth = legacyUnit.GetComponent<UnitHealth>();
            if (unitHealth != null)
            {
                unitHealth.TakeDamage(damagePerShot);
            }
            return;
        }

        // Check for other towers
        NetworkTower targetTower = target.GetComponent<NetworkTower>();
        if (targetTower != null)
        {
            targetTower.TakeDamage(damagePerShot);
        }
    }

    public void TakeDamage(int damage)
    {
        // Only server can modify health
        if (!IsServer) return;
        
        if (networkIsDestroyed.Value) return;

        networkCurrentHealth.Value = Mathf.Max(0, networkCurrentHealth.Value - damage);

        if (networkCurrentHealth.Value <= 0)
        {
            DestroyTower();
        }
    }

    private void DestroyTower()
    {
        if (!IsServer || networkIsDestroyed.Value) return;

        networkIsDestroyed.Value = true;
        
        // Notify clients about destruction
        OnTowerDestroyedClientRpc();
        
        // Trigger events
        OnTowerDestroyed?.Invoke(this);
        
        if (isEnemyKingTower || isPlayerKingTower)
        {
            OnKingTowerDestroyed?.Invoke(this);
            
            // End the game if a king tower is destroyed
            GameManager gameManager = FindObjectOfType<GameManager>();
            if (gameManager != null)
            {
                if (isPlayerKingTower)
                {
                    gameManager.LoseMatch("King Tower destroyed");
                }
                else if (isEnemyKingTower)
                {
                    gameManager.WinMatch("Enemy King Tower destroyed");
                }
            }
        }
    }

    [ClientRpc]
    private void OnTowerDestroyedClientRpc()
    {
        // Play destruction sound
        if (destructionSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(destructionSound);
        }

        // Spawn destruction effect
        if (destructionEffect != null)
        {
            Instantiate(destructionEffect, transform.position, transform.rotation);
        }

        // Hide health bar
        if (healthBarInstance != null)
        {
            healthBarInstance.gameObject.SetActive(false);
        }

        // Disable collider and renderer
        Collider towerCollider = GetComponent<Collider>();
        if (towerCollider != null)
        {
            towerCollider.enabled = false;
        }

        Renderer towerRenderer = GetComponent<Renderer>();
        if (towerRenderer != null)
        {
            towerRenderer.enabled = false;
        }

        // Start despawn countdown if we're the server
        if (IsServer)
        {
            StartCoroutine(DespawnAfterDelay(5f));
        }
    }

    private IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (NetworkObject != null)
        {
            NetworkObject.Despawn();
        }
    }

    // Network event handlers
    private void OnHealthChanged(int previousValue, int newValue)
    {
        // Update health bar
        if (healthBarInstance != null)
        {
            healthBarInstance.UpdateHealth(newValue);
        }
    }

    private void OnDestroyedStateChanged(bool previousValue, bool newValue)
    {
        if (newValue && !previousValue)
        {
            // Tower was just destroyed
            // Visual/audio effects are handled in OnTowerDestroyedClientRpc
        }
    }

    private void OnAttackingStateChanged(bool previousValue, bool newValue)
    {
        // Update visual effects based on attacking state
        // You can add attack animations here
    }

    // Public getters
    public int GetCurrentHealth()
    {
        return networkCurrentHealth.Value;
    }

    public int GetMaxHealth()
    {
        return maxHealth;
    }

    public bool IsDestroyed()
    {
        return networkIsDestroyed.Value;
    }

    public float GetHealthPercentage()
    {
        return (float)networkCurrentHealth.Value / maxHealth;
    }

    // Public methods for external systems
    public void Heal(int amount)
    {
        if (!IsServer || networkIsDestroyed.Value) return;

        networkCurrentHealth.Value = Mathf.Min(maxHealth, networkCurrentHealth.Value + amount);
    }

    public void SetMaxHealth(int newMaxHealth)
    {
        if (!IsServer) return;

        maxHealth = newMaxHealth;
        networkCurrentHealth.Value = Mathf.Min(networkCurrentHealth.Value, maxHealth);
    }

    // Gizmos for debugging
    private void OnDrawGizmosSelected()
    {
        // Draw attack range
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}