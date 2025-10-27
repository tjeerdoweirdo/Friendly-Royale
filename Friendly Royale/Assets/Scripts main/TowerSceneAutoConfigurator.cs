using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ensures towers in a freshly loaded battle scene have correct factions & owner tags
/// even when loaded from another scene (main menu) before units begin attacking.
/// Logic:
/// 1. On scene load (or Awake if already active) gather all Tower & KingTower instances.
/// 2. If ALL towers currently share the same faction (or are unassigned), assign sides based on X position.
/// 3. Apply PlayerPrefs side choice if available (LocalPlayerIsPlayer1) so local side becomes Player faction.
/// 4. Fire a global event DamageArmed to allow units to start dealing damage only after configuration.
/// 5. Optional grace frame to let health bars initialize.
/// </summary>
[DefaultExecutionOrder(-500)]
public class TowerSceneAutoConfigurator : MonoBehaviour
{
    public bool runAutomatically = true;
    public bool logDetails = true;
    [Tooltip("If true, will delay arming damage by one frame after configuration to ensure health bars are present.")]
    public bool oneFrameDelayBeforeArming = true;

    [Header("Assignment Mode")]
    [Tooltip("If true, assigns Player/Enemy per client based on which King Tower is closest to the local camera. This makes both clients feel like Player1.")]
    public bool assignByCameraProximity = true;

    public static bool DamageArmed { get; private set; } = false;
    public static System.Action OnDamageArmed; // units can subscribe if they want to wait

    private static TowerSceneAutoConfigurator _instance;
    public static TowerSceneAutoConfigurator Instance => _instance;

    private bool configured = false;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            if (logDetails) Debug.Log("[TowerSceneAutoConfigurator] Duplicate instance detected; destroying this one.");
            Destroy(this);
            return;
        }
        _instance = this;
        if (runAutomatically)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
    }

    void Start()
    {
        // If scene already loaded (additive) we can configure now
        if (runAutomatically && !configured)
        {
            ConfigureIfNeeded();
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!runAutomatically) return;
        ConfigureIfNeeded();
    }

    public void ConfigureIfNeeded()
    {
        if (configured) return;
        var towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
        if (towers == null || towers.Length == 0) return;

        // Determine if all towers share same faction value currently
        bool allSame = towers.Select(t => t.faction).Distinct().Count() <= 1;
        if (!allSame)
        {
            if (logDetails) Debug.Log("[TowerSceneAutoConfigurator] Towers already have differing factions; skipping auto assignment.");
            ArmDamage();
            configured = true;
            return;
        }

        // Identify king towers to anchor assignment
        var ordered = towers.OrderBy(t => t.transform.position.x).ToList();
        var kings = ordered.OfType<KingTower>().ToList();

        bool usedCameraMode = false;
        if (assignByCameraProximity && kings.Count >= 2)
        {
            var cam = Camera.main ?? FindFirstObjectByType<Camera>();
            if (cam != null)
            {
                // Choose nearest king to camera as Player for this client
                var nearest = kings.OrderBy(k => Vector3.Distance(k.transform.position, cam.transform.position)).First();
                var farthest = kings.OrderByDescending(k => Vector3.Distance(k.transform.position, cam.transform.position)).First();
                AssignTower(nearest, Unit.Faction.Player);
                AssignTower(farthest, Unit.Faction.Enemy);
                usedCameraMode = true;
            }
        }

        if (!usedCameraMode)
        {
            // Fallback: left/right based on PlayerPrefs (original behavior)
            int localIsPlayer1 = PlayerPrefs.GetInt("LocalPlayerIsPlayer1", 1); // default Player1
            bool localPlayerIsLeft = localIsPlayer1 == 1; // assume left side becomes Player

            if (kings.Count >= 2)
            {
                var leftKing = kings.OrderBy(k => k.transform.position.x).First();
                var rightKing = kings.OrderByDescending(k => k.transform.position.x).First();
                AssignTower(leftKing, localPlayerIsLeft ? Unit.Faction.Player : Unit.Faction.Enemy);
                AssignTower(rightKing, localPlayerIsLeft ? Unit.Faction.Enemy : Unit.Faction.Player);
            }
        }

        // Assign remaining towers based on which king they are closer to
        KingTower playerKing = towers.OfType<KingTower>().FirstOrDefault(k => k.faction == Unit.Faction.Player);
        KingTower enemyKing = towers.OfType<KingTower>().FirstOrDefault(k => k.faction == Unit.Faction.Enemy);
        foreach (var t in ordered)
        {
            if (t is KingTower) continue;
            if (playerKing != null && enemyKing != null)
            {
                float dPlayer = Vector3.Distance(t.transform.position, playerKing.transform.position);
                float dEnemy = Vector3.Distance(t.transform.position, enemyKing.transform.position);
                AssignTower(t, dPlayer <= dEnemy ? Unit.Faction.Player : Unit.Faction.Enemy);
            }
        }

        if (logDetails)
        {
            foreach (var t in ordered)
            {
                Debug.Log($"[TowerSceneAutoConfigurator] Assigned {t.towerName} faction={t.faction} ownerTag={t.ownerTag}");
            }
        }

        configured = true;
        if (oneFrameDelayBeforeArming)
            StartCoroutine(ArmDamageNextFrame());
        else
            ArmDamage();
    }

    private System.Collections.IEnumerator ArmDamageNextFrame()
    {
        yield return null; // wait one frame so health bars can catch up
        ArmDamage();
    }

    private void AssignTower(Tower t, Unit.Faction faction)
    {
        t.faction = faction;
        t.ownerTag = faction == Unit.Faction.Player ? "Player" : "Enemy";
    }

    private void ArmDamage()
    {
        if (DamageArmed) return;
        DamageArmed = true;
        if (logDetails) Debug.Log("[TowerSceneAutoConfigurator] Damage armed; towers ready to receive damage.");
        OnDamageArmed?.Invoke();
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_instance == this) _instance = null;
    }

    /// <summary>
    /// Force arm damage even if configurator failed to run (failsafe). Optionally runs configuration first if possible.
    /// </summary>
    public static void ForceArmDamage(string reason, bool configureIfNeeded = true)
    {
        if (DamageArmed) return;
        if (Instance != null && configureIfNeeded)
        {
            Instance.ConfigureIfNeeded();
        }
        DamageArmed = true;
        Debug.Log($"[TowerSceneAutoConfigurator] ForceArmDamage invoked ({reason}). DamageArmed set TRUE.");
        OnDamageArmed?.Invoke();
    }
}
