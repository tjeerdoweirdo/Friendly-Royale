using UnityEngine;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine.AI;
using UnityEngine.UI;
using Unity.Netcode;
using System.Linq;

/// <summary>
/// Spawns units from cards at specified positions for both Player and Enemy factions with optional network support.
/// Handles troop spawning, building placement, and swarm formations.
/// Units are assigned paths and target towers automatically.
/// </summary>
public class CardSpawner : NetworkBehaviour
{
    [Header("Spawn Points")]
    public Transform leftLaneSpawnPlayer;
    public Transform rightLaneSpawnPlayer;
    public Transform leftLaneSpawnEnemy;
    public Transform rightLaneSpawnEnemy;

    [Header("Network Settings")]
    [Tooltip("Enable networking for card spawning")]
    public bool enableNetworking = true;
    [Tooltip("If true, the server will broadcast a ClientRpc for clients to spawn locally instead of spawning a shared NetworkObject. This makes placement work even if scenes/prefabs differ.")]
    public bool alwaysClientSideSpawn = false;
    
    // Network state
    private bool isNetworkEnabled => enableNetworking && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

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

    // Spawn choice UI removed - drag-and-drop system handles all placement

    // CardSpawner now has integrated network functionality

    // --- Networked Spawn Path (ServerRpc -> ClientRpc) ---
    bool IsNetworkReady()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    void OnEnable()
    {
        // After towers are configured, ensure we bind our king tower references based on faction
        TowerSceneAutoConfigurator.OnDamageArmed += AutoAssignKingTowers;
    }

    void OnDisable()
    {
        TowerSceneAutoConfigurator.OnDamageArmed -= AutoAssignKingTowers;
    }

    private void AutoAssignKingTowers()
    {
        try
        {
            var kings = FindObjectsByType<KingTower>(FindObjectsSortMode.None);
            if (kings == null || kings.Length == 0) return;
            foreach (var k in kings)
            {
                if (k == null) continue;
                if (k.faction == Unit.Faction.Player)
                {
                    playerKingTower = k;
                }
                else if (k.faction == Unit.Faction.Enemy)
                {
                    enemyKingTower = k;
                }
            }
            if (playerKingTower == null || enemyKingTower == null)
            {
                Debug.LogWarning($"[CardSpawner] AutoAssignKingTowers: incomplete assignment (player={playerKingTower}, enemy={enemyKingTower}). Ensure TowerSceneAutoConfigurator ran.");
            }
            else
            {
                Debug.Log($"[CardSpawner] AutoAssignKingTowers: player='{playerKingTower.towerName}' enemy='{enemyKingTower.towerName}'");
            }
        }
        catch { /* ignore */ }
    }

    Card FindCardById(string cardID)
    {
        if (string.IsNullOrEmpty(cardID)) return null;
        // Prefer DeckManager's catalog
        if (DeckManager.Instance != null && DeckManager.Instance.allCards != null)
        {
            var found = DeckManager.Instance.allCards.FirstOrDefault(c => c != null && c.cardID == cardID);
            if (found != null) return found;
        }
        // Fallback: find any loaded ScriptableObjects (editor/runtime)
        var all = Resources.FindObjectsOfTypeAll<Card>();
        foreach (var c in all)
        {
            if (c != null && c.cardID == cardID) return c;
        }
        return null;
    }

    // Public server-side entry point usable by other server RPCs to avoid nested ServerRpc calls
    public void SpawnAuthoritative(string cardID, Vector3 position, Unit.Faction faction, ulong clientId)
    {
        Debug.Log($"[CardSpawner] SpawnAuthoritative called for cardID='{cardID}' pos={position} faction={faction} client={clientId}");
        var card = FindCardById(cardID);
        if (card == null)
        {
            Debug.LogWarning($"[CardSpawner] SpawnAuthoritative: card not found '{cardID}' from client {clientId}");
            return;
        }

        // Prefer authoritative spawning if prefab has a NetworkObject; otherwise, fall back to broadcast instantiate.
    var prefabHasNetObj = card.unitPrefab != null && card.unitPrefab.GetComponent<NetworkObject>() != null;
    Debug.Log($"[CardSpawner] SpawnAuthoritative: card '{card.cardName}' found; prefab={(card.unitPrefab!=null?card.unitPrefab.name:"null")}; prefabHasNetObj={prefabHasNetObj}; networkReady={IsNetworkReady()}");
    if (prefabHasNetObj && IsNetworkReady() && !alwaysClientSideSpawn)
        {
            // Ensure the prefab is registered so NGO can spawn it
            try { NetworkManager.Singleton.AddNetworkPrefab(card.unitPrefab); } catch { /* ignore duplicates */ }

            // Compute lane/paths info
            bool useLeftPath = position.x < 0f;
            int level = CalculateCardLevel(card);

            if (card.cardType == CardType.Building)
            {
                var go = Instantiate(card.unitPrefab, position, Quaternion.identity);
                var net = go.GetComponent<NetworkObject>();
                Debug.Log($"[CardSpawner] SpawnAuthoritative spawning building '{go.name}' netObj={(net!=null)}");
                net.Spawn();
                // Server config uses server-side faction for gameplay; clients will re-map via placement system RPC
                ConfigureSpawnedObject(go, card, faction, level, position, useLeftPath);
                var ncps = FindSpawnedPlacementSystem();
                if (ncps != null)
                {
                    ncps.FinalizeSpawnClientRpc(net.NetworkObjectId, cardID, level, position, useLeftPath, clientId);
                }
                else
                {
                    Debug.LogError("[CardSpawner] No spawned NetworkCardPlacementSystem found to finalize spawn.");
                }
            }
            else
            {
                // Troop (supports swarm)
                Vector3[] spawnPositions = GetSwarmPositions(position, card);
                foreach (var pos in spawnPositions)
                {
                    bool left = pos.x < 0f;
                    var go = Instantiate(card.unitPrefab, pos, Quaternion.identity);
                    var net = go.GetComponent<NetworkObject>();
                    Debug.Log($"[CardSpawner] SpawnAuthoritative spawning troop '{go.name}' at {pos} netObj={(net!=null)}");
                    net.Spawn();
                    // Server config uses server-side faction for gameplay; clients will re-map via placement system RPC
                    ConfigureSpawnedObject(go, card, faction, level, pos, left);
                    var ncps = FindSpawnedPlacementSystem();
                    if (ncps != null)
                    {
                        ncps.FinalizeSpawnClientRpc(net.NetworkObjectId, cardID, level, pos, left, clientId);
                    }
                    else
                    {
                        Debug.LogError("[CardSpawner] No spawned NetworkCardPlacementSystem found to finalize spawn.");
                    }
                }
            }
        }
        else
        {
            // Fallback to broadcast instantiate via placement system (works even if this spawner isn't a networked object)
            string reason = !IsNetworkReady() ? "network not ready" : (!prefabHasNetObj ? "prefab missing NetworkObject" : (alwaysClientSideSpawn ? "alwaysClientSideSpawn=true" : "unknown"));
            Debug.Log($"[CardSpawner] SpawnAuthoritative fallback to placement system ClientRpc for cardID='{cardID}' (reason: {reason})");
            var ncps = FindSpawnedPlacementSystem();
            if (ncps != null && ncps.IsServer)
            {
                ncps.SpawnCardForAllClientsClientRpc(position, cardID, faction, clientId);
            }
            else
            {
                Debug.LogError("[CardSpawner] No spawned NetworkCardPlacementSystem available to broadcast client spawns.");
            }
        }
    }

    private NetworkCardPlacementSystem FindSpawnedPlacementSystem()
    {
        var all = FindObjectsByType<NetworkCardPlacementSystem>(FindObjectsSortMode.None);
        foreach (var sys in all)
        {
            if (sys == null) continue;
            var no = sys.GetComponent<NetworkObject>();
            if (no != null && no.IsSpawned)
            {
                return sys;
            }
        }
        return null;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestSpawnServerRpc(Vector3 position, string cardID, Unit.Faction faction, ulong clientId)
    {
        // Delegate to the authoritative server-side method
        SpawnAuthoritative(cardID, position, faction, clientId);
    }

    [ClientRpc]
    void SpawnClientRpc(Vector3 position, string cardID, ulong placerClientId)
    {
        Debug.Log($"[CardSpawner] SpawnClientRpc invoked for cardID='{cardID}' pos={position} placer={placerClientId}");
        var card = FindCardById(cardID);
        if (card == null)
        {
            Debug.LogWarning($"[CardSpawner] SpawnClientRpc: card not found '{cardID}'");
            return;
        }
        // Compute local faction based on who placed it
        ulong localId = Unity.Netcode.NetworkManager.Singleton != null ? Unity.Netcode.NetworkManager.Singleton.LocalClientId : 0UL;
        var localFaction = (placerClientId == localId) ? Unit.Faction.Player : Unit.Faction.Enemy;
        StartCoroutine(SpawnUnitAtPosition(card, position, localFaction));
    }

    int CalculateCardLevel(Card card)
    {
        int level = 1;
        string arenaID = "default";
        if (DeckManager.Instance != null && DeckManager.Instance.selectedArena != null)
        {
            arenaID = DeckManager.Instance.selectedArena.arenaID;
        }
        else if (DeckManager.Instance != null && !string.IsNullOrEmpty(DeckManager.Instance.selectedArenaID))
        {
            arenaID = DeckManager.Instance.selectedArenaID;
        }
        else
        {
            var sceneArena = FindFirstObjectByType<Arena>();
            if (sceneArena != null) arenaID = sceneArena.arenaID;
        }
        if (PlayerProgress.Instance != null && !string.IsNullOrEmpty(card.cardID))
        {
            level = PlayerProgress.Instance.GetCardLevel(card.cardID, arenaID);
            if (level < 1) level = 1;
        }
        return level;
    }

    public void ConfigureSpawnedObject(GameObject go, Card card, Unit.Faction faction, int level, Vector3 worldPos, bool useLeftPath)
    {
        if (card.cardType == CardType.Building)
        {
            var b = go.GetComponent<Building>();
            if (b != null)
            {
                b.faction = faction;
                b.buildingType = (Building.BuildingType)card.buildingType;
                if (b.buildingType == Building.BuildingType.Defense)
                {
                    b.attackRange = card.defenseAttackRange;
                    b.attackDamage = card.defenseAttackDamage;
                    b.attackCooldown = card.defenseAttackCooldown;
                }
                else if (b.buildingType == Building.BuildingType.Spawner)
                {
                    b.unitPrefab = card.spawnUnitPrefab;
                    b.spawnInterval = card.spawnInterval;
                }
            }
            return;
        }

        if (card.unitPrefab == null) return;

        Transform[] leftPathToUse = (faction == Unit.Faction.Player) ? leftPathPlayer : leftPathEnemy;
        Transform[] rightPathToUse = (faction == Unit.Faction.Player) ? rightPathPlayer : rightPathEnemy;

        var unit = go.GetComponent<Unit>();
        var hp = go.GetComponent<UnitHealth>();

        if (unit != null)
        {
            unit.faction = faction;
            unit.SetBothPaths(leftPathToUse, rightPathToUse, useLeftPath);
        }

        float multiplier = 1f + 0.10f * (level - 1);
        if (hp != null)
        {
            hp.maxHealth = Mathf.RoundToInt(card.GetHealthForLevel(level));
            hp.currentHealth = hp.maxHealth;
            hp.cardLevel = level;
        }

        if (unit != null)
        {
            unit.moveSpeed = card.baseSpeed * multiplier;
            unit.attackDamage = Mathf.RoundToInt(card.baseDamage * multiplier);
            unit.attackRange = card.baseRange;
            unit.attackCooldown = card.baseAttackCooldown;

            if (TryGetCardValue<bool>(card, "isRanged", out bool isRangedVal)) unit.isRanged = isRangedVal;
            if (TryGetCardValue<GameObject>(card, "projectilePrefab", out GameObject projPrefabVal)) unit.projectilePrefab = projPrefabVal;
            if (TryGetCardValue<float>(card, "projectileSpeed", out float projSpeedVal)) unit.projectileSpeed = projSpeedVal;
            else if (TryGetCardValue<double>(card, "projectileSpeed", out double projSpeedDoubleVal)) unit.projectileSpeed = (float)projSpeedDoubleVal;
            else if (TryGetCardValue<int>(card, "projectileSpeed", out int projSpeedIntVal)) unit.projectileSpeed = (float)projSpeedIntVal;

            if (TryGetCardValue<string>(card, "firePointName", out string firePointNameVal) && !string.IsNullOrEmpty(firePointNameVal))
            {
                Transform child = go.transform.Find(firePointNameVal);
                if (child != null) unit.firePoint = child;
            }
            if (unit.isRanged && unit.firePoint == null)
            {
                Transform fp = go.transform.Find("FirePoint") ?? go.transform.Find("Muzzle") ?? go.transform.Find("firePoint");
                if (fp != null) unit.firePoint = fp;
            }

            unit.endTargetTower = (faction == Unit.Faction.Player) ? enemyKingTower : playerKingTower;
            unit.SyncAgentToStats();
            if (unit.agent != null)
            {
                if (unit.path != null && unit.path.Length > 0 && unit.path[0] != null)
                    unit.agent.SetDestination(unit.path[0].position);
                else if (unit.endTargetTower != null)
                    unit.agent.SetDestination(unit.endTargetTower.transform.position);
            }
        }
    }

    

    // Reflection helper for card properties
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




    // Spawn choice UI methods removed - drag-and-drop system handles all placement

    /// <summary>
    /// Legacy method: spawn on a side (for backward compatibility with AI/bots).
    /// </summary>
    public void SpawnOnSideImmediate(bool leftSide, Card card, Unit.Faction faction)
    {
        if (card == null) return;
        Vector3 spawnPos = GetSpawnPointPosition(faction, leftSide);
        StartCoroutine(SpawnUnitAtPosition(card, spawnPos, faction));
    }

    /// <summary>
    /// Legacy method: spawn on a side with explicit level (for enemy bots).
    /// </summary>
    public void SpawnOnSideImmediate(bool leftSide, Card card, Unit.Faction faction, int level)
    {
        if (card == null) return;
        Vector3 spawnPos = GetSpawnPointPosition(faction, leftSide);
        StartCoroutine(SpawnUnitAtPosition(card, spawnPos, faction, level));
    }

    /// <summary>
    /// Legacy method: spawn on a side with explicit level and position (for enemy bots with position control).
    /// </summary>
    public void SpawnOnSideImmediate(bool leftSide, Card card, Unit.Faction faction, int level, Vector3 position)
    {
        if (card == null) return;
        // Use provided position instead of default spawn point
        StartCoroutine(SpawnUnitAtPosition(card, position, faction, level));
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
    /// Spawn a unit or building at an arbitrary world position (for drag-and-drop placement).
    /// This version doesn't use lane restrictions and places exactly where specified.
    /// </summary>
    public IEnumerator SpawnUnitAtPosition(Card card, Vector3 worldPos, Unit.Faction faction, int levelOverride = -1)
    {
        if (card == null)
        {
            Debug.LogWarning("[CardSpawner] SpawnUnitAtPosition called with null card");
            yield break;
        }

        Debug.Log($"[CardSpawner] SpawnUnitAtPosition called for '{card.cardName}' at {worldPos} faction={faction} levelOverride={levelOverride} isNetworkEnabled={isNetworkEnabled}");

        // Handle network spawning if enabled
        if (isNetworkEnabled)
        {
            // For now, just proceed with local spawning
            // TODO: Add proper network spawning logic
            Debug.Log("[CardSpawner] Network spawning not yet implemented - using local spawning");
        }

        // Determine level: use override if provided, else PlayerProgress
        int level = levelOverride > 0 ? levelOverride : 1;
        if (levelOverride <= 0)
        {
            string arenaID = "default";
            
            // Try multiple sources for arena ID
            if (DeckManager.Instance != null && DeckManager.Instance.selectedArena != null)
            {
                arenaID = DeckManager.Instance.selectedArena.arenaID;
                Debug.Log($"[CardSpawner] Using arena ID '{arenaID}' from DeckManager.selectedArena");
            }
            else if (DeckManager.Instance != null && !string.IsNullOrEmpty(DeckManager.Instance.selectedArenaID))
            {
                arenaID = DeckManager.Instance.selectedArenaID;
                Debug.Log($"[CardSpawner] Using arena ID '{arenaID}' from DeckManager.selectedArenaID");
            }
            else
            {
                // Try to find arena in scene as fallback
                Arena sceneArena = FindFirstObjectByType<Arena>();
                if (sceneArena != null)
                {
                    arenaID = sceneArena.arenaID;
                    Debug.Log($"[CardSpawner] Using arena ID '{arenaID}' from scene Arena component");
                }
                else
                {
                    Debug.LogWarning($"[CardSpawner] No arena found - using default arena ID '{arenaID}'");
                }
            }
            
            if (PlayerProgress.Instance != null)
            {
                if (string.IsNullOrEmpty(card.cardID))
                {
                    Debug.LogWarning($"[CardSpawner] Card '{card.cardName}' has null or empty cardID - using default level 1");
                }
                else
                {
                    level = PlayerProgress.Instance.GetCardLevel(card.cardID, arenaID);
                    if (level < 1) level = 1;
                    Debug.Log($"[CardSpawner] Card '{card.cardName}' (ID: '{card.cardID}') spawning at level {level} for arena '{arenaID}'");
                }
            }
            else
            {
                Debug.LogWarning("[CardSpawner] PlayerProgress.Instance is null - using default level 1");
            }
        }

        // --- BUILDING CASE ---
        if (card.cardType == CardType.Building)
        {
            if (card.unitPrefab == null)
            {
                Debug.LogWarning($"[CardSpawner] Cannot spawn building '{card.cardName}' - unitPrefab is null");
                yield break;
            }

            GameObject go = Instantiate(card.unitPrefab, worldPos, Quaternion.identity);
            Debug.Log($"[CardSpawner] Instantiated building prefab '{(go!=null?go.name:"null")}' at {worldPos}");
            
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

        // For position-based spawning, choose the nearest appropriate path
        bool useLeftPath = worldPos.x < 0f; // Simple heuristic based on x position
        Transform[] pathToUse = null;
        
        if (faction == Unit.Faction.Player)
        {
            pathToUse = useLeftPath ? leftPathPlayer : rightPathPlayer;
        }
        else
        {
            pathToUse = useLeftPath ? leftPathEnemy : rightPathEnemy;
        }

        // Handle swarm spawning or single unit spawning
        Vector3[] spawnPositions = GetSwarmPositions(worldPos, card);
        int unitsToSpawn = card.isSwarm ? card.swarmCount : 1;

        for (int i = 0; i < unitsToSpawn && i < spawnPositions.Length; i++)
        {
            if (card.unitPrefab == null)
            {
                Debug.LogWarning($"[CardSpawner] Cannot spawn troop for '{card.cardName}' - unitPrefab is null");
                yield break;
            }

            GameObject troopGo = Instantiate(card.unitPrefab, spawnPositions[i], Quaternion.identity);
            Debug.Log($"[CardSpawner] Instantiated troop prefab '{(troopGo!=null?troopGo.name:"null")}' at {spawnPositions[i]}");

            Unit unit = troopGo.GetComponent<Unit>();
            UnitHealth healthTroop = troopGo.GetComponent<UnitHealth>();

            if (unit != null)
            {
                unit.faction = faction;
                
                // Set path if available, but don't require it for position-based spawning
                if (pathToUse != null && pathToUse.Length > 0)
                {
                    Transform[] leftPathToUse = (faction == Unit.Faction.Player) ? leftPathPlayer : leftPathEnemy;
                    Transform[] rightPathToUse = (faction == Unit.Faction.Player) ? rightPathPlayer : rightPathEnemy;
                    unit.SetBothPaths(leftPathToUse, rightPathToUse, useLeftPath);
                }
                else
                {
                    // No paths available - unit will target king tower directly
                    Debug.Log($"[CardSpawner] No paths available for {card.cardName}, unit will target end tower directly");
                }
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

            // Small delay between spawning each unit in the swarm to avoid overlapping spawn effects
            if (i < unitsToSpawn - 1)
                yield return new WaitForSeconds(0.1f);
        }

        yield return null;
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
            
            // Try multiple sources for arena ID
            if (DeckManager.Instance != null && DeckManager.Instance.selectedArena != null)
            {
                arenaID = DeckManager.Instance.selectedArena.arenaID;
                Debug.Log($"[CardSpawner] Using arena ID '{arenaID}' from DeckManager.selectedArena");
            }
            else if (DeckManager.Instance != null && !string.IsNullOrEmpty(DeckManager.Instance.selectedArenaID))
            {
                arenaID = DeckManager.Instance.selectedArenaID;
                Debug.Log($"[CardSpawner] Using arena ID '{arenaID}' from DeckManager.selectedArenaID");
            }
            else
            {
                // Try to find arena in scene as fallback
                Arena sceneArena = FindFirstObjectByType<Arena>();
                if (sceneArena != null)
                {
                    arenaID = sceneArena.arenaID;
                    Debug.Log($"[CardSpawner] Using arena ID '{arenaID}' from scene Arena component");
                }
                else
                {
                    Debug.LogWarning($"[CardSpawner] No arena found - using default arena ID '{arenaID}'");
                }
            }
            
            if (PlayerProgress.Instance != null)
            {
                if (string.IsNullOrEmpty(card.cardID))
                {
                    Debug.LogWarning($"[CardSpawner] Card '{card.cardName}' has null or empty cardID - using default level 1");
                }
                else
                {
                    level = PlayerProgress.Instance.GetCardLevel(card.cardID, arenaID);
                    if (level < 1) level = 1;
                    Debug.Log($"[CardSpawner] Card '{card.cardName}' (ID: '{card.cardID}') spawning at level {level} for arena '{arenaID}'");
                }
            }
            else
            {
                Debug.LogWarning("[CardSpawner] PlayerProgress.Instance is null - using default level 1");
            }
        }

        // --- BUILDING CASE ---
        if (card.cardType == CardType.Building)
        {
            GameObject go = Instantiate(card.unitPrefab, worldPos, Quaternion.identity);

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

        if (faction == Unit.Faction.Player)
        {
            spawnPoint = left ? leftLaneSpawnPlayer : rightLaneSpawnPlayer;
        }
        else
        {
            spawnPoint = left ? leftLaneSpawnEnemy : rightLaneSpawnEnemy;
        }

        if (spawnPoint == null)
        {
            Debug.LogWarning("[CardSpawner] Missing spawn point assignment for troop spawn.");
            yield break;
        }

        // Validate that we have both path arrays for dynamic switching
        Transform[] leftPathToUse = (faction == Unit.Faction.Player) ? leftPathPlayer : leftPathEnemy;
        Transform[] rightPathToUse = (faction == Unit.Faction.Player) ? rightPathPlayer : rightPathEnemy;
        
        if ((leftPathToUse == null || leftPathToUse.Length == 0) && (rightPathToUse == null || rightPathToUse.Length == 0))
        {
            Debug.LogWarning("[CardSpawner] Missing both left and right path assignments for troop spawn.");
            // continue - unit will target the king tower directly
        }

        // Handle swarm spawning or single unit spawning
        Vector3[] spawnPositions = GetSwarmPositions(spawnPoint.position, card);
        int unitsToSpawn = card.isSwarm ? card.swarmCount : 1;

        for (int i = 0; i < unitsToSpawn && i < spawnPositions.Length; i++)
        {
            GameObject troopGo = Instantiate(card.unitPrefab, spawnPositions[i], Quaternion.identity);

            Unit unit = troopGo.GetComponent<Unit>();
            UnitHealth healthTroop = troopGo.GetComponent<UnitHealth>();

            if (unit != null)
            {
                unit.faction = faction;
                
                // Set both paths for dynamic switching, starting with the originally chosen one
                unit.SetBothPaths(leftPathToUse, rightPathToUse, left);
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

            // Small delay between spawning each unit in the swarm to avoid overlapping spawn effects
            if (i < unitsToSpawn - 1)
                yield return new WaitForSeconds(0.1f);
        }

        yield return null;
    }

    /// <summary>
    /// Calculates spawn positions for swarm units in a circle formation
    /// </summary>
    private Vector3[] GetSwarmPositions(Vector3 centerPos, Card card)
    {
        if (!card.isSwarm || card.swarmCount <= 1)
            return new Vector3[] { centerPos };

        return GetCirclePositions(centerPos, card.swarmCount, card.swarmSpacing);
    }

    private Vector3[] GetCirclePositions(Vector3 center, int count, float spacing)
    {
        Vector3[] positions = new Vector3[count];
        if (count == 1)
        {
            positions[0] = center;
            return positions;
        }

        for (int i = 0; i < count; i++)
        {
            float angle = (2f * Mathf.PI * i) / count;
            Vector3 offset = new Vector3(Mathf.Cos(angle) * spacing, 0, Mathf.Sin(angle) * spacing);
            positions[i] = center + offset;
        }
        return positions;
    }













    void Start()
    {
        // Ensure king tower references match local factions
        AutoAssignKingTowers();
        // CardSpawner initialized
    }
}
