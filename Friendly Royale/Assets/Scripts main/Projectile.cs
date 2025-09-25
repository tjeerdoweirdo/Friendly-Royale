using UnityEngine;
using System;

/// <summary>
/// Enhanced projectile:
/// - homing with configurable turn speed
/// - optional lead prediction for moving targets
/// - supports Rigidbody velocity or kinematic movement
/// - optional splash (AOE) damage with layer mask filtering
/// - pooling-friendly (resets on enable)
/// - compatible with existing Configure(...) and SetVelocity(...)
/// - ignores and does not damage friendly units (by ownerTag)
/// </summary>

[RequireComponent(typeof(Collider))]
public class Projectile : MonoBehaviour
{
    [Header("Damage")]
    public int damage = 100;
    [Tooltip("Faction of the owner (Player or Enemy). Projectiles ignore and do not damage targets with this faction.")]
    public Unit.Faction ownerFaction = Unit.Faction.Player;

    [Header("Effects")]
    [Tooltip("Effect prefab to spawn on hit (e.g. explosion, impact). Optional.")]
    public GameObject effectPrefab;

    [Header("Motion")]
    public float speed = 10f;
    public bool homing = false;
    public Transform target; // optional homing target

    [Header("Homing / Aim")]
    [Tooltip("How quickly the projectile can turn (degrees per second) when homing.")]
    public float turnSpeedDeg = 720f;
    [Tooltip("If true, attempts to lead the target using its Rigidbody.velocity.")]
    public bool leadTarget = false;
    [Tooltip("Max time (seconds) used for lead prediction (keeps predictions from exploding for far targets).")]
    public float maxLeadTime = 2f;

    [Header("Auto Homing")]
    [Tooltip("If true, will automatically search for objects with the given tag to home in on.")]
    public bool autoHome = false;
    [Tooltip("The tag to search for when auto homing is enabled.")]
    public string autoHomeTargetTag = "Enemy";
    [Tooltip("If true, the auto-home search will run repeatedly while this projectile is active.")]
    public bool autoHomeContinuous = false;

    [Header("Lifetime")]
    public float lifetime = 5f;

    [Header("Splash / AoE")]
    [Tooltip("0 = no splash. >0 will apply damage in radius to objects matching damageMask.")]
    public float splashRadius = 0f;
    [Tooltip("Layers that can be damaged by splash or direct hits.")]
    public LayerMask damageMask = ~0; // default: all

    [Header("Misc")]
    [Tooltip("If true the projectile uses Rigidbody velocity when SetVelocity(...) is called.")]
    public bool useRigidbodyIfPresent = true;
    [Tooltip("If true, OnTriggerEnter checks will ignore triggers on other projectiles to avoid accidental hits.")]
    public bool ignoreOtherProjectiles = true;

    // internal
    Rigidbody rb;
    Vector3 initialVelocity = Vector3.zero;
    float age = 0f;
    GameObject ownerGameObject = null;
    Collider col;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            Debug.LogWarning($"Projectile ({name}): Collider is not a trigger. It's recommended to set projectile collider as 'isTrigger'.");
        }
    }

    void OnEnable()
    {
        age = 0f;
        initialVelocity = Vector3.zero; // Reset velocity for pooling/reuse

        if (useRigidbodyIfPresent && rb != null && initialVelocity.sqrMagnitude > 0.0001f)
        {
            rb.velocity = initialVelocity;
        }

        if (autoHome && target == null)
        {
            GameObject found = FindNearestWithTag(autoHomeTargetTag);
            if (found != null)
            {
                target = found.transform;
                homing = true;
            }
        }
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age >= lifetime)
        {
            DestroyProjectile();
            return;
        }

        if (autoHomeContinuous && autoHome && target == null)
        {
            GameObject found = FindNearestWithTag(autoHomeTargetTag);
            if (found != null)
            {
                target = found.transform;
                homing = true;
            }
        }

        // Non-physics movement (visual/homing calculations done here)
        if (rb == null || !useRigidbodyIfPresent)
        {
            if (homing && target != null)
            {
                Vector3 desiredDir = ComputeAimDirection(target);
                float maxRadians = turnSpeedDeg * Mathf.Deg2Rad * Time.deltaTime;
                Vector3 newDir = Vector3.RotateTowards(transform.forward, desiredDir, maxRadians, 0f).normalized;
                transform.forward = newDir;
                transform.position += newDir * speed * Time.deltaTime;
            }
            else
            {
                if (initialVelocity.sqrMagnitude > 0.0001f)
                {
                    transform.position += initialVelocity * Time.deltaTime;
                    if (initialVelocity.sqrMagnitude > 0.001f)
                        transform.forward = Vector3.Lerp(transform.forward, initialVelocity.normalized, Time.deltaTime * 8f);
                }
                else
                {
                    transform.position += transform.forward * speed * Time.deltaTime;
                }
            }
        }
        else
        {
            if (rb.velocity.sqrMagnitude > 0.001f)
            {
                transform.forward = Vector3.Lerp(transform.forward, rb.velocity.normalized, Time.deltaTime * 10f);
            }

            if (homing && target != null)
            {
                Vector3 desiredDir = ComputeAimDirection(target);
                Vector3 currentVel = rb.velocity;
                float speedMag = currentVel.magnitude;
                if (speedMag < 0.001f) speedMag = speed;

                Vector3 desiredVel = desiredDir * speedMag;
                float t = Mathf.Clamp01((turnSpeedDeg * Time.deltaTime) / 180f);
                Vector3 newVel = Vector3.Slerp(currentVel, desiredVel, t);
                if (newVel.sqrMagnitude < 0.001f) newVel = desiredDir * speedMag;
                rb.velocity = newVel;
            }
        }
    }

    Vector3 ComputeAimDirection(Transform tgt)
    {
        Vector3 from = transform.position;
        Vector3 to = tgt.position;

        if (leadTarget)
        {
            Rigidbody targetRb = tgt.GetComponent<Rigidbody>() ?? tgt.GetComponentInParent<Rigidbody>();
            if (targetRb != null)
            {
                float dist = Vector3.Distance(from, to);
                float travelTime = speed > 0.001f ? dist / speed : 0f;
                travelTime = Mathf.Clamp(travelTime, 0f, maxLeadTime);

                Vector3 predicted = to + targetRb.velocity * travelTime;
                Vector3 dir = (predicted - from).normalized;
                if (dir.sqrMagnitude > 0.0001f) return dir;
            }
        }

        Vector3 direct = (to - from).normalized;
        if (direct.sqrMagnitude < 0.0001f) direct = transform.forward;
        return direct;
    }

    GameObject FindNearestWithTag(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return null;

        GameObject[] objs = GameObject.FindGameObjectsWithTag(tag);
        GameObject nearest = null;
        float minDist = Mathf.Infinity;
        Vector3 pos = transform.position;

        foreach (var obj in objs)
        {
            float dist = (obj.transform.position - pos).sqrMagnitude;
            if (dist < minDist)
            {
                minDist = dist;
                nearest = obj;
            }
        }
        return nearest;
    }

    /// <summary>
    /// Set initial velocity. If Rigidbody present and useRigidbodyIfPresent=true, sets rb.velocity.
    /// Otherwise stores initialVelocity and kinematic movement will use it.
    /// </summary>
    public void SetVelocity(Vector3 v)
    {
        initialVelocity = v;
        if (useRigidbodyIfPresent && rb != null)
        {
            rb.velocity = v;
        }

        if (v.sqrMagnitude > 0.001f)
        {
            transform.forward = v.normalized;
        }
    }

    /// <summary>
    /// Configure projectile after instantiation.
    /// </summary>
    public void Configure(int damageAmount, string ownerTagValue, Transform homingTarget = null, GameObject ownerObj = null)
    {
        damage = damageAmount;
        if (Enum.TryParse(ownerTagValue, out Unit.Faction parsedFaction))
            ownerFaction = parsedFaction;
        else
            ownerFaction = Unit.Faction.Player;
        ownerGameObject = ownerObj;

        if (homingTarget != null)
        {
            homing = true;
            target = homingTarget;
        }
    }

    void OnTriggerEnter(Collider other)
    {
    // --- FRIENDLY FIRE PREVENTION (faction-based) ---
    Unit otherUnit = other.GetComponent<Unit>() ?? other.GetComponentInParent<Unit>();
    if (otherUnit != null && otherUnit.faction == ownerFaction) return;
    if (ownerGameObject != null && other.gameObject == ownerGameObject) return;
    if (ownerGameObject != null && other.attachedRigidbody != null && other.attachedRigidbody.gameObject == ownerGameObject) return;
    // ---------------------------------

        if (ignoreOtherProjectiles)
        {
            Projectile otherProj = other.GetComponent<Projectile>();
            if (otherProj != null) return;
        }

        // attempt to find UnitHealth on the collider or its parents
        UnitHealth unit = other.GetComponent<UnitHealth>() ?? other.GetComponentInParent<UnitHealth>();

        // --- FRIENDLY FIRE PREVENTION (redundant, but safe for splash) ---
        if (unit != null)
        {
            Unit unitComp = unit.GetComponent<Unit>() ?? unit.GetComponentInParent<Unit>();
            if (unitComp != null && unitComp.faction == ownerFaction)
                return;
        }
        // ---------------------------------------------------------------

        if (unit != null)
        {
            if (splashRadius > 0f)
            {
                ApplySplashDamage(transform.position);
            }
            else
            {
                unit.TakeDamage(damage, gameObject);
            }

            OnHitTarget(unit.gameObject);
            return;
        }

        // tower
        Tower tower = other.GetComponent<Tower>() ?? other.GetComponentInParent<Tower>();
        if (tower != null)
        {
            if (splashRadius > 0f)
            {
                ApplySplashDamage(transform.position);
            }
            else
            {
                tower.TakeDamage(damage);
            }

            OnHitTarget(tower.gameObject);
            return;
        }

        // hit environment or other collider: do splash or environment logic
        if (splashRadius > 0f)
        {
            ApplySplashDamage(transform.position);
        }

        OnHitEnvironment(other.gameObject);
    }

    /// <summary>
    /// Apply splash damage to any UnitHealth / Tower within splashRadius filtered by damageMask and ownerTag.
    /// </summary>
    void ApplySplashDamage(Vector3 center)
    {
        Collider[] hits = Physics.OverlapSphere(center, splashRadius, damageMask);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i];

            // ignore owner object (friendly fire prevention)
            if (ownerGameObject != null && c.gameObject == ownerGameObject) continue;
            if (ownerGameObject != null && c.attachedRigidbody != null && c.attachedRigidbody.gameObject == ownerGameObject) continue;

            UnitHealth uh = c.GetComponent<UnitHealth>() ?? c.GetComponentInParent<UnitHealth>();
            if (uh != null)
            {
                Unit unitComp = uh.GetComponent<Unit>() ?? uh.GetComponentInParent<Unit>();
                if (unitComp != null && unitComp.faction == ownerFaction) continue;
                uh.TakeDamage(damage, gameObject);
                continue;
            }

            Tower t = c.GetComponent<Tower>() ?? c.GetComponentInParent<Tower>();
            if (t != null)
            {
                t.TakeDamage(damage);
            }
        }
    }

    protected virtual void OnHitTarget(GameObject targetObj)
    {
        if (effectPrefab != null)
        {
            GameObject effect = Instantiate(effectPrefab, transform.position, Quaternion.identity);
            Destroy(effect, 1f);
        }
        DestroyProjectile();
    }

    protected virtual void OnHitEnvironment(GameObject env)
    {
        if (effectPrefab != null)
        {
            GameObject effect = Instantiate(effectPrefab, transform.position, Quaternion.identity);
            Destroy(effect, 1f);
        }
        DestroyProjectile();
    }

    void DestroyProjectile()
    {
        // If using pooling, replace Destroy with returning to pool
        Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        if (splashRadius > 0f)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, splashRadius);
        }
    }
}