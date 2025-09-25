using UnityEngine;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine.AI;
using UnityEngine.UI;

/// <summary>
/// Spawns units from cards into left/right lanes for both Player and Enemy factions.
/// Enhanced to try wiring ranged/projectile settings from Card (if present) and ensure agent sync.
/// Also assigns endTargetTower (king towers) as final destination.
/// Provides a small UI flow to choose left/right spawn after selecting a card.
/// </summary>
public class CardSpawner : MonoBehaviour
{
    [Header("Spawn Points")]
    public Transform leftLaneSpawnPlayer;
    public Transform rightLaneSpawnPlayer;
    public Transform leftLaneSpawnEnemy;
    public Transform rightLaneSpawnEnemy;

    [Header("Special Building Spawn Points")]
    public Transform specialBuildingSpawn1;
    public Transform specialBuildingSpawn2;
    // Tracks which building occupies each special spawn (null if empty)
    [HideInInspector]
    public GameObject[] specialBuildingOccupants = new GameObject[2];

    /// <summary>
    /// Call this to free a special building spot when a building is destroyed.
    /// </summary>
    public void FreeSpecialBuildingSpot(int spotIndex, GameObject building)
    {
        if (spotIndex >= 0 && spotIndex < specialBuildingOccupants.Length)
        {
            // Only clear if the occupant matches
            if (specialBuildingOccupants[spotIndex] == building)
            {
                specialBuildingOccupants[spotIndex] = null;
            }
        }
    }

    [Header("Lane Paths (Waypoints)")]
    public Transform[] leftPathPlayer;   // assign path waypoints in inspector
    public Transform[] rightPathPlayer;
    public Transform[] leftPathEnemy;
    public Transform[] rightPathEnemy;

    [Header("King Towers (end of path)")]
    [Tooltip("The player's king tower (enemy units will target this).")]
    public Tower playerKingTower;
    [Tooltip("The enemy's king tower (player units will target this).")]
    public Tower enemyKingTower;

    [Header("Placement")]
    public float playRange = 20f; // restrict placement if needed (optional)

    [Header("Optional Spawn-Choice UI (assign to enable)")]
    [Tooltip("Panel/GameObject that contains left/right buttons (will be enabled/disabled by spawner).")]
    public GameObject spawnChoicePanel;
    public Button leftSpawnButton;   // assign UI Button for left
    public Button rightSpawnButton;  // assign UI Button for right

    // pending selection state
    Card pendingCard;
    Unit.Faction pendingFaction;
    bool panelVisible = false;

    #region Reflection helper
    bool TryGetCardValue<T>(object card, string name, out T value)
    {
        value = default;
        if (card == null) return false;

        Type t = card.GetType();

        FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null)
        {
            object raw = f.GetValue(card);
            if (raw is T typed) { value = typed; return true; }
            try { value = (T)Convert.ChangeType(raw, typeof(T)); return true; } catch { }
        }

        PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null)
        {
            object raw = p.GetValue(card, null);
            if (raw is T typed2) { value = typed2; return true; }
            try { value = (T)Convert.ChangeType(raw, typeof(T)); return true; } catch { }
        }

        return false;
    }
    #endregion

    void Awake()
    {
        // ensure UI panel starts hidden
        if (spawnChoicePanel != null) spawnChoicePanel.SetActive(false);

        // wire up buttons safely (remove old listeners)
        if (leftSpawnButton != null)
        {
            leftSpawnButton.onClick.RemoveAllListeners();
            leftSpawnButton.onClick.AddListener(OnLeftSpawnClicked);
        }
        if (rightSpawnButton != null)
        {
            rightSpawnButton.onClick.RemoveAllListeners();
            rightSpawnButton.onClick.AddListener(OnRightSpawnClicked);
        }
        // Cancel button removed
    }

    /// <summary>
    /// Public call: show the left/right spawn choice UI for the selected card.
    /// If no UI is assigned, will spawn immediately (left by default).
    /// </summary>
    public void ShowSpawnChoice(Card card, Unit.Faction faction)
    {
        if (card == null) return;

        // store pending
        pendingCard = card;
        pendingFaction = faction;

        if (spawnChoicePanel == null || leftSpawnButton == null || rightSpawnButton == null)
        {
            // fallback: if no UI assigned, spawn immediately on left side
            SpawnOnSideImmediate(true, pendingCard, pendingFaction);
            ClearPending();
            return;
        }

        // show UI
        spawnChoicePanel.SetActive(true);
        panelVisible = true;
        // buttons are already wired to call OnLeftSpawnClicked / OnRightSpawnClicked
    }

    void HideSpawnChoice()
    {
        if (spawnChoicePanel != null) spawnChoicePanel.SetActive(false);
        panelVisible = false;
        ClearPending();
    }

    void ClearPending()
    {
        pendingCard = null;
    }

    void OnLeftSpawnClicked()
    {
        if (pendingCard == null) { HideSpawnChoice(); return; }
        StartCoroutine(SpawnUnitFromCard(pendingCard, GetSpawnPointPosition(pendingFaction, true), pendingFaction));
        HideSpawnChoice();
    }

    void OnRightSpawnClicked()
    {
        if (pendingCard == null) { HideSpawnChoice(); return; }
        StartCoroutine(SpawnUnitFromCard(pendingCard, GetSpawnPointPosition(pendingFaction, false), pendingFaction));
        HideSpawnChoice();
    }

    /// <summary>
    /// Convenience: spawn immediately on a given side without showing UI.
    /// </summary>
    public void SpawnOnSideImmediate(bool leftSide, Card card, Unit.Faction faction)
    {
        if (card == null) return;
        StartCoroutine(SpawnUnitFromCard(card, GetSpawnPointPosition(faction, leftSide), faction));
    }

    /// <summary>
    /// Overload: spawn immediately with explicit level (for enemy bots).
    /// </summary>
    public void SpawnOnSideImmediate(bool leftSide, Card card, Unit.Faction faction, int level)
    {
        if (card == null) return;
        StartCoroutine(SpawnUnitFromCard(card, GetSpawnPointPosition(faction, leftSide), faction, level));
    }

    /// <summary>
    /// Returns a sensible worldPos used by the existing SpawnUnitFromCard logic
    /// so it picks left/right lane. Uses configured spawn points.
    /// </summary>
    Vector3 GetSpawnPointPosition(Unit.Faction faction, bool leftSide)
    {
        if (faction == Unit.Faction.Player)
        {
            if (leftSide && leftLaneSpawnPlayer != null) return leftLaneSpawnPlayer.position;
            if (!leftSide && rightLaneSpawnPlayer != null) return rightLaneSpawnPlayer.position;
        }
        else
        {
            if (leftSide && leftLaneSpawnEnemy != null) return leftLaneSpawnEnemy.position;
            if (!leftSide && rightLaneSpawnEnemy != null) return rightLaneSpawnEnemy.position;
        }

        // fallback to Vector3.zero if spawn points missing
        return Vector3.zero;
    }

    /// <summary>
    /// Spawn a unit or building based on the card at a world position.
    /// </summary>
    public IEnumerator SpawnUnitFromCard(Card card, Vector3 worldPos, Unit.Faction faction)
    {
        // Default to PlayerProgress if no explicit level provided
        return SpawnUnitFromCard(card, worldPos, faction, -1);
    }

    /// <summary>
    /// Overload: spawn with explicit level (for bots).
    /// </summary>
    public IEnumerator SpawnUnitFromCard(Card card, Vector3 worldPos, Unit.Faction faction, int levelOverride)
    {
        if (card == null)
            yield break;

        // Determine level: use override if provided, else PlayerProgress
        int level = levelOverride > 0 ? levelOverride : 1;
        if (levelOverride <= 0)
        {
            string arenaID = "default";
            if (DeckManager.Instance != null && DeckManager.Instance.selectedArena != null)
                arenaID = DeckManager.Instance.selectedArena.arenaID;
            if (PlayerProgress.Instance != null)
            {
                level = PlayerProgress.Instance.GetCardLevel(card.cardID, arenaID);
                if (level < 1) level = 1;
            }
        }

        // --- BUILDING CASE ---
        if (card.cardType == CardType.Building)
        {
            // Remove restriction: allow placement even if both spots are occupied
            int spotIndex = 0;
            Transform spawnT = specialBuildingSpawn1;
            // Alternate between spawn points for new buildings
            if (specialBuildingSpawn2 != null && (specialBuildingOccupants[0] != null)) {
                spotIndex = 1;
                spawnT = specialBuildingSpawn2;
            }
            if (spawnT == null)
            {
                Debug.LogWarning($"Special building spawn point {spotIndex + 1} is not assigned.");
                yield break;
            }

            GameObject go = Instantiate(card.unitPrefab, spawnT.position, spawnT.rotation);
            // Track only the first two buildings for compatibility, but do not restrict placement
            if (spotIndex < specialBuildingOccupants.Length)
                specialBuildingOccupants[spotIndex] = go;

            Building building = go.GetComponent<Building>();
            if (building != null)
            {
                building.faction = faction;
                building.buildingType = (Building.BuildingType)card.buildingType;

                // Defensive building stats
                if (building.buildingType == Building.BuildingType.Defense)
                {
                    building.attackRange = card.defenseAttackRange;
                    building.attackDamage = card.defenseAttackDamage;
                    building.attackCooldown = card.defenseAttackCooldown;
                }
                // Spawner building stats
                else if (building.buildingType == Building.BuildingType.Spawner)
                {
                    building.unitPrefab = card.spawnUnitPrefab;
                    building.spawnInterval = card.spawnInterval;
                }
            }

            yield return null;
            yield break;
        }

        // --- TROOP / UNIT CASE ---
        if (card.unitPrefab == null)
        {
            Debug.LogWarning("[CardSpawner] Card has no unitPrefab assigned for troop spawn.");
            yield break;
        }

        // Decide lane by worldPos.x (you used this pattern before)
        bool left = worldPos.x < 0f;
        Transform spawnPoint = null;
        Transform[] path = null;

        if (faction == Unit.Faction.Player)
        {
            spawnPoint = left ? leftLaneSpawnPlayer : rightLaneSpawnPlayer;
            path = left ? leftPathPlayer : rightPathPlayer;
        }
        else
        {
            spawnPoint = left ? leftLaneSpawnEnemy : rightLaneSpawnEnemy;
            path = left ? leftPathEnemy : rightPathEnemy;
        }

        if (spawnPoint == null)
        {
            Debug.LogWarning("[CardSpawner] Missing spawn point assignment for troop spawn.");
            yield break;
        }

        if (path == null || path.Length == 0)
        {
            Debug.LogWarning("[CardSpawner] Missing path assignment for troop spawn.");
            // continue - it's possible a unit will target the king tower directly
        }

        GameObject troopGo = Instantiate(card.unitPrefab, spawnPoint.position, Quaternion.identity);

        Unit unit = troopGo.GetComponent<Unit>();
        UnitHealth healthTroop = troopGo.GetComponent<UnitHealth>();

        if (unit != null)
        {
            unit.faction = faction;
            unit.path = path;
        }

        // Apply level multipliers and health
        float multiplier = 1f + 0.10f * (level - 1); // +10% per level

        if (healthTroop != null)
        {
            healthTroop.maxHealth = Mathf.RoundToInt(card.GetHealthForLevel(level));
            healthTroop.currentHealth = healthTroop.maxHealth;
            healthTroop.cardLevel = level;
        }

        if (unit != null)
        {
            unit.moveSpeed = card.baseSpeed * multiplier;
            unit.attackDamage = Mathf.RoundToInt(card.baseDamage * multiplier);
            unit.attackRange = card.baseRange;
            unit.attackCooldown = card.baseAttackCooldown;

            // reflection-based optional wiring (single place)
            if (TryGetCardValue<bool>(card, "isRanged", out bool isRangedVal)) unit.isRanged = isRangedVal;
            if (TryGetCardValue<GameObject>(card, "projectilePrefab", out GameObject projPrefabVal)) unit.projectilePrefab = projPrefabVal;

            // projectileSpeed might be float/double/int depending on serialization - try common types
            if (TryGetCardValue<float>(card, "projectileSpeed", out float projSpeedVal))
            {
                unit.projectileSpeed = projSpeedVal;
            }
            else if (TryGetCardValue<double>(card, "projectileSpeed", out double projSpeedDoubleVal))
            {
                unit.projectileSpeed = (float)projSpeedDoubleVal;
            }
            else if (TryGetCardValue<int>(card, "projectileSpeed", out int projSpeedIntVal))
            {
                unit.projectileSpeed = (float)projSpeedIntVal;
            }

            if (TryGetCardValue<string>(card, "firePointName", out string firePointNameVal) && !string.IsNullOrEmpty(firePointNameVal))
            {
                Transform child = troopGo.transform.Find(firePointNameVal);
                if (child != null) unit.firePoint = child;
            }

            // Attempt to find reasonable default firepoint if ranged and none assigned
            if (unit.isRanged && unit.firePoint == null)
            {
                Transform fp = troopGo.transform.Find("FirePoint") ?? troopGo.transform.Find("Muzzle") ?? troopGo.transform.Find("firePoint");
                if (fp != null) unit.firePoint = fp;
            }

            // assign endTargetTower: units of Player faction should target enemyKingTower, and enemy units target playerKingTower
            unit.endTargetTower = (faction == Unit.Faction.Player) ? enemyKingTower : playerKingTower;

            // Ensure NavMeshAgent / internal agent syncs with stats and starts moving
            unit.SyncAgentToStats();

            if (unit.agent != null)
            {
                if (unit.path != null && unit.path.Length > 0 && unit.path[0] != null)
                {
                    unit.agent.SetDestination(unit.path[0].position);
                }
                else if (unit.endTargetTower != null)
                {
                    unit.agent.SetDestination(unit.endTargetTower.transform.position);
                }
            }
        }

        yield return null;
    }
}
