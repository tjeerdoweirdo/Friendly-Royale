using UnityEngine;
using UnityEngine.AI;
using System.Collections;

/// <summary>
/// FireSpirit-like trooper: charges toward enemy buildings/units and explodes on contact or arrival.
/// Attach to the same GameObject that has your Unit and UnitHealth components.
/// </summary>
[RequireComponent(typeof(Unit))]
[RequireComponent(typeof(UnitHealth))]
public class FireSpirit : MonoBehaviour
{
    [Header("Movement / Targeting")]
    public float chargeSpeed = 7.5f;
    public float fuseTime = 3.5f;
    public bool explodeOnContact = true;
    public bool explodeOnReachTarget = true;

    [Header("Explosion")]
    public float explosionRadius = 3.2f;
    public int explosionDamage = 65;
    public LayerMask damageMask = ~0;
    public float reachExplodeDistance = 1.0f;

    [Header("FX / SFX")]
    public GameObject spawnEffectPrefab;
    public GameObject explosionEffectPrefab;
    public AudioClip spawnClip;
    public AudioClip explosionClip;
    public AudioSource audioSource;

    [Header("Optional physics hop")]
    public bool doInitialHop = true;
    public float hopForce = 3f;

    // runtime
    private Unit unit;
    private UnitHealth myHealth;
    private float lifeTimer = 0f;
    private bool isExploding = false;

    void Awake()
    {
        unit = GetComponent<Unit>();
        myHealth = GetComponent<UnitHealth>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();

        // Configure unit stats for a fast charging small troop
        unit.isRanged = false;
        unit.moveSpeed = chargeSpeed;
        unit.attackRange = 0.0f; // we use explosion instead of normal melee
        unit.SyncAgentToStats();

        if (fuseTime > 0f) lifeTimer = fuseTime;
    }

    void Start()
    {
        // spawn effect / sound
        if (spawnEffectPrefab != null) Instantiate(spawnEffectPrefab, transform.position, Quaternion.identity);
        if (spawnClip != null && audioSource != null) audioSource.PlayOneShot(spawnClip);

        // small hop to emphasize charge
        if (doInitialHop)
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.AddForce(Vector3.up * hopForce, ForceMode.Impulse);
            }
        }

        // If the unit doesn't already have an endTargetTower, attempt to pick the nearest enemy building/tower.
        if (unit.endTargetTower == null)
        {
            unit.endTargetTower = FindNearestEnemyTower();
            // If we found a tower, ensure the agent heads there:
            if (unit.endTargetTower != null && unit.agent != null)
                unit.agent.SetDestination(unit.endTargetTower.transform.position);
        }
    }

    void Update()
    {
        if (isExploding) return;

        // fuse timer
        if (fuseTime > 0f)
        {
            lifeTimer -= Time.deltaTime;
            if (lifeTimer <= 0f)
            {
                Explode();
                return;
            }
        }

        // if configured to explode on reaching the end target, check distance
        if (explodeOnReachTarget && unit.endTargetTower != null)
        {
            float d = Vector3.Distance(transform.position, unit.endTargetTower.transform.position);
            if (d <= reachExplodeDistance)
            {
                Explode();
                return;
            }
        }
    }

    // Explode when colliding with enemy unit/tower (if enabled)
    void OnTriggerEnter(Collider other)
    {
        if (!explodeOnContact || isExploding) return;
        if (IsEnemyCollider(other))
        {
            Explode();
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!explodeOnContact || isExploding) return;
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
    /// Perform the explosion: area damage, FX/SFX, and self-destruct.
    /// </summary>
    void Explode()
    {
        if (isExploding) return;
        isExploding = true;

        // Play explosion effect & sound
        if (explosionEffectPrefab != null)
            Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);

        if (explosionClip != null && audioSource != null)
            audioSource.PlayOneShot(explosionClip);

        // Do area damage (ENEMY ONLY)
        Collider[] hits = Physics.OverlapSphere(transform.position, explosionRadius, damageMask);
        foreach (var hit in hits)
        {
            if (hit == null) continue;

            // Damage Units (UnitHealth) - ENEMY ONLY
            var uh = hit.GetComponentInParent<UnitHealth>();
            var unitComp = hit.GetComponentInParent<Unit>();
            if (uh != null && uh.IsAlive && unitComp != null && unitComp.faction != unit.faction)
            {
                try
                {
                    uh.TakeDamage(explosionDamage, this.gameObject);
                }
                catch
                {
                    try { uh.TakeDamage(explosionDamage); } catch { }
                }
                // Optional: Knockback for viability
                Rigidbody rb = hit.GetComponentInParent<Rigidbody>();
                if (rb != null)
                {
                    Vector3 dir = (hit.transform.position - transform.position).normalized;
                    rb.AddForce(dir * 4f, ForceMode.Impulse);
                }
            }

            // Damage Tower - ENEMY ONLY
            var tw = hit.GetComponentInParent<Tower>();
            if (tw != null)
            {
                var towerFaction = (tw.ownerTag == "Player") ? Unit.Faction.Player : Unit.Faction.Enemy;
                if (towerFaction != unit.faction)
                {
                    try { tw.TakeDamage(explosionDamage); } catch { }
                }
            }
        }

        // Destroy self (small delay to allow SFX to fire if needed)
        Destroy(gameObject);
    }

    Tower FindNearestEnemyTower()
    {
        Tower[] all = FindObjectsOfType<Tower>();
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

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
}