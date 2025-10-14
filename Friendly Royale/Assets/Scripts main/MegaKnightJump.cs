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
    public float jumpRange = 600f;
    public float jumpHeight = 2f;
    public float jumpDuration = 1f;
    public float jumpCooldown = 0.01f;

    [Header("Splash Damage")]
    public float splashRadius = 3f;
    public int splashDamage = 50;

    [Header("Effects")]
    public GameObject jumpEffectPrefab;
    public GameObject landingIndicatorPrefab;
    public AudioClip jumpSound;
    public AudioClip landingSound;
    public AudioClip meleeSound;

    [Header("Animation & Wind-up")]
    public Animator animator;
    public AnimationClip jumpWindUpClip;
    public AnimationClip jumpClip;
    public AnimationClip landingClip;
    public AnimationClip meleeClip;
    public float windUpDuration = 0.5f;
    public bool smoothRotation = true;
    public float rotationSpeed = 360f;

    private Unit unit;
    private Vector3 jumpStart;
    private Vector3 jumpTarget;
    private float jumpTimer;
    private bool isJumping;
    private bool isWindingUp;
    private float windUpTimer;
    private float lastJumpTime;
    private float lastMeleeTime;
    private AudioSource audioSource;
    private GameObject landingIndicatorInstance;
    private Quaternion targetRotation;
    private Transform currentTarget;

    void Awake()
    {
        unit = GetComponent<Unit>();
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
    }

    void OnDestroy()
    {
        // Clean up landing indicator when Mega Knight is destroyed/killed
        HideLandingIndicator();
    }

    void Update()
    {
        if (isJumping)
        {
            HandleJump();
            return;
        }

        if (isWindingUp)
        {
            HandleWindUp();
            return;
        }

        Transform enemy = FindClosestEnemy();
        if (enemy == null) return;

        float dist = Vector3.Distance(transform.position, enemy.position);

        // Melee if enemy is close
        if (dist <= meleeRange && Time.time - lastMeleeTime >= meleeCooldown)
        {
            FaceTarget(enemy.position);
            DoMelee(enemy.gameObject);
            lastMeleeTime = Time.time;
        }
        // Jump if cooldown ready and enemy is not too close
        else if (dist <= jumpRange && Time.time - lastJumpTime >= jumpCooldown)
        {
            StartJumpWindUp(enemy.position, enemy);
            lastJumpTime = Time.time;
        }

        // Handle smooth rotation towards target if not attacking
        if (smoothRotation && !isJumping && !isWindingUp)
        {
            HandleSmoothRotation();
        }
    }

    void DoMelee(GameObject target)
    {
        // Play melee animation
        if (animator != null && meleeClip != null)
        {
            animator.Play(meleeClip.name);
        }

        if (meleeSound != null)
            audioSource.PlayOneShot(meleeSound);

        // Try Unit
        Unit u = target.GetComponent<Unit>();
        if (u != null && u.faction != unit.faction && u.health != null && u.health.IsAlive)
        {
            u.health.TakeDamage(meleeDamage, gameObject);
            return;
        }

        // Prefer Tower over generic Health to keep a single HP source for towers
        Tower tw = target.GetComponentInParent<Tower>();
        if (tw != null && IsEnemy(target))
        {
            tw.TakeDamage(meleeDamage);
            return;
        }

        // Try generic Health (non-tower buildings)
        Health h = target.GetComponent<Health>();
        if (h != null && !h.isDead && IsEnemy(target))
        {
            h.TakeDamage(meleeDamage);
            return;
        }

        // Try UnitHealth (non-Unit wrappers)
        UnitHealth uh = target.GetComponent<UnitHealth>();
        if (uh != null && uh.IsAlive && IsEnemy(target))
        {
            uh.TakeDamage(meleeDamage, gameObject);
            return;
        }
    }

    void StartJumpWindUp(Vector3 target, Transform targetTransform)
    {
        jumpTarget = target;
        currentTarget = targetTransform;
        isWindingUp = true;
        windUpTimer = 0f;

        // Face the target immediately
        FaceTarget(jumpTarget);

        // Play wind-up animation
        if (animator != null && jumpWindUpClip != null)
        {
            animator.Play(jumpWindUpClip.name);
        }

        // Show landing indicator during wind-up
        ShowLandingIndicator();
    }

    void HandleWindUp()
    {
        windUpTimer += Time.deltaTime;
        
        // Continue facing the target during wind-up
        if (currentTarget != null)
        {
            FaceTarget(currentTarget.position);
        }
        else
        {
            FaceTarget(jumpTarget);
        }

        if (windUpTimer >= windUpDuration)
        {
            isWindingUp = false;
            DoJump(jumpTarget);
        }
    }

    void DoJump(Vector3 target)
    {
        jumpStart = transform.position;
        jumpTarget = target;
        jumpTimer = 0f;
        isJumping = true;

        // Face jump direction
        FaceTarget(jumpTarget);

        if (jumpSound != null)
            audioSource.PlayOneShot(jumpSound);

        // Play jump animation
        if (animator != null && jumpClip != null)
        {
            animator.Play(jumpClip.name);
        }
    }

    void FaceTarget(Vector3 targetPosition)
    {
        Vector3 direction = (targetPosition - transform.position);
        direction.y = 0f; // Keep on horizontal plane
        
        if (direction.sqrMagnitude > 0.001f)
        {
            targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            
            if (smoothRotation && !isJumping)
            {
                // Smooth rotation will be handled in HandleSmoothRotation
            }
            else
            {
                // Instant rotation for jumping
                transform.rotation = targetRotation;
            }
        }
    }

    void HandleSmoothRotation()
    {
        if (targetRotation != Quaternion.identity)
        {
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, 
                targetRotation, 
                rotationSpeed * Time.deltaTime
            );
        }
    }

    void HandleJump()
    {
        jumpTimer += Time.deltaTime;
        float t = Mathf.Clamp01(jumpTimer / jumpDuration);

        // Parabola movement
        Vector3 pos = Vector3.Lerp(jumpStart, jumpTarget, t);
        pos.y += Mathf.Sin(Mathf.PI * t) * jumpHeight;
        transform.position = pos;

        // Maintain facing direction during jump
        Vector3 jumpDirection = (jumpTarget - jumpStart).normalized;
        if (jumpDirection.sqrMagnitude > 0.001f)
        {
            jumpDirection.y = 0f;
            transform.rotation = Quaternion.LookRotation(jumpDirection, Vector3.up);
        }

        if (t >= 1f)
        {
            isJumping = false;

            // Hide landing indicator
            HideLandingIndicator();

            // Play landing animation
            if (animator != null && landingClip != null)
            {
                animator.Play(landingClip.name);
            }

            if (jumpEffectPrefab != null)
                Instantiate(jumpEffectPrefab, transform.position, Quaternion.identity);

            if (landingSound != null)
                audioSource.PlayOneShot(landingSound);

            SplashDamage();
        }
    }

    void ShowLandingIndicator()
    {
        if (landingIndicatorPrefab != null)
        {
            // Hide any existing indicator first
            HideLandingIndicator();
            
            // Create new indicator at target position
            Vector3 indicatorPos = jumpTarget;
            indicatorPos.y = 0.1f; // Slightly above ground
            landingIndicatorInstance = Instantiate(landingIndicatorPrefab, indicatorPos, Quaternion.identity);
            
            // Scale the indicator to match splash radius
            if (landingIndicatorInstance != null)
            {
                landingIndicatorInstance.transform.localScale = Vector3.one * splashRadius * 2f;
            }
        }
    }

    void HideLandingIndicator()
    {
        if (landingIndicatorInstance != null)
        {
            Destroy(landingIndicatorInstance);
            landingIndicatorInstance = null;
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

            // Prefer Tower over generic Health so tower HP/Death is consistent
            Tower tw = hit.GetComponentInParent<Tower>();
            if (tw != null && IsEnemy(hit.gameObject))
            {
                tw.TakeDamage(splashDamage);
                continue;
            }

            // Then generic Health (non-tower objects)
            Health h = hit.GetComponent<Health>();
            if (h != null && !h.isDead && IsEnemy(hit.gameObject))
            {
                h.TakeDamage(splashDamage);
                continue;
            }

            // Then UnitHealth
            UnitHealth uh = hit.GetComponent<UnitHealth>();
            if (uh != null && uh.IsAlive && IsEnemy(hit.gameObject))
            {
                uh.TakeDamage(splashDamage, gameObject);
                continue;
            }
        }
    }

    Transform FindClosestEnemy()
    {
        Unit[] allUnits = FindObjectsByType<Unit>(FindObjectsSortMode.None);
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
        Tower[] allTowers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
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

    // Determines if the target belongs to the opposing faction.
    // Checks Unit/Tower on self and parents to handle colliders on child objects.
    bool IsEnemy(GameObject targetGO)
    {
        if (targetGO == null) return false;

        // Direct components
        var u = targetGO.GetComponent<Unit>();
        if (u != null) return u.faction != unit.faction;
        var tw = targetGO.GetComponent<Tower>();
        if (tw != null) return tw.faction != unit.faction;

        // Parent lookup (common when colliders are on children)
        u = targetGO.GetComponentInParent<Unit>();
        if (u != null) return u.faction != unit.faction;
        tw = targetGO.GetComponentInParent<Tower>();
        if (tw != null) return tw.faction != unit.faction;

        // Unknown allegiance: do not damage to avoid friendly fire
        return false;
    }
}
