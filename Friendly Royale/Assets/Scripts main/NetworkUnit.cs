using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;
using System.Collections;

/// <summary>
/// Networked version of Unit that synchronizes position, health, attacks, and state across all clients.
/// Inherits from NetworkBehaviour to provide multiplayer functionality.
/// </summary>
[RequireComponent(typeof(UnitHealth))]
public class NetworkUnit : NetworkBehaviour
{
    public enum Faction { Player, Enemy }
    public enum UnitRole { Normal, Buffer, Debuffer, Healer }
    public enum EffectStat { None, AttackSpeed, MoveSpeed, AttackDamage, Health }
    public enum EffectMode { None, Aura, OnHit }

    [Header("Faction")]
    public Faction faction = Faction.Player;

    [Header("Role & Effects")]
    public UnitRole unitRole = UnitRole.Normal;
    public EffectStat effectStat = EffectStat.None;
    [Tooltip("Positive for buff/heal, negative for debuff")] public float effectAmount = 0f;
    [Tooltip("Duration of buff/debuff/heal in seconds")] public float effectDuration = 3f;
    public EffectMode effectMode = EffectMode.None;
    [Tooltip("Aura radius for buff/debuff/heal")] public float auraRadius = 3f;
    [Tooltip("Interval for aura effect")] public float auraInterval = 1f;
    
    [Header("Visual Effect")]
    [Tooltip("Prefab to spawn for the effect (e.g. aura, heal, debuff visuals)")]
    public GameObject effectPrefab;
    private float auraTimer = 0f;
    private GameObject spawnedEffectInstance;

    [Header("Stats")]
    public float moveSpeed = 3f;
    public float attackRange = 1.2f;
    public int attackDamage = 10;
    public float attackCooldown = 1f;
    public float targetSearchInterval = 0.25f;
    public float stopDistanceToWaypoint = 0.1f;

    [Header("Ranged (optional)")]
    public bool isRanged = false;
    [Tooltip("Prefab that contains your Projectile script.")]
    public GameObject projectilePrefab;
    [Tooltip("Transform where projectiles will spawn (muzzle).")]
    public Transform firePoint;
    public float projectileSpeed = 12f;

    [Header("Splash Attack (optional)")]
    [Tooltip("If true, this unit deals splash damage on attack")]
    public bool isSplash = false;
    [Tooltip("Splash radius for splash attacks")] 
    public float splashRadius = 2.5f;

    [Header("Path")]
    [Tooltip("Left lane waypoints for this unit to follow.")]
    public Transform[] leftPath;
    [Tooltip("Right lane waypoints for this unit to follow.")]
    public Transform[] rightPath;
    [Tooltip("Current active path (left or right).")]
    public Transform[] path;
    private int currentWaypoint = 0;
    private bool usingLeftPath = true;

    [Header("End target")]
    [Tooltip("Tower that is the final destination for this unit.")]
    public Tower endTargetTower;

    [Header("NavMeshAgent")]
    public NavMeshAgent agent;
    [Tooltip("If true and no agent is present, add one at runtime.")]
    public bool addAgentIfMissing = true;
    [Tooltip("If true override agent settings.")]
    public bool overrideAgentSettings = true;
    [Tooltip("Agent type ID to use.")]
    public int agentTypeID = 0;

    [Header("Perception")]
    public float detectionRange = 10f;
    [Range(0, 360)]
    public float viewAngle = 120f;
    public float eyeHeight = 0.9f;
    public LayerMask obstacleMask = ~0;
    public float lostTargetTimeout = 3f;
    public float visualCheckInterval = 0.2f;

    // Network Variables for synchronization
    private NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>();
    private NetworkVariable<Quaternion> networkRotation = new NetworkVariable<Quaternion>();
    private NetworkVariable<bool> networkIsMoving = new NetworkVariable<bool>();
    private NetworkVariable<bool> networkIsAttacking = new NetworkVariable<bool>();
    private NetworkVariable<int> networkCurrentWaypoint = new NetworkVariable<int>();
    private NetworkVariable<float> networkMoveSpeed = new NetworkVariable<float>();
    
    // Local variables
    private Transform currentTarget;
    private float lastAttackTime = 0f;
    private float targetSearchTimer = 0f;
    private UnitHealth unitHealth;
    
    // Audio
    [Header("Audio")]
    public AudioClip attackSound;
    public AudioClip moveSound;
    public AudioClip deathSound;
    private AudioSource audioSource;
    
    private void Awake()
    {
        // Get components
        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
            if (agent == null && addAgentIfMissing)
            {
                agent = gameObject.AddComponent<NavMeshAgent>();
            }
        }
        
        unitHealth = GetComponent<UnitHealth>();
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
    }
    
    public override void OnNetworkSpawn()
    {
        // Initialize network variables
        if (IsOwner)
        {
            networkPosition.Value = transform.position;
            networkRotation.Value = transform.rotation;
            networkMoveSpeed.Value = moveSpeed;
        }
        
        // Subscribe to network variable changes
        networkPosition.OnValueChanged += OnPositionChanged;
        networkRotation.OnValueChanged += OnRotationChanged;
        networkIsMoving.OnValueChanged += OnMovingStateChanged;
        networkIsAttacking.OnValueChanged += OnAttackingStateChanged;
        
        // Setup agent if we have authority
        if (IsOwner && agent != null)
        {
            SetupAgent();
        }
        
        // Subscribe to health events
        if (unitHealth != null)
        {
            unitHealth.onDie.AddListener(OnUnitDeath);
        }
    }
    
    public override void OnNetworkDespawn()
    {
        // Unsubscribe from events
        networkPosition.OnValueChanged -= OnPositionChanged;
        networkRotation.OnValueChanged -= OnRotationChanged;
        networkIsMoving.OnValueChanged -= OnMovingStateChanged;
        networkIsAttacking.OnValueChanged -= OnAttackingStateChanged;
        
        if (unitHealth != null)
        {
            unitHealth.onDie.RemoveListener(OnUnitDeath);
        }
    }
    
    private void Update()
    {
        // Only the owner should control the unit's logic
        if (!IsOwner) return;
        
        UpdateTargetSearch();
        UpdateMovement();
        UpdateCombat();
        UpdateNetworkVariables();
    }
    
    private void SetupAgent()
    {
        if (agent == null) return;
        
        agent.agentTypeID = agentTypeID;
        
        if (overrideAgentSettings)
        {
            agent.speed = moveSpeed;
            agent.stoppingDistance = stopDistanceToWaypoint;
            agent.acceleration = 8f;
            agent.angularSpeed = 360f;
        }
    }
    
    private void UpdateTargetSearch()
    {
        targetSearchTimer += Time.deltaTime;
        if (targetSearchTimer >= targetSearchInterval)
        {
            targetSearchTimer = 0f;
            FindTarget();
        }
    }
    
    private void FindTarget()
    {
        // Find enemies within detection range
        Collider[] enemies = Physics.OverlapSphere(transform.position, detectionRange);
        Transform closestEnemy = null;
        float closestDistance = Mathf.Infinity;
        
        foreach (Collider enemyCollider in enemies)
        {
            NetworkUnit enemyUnit = enemyCollider.GetComponent<NetworkUnit>();
            if (enemyUnit != null && enemyUnit.faction != faction)
            {
                float distance = Vector3.Distance(transform.position, enemyCollider.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestEnemy = enemyCollider.transform;
                }
            }
            
            // Also check for towers
            Tower enemyTower = enemyCollider.GetComponent<Tower>();
            if (enemyTower != null)
            {
                // Determine if this tower is an enemy based on faction
                bool isEnemyTower = (faction == Faction.Player && enemyTower.CompareTag("EnemyTower")) ||
                                   (faction == Faction.Enemy && enemyTower.CompareTag("PlayerTower"));
                                   
                if (isEnemyTower)
                {
                    float distance = Vector3.Distance(transform.position, enemyCollider.transform.position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestEnemy = enemyCollider.transform;
                    }
                }
            }
        }
        
        currentTarget = closestEnemy;
    }
    
    private void UpdateMovement()
    {
        if (agent == null) return;
        
        bool wasMoving = networkIsMoving.Value;
        
        if (currentTarget != null)
        {
            // Move towards target if close enough
            float distanceToTarget = Vector3.Distance(transform.position, currentTarget.position);
            if (distanceToTarget <= attackRange)
            {
                // Stop moving, start attacking
                agent.SetDestination(transform.position);
                networkIsMoving.Value = false;
            }
            else
            {
                // Move towards target
                agent.SetDestination(currentTarget.position);
                networkIsMoving.Value = true;
            }
        }
        else
        {
            // No target, follow path
            FollowPath();
        }
        
        // Play movement sound
        if (!wasMoving && networkIsMoving.Value && moveSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(moveSound);
        }
    }
    
    private void FollowPath()
    {
        if (path == null || path.Length == 0) return;
        
        if (currentWaypoint >= path.Length)
        {
            // Reached end of path, target end tower if available
            if (endTargetTower != null)
            {
                agent.SetDestination(endTargetTower.transform.position);
                networkIsMoving.Value = true;
            }
            else
            {
                networkIsMoving.Value = false;
            }
            return;
        }
        
        // Move to current waypoint
        Transform waypoint = path[currentWaypoint];
        if (waypoint != null)
        {
            agent.SetDestination(waypoint.position);
            networkIsMoving.Value = true;
            
            // Check if we reached the waypoint
            float distanceToWaypoint = Vector3.Distance(transform.position, waypoint.position);
            if (distanceToWaypoint <= stopDistanceToWaypoint)
            {
                currentWaypoint++;
                networkCurrentWaypoint.Value = currentWaypoint;
            }
        }
    }
    
    private void UpdateCombat()
    {
        if (currentTarget == null)
        {
            networkIsAttacking.Value = false;
            return;
        }
        
        float distanceToTarget = Vector3.Distance(transform.position, currentTarget.position);
        
        if (distanceToTarget <= attackRange)
        {
            // Can attack
            if (Time.time - lastAttackTime >= attackCooldown)
            {
                AttackTarget();
                lastAttackTime = Time.time;
                networkIsAttacking.Value = true;
            }
        }
        else
        {
            networkIsAttacking.Value = false;
        }
    }
    
    private void AttackTarget()
    {
        if (currentTarget == null) return;
        
        // Request attack on server
        RequestAttackServerRpc(currentTarget.GetComponent<NetworkObject>().NetworkObjectId);
        
        // Play attack sound locally
        if (attackSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(attackSound);
        }
    }
    
    [ServerRpc]
    private void RequestAttackServerRpc(ulong targetNetworkId)
    {
        // Validate attack on server
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkId, out NetworkObject targetNetObj))
        {
            Transform target = targetNetObj.transform;
            float distance = Vector3.Distance(transform.position, target.position);
            
            if (distance <= attackRange)
            {
                // Execute attack
                ExecuteAttackClientRpc(targetNetworkId);
            }
        }
    }
    
    [ClientRpc]
    private void ExecuteAttackClientRpc(ulong targetNetworkId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkId, out NetworkObject targetNetObj))
        {
            Transform target = targetNetObj.transform;
            
            if (isRanged && projectilePrefab != null)
            {
                // Spawn projectile
                SpawnProjectile(target);
            }
            else
            {
                // Melee attack
                DealDamageToTarget(target);
            }
        }
    }
    
    private void SpawnProjectile(Transform target)
    {
        if (firePoint == null) firePoint = transform;
        
        GameObject projectile = Instantiate(projectilePrefab, firePoint.position, firePoint.rotation);
        Projectile projectileScript = projectile.GetComponent<Projectile>();
        
        if (projectileScript != null)
        {
            projectileScript.Configure(attackDamage, faction.ToString(), target);
            // Set projectile speed if it has the property
            // You may need to adjust this based on your Projectile implementation
        }
    }
    
    private void DealDamageToTarget(Transform target)
    {
        if (isSplash)
        {
            // Splash damage
            Collider[] hitTargets = Physics.OverlapSphere(target.position, splashRadius);
            foreach (Collider hit in hitTargets)
            {
                UnitHealth targetHealth = hit.GetComponent<UnitHealth>();
                if (targetHealth != null)
                {
                    targetHealth.TakeDamage(attackDamage);
                }
            }
        }
        else
        {
            // Single target damage
            UnitHealth targetHealth = target.GetComponent<UnitHealth>();
            if (targetHealth != null)
            {
                targetHealth.TakeDamage(attackDamage);
            }
        }
    }
    
    private void UpdateNetworkVariables()
    {
        networkPosition.Value = transform.position;
        networkRotation.Value = transform.rotation;
    }
    
    private void OnPositionChanged(Vector3 previousValue, Vector3 newValue)
    {
        if (!IsOwner)
        {
            // Smoothly interpolate to new position for non-owners
            transform.position = Vector3.Lerp(transform.position, newValue, Time.deltaTime * 10f);
        }
    }
    
    private void OnRotationChanged(Quaternion previousValue, Quaternion newValue)
    {
        if (!IsOwner)
        {
            transform.rotation = Quaternion.Lerp(transform.rotation, newValue, Time.deltaTime * 10f);
        }
    }
    
    private void OnMovingStateChanged(bool previousValue, bool newValue)
    {
        // Update animation or visual effects based on movement state
        // This is where you'd trigger walk/idle animations
    }
    
    private void OnAttackingStateChanged(bool previousValue, bool newValue)
    {
        // Update animation or visual effects based on attack state
        // This is where you'd trigger attack animations
    }
    
    private void OnUnitDeath()
    {
        // Handle unit death
        if (deathSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(deathSound);
        }
        
        // Despawn the network object after a delay
        if (IsServer)
        {
            StartCoroutine(DespawnAfterDelay(2f));
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
    
    public void SetPath(Transform[] newPath, Transform endTarget)
    {
        if (!IsOwner) return;
        
        path = newPath;
        endTargetTower = endTarget.GetComponent<Tower>();
        currentWaypoint = 0;
        networkCurrentWaypoint.Value = 0;
    }
    
    // Public methods for external systems
    public void SetFaction(Faction newFaction)
    {
        if (IsOwner)
        {
            faction = newFaction;
        }
    }
    
    public void ApplyBuff(EffectStat stat, float amount, float duration)
    {
        if (!IsOwner) return;
        
        // Apply buff logic
        StartCoroutine(ApplyTemporaryEffect(stat, amount, duration));
    }
    
    private IEnumerator ApplyTemporaryEffect(EffectStat stat, float amount, float duration)
    {
        // Apply the effect
        switch (stat)
        {
            case EffectStat.MoveSpeed:
                float originalSpeed = moveSpeed;
                moveSpeed += amount;
                networkMoveSpeed.Value = moveSpeed;
                yield return new WaitForSeconds(duration);
                moveSpeed = originalSpeed;
                networkMoveSpeed.Value = moveSpeed;
                break;
                
            case EffectStat.AttackSpeed:
                float originalCooldown = attackCooldown;
                attackCooldown = Mathf.Max(0.1f, attackCooldown - amount);
                yield return new WaitForSeconds(duration);
                attackCooldown = originalCooldown;
                break;
        }
    }
}