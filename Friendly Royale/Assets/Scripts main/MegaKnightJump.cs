using UnityEngine;

/// <summary>
/// MegaKnight behavior:
/// - If an enemy is close → melee attack
/// - If enemy is farther (but within jumpRange) → jump to them
/// - Jump deals splash damage on landing (Units, Towers, Health)
/// </summary>
[RequireComponent(typeof(Unit))]
public class MegaKnightJump : MonoBehaviour
{
    [Header("Melee Settings")]
    public float meleeRange = 2f;
    public int meleeDamage = 30;
    public float meleeCooldown = 1f;

    [Header("Jump Settings")]
    public float jumpRange = 6f;
    public float jumpHeight = 2f;
    public float jumpDuration = 1f;
    public float jumpCooldown = 5f;

    [Header("Splash Damage")]
    public float splashRadius = 3f;
    public int splashDamage = 50;

    [Header("Effects")]
    public GameObject jumpEffectPrefab;
    public AudioClip jumpSound;
    public AudioClip meleeSound;

    private Unit unit;
    private Vector3 jumpStart;
    private Vector3 jumpTarget;
    private float jumpTimer;
    private bool isJumping;
    private float lastJumpTime;
    private float lastMeleeTime;
    private AudioSource audioSource;

    void Awake()
    {
        unit = GetComponent<Unit>();
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
    }

    void Update()
    {
        if (isJumping)
        {
            HandleJump();
            return;
        }

        Transform enemy = FindClosestEnemy();
        if (enemy == null) return;

        float dist = Vector3.Distance(transform.position, enemy.position);

        // Melee if enemy is close
        if (dist <= meleeRange && Time.time - lastMeleeTime >= meleeCooldown)
        {
            DoMelee(enemy.gameObject);
            lastMeleeTime = Time.time;
        }
        // Jump if cooldown ready and enemy is not too close
        else if (dist <= jumpRange && Time.time - lastJumpTime >= jumpCooldown)
        {
            DoJump(enemy.position);
            lastJumpTime = Time.time;
        }
    }

    void DoMelee(GameObject target)
    {
        if (meleeSound != null)
            audioSource.PlayOneShot(meleeSound);

        // Try Unit
        Unit u = target.GetComponent<Unit>();
        if (u != null && u.faction != unit.faction && u.health != null && u.health.IsAlive)
        {
            u.health.TakeDamage(meleeDamage, gameObject);
            return;
        }

        // Try generic Health
        Health h = target.GetComponent<Health>();
        if (h != null && !h.isDead)
        {
            h.TakeDamage(meleeDamage);
            return;
        }

        // Try UnitHealth
        UnitHealth uh = target.GetComponent<UnitHealth>();
        if (uh != null && uh.IsAlive)
        {
            uh.TakeDamage(meleeDamage, gameObject);
            return;
        }

        // Try Tower
        Tower tw = target.GetComponent<Tower>();
        if (tw != null)
        {
            tw.TakeDamage(meleeDamage);
        }
    }

    void DoJump(Vector3 target)
    {
        jumpStart = transform.position;
        jumpTarget = target;
        jumpTimer = 0f;
        isJumping = true;

        if (jumpSound != null)
            audioSource.PlayOneShot(jumpSound);
    }

    void HandleJump()
    {
        jumpTimer += Time.deltaTime;
        float t = Mathf.Clamp01(jumpTimer / jumpDuration);

        // Parabola
        Vector3 pos = Vector3.Lerp(jumpStart, jumpTarget, t);
        pos.y += Mathf.Sin(Mathf.PI * t) * jumpHeight;
        transform.position = pos;

        if (t >= 1f)
        {
            isJumping = false;

            if (jumpEffectPrefab != null)
                Instantiate(jumpEffectPrefab, transform.position, Quaternion.identity);

            SplashDamage();
        }
    }

    void SplashDamage()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, splashRadius);
        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue; // ignore self

            // Try Unit
            Unit u = hit.GetComponent<Unit>();
            if (u != null && u.faction != unit.faction && u.health != null && u.health.IsAlive)
            {
                u.health.TakeDamage(splashDamage, gameObject);
                continue;
            }

            // Try generic Health
            Health h = hit.GetComponent<Health>();
            if (h != null && !h.isDead)
            {
                h.TakeDamage(splashDamage);
                continue;
            }

            // Try UnitHealth
            UnitHealth uh = hit.GetComponent<UnitHealth>();
            if (uh != null && uh.IsAlive)
            {
                uh.TakeDamage(splashDamage, gameObject);
                continue;
            }

            // Try Tower
            Tower tw = hit.GetComponent<Tower>();
            if (tw != null)
            {
                tw.TakeDamage(splashDamage);
            }
        }
    }

    Transform FindClosestEnemy()
    {
        Unit[] allUnits = FindObjectsOfType<Unit>();
        Transform closest = null;
        float closestDist = Mathf.Infinity;

        foreach (var u in allUnits)
        {
            if (u == unit) continue;
            if (u.faction == unit.faction) continue;
            if (u.health == null || !u.health.IsAlive) continue;

            float dist = Vector3.Distance(transform.position, u.transform.position);
            if (dist < jumpRange && dist < closestDist)
            {
                closestDist = dist;
                closest = u.transform;
            }
        }

        // Also allow Towers
        Tower[] allTowers = FindObjectsOfType<Tower>();
        foreach (var tw in allTowers)
        {
            if (tw.faction == unit.faction) continue;

            float dist = Vector3.Distance(transform.position, tw.transform.position);
            if (dist < jumpRange && dist < closestDist)
            {
                closestDist = dist;
                closest = tw.transform;
            }
        }

        return closest;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, meleeRange);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, jumpRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, splashRadius);
    }
}
