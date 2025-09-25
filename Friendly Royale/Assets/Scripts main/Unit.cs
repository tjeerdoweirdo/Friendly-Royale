using UnityEngine;
using UnityEngine.AI;
using System.Collections;

/// <summary>
/// Unit with NavMeshAgent movement, waypoint lane following, melee/ranged attacks,
/// visual spotting (FOV + LOS), chasing behavior, audio feedback, and an end-target tower.
/// </summary>
[RequireComponent(typeof(UnitHealth))]
public class Unit : MonoBehaviour
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
    [Tooltip("If true, this unit deals splash damage on attack (e.g. MegaKnight)")]
    public bool isSplash = false;
    [Tooltip("Splash radius for splash attacks")] 
    public float splashRadius = 2.5f;

    [Header("Path")]
    [Tooltip("Waypoints for this unit to follow. Usually set by a spawner.")]
    public Transform[] path;
    private int currentWaypoint = 0;

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
    private float retargetTimer = 0f;
    public float retargetInterval = 3f;

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
            agent.acceleration = 8f;
            agent.angularSpeed = 120f;
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

        // If path exists, go to first waypoint; otherwise if there's an end target, go to it
        if (path != null && path.Length > 0 && agent != null)
            agent.SetDestination(path[currentWaypoint].position);
        else if (endTargetTower != null && agent != null)
            agent.SetDestination(endTargetTower.transform.position);
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


        // Retarget to the closest enemy every retargetInterval seconds
        retargetTimer += Time.deltaTime;
        if (retargetTimer >= retargetInterval)
        {
            retargetTimer = 0f;
            RetargetToClosestEnemy();
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
                if (!CanSeeTarget(currentTarget))
                {
                    lostTimer += visualCheckInterval;
                    if (lostTimer >= lostTargetTimeout)
                    {
                        // lost target — forget and resume path/end-target
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
            PlaySpotSound();
        }
    }

    // Retarget to the closest enemy, regardless of FOV/LOS, every retargetInterval seconds
    void RetargetToClosestEnemy()
    {
        Unit[] allUnits = UnityEngine.Object.FindObjectsByType<Unit>(UnityEngine.FindObjectsSortMode.None);
        Transform closest = null;
        float closestDist = Mathf.Infinity;

        // 1. Search for closest enemy unit
        foreach (var u in allUnits)
        {
            if (u == this) continue;
            if (u.faction == this.faction) continue;
            if (u.health == null || !u.health.IsAlive) continue;
            float dist = Vector3.Distance(transform.position, u.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = u.transform;
            }
        }

        // 2. If no unit found, search for closest enemy building
        if (closest == null)
        {
            Building[] allBuildings = UnityEngine.Object.FindObjectsByType<Building>(UnityEngine.FindObjectsSortMode.None);
            foreach (var b in allBuildings)
            {
                if (b == null || b.gameObject == this.gameObject) continue;
                if ((int)b.faction == (int)this.faction) continue;
                var bHealth = b.GetComponent<UnitHealth>();
                if (bHealth != null && !bHealth.IsAlive) continue;
                float dist = Vector3.Distance(transform.position, b.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = b.transform;
                }
            }
        }

        // 3. If still nothing, search for closest enemy tower
        if (closest == null)
        {
            Tower[] allTowers = UnityEngine.Object.FindObjectsByType<Tower>(UnityEngine.FindObjectsSortMode.None);
            foreach (var t in allTowers)
            {
                var towerFaction = (t.ownerTag == "Player") ? Faction.Player : Faction.Enemy;
                var building = t.GetComponent<Building>();
                if (building != null)
                    towerFaction = building.faction;
                if (towerFaction == this.faction) continue;
                float dist = Vector3.Distance(transform.position, t.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = t.transform;
                }
            }
        }

        if (closest != null)
        {
            currentTarget = closest;
            lostTimer = 0f;
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

    void ShootProjectile()
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
        }
        else
        {
            agent.speed = Mathf.Max(0.001f, agent.speed);
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
    }
}
