using UnityEngine;

// Require either Health or UnitHealth for compatibility
[RequireComponent(typeof(Health))]
public class Building : MonoBehaviour
{
    [Header("Destroy On Despawn (optional)")]
    [Tooltip("Any extra GameObjects to destroy when this building despawns due to lifetime or health.")]
    public GameObject[] destroyOnDespawn;
    [Header("UI (Defense Target)")]
    [Tooltip("Optional: Shows the name of the current target for defense buildings.")]
    public TMPro.TextMeshProUGUI targetTextUI;
    // --- Spawner (Furnace) logic ---
    [Header("Spawner Settings (if Spawner type)")]
    public GameObject unitPrefab;                // the troop to spawn
    public Transform[] spawnPoints;              // local spawn points (lanes)
    public float spawnInterval = 2.5f;           // seconds between spawns
    public int maxActive = 3;                    // simultaneous active spawned units
    public int maxTotalSpawns = 6;               // total number of spawns before spawner expires (set 0 for unlimited)
    [Header("Lifetime & placement")]
    public bool expireBySpawnCount = true;       // if true, spawner expires after maxTotalSpawns
    [Header("Targeting (Clash-like)")]
    public bool faceNearestEnemyBuilding = true; // rotate spawns to face nearest enemy building / tower
    public Transform forcedTarget;               // optional explicit target (e.g. enemy spawn point / tower)
    [Header("Behavior")]
    public bool randomizeSpawnPoint = false;     // choose a spawn point randomly vs round-robin
    public bool parentSpawnedToMap = false;      // whether spawned units get parented to spawner (usually false)
    [Header("FX & Audio")]
    public GameObject spawnEffectPrefab;
    public AudioClip spawnSound;
    public AudioSource audioSource;
    [Header("Animation")]
    public Animator animator;
    public AnimationClip spawnAnimationClip;
    [Header("UI (non-floating)")]
    public TMPro.TextMeshProUGUI spawnTextUI;

    // runtime
    private readonly System.Collections.Generic.List<GameObject> activeSpawns = new System.Collections.Generic.List<GameObject>();
    private int roundRobinIndex = 0;
    private int totalSpawned = 0;
    private Coroutine spawnRoutine;
// removed stray brace
    public enum BuildingType
    {
        Default,    // Just a building (no special behavior)
        Spawner,    // Spawns units (like Furnace)
        Defense     // Attacks enemies (like Cannon, Tesla, etc.)
    }

    public enum AttackMode
    {
        Direct, // Like a cannon
        Arc     // Like a mortar
    }

    [Header("Building Type")]
    public BuildingType buildingType = BuildingType.Default;

    // Do not set a default faction here; let the spawner assign it!
    public Unit.Faction faction;

    [Header("Lifetime & Decay")]
    public float lifetime = 0f; // seconds, 0 = infinite (default to infinite to prevent instant despawn)
    [HideInInspector]
    private float hpDecayPerSecond = 0f; // HP lost per second, auto-calculated
    private float lifeTimer = 0f;
    private Health health;
    private UnitHealth unitHealth;

    [Header("Attack Settings (for Defense/Spawner type)")]
    [Tooltip("These fields are used if the building is a Defense or a Spawner that can attack (e.g. Furnace with attack). Set as needed.")]
    public float attackRange = 6f;
    public float attackDamage = 12f;
    public float attackCooldown = 1.0f;
    public AttackMode attackMode = AttackMode.Direct;
    public GameObject projectilePrefab; // Assign in inspector
    public Transform shootPoint; // Assign in inspector (where projectile spawns)
    public float arcHeight = 3f; // Only for arc mode
    private float lastAttack = 0f;


    void OnEnable()
    {
        // Ensure building has a UnitHealth for targeting by units
        unitHealth = GetComponent<UnitHealth>();
        if (unitHealth == null)
            unitHealth = gameObject.AddComponent<UnitHealth>();
        // Optionally sync maxHealth from Health if present
        health = GetComponent<Health>();
        if (health != null && unitHealth != null)
        {
            unitHealth.maxHealth = Mathf.RoundToInt(health.maxHealth);
            unitHealth.currentHealth = Mathf.RoundToInt(health.currentHealth);
        }
        CalculateDecay();
    }

    void Start()
    {
        health = GetComponent<Health>();
        unitHealth = GetComponent<UnitHealth>();
        if (lifetime < 0f) lifetime = 0f; // Prevent negative lifetime
        if (lifetime > 0f)
            lifeTimer = lifetime;
        CalculateDecay();

        // Spawner logic: only start if this is a spawner
        if (buildingType == BuildingType.Spawner)
        {
            // ensure audio source
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
            // Set health to max if not already
            if (health != null)
            {
                if (health.maxHealth <= 0f) health.maxHealth = 100f;
                if (health.currentHealth <= 0f) health.currentHealth = health.maxHealth;
            }
            // Set lifetime to a reasonable default if not set (0 = infinite)
            if (this.lifetime < 0f) this.lifetime = 0f;
            spawnRoutine = StartCoroutine(SpawnLoop());
        }
    }

    void CalculateDecay()
    {
        // HP decay removed: buildings no longer lose health over time
        hpDecayPerSecond = 0f;
    }

    protected virtual void Update()
    {
        // Lifetime logic
        if (lifetime > 0f)
        {
            lifeTimer -= Time.deltaTime;
            if (lifeTimer <= 0f)
            {
                // Destroy any extra objects on despawn
                if (destroyOnDespawn != null)
                {
                    foreach (var go in destroyOnDespawn)
                    {
                        if (go != null)
                            Destroy(go);
                    }
                }
                Destroy(gameObject);
                return;
            }
        }

        // HP decay removed

        // Destroy if health is 0 (Health or UnitHealth)
        bool dead = (health != null && health.isDead) || (unitHealth != null && !unitHealth.IsAlive);
        if (dead)
        {
            // Destroy any extra objects on despawn
            if (destroyOnDespawn != null)
            {
                foreach (var go in destroyOnDespawn)
                {
                    if (go != null)
                        Destroy(go);
                }
            }
            Destroy(gameObject);
            return;
        }

        // Spawner logic
        if (buildingType == BuildingType.Spawner)
        {
            // Remove destroyed spawned units from tracking
            for (int i = activeSpawns.Count - 1; i >= 0; i--)
            {
                if (activeSpawns[i] == null)
                    activeSpawns.RemoveAt(i);
            }
            // Update UI (no lifetime shown, handled by Building)
            if (spawnTextUI != null)
            {
                string a = $"Active: {activeSpawns.Count}/{maxActive}";
                string s = (maxTotalSpawns > 0) ? $"\nLeft: {Mathf.Max(0, maxTotalSpawns - totalSpawned)}" : "";
                spawnTextUI.text = a + s;
            }
            // Don't run attack logic for spawner
            return;
        }

        // Only attack if this is a defense building
        if (buildingType != BuildingType.Defense) return;

        lastAttack += Time.deltaTime;
        if (lastAttack < attackCooldown) return;

        Unit target = FindTargetUnit();
        if (targetTextUI != null)
        {
            if (target != null)
                targetTextUI.text = $"Target: {target.gameObject.name}";
            else
                targetTextUI.text = "Target: None";
        }
        if (target != null)
        {
            // Rotate to face target
            Vector3 dir = (target.transform.position - transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion lookRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, lookRot, 360f * Time.deltaTime);
            }
            lastAttack = 0f;
            AttackTarget(target);
        }
    }

    // --- Spawner coroutine and helpers ---
    private System.Collections.IEnumerator SpawnLoop()
    {
        while (true)
        {
            if (this == null || gameObject == null) yield break;
            if (expireBySpawnCount && maxTotalSpawns > 0 && totalSpawned >= maxTotalSpawns)
            {
                ExpireSpawner();
                yield break;
            }
            for (int i = activeSpawns.Count - 1; i >= 0; i--)
                if (activeSpawns[i] == null) activeSpawns.RemoveAt(i);
            if (unitPrefab != null && activeSpawns.Count < maxActive
                && (!expireBySpawnCount || maxTotalSpawns == 0 || totalSpawned < maxTotalSpawns))
            {
                Transform spawnPoint = ChooseSpawnPoint();
                Vector3 pos = (spawnPoint != null) ? spawnPoint.position : transform.position;
                Quaternion rot = (spawnPoint != null) ? spawnPoint.rotation : transform.rotation;
                Transform facingTarget = forcedTarget;
                if (faceNearestEnemyBuilding && facingTarget == null)
                    facingTarget = FindNearestEnemyBuildingTransform();
                if (facingTarget != null)
                {
                    Vector3 dir = (facingTarget.position - pos);
                    if (dir.sqrMagnitude > 0.001f)
                        rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                }
                GameObject spawned = Instantiate(unitPrefab, pos, rot);
                if (parentSpawnedToMap)
                    spawned.transform.SetParent(null, true);
                var unitComp = spawned.GetComponent<Unit>();
                if (unitComp != null)
                {
                    unitComp.faction = this.faction;
                    unitComp.SyncAgentToStats();
                }
                activeSpawns.Add(spawned);
                totalSpawned++;
                if (spawnEffectPrefab != null) Instantiate(spawnEffectPrefab, pos, rot);
                if (spawnSound != null && audioSource != null) audioSource.PlayOneShot(spawnSound);
                if (animator != null && spawnAnimationClip != null) animator.Play(spawnAnimationClip.name);
                if (expireBySpawnCount && maxTotalSpawns > 0 && totalSpawned >= maxTotalSpawns)
                {
                    ExpireSpawner();
                    yield break;
                }
            }
            yield return new WaitForSeconds(spawnInterval);
        }
    }

    private Transform ChooseSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0) return null;
        if (randomizeSpawnPoint) return spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Length)];
        Transform t = spawnPoints[roundRobinIndex % spawnPoints.Length];
        roundRobinIndex++;
        return t;
    }

    private Transform FindNearestEnemyBuildingTransform()
    {
        Building[] allBuildings = Object.FindObjectsByType<Building>(FindObjectsSortMode.None);
        float bestDist = Mathf.Infinity;
        Building best = null;
        foreach (var b in allBuildings)
        {
            if (b == this) continue;
            if (b.faction == this.faction) continue;
            float d = Vector3.Distance(transform.position, b.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = b;
            }
        }
        return best != null ? best.transform : null;
    }

    private void ExpireSpawner()
    {
        StopAllCoroutines();
        if (this != null && gameObject != null)
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
    // removed extra brace so class continues
    }

    void AttackTarget(Unit target)
    {
        if (projectilePrefab != null && shootPoint != null)
        {
            Vector3 targetPos = target.transform.position;
            GameObject projObj = Instantiate(projectilePrefab, shootPoint.position, Quaternion.identity);
            Projectile proj = projObj.GetComponent<Projectile>();
            if (proj != null)
            {
                proj.Configure((int)attackDamage, faction.ToString(), target.transform, gameObject);
                if (attackMode == AttackMode.Direct)
                {
                    Vector3 dir = (targetPos - shootPoint.position).normalized;
                    proj.SetVelocity(dir * proj.speed);
                }
                else if (attackMode == AttackMode.Arc)
                {
                    Vector3 velocity = CalculateArcVelocity(targetPos, shootPoint.position, arcHeight);
                    proj.SetVelocity(velocity);
                }
            }
        }
        else
        {
            // fallback: instant damage
            Health h = target.GetComponent<Health>();
            if (h != null) h.TakeDamage(attackDamage);
        }
    }

    // Calculates initial velocity for an arc shot
    Vector3 CalculateArcVelocity(Vector3 target, Vector3 origin, float height)
    {
        float gravity = Physics.gravity.y;
        Vector3 displacement = target - origin;
        Vector3 displacementXZ = new Vector3(displacement.x, 0, displacement.z);

        float time = Mathf.Sqrt(-2 * height / gravity) + Mathf.Sqrt(2 * (displacement.y - height) / gravity);
        Vector3 velocityY = Vector3.up * Mathf.Sqrt(-2 * gravity * height);
        Vector3 velocityXZ = displacementXZ / time;
        return velocityXZ + velocityY;
    }

    Unit FindTargetUnit()
    {
    Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        float best = Mathf.Infinity;
        Unit bestU = null;
        foreach (var u in units)
        {
            if (u.faction == faction) continue;
            float d = Vector3.Distance(transform.position, u.transform.position);
            if (d <= attackRange && d < best)
            {
                best = d;
                bestU = u;
            }
        }
        return bestU;
    }
}