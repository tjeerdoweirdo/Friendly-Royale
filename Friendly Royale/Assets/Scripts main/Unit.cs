using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using Unity.Netcode;

/// <summary>
/// Unit with NavMeshAgent movement, waypoint lane following, melee/ranged attacks,
/// visual spotting (FOV + LOS), chasing behavior, audio feedback, and an end-target tower.
/// </summary>
[RequireComponent(typeof(UnitHealth))]
public class Unit : NetworkBehaviour
{
    public enum Faction { Player, Enemy }

    public enum UnitRole { Normal, Buffer, Debuffer, Healer }
    public enum EffectStat { None, AttackSpeed, MoveSpeed, AttackDamage, Health }
    public enum EffectMode { None, Aura, OnHit }

    [Header("Network Settings")]
    [Tooltip("Enable networking for this unit")]
    public bool enableNetworking = false;
    
    [Header("Faction")]
    public Faction faction = Faction.Player;
    
    // Network Variables
    private NetworkVariable<int> networkFaction = new NetworkVariable<int>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    
    private NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    
    private NetworkVariable<bool> networkIsAlive = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    
    // Network state
    private bool isNetworkEnabled => enableNetworking && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

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
    [Tooltip("If true, this unit deals splash damage on attack (e.g. MegaKnight)")]
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
    
    [Header("Path Switching")]
    [Tooltip("How often to check if we should switch paths (in seconds).")]
    public float pathSwitchCheckInterval = 2f;
    [Tooltip("Distance ahead to look when checking for path obstacles.")]
    public float pathLookaheadDistance = 5f;
    [Tooltip("Radius to check for enemies when considering path switch.")]
    public float enemyDetectionRadius = 4f;
    [Tooltip("Maximum number of enemies in area before considering path switch.")]
    public int maxEnemiesBeforeSwitch = 2;
    private float pathSwitchTimer = 0f;

    [Header("End target (assigned by spawner)")]
    [Tooltip("If set, this tower will be considered the final destination / goal for this unit.")]
    public Tower endTargetTower;

    [Header("NavMeshAgent")]
    [Tooltip("Optional: assign a NavMeshAgent in the inspector. If left empty the script will try GetComponent<NavMeshAgent>() and (optionally) add one.")]
    public NavMeshAgent agent;
    [Tooltip("If true and no agent is present, the script will AddComponent<NavMeshAgent>() at runtime.")]
    public bool addAgentIfMissing = true;
    [Tooltip("If true the script will override some agent runtime settings (speed, stoppingDistance, avoidance). Turn off to preserve custom agent settings.")]
    public bool overrideAgentSettings = true;
    [Header("Bridge Navigation")]
    [Tooltip("Agent type ID to use (0 = Humanoid default). Make sure this agent type has proper Step Height settings in Navigation window.")]
    public int agentTypeID = 0;

    [Header("Perception")]
    public float detectionRange = 10f;
    [Range(0, 360)]
    public float viewAngle = 120f;
    public float eyeHeight = 0.9f;
    public LayerMask obstacleMask = ~0;
    public float lostTargetTimeout = 3f;
    public float visualCheckInterval = 0.2f;

    [Header("Sound (optional)")]
    public AudioSource sfxSource;
    public AudioSource movementSource;
    public AudioClip spotClip;
    public AudioClip attackClip;
    public AudioClip movementClip;
    public float movementPlayThreshold = 0.2f;

    // runtime
    [HideInInspector] public UnitHealth health;
    private float lastAttackTime = 0f;
    private Transform currentTarget;
    private float targetSearchTimer = 0f;

    // perception runtime
    private float visualTimer = 0f;
    private float lostTimer = 0f;
    
    void Awake()
    {
        health = GetComponent<UnitHealth>();

        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (agent == null && addAgentIfMissing) agent = gameObject.AddComponent<NavMeshAgent>();

        if (agent != null && overrideAgentSettings)
        {
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            agent.updateRotation = true;
            agent.updateUpAxis = false; // set to true in full 3D games
            agent.radius = 0.4f;
            agent.height = 2f; // Ensure height is set properly
            agent.acceleration = 8f;
            agent.angularSpeed = 120f;
            // Bridge navigation settings - CRITICAL for bridge traversal
            agent.agentTypeID = agentTypeID; // Use specified agent type
            agent.areaMask = -1; // Allow all NavMesh areas
            agent.baseOffset = 1f; // Lift units above the NavMesh to prevent sinking
        }

        // Setup audio sources if not assigned
        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
        }
        if (movementSource == null)
        {
            movementSource = gameObject.AddComponent<AudioSource>();
            movementSource.playOnAwake = false;
            movementSource.loop = true;
            movementSource.clip = movementClip;
        }
        else
        {
            if (movementSource.clip == null && movementClip != null) movementSource.clip = movementClip;
            movementSource.loop = true;
        }
    }

    void Start()
    {
        SyncAgentToStats();
        
        // Fix positioning issues - ensure unit is properly grounded
        StartCoroutine(FixPositioning());

        // If path exists, go to first waypoint; otherwise if there's an end target, go to it
        if (path != null && path.Length > 0 && agent != null)
            agent.SetDestination(path[currentWaypoint].position);
        else if (endTargetTower != null && agent != null)
            agent.SetDestination(endTargetTower.transform.position);
            
        // Initialize path switching timer with random offset to spread out checks
        pathSwitchTimer = UnityEngine.Random.Range(0f, pathSwitchCheckInterval * 0.5f);
    }
    
    public override void OnNetworkSpawn()
    {
        if (isNetworkEnabled)
        {
            // Initialize network variables
            if (IsServer)
            {
                networkFaction.Value = (int)faction;
                networkPosition.Value = transform.position;
                networkIsAlive.Value = true;
            }
            
            // Subscribe to network variable changes
            networkFaction.OnValueChanged += OnNetworkFactionChanged;
            networkPosition.OnValueChanged += OnNetworkPositionChanged;
            networkIsAlive.OnValueChanged += OnNetworkAliveChanged;
        }
    }
    
    public override void OnNetworkDespawn()
    {
        if (isNetworkEnabled)
        {
            // Unsubscribe from events
            networkFaction.OnValueChanged -= OnNetworkFactionChanged;
            networkPosition.OnValueChanged -= OnNetworkPositionChanged;
            networkIsAlive.OnValueChanged -= OnNetworkAliveChanged;
        }
    }
    
    private void OnNetworkFactionChanged(int previousValue, int newValue)
    {
        faction = (Faction)newValue;
    }
    
    private void OnNetworkPositionChanged(Vector3 previousValue, Vector3 newValue)
    {
        if (!IsOwner && isNetworkEnabled)
        {
            // Non-owners should move towards the network position
            transform.position = Vector3.Lerp(transform.position, newValue, Time.deltaTime * 10f);
        }
    }
    
    private void OnNetworkAliveChanged(bool previousValue, bool newValue)
    {
        if (!newValue && previousValue)
        {
            // Unit just died
            Die();
        }
    }

    /// <summary>
    /// Fixes positioning issues that can occur with NavMeshAgent spawning
    /// </summary>
    System.Collections.IEnumerator FixPositioning()
    {
        yield return new WaitForEndOfFrame();
        
        if (agent != null)
        {
            // Temporarily disable the agent to fix position
            bool wasEnabled = agent.enabled;
            agent.enabled = false;
            
            // Raycast down to find the ground
            RaycastHit hit;
            Vector3 rayStart = transform.position + Vector3.up * 2f;
            if (Physics.Raycast(rayStart, Vector3.down, out hit, 5f))
            {
                // Position the unit on the ground
                transform.position = hit.point;
            }
            
            // Re-enable the agent
            agent.enabled = wasEnabled;
            
            // Warp the agent to the correct position
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.Warp(transform.position);
            }
        }
    }

    void Update()
    {
        if (health == null || !health.IsAlive) return;

        // Handle aura effects and visuals
        bool auraActive = (unitRole == UnitRole.Buffer || unitRole == UnitRole.Debuffer || unitRole == UnitRole.Healer)
            && effectMode == EffectMode.Aura && effectStat != EffectStat.None;
        if (auraActive)
        {
            auraTimer += Time.deltaTime;
            if (auraTimer >= auraInterval)
            {
                auraTimer = 0f;
                ApplyAuraEffect();
            }
            // Spawn effectPrefab if not already spawned
            if (effectPrefab != null && spawnedEffectInstance == null)
            {
                spawnedEffectInstance = Instantiate(effectPrefab, transform.position, Quaternion.identity, transform);
            }
            // Keep effect at unit's position (if not parented)
            if (spawnedEffectInstance != null && spawnedEffectInstance.transform.parent != transform)
            {
                spawnedEffectInstance.transform.position = transform.position;
            }
        }
        else
        {
            // Destroy effectPrefab if it exists and aura is not active
            if (spawnedEffectInstance != null)
            {
                Destroy(spawnedEffectInstance);
                spawnedEffectInstance = null;
            }
        }


        // Check for positioning issues and fix them
        CheckAndFixPositioning();

        // Check for path switching and optimization when not chasing an enemy
        if (currentTarget == null)
        {
            pathSwitchTimer += Time.deltaTime;
            if (pathSwitchTimer >= pathSwitchCheckInterval)
            {
                pathSwitchTimer = 0f;
                EvaluatePathSwitch();
                
                // Also optimize current path every few checks
                if (UnityEngine.Random.value < 0.3f) // 30% chance to also optimize current path
                {
                    OptimizeCurrentPath();
                }
            }
        }

        // periodic generic target search (kept for fallback cases)
        targetSearchTimer += Time.deltaTime;
        if (targetSearchTimer >= targetSearchInterval)
        {
            targetSearchTimer = 0f;
            // optional fallback target search (kept commented out by default)
            //FindTarget();
        }

        // Visual detection runs at visualCheckInterval
        visualTimer += Time.deltaTime;
        if (visualTimer >= visualCheckInterval)
        {
            visualTimer = 0f;
            if (currentTarget == null)
            {
                TrySpotTargets();
            }
            else
            {
                // Check if target is still in range
                float distanceToTarget = Vector3.Distance(transform.position, currentTarget.position);
                if (distanceToTarget > detectionRange)
                {
                    // Target is out of range - forget it and resume path
                    Debug.Log($"{gameObject.name} lost target {currentTarget.name} - out of range ({distanceToTarget:F1} > {detectionRange})");
                    
                    // Notify network extension
                    // Network sync: target lost
                    if (isNetworkEnabled && IsServer)
                    {
                        OnTargetLostServerRpc();
                    }
                    
                    currentTarget = null;
                    lostTimer = 0f;
                    ResumePathOrEndTarget();
                }
                else if (!CanSeeTarget(currentTarget))
                {
                    lostTimer += visualCheckInterval;
                    if (lostTimer >= lostTargetTimeout)
                    {
                        // lost line of sight — forget and resume path/end-target
                        Debug.Log($"{gameObject.name} lost target {currentTarget.name} - no line of sight");
                        
                        // Notify network extension
                        // Network sync: target lost
                        if (isNetworkEnabled && IsServer)
                        {
                            OnTargetLostServerRpc();
                        }
                        
                        currentTarget = null;
                        lostTimer = 0f;
                        ResumePathOrEndTarget();
                    }
                }
                else
                {
                    lostTimer = 0f;
                }
            }
        }

        // If we have a current target (spotted enemy), chase until in attack range
        if (currentTarget != null)
        {
            // Verify target is still alive before continuing to chase
            bool targetStillValid = false;
            var targetUnit = currentTarget.GetComponent<Unit>();
            var targetHealth = currentTarget.GetComponent<UnitHealth>();
            var targetTower = currentTarget.GetComponent<Tower>();
            var targetBuilding = currentTarget.GetComponent<Health>();
            
            if (targetUnit != null && targetUnit.health != null && targetUnit.health.IsAlive)
                targetStillValid = true;
            else if (targetHealth != null && targetHealth.IsAlive)
                targetStillValid = true;
            else if (targetTower != null)
                targetStillValid = true;
            else if (targetBuilding != null && !targetBuilding.isDead)
                targetStillValid = true;
            
            if (!targetStillValid)
            {
                Debug.Log($"{gameObject.name} target {currentTarget.name} is no longer valid - forgetting");
                currentTarget = null;
                ResumePathOrEndTarget();
                return;
            }
            
            float dist = Vector3.Distance(transform.position, currentTarget.position);
            if (dist <= attackRange + 0.1f)
            {
                if (agent != null) agent.isStopped = true;
                TryAttack();
            }
            else
            {
                // chase target
                if (agent != null)
                {
                    agent.isStopped = false;
                    agent.SetDestination(currentTarget.position);
                }
                HandleMovementSound();
                // face roughly toward target
                Vector3 lookDir = currentTarget.position - transform.position;
                lookDir.y = 0;
                if (lookDir.sqrMagnitude > 0.001f)
                    transform.forward = Vector3.Lerp(transform.forward, lookDir.normalized, Time.deltaTime * 10f);
            }
            return; // skip path movement while chasing
        }

        // No current enemy target: follow path or go to end target
        if (agent != null && path != null && path.Length > 0)
        {
            MoveAlongPathWithAgent();
            HandleMovementSound();
        }
        else
        {
            // if there's no path but an end target, move toward it directly
            if (endTargetTower != null && agent != null)
            {
                // chase end tower until in range, then attack
                float dist = Vector3.Distance(transform.position, endTargetTower.transform.position);
                if (dist <= attackRange + 0.1f)
                {
                    if (agent != null) agent.isStopped = true;
                    // ensure currentTarget points to end target so TryAttack handles it
                    currentTarget = endTargetTower.transform;
                    TryAttack();
                }
                else
                {
                    agent.isStopped = false;
                    agent.SetDestination(endTargetTower.transform.position);
                    HandleMovementSound();
                    // face
                    Vector3 lookDir = endTargetTower.transform.position - transform.position;
                    lookDir.y = 0;
                    if (lookDir.sqrMagnitude > 0.001f)
                        transform.forward = Vector3.Lerp(transform.forward, lookDir.normalized, Time.deltaTime * 10f);
                }
            }
            else
            {
                MoveAlongPathFallback();
                if (movementSource != null && movementSource.isPlaying) movementSource.Stop();
            }
        }
    }

    void HandleMovementSound()
    {
        if (movementSource == null || movementSource.clip == null) return;
        if (agent == null)
        {
            if (!movementSource.isPlaying) movementSource.Play();
            return;
        }

        bool shouldPlay = !agent.isStopped && agent.velocity.magnitude > movementPlayThreshold;
        if (shouldPlay && !movementSource.isPlaying)
            movementSource.Play();
        else if (!shouldPlay && movementSource.isPlaying)
            movementSource.Stop();
    }

    void MoveAlongPathWithAgent()
    {
        if (path == null || path.Length == 0) return;
        Transform wp = path[currentWaypoint];
        if (wp == null) return;

        if (agent.isStopped) agent.isStopped = false;

        // If this is the last waypoint AND we have an endTargetTower, set destination to the tower
        if (currentWaypoint == path.Length - 1 && endTargetTower != null)
        {
            agent.SetDestination(endTargetTower.transform.position);
        }
        else
        {
            agent.SetDestination(wp.position);
        }

        // check arrival
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + stopDistanceToWaypoint)
        {
            if (currentWaypoint < path.Length - 1)
            {
                currentWaypoint++;
            }
            else
            {
                // reached final waypoint
                if (endTargetTower != null)
                {
                    // if tower in attack range, attack it; otherwise chase tower
                    float distToTower = Vector3.Distance(transform.position, endTargetTower.transform.position);
                    if (distToTower <= attackRange + 0.1f)
                    {
                        agent.isStopped = true;
                        currentTarget = endTargetTower.transform;
                        TryAttack();
                    }
                    else
                    {
                        // keep chasing the tower's exact position
                        agent.SetDestination(endTargetTower.transform.position);
                    }
                }
                else
                {
                    // no end target: stop at last waypoint
                    agent.isStopped = true;
                }
            }
        }

        // rotate toward velocity for nicer visuals
        if (agent.velocity.sqrMagnitude > 0.01f)
        {
            var lookDir = agent.velocity.normalized;
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude > 0.001f)
                transform.forward = Vector3.Lerp(transform.forward, lookDir, Time.deltaTime * 10f);
        }
    }

    void MoveAlongPathFallback()
    {
        if (path == null || path.Length == 0) return;

        Transform wp = path[currentWaypoint];
        Vector3 dir = wp.position - transform.position;
        dir.y = 0;

        if (dir.magnitude > stopDistanceToWaypoint)
        {
            transform.position += dir.normalized * moveSpeed * Time.deltaTime;
            transform.forward = Vector3.Lerp(transform.forward, dir.normalized, Time.deltaTime * 10f);
        }
        else
        {
            if (currentWaypoint < path.Length - 1)
                currentWaypoint++;
            else
            {
                // at final WP, if we have endTargetTower, go toward it
                if (endTargetTower != null)
                {
                    Vector3 toTower = endTargetTower.transform.position - transform.position;
                    toTower.y = 0;
                    if (toTower.magnitude > attackRange)
                    {
                        transform.position += toTower.normalized * moveSpeed * Time.deltaTime;
                    }
                    else
                    {
                        // in range: set currentTarget so TryAttack hits tower
                        currentTarget = endTargetTower.transform;
                        TryAttack();
                    }
                }
            }
        }
    }

    void TrySpotTargets()
    {
        Unit[] allUnits = UnityEngine.Object.FindObjectsByType<Unit>(UnityEngine.FindObjectsSortMode.None);
        Transform best = null;
        float bestDist = Mathf.Infinity;

        // 1. Search for enemy units
        foreach (var u in allUnits)
        {
            if (u == this) continue;
            if (u.faction == this.faction) continue;
            if (u.health == null || !u.health.IsAlive) continue;

            Vector3 to = u.transform.position - transform.position;
            float sqr = to.sqrMagnitude;
            if (sqr > detectionRange * detectionRange) continue;

            float angle = Vector3.Angle(transform.forward, to);
            if (angle > viewAngle * 0.5f) continue;

            if (!CanSeeTarget(u.transform)) continue;

            float dist = Mathf.Sqrt(sqr);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = u.transform;
            }
        }

        // 2. If no unit found, search for enemy buildings
        if (best == null)
        {
            Building[] allBuildings = UnityEngine.Object.FindObjectsByType<Building>(UnityEngine.FindObjectsSortMode.None);
            foreach (var b in allBuildings)
            {
                if (b == null || b.gameObject == this.gameObject) continue;
                if ((int)b.faction == (int)this.faction) continue;
                var bHealth = b.GetComponent<UnitHealth>();
                if (bHealth != null && !bHealth.IsAlive) continue;
                Vector3 to = b.transform.position - transform.position;
                float sqr = to.sqrMagnitude;
                if (sqr > detectionRange * detectionRange) continue;
                float angle = Vector3.Angle(transform.forward, to);
                if (angle > viewAngle * 0.5f) continue;
                if (!CanSeeTarget(b.transform)) continue;
                float dist = Mathf.Sqrt(sqr);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = b.transform;
                }
            }
        }

        // 3. If still nothing, search for enemy towers
        if (best == null)
        {
            Tower[] allTowers = UnityEngine.Object.FindObjectsByType<Tower>(UnityEngine.FindObjectsSortMode.None);
            foreach (var t in allTowers)
            {
                var towerFaction = (t.ownerTag == "Player") ? Faction.Player : Faction.Enemy;
                var building = t.GetComponent<Building>();
                if (building != null)
                    towerFaction = building.faction;
                if (towerFaction == this.faction) continue;

                Vector3 to = t.transform.position - transform.position;
                if (to.sqrMagnitude > detectionRange * detectionRange) continue;
                float angle = Vector3.Angle(transform.forward, to);
                if (angle > viewAngle * 0.5f) continue;
                if (!CanSeeTarget(t.transform)) continue;

                float dist = to.magnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = t.transform;
                }
            }
        }

        if (best != null)
        {
            currentTarget = best;
            lostTimer = 0f;
            Debug.Log($"{gameObject.name} spotted new target: {best.name} at distance {bestDist:F1}");
            PlaySpotSound();
            
            // Notify network extension
            // Network sync: target changed
            if (isNetworkEnabled && IsServer)
            {
                OnTargetChangedServerRpc(best ? best.GetComponent<NetworkObject>().NetworkObjectId : 0);
            }
        }
    }



    bool CanSeeTarget(Transform tgt)
    {
        if (tgt == null) return false;
        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPos = tgt.position + Vector3.up * (eyeHeight * 0.5f);

        Vector3 dir = targetPos - eye;
        float dist = dir.magnitude;
        if (dist <= 0.001f) return true;

        Ray r = new Ray(eye, dir.normalized);
        RaycastHit hit;
        if (Physics.Raycast(r, out hit, dist, obstacleMask))
        {
            if (hit.collider != null)
            {
                Transform hitRoot = hit.collider.transform;
                if (hitRoot == tgt || hitRoot.IsChildOf(tgt))
                    return true;
                return false;
            }
            return false;
        }

        return true;
    }

    void PlaySpotSound()
    {
        if (sfxSource != null && spotClip != null) sfxSource.PlayOneShot(spotClip);
    }

    void PlayAttackSound()
    {
        if (sfxSource != null && attackClip != null) sfxSource.PlayOneShot(attackClip);
    }

    void TryAttack()
    {
        if (Time.time - lastAttackTime < attackCooldown) return;
        lastAttackTime = Time.time;
        if (currentTarget == null) return;

        // Notify network extension of attack
        // Network sync: attack performed
        if (isNetworkEnabled && IsServer)
        {
            OnAttackPerformedServerRpc(currentTarget ? currentTarget.GetComponent<NetworkObject>().NetworkObjectId : 0);
        }

        // On-hit buff/debuff/heal
        if ((unitRole == UnitRole.Buffer || unitRole == UnitRole.Debuffer || unitRole == UnitRole.Healer) && effectMode == EffectMode.OnHit && effectStat != EffectStat.None)
        {
            ApplyEffectToTarget(currentTarget);
        }

        // Splash attack (MegaKnight style)
        if (isSplash && splashRadius > 0f)
        {
            Collider[] hits = Physics.OverlapSphere(currentTarget.position, splashRadius);
            foreach (var hit in hits)
            {
                // Damage enemy units
                if (hit.TryGetComponent<Unit>(out var unitTarget))
                {
                    if (unitTarget.faction != this.faction && unitTarget.health != null && unitTarget.health.IsAlive)
                    {
                        unitTarget.health.TakeDamage(attackDamage, gameObject);
                    }
                }
                // Damage buildings or towers
                else if (hit.TryGetComponent<Health>(out var healthTarget))
                {
                    if (!healthTarget.isDead)
                    {
                        healthTarget.TakeDamage(attackDamage);
                    }
                }
            }
            PlayAttackSound();
            return;
        }

        // Ranged attack
        if (isRanged && projectilePrefab != null && firePoint != null)
        {
            Vector3 lookDir = (currentTarget.position - transform.position);
            lookDir.y = 0;
            if (lookDir.sqrMagnitude > 0.001f)
                transform.forward = lookDir.normalized;

            ShootProjectile();
            PlayAttackSound();
            return;
        }

        // Melee/direct damage
        var targetHealth = currentTarget.GetComponent<UnitHealth>();
        if (targetHealth != null && targetHealth.IsAlive)
        {
            targetHealth.TakeDamage(attackDamage, gameObject);
            PlayAttackSound();
            return;
        }

        // Try to damage Health (for buildings or towers)
        var healthComp = currentTarget.GetComponent<Health>();
        if (healthComp != null && !healthComp.isDead)
        {
            healthComp.TakeDamage(attackDamage);
            PlayAttackSound();
            return;
        }

        // Try to damage Tower (legacy)
        var targetTower = currentTarget.GetComponent<Tower>();
        if (targetTower != null)
        {
            if (targetTower.GetComponent<Health>() == null)
            {
                targetTower.TakeDamage(attackDamage);
                PlayAttackSound();
            }
        }
    }

    // --- Buff/Debuff/Heal Logic ---
    void ApplyAuraEffect()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, auraRadius);
        foreach (var hit in hits)
        {
            if (unitRole == UnitRole.Buffer && hit.TryGetComponent<Unit>(out var ally))
            {
                if (ally.faction == this.faction && ally != this)
                    ApplyEffectToTarget(ally.transform);
            }
            else if (unitRole == UnitRole.Debuffer && hit.TryGetComponent<Unit>(out var enemy))
            {
                if (enemy.faction != this.faction)
                    ApplyEffectToTarget(enemy.transform);
            }
            else if (unitRole == UnitRole.Healer && hit.TryGetComponent<Unit>(out var healTarget))
            {
                if (healTarget.faction == this.faction && healTarget != this)
                    ApplyEffectToTarget(healTarget.transform);
            }
        }
    }

    void ApplyEffectToTarget(Transform target)
    {
        if (target == null) return;
        var unit = target.GetComponent<Unit>();
        if (unit == null) return;

        switch (effectStat)
        {
            case EffectStat.AttackSpeed:
                StartCoroutine(TempModifyStat(unit, nameof(unit.attackCooldown), -effectAmount, effectDuration));
                break;
            case EffectStat.MoveSpeed:
                StartCoroutine(TempModifyStat(unit, nameof(unit.moveSpeed), effectAmount, effectDuration));
                break;
            case EffectStat.AttackDamage:
                StartCoroutine(TempModifyStat(unit, nameof(unit.attackDamage), effectAmount, effectDuration));
                break;
            case EffectStat.Health:
                if (unitRole == UnitRole.Healer && unit.health != null && unit.health.IsAlive)
                {
                    unit.health.Heal((int)effectAmount);
                }
                break;
        }
    }

    IEnumerator TempModifyStat(Unit target, string statName, float amount, float duration)
    {
        var field = typeof(Unit).GetField(statName);
        if (field == null) yield break;
        float original = (float)field.GetValue(target);
        field.SetValue(target, original + amount);
        if (statName == nameof(moveSpeed) && target.agent != null)
            target.agent.speed = Mathf.Max(0.01f, target.moveSpeed + amount);
        yield return new WaitForSeconds(duration);
        // Only revert if still alive
        if (target != null && target.health != null && target.health.IsAlive)
        {
            field.SetValue(target, original);
            if (statName == nameof(moveSpeed) && target.agent != null)
                target.agent.speed = Mathf.Max(0.01f, target.moveSpeed);
        }
    }
    // ...existing code...
// (Remove any extra closing brackets here)

    public void ShootProjectile()
    {
        if (projectilePrefab == null || firePoint == null) return;

        // Calculate direction first
        Vector3 aimPoint = (currentTarget != null) ? currentTarget.position : (firePoint.position + transform.forward * 10f);
        Vector3 dir = (aimPoint - firePoint.position).normalized;
        if (dir.sqrMagnitude < 0.0001f) dir = firePoint.forward;

        // Offset spawn position forward to avoid immediate collision
        float spawnOffset = 0.5f; // You can tweak this value as needed
        Vector3 spawnPos = firePoint.position + dir * spawnOffset;

        GameObject projGO = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
        Projectile proj = projGO.GetComponent<Projectile>();

        if (proj != null)
        {
            proj.speed = projectileSpeed;
            Transform homingTarget = proj.homing ? currentTarget : null;
            proj.Configure(attackDamage, faction.ToString(), homingTarget, this.gameObject);
            proj.SetVelocity(dir * projectileSpeed);
            projGO.transform.forward = dir;
        }
        else
        {
            Rigidbody rb = projGO.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = dir * projectileSpeed;
                projGO.transform.forward = dir;
            }
        }
    }

    public void SetPath(Transform[] waypoints)
    {
        path = waypoints;
        currentWaypoint = 0;
        if (agent != null && path != null && path.Length > 0)
            agent.SetDestination(path[currentWaypoint].position);
    }

    /// <summary>
    /// Set both left and right paths for this unit, allowing dynamic switching
    /// </summary>
    public void SetBothPaths(Transform[] leftWaypoints, Transform[] rightWaypoints, bool startWithLeft = true)
    {
        leftPath = leftWaypoints;
        rightPath = rightWaypoints;
        usingLeftPath = startWithLeft;
        path = usingLeftPath ? leftPath : rightPath;
        currentWaypoint = 0;
        
        if (agent != null && path != null && path.Length > 0)
            agent.SetDestination(path[currentWaypoint].position);
    }

    /// <summary>
    /// Evaluates whether the unit should switch to the alternate path
    /// </summary>
    void EvaluatePathSwitch()
    {
        // Don't switch if we don't have both paths or are near the end
        if (leftPath == null || rightPath == null || leftPath.Length == 0 || rightPath.Length == 0)
            return;
            
        if (path == null || currentWaypoint >= path.Length - 1)
            return;

        // Don't switch if we're very close to the next waypoint (avoid thrashing)
        if (currentWaypoint < path.Length && Vector3.Distance(transform.position, path[currentWaypoint].position) < 2f)
            return;

        // Don't switch if we're already very close to the king tower
        if (endTargetTower != null && Vector3.Distance(transform.position, endTargetTower.transform.position) < 8f)
            return;

        Transform[] alternatePath = usingLeftPath ? rightPath : leftPath;
        
        // Find the best waypoint on the alternate path (prioritizes forward progress)
        int bestWaypointIndex = FindClosestWaypoint(alternatePath);
        if (bestWaypointIndex == -1) return;

        // Check if switching would be beneficial
        if (ShouldSwitchPath(alternatePath, bestWaypointIndex))
        {
            SwitchToAlternatePath(alternatePath, bestWaypointIndex);
        }
    }

    /// <summary>
    /// Continuously checks and fixes positioning issues during gameplay
    /// </summary>
    void CheckAndFixPositioning()
    {
        if (agent == null || !agent.enabled) return;
        
        // Check if unit is too far below or above the NavMesh
        if (agent.isOnNavMesh)
        {
            float yDifference = transform.position.y - agent.nextPosition.y;
            
            // If the unit is sinking too much into the ground or floating too high
            if (Mathf.Abs(yDifference) > 0.5f)
            {
                Vector3 correctedPosition = transform.position;
                correctedPosition.y = agent.nextPosition.y + agent.baseOffset;
                transform.position = correctedPosition;
            }
        }
        else
        {
            // If agent fell off NavMesh, try to get back on
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 2f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
        }
    }

    /// <summary>
    /// Finds the best waypoint index on the given path (prioritizes forward progress)
    /// </summary>
    int FindClosestWaypoint(Transform[] targetPath)
    {
        if (targetPath == null || targetPath.Length == 0) return -1;
        
        // First, try to find a waypoint that's ahead of our current progress
        int forwardWaypoint = FindForwardWaypoint(targetPath);
        if (forwardWaypoint != -1) return forwardWaypoint;
        
        // If no forward waypoint found, find the closest one overall
        float closestDistance = Mathf.Infinity;
        int closestIndex = -1;
        
        for (int i = 0; i < targetPath.Length; i++)
        {
            if (targetPath[i] == null) continue;
            
            float distance = Vector3.Distance(transform.position, targetPath[i].position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = i;
            }
        }
        
        return closestIndex;
    }

    /// <summary>
    /// Finds a waypoint that represents forward progress toward the king tower
    /// </summary>
    int FindForwardWaypoint(Transform[] targetPath)
    {
        if (targetPath == null || targetPath.Length == 0 || endTargetTower == null) return -1;
        
        Vector3 currentPos = transform.position;
        Vector3 kingPos = endTargetTower.transform.position;
        float currentDistanceToKing = Vector3.Distance(currentPos, kingPos);
        
        // Find the first waypoint that's closer to the king than we are and ahead of us
        for (int i = 0; i < targetPath.Length; i++)
        {
            if (targetPath[i] == null) continue;
            
            Vector3 waypointPos = targetPath[i].position;
            float waypointDistanceToKing = Vector3.Distance(waypointPos, kingPos);
            
            // Skip waypoints that are further from the king than we are (backwards progress)
            if (waypointDistanceToKing >= currentDistanceToKing) continue;
            
            // Check if this waypoint is generally in the forward direction
            Vector3 directionToKing = (kingPos - currentPos).normalized;
            Vector3 directionToWaypoint = (waypointPos - currentPos).normalized;
            
            // If the waypoint is in roughly the same direction as the king (dot product > 0)
            if (Vector3.Dot(directionToKing, directionToWaypoint) > 0.1f)
            {
                return i;
            }
        }
        
        return -1; // No suitable forward waypoint found
    }

    /// <summary>
    /// Determines if switching to the alternate path would be beneficial
    /// </summary>
    bool ShouldSwitchPath(Transform[] alternatePath, int alternateWaypointIndex)
    {
        // Check if current path is blocked ahead
        if (IsPathBlocked(path, currentWaypoint))
        {
            return !IsPathBlocked(alternatePath, alternateWaypointIndex);
        }
        
        // Check enemy density on current path vs alternate path
        int currentPathEnemies = CountEnemiesAroundPath(path, currentWaypoint);
        int alternatePathEnemies = CountEnemiesAroundPath(alternatePath, alternateWaypointIndex);
        
        // Switch if alternate path has significantly fewer enemies
        return (currentPathEnemies >= maxEnemiesBeforeSwitch && alternatePathEnemies < currentPathEnemies - 1);
    }

    /// <summary>
    /// Checks if the path ahead is blocked by obstacles
    /// </summary>
    bool IsPathBlocked(Transform[] checkPath, int waypointIndex)
    {
        if (checkPath == null || waypointIndex >= checkPath.Length - 1) return false;
        
        Vector3 start = transform.position;
        Vector3 end = checkPath[waypointIndex].position;
        
        // If we're checking ahead, look at the next waypoint too
        if (waypointIndex + 1 < checkPath.Length)
        {
            end = checkPath[waypointIndex + 1].position;
        }
        
        Vector3 direction = (end - start).normalized;
        float distance = Vector3.Distance(start, end);
        distance = Mathf.Min(distance, pathLookaheadDistance);
        
        // Use a sphere cast to check for obstacles
        return Physics.SphereCast(start, 0.5f, direction, out RaycastHit hit, distance, obstacleMask);
    }

    /// <summary>
    /// Counts enemies in the area around the specified path waypoint
    /// </summary>
    int CountEnemiesAroundPath(Transform[] checkPath, int waypointIndex)
    {
        if (checkPath == null || waypointIndex >= checkPath.Length) return 0;
        
        Vector3 checkPosition = checkPath[waypointIndex].position;
        Collider[] hits = Physics.OverlapSphere(checkPosition, enemyDetectionRadius);
        
        int enemyCount = 0;
        foreach (var hit in hits)
        {
            Unit enemyUnit = hit.GetComponent<Unit>();
            if (enemyUnit != null && enemyUnit.faction != this.faction && 
                enemyUnit.health != null && enemyUnit.health.IsAlive)
            {
                enemyCount++;
            }
        }
        
        return enemyCount;
    }

    /// <summary>
    /// Switches to the alternate path at the specified waypoint
    /// </summary>
    void SwitchToAlternatePath(Transform[] newPath, int waypointIndex)
    {
        Vector3 oldDestination = agent != null ? agent.destination : Vector3.zero;
        string oldPathName = usingLeftPath ? "left" : "right";
        
        usingLeftPath = !usingLeftPath;
        path = newPath;
        currentWaypoint = waypointIndex;
        
        // Set new destination
        if (agent != null && path != null && currentWaypoint < path.Length)
        {
            Vector3 newDestination = path[currentWaypoint].position;
            agent.SetDestination(newDestination);
            
            // Log the switch with progress information
            float distanceToKing = endTargetTower != null ? 
                Vector3.Distance(transform.position, endTargetTower.transform.position) : 0f;
            
            Debug.Log($"{gameObject.name} switched from {oldPathName} to {(usingLeftPath ? "left" : "right")} path " +
                     $"(waypoint {currentWaypoint}, {distanceToKing:F1}u from king)");
        }
        
        // Reset the path switch timer to prevent immediate re-evaluation
        pathSwitchTimer = 0f;
    }

    /// <summary>
    /// Allows the unit to skip to a more advanced waypoint if it makes sense
    /// </summary>
    public void OptimizeCurrentPath()
    {
        if (path == null || path.Length == 0 || endTargetTower == null) return;
        
        Vector3 currentPos = transform.position;
        Vector3 kingPos = endTargetTower.transform.position;
        float currentDistanceToKing = Vector3.Distance(currentPos, kingPos);
        
        // Look for a waypoint further ahead that we can skip to
        for (int i = currentWaypoint + 1; i < path.Length; i++)
        {
            if (path[i] == null) continue;
            
            Vector3 waypointPos = path[i].position;
            float waypointDistanceToKing = Vector3.Distance(waypointPos, kingPos);
            
            // If this waypoint is much closer to the king and we can reach it directly
            if (waypointDistanceToKing < currentDistanceToKing - 3f)
            {
                // Check if we have a clear path to this waypoint
                Vector3 directionToWaypoint = (waypointPos - currentPos).normalized;
                float distanceToWaypoint = Vector3.Distance(currentPos, waypointPos);
                
                if (!Physics.SphereCast(currentPos, 0.5f, directionToWaypoint, out RaycastHit hit, 
                    distanceToWaypoint, obstacleMask))
                {
                    // We can skip ahead to this waypoint
                    currentWaypoint = i;
                    if (agent != null)
                    {
                        agent.SetDestination(path[currentWaypoint].position);
                    }
                    Debug.Log($"{gameObject.name} optimized path by skipping to waypoint {currentWaypoint}");
                    break;
                }
            }
        }
    }

    public void SyncAgentToStats()
    {
        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
            if (agent == null && addAgentIfMissing) agent = gameObject.AddComponent<NavMeshAgent>();
        }

        if (agent == null) return;

        if (overrideAgentSettings)
        {
            agent.speed = Mathf.Max(0.01f, moveSpeed);
            agent.stoppingDistance = attackRange;
            agent.acceleration = Mathf.Max(4f, moveSpeed * 3f);
            
            // Ensure proper positioning settings
            agent.baseOffset = 1f; // Keep units above ground
            agent.height = 2f;
            agent.radius = 0.4f;
        }
        else
        {
            agent.speed = Mathf.Max(0.001f, agent.speed);
        }
        
        // Ensure agent is properly positioned on NavMesh
        if (agent.enabled && !agent.isOnNavMesh)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 5f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
        }
    }

    void ResumePathOrEndTarget()
    {
        if (agent == null) return;
        if (path != null && path.Length > 0 && path[currentWaypoint] != null)
            agent.SetDestination(path[currentWaypoint].position);
        else if (endTargetTower != null)
            agent.SetDestination(endTargetTower.transform.position);
    }

    string FactionToTag(Faction f) => (f == Faction.Player) ? "Player" : "Enemy";

    // Debug method - call this if units get stuck
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public void DebugAgentStatus()
    {
        if (agent == null) return;
        Debug.Log($"{gameObject.name} - Has Path: {agent.hasPath}, Path Status: {agent.pathStatus}, " +
                  $"Remaining Distance: {agent.remainingDistance}, Is Stopped: {agent.isStopped}");
        
        // Try to recalculate path
        if (agent.pathStatus != UnityEngine.AI.NavMeshPathStatus.PathComplete)
        {
            Vector3 currentDest = agent.destination;
            agent.ResetPath();
            agent.SetDestination(currentDest);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Quaternion left = Quaternion.AngleAxis(-viewAngle * 0.5f, Vector3.up);
        Quaternion right = Quaternion.AngleAxis(viewAngle * 0.5f, Vector3.up);
        Vector3 leftDir = left * transform.forward;
        Vector3 rightDir = right * transform.forward;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(eye, eye + leftDir.normalized * detectionRange);
        Gizmos.DrawLine(eye, eye + rightDir.normalized * detectionRange);

        // Debug NavMesh path
        if (agent != null && agent.hasPath)
        {
            Gizmos.color = Color.green;
            Vector3[] corners = agent.path.corners;
            for (int i = 0; i < corners.Length - 1; i++)
            {
                Gizmos.DrawLine(corners[i], corners[i + 1]);
            }
        }

        // Show destination
        if (agent != null && agent.destination != Vector3.zero)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(agent.destination, Vector3.one * 0.5f);
        }

        // Show path switching detection areas
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, enemyDetectionRadius);
        
        // Show available paths
        if (leftPath != null && leftPath.Length > 0)
        {
            Gizmos.color = usingLeftPath ? Color.green : Color.gray;
            for (int i = 0; i < leftPath.Length - 1; i++)
            {
                if (leftPath[i] != null && leftPath[i + 1] != null)
                    Gizmos.DrawLine(leftPath[i].position, leftPath[i + 1].position);
            }
        }
        
        if (rightPath != null && rightPath.Length > 0)
        {
            Gizmos.color = !usingLeftPath ? Color.green : Color.gray;
            for (int i = 0; i < rightPath.Length - 1; i++)
            {
                if (rightPath[i] != null && rightPath[i + 1] != null)
                    Gizmos.DrawLine(rightPath[i].position, rightPath[i + 1].position);
            }
        }
    }
    
    // Network RPC methods
    [ServerRpc(RequireOwnership = false)]
    private void OnTargetLostServerRpc()
    {
        // Server handles target lost logic
        // Can be extended for multiplayer synchronization
    }
    
    [ServerRpc(RequireOwnership = false)]  
    private void OnTargetChangedServerRpc(ulong targetNetworkId)
    {
        // Server handles target change logic
        // Can be extended for multiplayer synchronization
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void OnAttackPerformedServerRpc(ulong targetNetworkId)
    {
        // Server handles attack performed logic
        // Can be extended for multiplayer synchronization
    }
    
    /// <summary>
    /// Called when the unit dies
    /// </summary>
    private void Die()
    {
        if (isNetworkEnabled && IsServer)
        {
            networkIsAlive.Value = false;
        }
        
        // Destroy the unit
        if (gameObject != null)
        {
            Destroy(gameObject);
        }
    }
}
