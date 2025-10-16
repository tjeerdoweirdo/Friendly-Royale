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

        // Determine local side preference
        int localIsPlayer1 = PlayerPrefs.GetInt("LocalPlayerIsPlayer1", 1); // default Player1
        bool localPlayerIsLeft = localIsPlayer1 == 1; // assume left side (lowest X) becomes Player if true

        // Sort towers by X to decide sides deterministically
        var ordered = towers.OrderBy(t => t.transform.position.x).ToList();
        int half = ordered.Count / 2; // crude split; king towers may also help refine

        // Identify king towers if any to anchor sides
        var kings = ordered.OfType<KingTower>().ToList();
        if (kings.Count >= 2)
        {
            // Use minX as left king, maxX as right king for clarity
            var leftKing = kings.OrderBy(k => k.transform.position.x).First();
            var rightKing = kings.OrderByDescending(k => k.transform.position.x).First();
            AssignTower(leftKing, localPlayerIsLeft ? Unit.Faction.Player : Unit.Faction.Enemy);
            AssignTower(rightKing, localPlayerIsLeft ? Unit.Faction.Enemy : Unit.Faction.Player);
        }

        // Assign remaining towers by side of midpoint
        float midX = ordered.Average(t => t.transform.position.x);
        foreach (var t in ordered)
        {
            if (t is KingTower) continue; // already assigned above
            bool isLeft = t.transform.position.x <= midX;
            var desiredFaction = (isLeft == localPlayerIsLeft) ? Unit.Faction.Player : Unit.Faction.Enemy;
            AssignTower(t, desiredFaction);
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
