using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// FireSpirit trooper: bounces toward enemy units/buildings and explodes on contact.
/// Behaves like authentic Clash Royale Fire Spirits with smart targeting and leap attacks.
/// Attach to the same GameObject that has your Unit and UnitHealth components.
/// </summary>
[RequireComponent(typeof(Unit))]
[RequireComponent(typeof(UnitHealth))]
public class FireSpirit : MonoBehaviour
{
    [Header("Movement / Targeting")]
    public float chargeSpeed = 8.0f;
    public float fuseTime = 5.0f;
    public float targetScanRadius = 7.0f;
    public float retargetInterval = 0.5f;
    public bool prioritizeUnitsOverBuildings = true;

    [Header("Leap Attack")]
    public float leapRange = 4.0f;
    public float leapHeight = 2.5f;
    public float leapSpeed = 12.0f;
    public AnimationCurve leapCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Explosion")]
    public float explosionRadius = 2.5f;
    [Tooltip("Base explosion damage - will be overridden by card damage if sourceCard is assigned")]
    public int explosionDamage = 169;
    public LayerMask damageMask = ~0;
    public float damageReduction = 0.15f; // damage reduces by this % per unit distance from center

    [Header("Card Integration")]
    [Tooltip("The card that spawned this Fire Spirit - used to get damage values")]
    public Card sourceCard;
    [Tooltip("Card level for damage scaling")]
    public int cardLevel = 1;

    [Header("Hopping Movement")]
    public float hopInterval = 0.8f;
    public float hopForce = 4.0f;
    public float hopHeight = 1.2f;

    [Header("FX / SFX")]
    public GameObject spawnEffectPrefab;
    public GameObject explosionEffectPrefab;
    public GameObject flameTailPrefab;
    public AudioClip spawnClip;
    public AudioClip explosionClip;
    public AudioClip hopClip;
    public AudioSource audioSource;

    // runtime
    private Unit unit;
    private UnitHealth myHealth;
    private Transform currentTarget;
    private float lifeTimer = 0f;
    private float retargetTimer = 0f;
    private float hopTimer = 0f;
    private bool isExploding = false;
    private bool isLeaping = false;
    private Vector3 leapStartPos;
    private Vector3 leapTargetPos;
    private float leapProgress = 0f;
    private Rigidbody rb;
    private GameObject flameTail;

    void Awake()
    {
        unit = GetComponent<Unit>();
        myHealth = GetComponent<UnitHealth>();
        rb = GetComponent<Rigidbody>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();

        // Configure unit stats for a fast, small Fire Spirit
        unit.isRanged = false;
        unit.moveSpeed = chargeSpeed;
        unit.attackRange = 0.0f; // we use explosion instead of normal melee
        unit.SyncAgentToStats();

        if (fuseTime > 0f) lifeTimer = fuseTime;
        retargetTimer = retargetInterval;
        hopTimer = hopInterval;
    }

    /// <summary>
    /// Get the current explosion damage based on card level, or fallback to hardcoded value
    /// </summary>
    public int GetCurrentExplosionDamage()
    {
        if (sourceCard != null)
        {
            return Mathf.RoundToInt(sourceCard.GetDamageForLevel(cardLevel));
        }
        return explosionDamage; // fallback to inspector value
    }

    /// <summary>
    /// Set the card reference and level (called by CardSpawner)
    /// </summary>
    public void SetCardData(Card card, int level)
    {
        sourceCard = card;
        cardLevel = level;
        
        // Update explosion damage based on card
        if (card != null)
        {
            explosionDamage = Mathf.RoundToInt(card.GetDamageForLevel(level));
        }
    }

    void Start()
    {
        // spawn effect / sound
        if (spawnEffectPrefab != null) Instantiate(spawnEffectPrefab, transform.position, Quaternion.identity);
        if (spawnClip != null && audioSource != null) audioSource.PlayOneShot(spawnClip);

        // Create flame tail effect
        if (flameTailPrefab != null)
        {
            flameTail = Instantiate(flameTailPrefab, transform.position, Quaternion.identity);
            flameTail.transform.SetParent(transform);
        }

        // Initial hop to emphasize spawn
        if (rb != null)
        {
            rb.AddForce(Vector3.up * hopForce, ForceMode.Impulse);
            if (hopClip != null && audioSource != null) audioSource.PlayOneShot(hopClip);
        }

        // Find initial target
        currentTarget = FindBestTarget();
        UpdateNavigation();
    }

    void Update()
    {
        if (isExploding) return;

        // fuse timer (safety mechanism)
        if (fuseTime > 0f)
        {
            lifeTimer -= Time.deltaTime;
            if (lifeTimer <= 0f)
            {
                Explode();
                return;
            }
        }

        // Handle leap attack
        if (isLeaping)
        {
            HandleLeapAttack();
            return;
        }

        // Retarget enemies periodically
        retargetTimer -= Time.deltaTime;
        if (retargetTimer <= 0f)
        {
            retargetTimer = retargetInterval;
            Transform newTarget = FindBestTarget();
            if (newTarget != currentTarget)
            {
                currentTarget = newTarget;
                UpdateNavigation();
            }
        }

        // Check if we should initiate leap attack
        if (currentTarget != null && !isLeaping)
        {
            float distanceToTarget = Vector3.Distance(transform.position, currentTarget.position);
            if (distanceToTarget <= leapRange)
            {
                StartLeapAttack();
                return;
            }
        }

        // Bouncy hop movement
        hopTimer -= Time.deltaTime;
        if (hopTimer <= 0f && !isLeaping && rb != null)
        {
            hopTimer = hopInterval;
            DoHop();
        }

        // Check if we've reached our destination without a target
        if (currentTarget == null && unit.agent != null && !unit.agent.pathPending && unit.agent.remainingDistance < 0.5f)
        {
            Explode(); // Self-destruct if we have nowhere to go
        }
    }

    // Explode when colliding with enemy unit/tower
    void OnTriggerEnter(Collider other)
    {
        if (isExploding || isLeaping) return;
        if (IsEnemyCollider(other))
        {
            Explode();
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (isExploding || isLeaping) return;
        if (IsEnemyCollider(collision.collider))
        {
            Explode();
        }
    }

    bool IsEnemyCollider(Collider c)
    {
        if (c == null) return false;

        // check for Unit component
        var unitComp = c.GetComponentInParent<Unit>();
        if (unitComp != null && unitComp.faction != unit.faction && unitComp.health != null && unitComp.health.IsAlive)
            return true;

        // check for Tower
        var tower = c.GetComponentInParent<Tower>();
        if (tower != null)
        {
            var towerFaction = (tower.ownerTag == "Player") ? Unit.Faction.Player : Unit.Faction.Enemy;
            if (towerFaction != unit.faction) return true;
        }

        return false;
    }

    /// <summary>
    /// Find the best target prioritizing units over buildings and preferring closer targets
    /// </summary>
    Transform FindBestTarget()
    {
        Collider[] nearbyColliders = Physics.OverlapSphere(transform.position, targetScanRadius, damageMask);
        
        Transform bestUnitTarget = null;
        Transform bestBuildingTarget = null;
        float bestUnitDistance = Mathf.Infinity;
        float bestBuildingDistance = Mathf.Infinity;

        foreach (var collider in nearbyColliders)
        {
            if (collider == null) continue;

            // Check for units first (higher priority)
            var unitComp = collider.GetComponentInParent<Unit>();
            if (unitComp != null && unitComp.faction != unit.faction && unitComp.health != null && unitComp.health.IsAlive)
            {
                float distance = Vector3.Distance(transform.position, collider.transform.position);
                if (distance < bestUnitDistance)
                {
                    bestUnitDistance = distance;
                    bestUnitTarget = collider.transform;
                }
            }

            // Check for buildings/towers
            var tower = collider.GetComponentInParent<Tower>();
            if (tower != null)
            {
                var towerFaction = (tower.ownerTag == "Player") ? Unit.Faction.Player : Unit.Faction.Enemy;
                if (towerFaction != unit.faction)
                {
                    float distance = Vector3.Distance(transform.position, collider.transform.position);
                    if (distance < bestBuildingDistance)
                    {
                        bestBuildingDistance = distance;
                        bestBuildingTarget = collider.transform;
                    }
                }
            }
        }

        // Prioritize units over buildings if configured
        if (prioritizeUnitsOverBuildings && bestUnitTarget != null)
            return bestUnitTarget;
        
        // Return closest target (unit or building)
        if (bestUnitTarget != null && bestBuildingTarget != null)
            return (bestUnitDistance < bestBuildingDistance) ? bestUnitTarget : bestBuildingTarget;
        
        return bestUnitTarget ?? bestBuildingTarget;
    }

    /// <summary>
    /// Update NavMeshAgent to move toward current target
    /// </summary>
    void UpdateNavigation()
    {
        if (unit.agent == null) return;

        if (currentTarget != null)
        {
            unit.agent.SetDestination(currentTarget.position);
        }
        else
        {
            // No specific target, find nearest enemy tower as fallback
            Tower nearestTower = FindNearestEnemyTower();
            if (nearestTower != null)
            {
                unit.agent.SetDestination(nearestTower.transform.position);
            }
        }
    }

    /// <summary>
    /// Perform a small hop for bouncy movement
    /// </summary>
    void DoHop()
    {
        if (rb != null && !isLeaping)
        {
            Vector3 hopDirection = Vector3.up;
            
            // Add slight forward momentum if moving
            if (unit.agent != null && unit.agent.velocity.magnitude > 0.5f)
            {
                hopDirection += unit.agent.velocity.normalized * 0.3f;
            }
            
            rb.AddForce(hopDirection * hopHeight, ForceMode.Impulse);
            
            if (hopClip != null && audioSource != null)
                audioSource.PlayOneShot(hopClip, 0.3f);
        }
    }

    /// <summary>
    /// Start leap attack toward target
    /// </summary>
    void StartLeapAttack()
    {
        if (currentTarget == null || isLeaping) return;

        isLeaping = true;
        leapStartPos = transform.position;
        leapTargetPos = currentTarget.position;
        leapProgress = 0f;

        // Disable NavMeshAgent during leap
        if (unit.agent != null)
            unit.agent.enabled = false;
    }

    /// <summary>
    /// Handle leap attack movement and collision
    /// </summary>
    void HandleLeapAttack()
    {
        if (!isLeaping) return;

        leapProgress += Time.deltaTime * leapSpeed / Vector3.Distance(leapStartPos, leapTargetPos);
        
        if (leapProgress >= 1f)
        {
            // Reached target, explode
            transform.position = leapTargetPos;
            Explode();
            return;
        }

        // Calculate arc position
        Vector3 linearPos = Vector3.Lerp(leapStartPos, leapTargetPos, leapProgress);
        float heightOffset = leapCurve.Evaluate(leapProgress) * leapHeight;
        transform.position = linearPos + Vector3.up * heightOffset;

        // Face movement direction
        Vector3 direction = (leapTargetPos - leapStartPos).normalized;
        if (direction != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(direction);
    }

    /// <summary>
    /// Perform the explosion: area damage with falloff, FX/SFX, and self-destruct.
    /// </summary>
    void Explode()
    {
        if (isExploding) return;
        isExploding = true;

        // Clean up flame tail
        if (flameTail != null)
            Destroy(flameTail);

        // Play explosion effect & sound
        if (explosionEffectPrefab != null)
            Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);

        if (explosionClip != null && audioSource != null)
            audioSource.PlayOneShot(explosionClip);

        // Do area damage with distance falloff (ENEMY ONLY)
        Collider[] hits = Physics.OverlapSphere(transform.position, explosionRadius, damageMask);
        int baseDamage = GetCurrentExplosionDamage();
        
        foreach (var hit in hits)
        {
            if (hit == null) continue;

            float distance = Vector3.Distance(transform.position, hit.transform.position);
            float damageMultiplier = Mathf.Max(0.1f, 1f - (distance / explosionRadius) * damageReduction);
            int finalDamage = Mathf.RoundToInt(baseDamage * damageMultiplier);

            // Damage Units (UnitHealth) - ENEMY ONLY
            var uh = hit.GetComponentInParent<UnitHealth>();
            var unitComp = hit.GetComponentInParent<Unit>();
            if (uh != null && uh.IsAlive && unitComp != null && unitComp.faction != unit.faction)
            {
                try
                {
                    uh.TakeDamage(finalDamage, this.gameObject);
                }
                catch
                {
                    try { uh.TakeDamage(finalDamage); } catch { }
                }
                
                // Knockback based on distance (closer = more knockback)
                Rigidbody targetRb = hit.GetComponentInParent<Rigidbody>();
                if (targetRb != null)
                {
                    Vector3 dir = (hit.transform.position - transform.position).normalized;
                    float knockbackForce = (1f - distance / explosionRadius) * 6f;
                    targetRb.AddForce(dir * knockbackForce, ForceMode.Impulse);
                }
            }

            // Damage Tower - ENEMY ONLY
            var tw = hit.GetComponentInParent<Tower>();
            if (tw != null)
            {
                var towerFaction = (tw.ownerTag == "Player") ? Unit.Faction.Player : Unit.Faction.Enemy;
                if (towerFaction != unit.faction)
                {
                    try { tw.TakeDamage(finalDamage); } catch { }
                }
            }
        }

        // Destroy self (small delay to allow SFX to play)
        Destroy(gameObject, 0.1f);
    }

    Tower FindNearestEnemyTower()
    {
        Tower[] all = FindObjectsByType<Tower>(FindObjectsSortMode.None);
        float best = Mathf.Infinity;
        Tower bestT = null;
        foreach (var t in all)
        {
            if (t == null) continue;
            var towerFaction = (t.ownerTag == "Player") ? Unit.Faction.Player : Unit.Faction.Enemy;
            if (towerFaction == unit.faction) continue;
            float d = Vector3.Distance(transform.position, t.transform.position);
            if (d < best)
            {
                best = d;
                bestT = t;
            }
        }
        return bestT;
    }

    void OnDestroy()
    {
        // Clean up flame tail if it exists
        if (flameTail != null)
            Destroy(flameTail);
    }

    void OnDrawGizmosSelected()
    {
        // Draw explosion radius
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
        
        // Draw target scan radius
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, targetScanRadius);
        
        // Draw leap range
        Gizmos.color = new Color(1f, 0.5f, 0f); // Orange
        Gizmos.DrawWireSphere(transform.position, leapRange);
        
        // Draw line to current target
        if (currentTarget != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, currentTarget.position);
        }
    }
}