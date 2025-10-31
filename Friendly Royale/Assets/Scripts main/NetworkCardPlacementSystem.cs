using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

/// <summary>
/// Networked version of CardPlacementSystem that handles multiplayer card placement validation
/// and visual feedback with proper client-server architecture.
/// </summary>
public class NetworkCardPlacementSystem : NetworkBehaviour
{
    [Header("Mode")]
    [Tooltip("When enabled, both players use the same 'Player 1' placement rules. There is no special Player 2 zone; placement validity is symmetric.")]
    public bool useUnifiedPlacementForAllPlayers = true;
    [Header("Opponent Placement Mapping (Client-Side Spawns)")]
    [Tooltip("If enabled, applies a local mapping to the opponent's placement positions when spawning. Affects ClientRpc spawns and (optionally) adjusts authoritative spawns client-side.")]
    public bool mapOpponentPositions = true;

    public enum OpponentMappingMode { None, MirrorAcrossX0, MirrorAcrossZ0, MirrorAcrossCustomPlane }
    [Tooltip("How to map opponent positions locally on this client when spawning via ClientRpc.")]
    public OpponentMappingMode opponentMappingMode = OpponentMappingMode.MirrorAcrossZ0;

    [Tooltip("Custom plane point for MirrorAcrossCustomPlane mode (origin point on the plane). If null, world origin is used.")]
    public Transform customPlanePoint;
    [Tooltip("Custom plane normal in world space for MirrorAcrossCustomPlane mode.")]
    public Vector3 customPlaneNormal = Vector3.forward;
    [Tooltip("Optional world offset applied after mapping opponent positions.")]
    public Vector3 opponentPositionOffset = Vector3.zero;
    [Header("Placement Areas - Layer Based")]
    [Tooltip("Layer mask for friendly/player placement areas")]
    public LayerMask friendlyPlacementLayerMask = 0;
    
    [Tooltip("Layer mask for enemy placement areas (usually restricted for player)")]
    public LayerMask enemyPlacementLayerMask = 0;
    
    [Tooltip("Layer mask for non-placeable areas (blocks all card placement)")]
    public LayerMask nonPlaceableLayerMask = 0;
    
    [Header("Placement Area Lists")]
    [Tooltip("List of valid placement areas (colliders where cards can be placed)")]
    public List<Collider> validPlacementAreas = new List<Collider>();
    
    [Tooltip("List of invalid placement areas (colliders where cards cannot be placed)")]
    public List<Collider> invalidPlacementAreas = new List<Collider>();
    
    [Header("Bridge Areas")]
    [Tooltip("Bridge areas where placement might be restricted")]
    public Collider[] bridgeAreas;

    [Header("Validation Settings")]
    [Tooltip("Layer mask for ground/walkable areas")]
    public LayerMask groundLayerMask = 1;
    
    [Tooltip("Layer mask for obstacles that block placement")]
    public LayerMask obstacleLayerMask = ~0;
    
    [Tooltip("Minimum distance from towers")]
    public float minDistanceFromTowers = 3f;
    
    [Tooltip("Minimum overlap distance between buildings")]
    public float minDistanceBetweenBuildings = 1f;
    
    [Tooltip("Range restriction for player cards")]
    public float maxPlacementRange = 15f;

    [Header("Visual Feedback")]
    [Tooltip("Prefab for valid placement indicator")]
    public GameObject validPlacementIndicator;
    
    [Tooltip("Prefab for invalid placement indicator")]
    public GameObject invalidPlacementIndicator;
    
    [Tooltip("Color for valid placement")]
    public Color validColor = Color.green;
    
    [Tooltip("Color for invalid placement")]
    public Color invalidColor = Color.red;
    
    [Header("Debug")]
    [Tooltip("When enabled, logs detailed reasons for placement invalidity on client.")]
    public bool debugPlacementLogs = false;
    [Tooltip("If true, do not treat ground as non-placeable even if its layer is included in nonPlaceableLayerMask (only when no explicit valid areas configured).")]
    public bool allowGroundEvenIfMarkedNonPlaceable = true;
    
    // Telemetry for status panel: tracks last observed opponent placement on this client
    public static Vector3? LastEnemyPlacementOriginal { get; private set; }
    public static Vector3? LastEnemyPlacementMapped { get; private set; }
    public static string LastEnemyPlacementCardId { get; private set; }
    public static double LastEnemyPlacementTime { get; private set; }

    private static NetworkCardPlacementSystem instance;
    public static NetworkCardPlacementSystem Instance => instance;
    
    private GameObject currentIndicator;
    private Camera playerCamera;
    
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        playerCamera = Camera.main;
        if (playerCamera == null)
        {
            playerCamera = FindFirstObjectByType<Camera>();
        }

        // Proactive diagnostics in network scenes: ensure spawners/backbone exist
        if (IsNetworkReady())
        {
            StartCoroutine(LogSceneSpawnReadiness());
        }
    }
    
    /// <summary>
    /// Check if a position is valid for card placement for a specific player
    /// </summary>
    public bool IsValidPlacementPosition(Vector3 position, Card card, Unit.Faction playerFaction, ulong clientId)
    {
        // Server-side validation
        if (IsServer)
        {
            return ValidatePositionOnServer(position, card, playerFaction, clientId);
        }
        
        // Client-side prediction (less strict)
        return ValidatePositionOnClient(position, card, playerFaction);
    }

    // Emits helpful diagnostics shortly after start to highlight missing scene wiring in networked scenes.
    private System.Collections.IEnumerator LogSceneSpawnReadiness()
    {
        // wait a few frames for scene objects to initialize
        float t = 0f;
        while (t < 0.5f)
        {
            yield return null;
            t += Time.unscaledDeltaTime;
        }

        var spawner = FindFirstObjectByType<CardSpawner>();
        var nm = NetworkManager.Singleton;
        int ncpsSpawnedCount = 0;
        foreach (var sys in FindObjectsByType<NetworkCardPlacementSystem>(FindObjectsSortMode.None))
        {
            var no = sys.GetComponent<NetworkObject>();
            if (no != null && no.IsSpawned) ncpsSpawnedCount++;
        }

        Debug.Log($"[NCPS][Diag] NetReady={(nm!=null && nm.IsListening)} role={(IsServer?"Server":"Client")} CardSpawner={(spawner?spawner.name:"<none>")} SpawnedNCPS={ncpsSpawnedCount}");

        if (spawner == null)
        {
            Debug.LogWarning("[NCPS][Diag] No CardSpawner found in the active scene. In networked scenes, ensure a CardSpawner exists on BOTH host and client scenes. Fallback ClientRpc spawns require it.");
        }
    }
    
    private bool ValidatePositionOnServer(Vector3 position, Card card, Unit.Faction playerFaction, ulong clientId)
    {
        // Comprehensive server-side validation
        // Permissive unified-mode path: if no explicit valid areas are configured, accept ground placements unless clearly invalid.
        if (useUnifiedPlacementForAllPlayers && (validPlacementAreas == null || validPlacementAreas.Count == 0))
        {
            // 1) Reject obviously invalid areas
            if (IsPositionInInvalidArea(position))
            {
                if (debugPlacementLogs) Debug.Log("[NCPS][Server] Invalid: non-placeable area in unified fallback.");
                return false;
            }

            // 2) Ground check with overlap guard
            bool onGround = IsPositionOnGround(position);
            if (!onGround && allowGroundEvenIfMarkedNonPlaceable)
            {
                int nl = nonPlaceableLayerMask.value; int gl = groundLayerMask.value;
                if ((nl & gl) != 0)
                {
                    // Non-placeable mask includes ground; allow as configured
                    onGround = true;
                    if (debugPlacementLogs) Debug.Log("[NCPS][Server] Ground overlaps nonPlaceable; allowing due to setting.");
                }
            }

            if (!onGround && debugPlacementLogs)
            {
                Debug.LogWarning("[NCPS][Server] Ground raycast failed in unified fallback; allowing with caution.");
            }

            // 3) Proximity constraints
            if (!CheckDistanceFromTowers(position))
            {
                if (debugPlacementLogs) Debug.Log("[NCPS][Server] Invalid: too close to towers.");
                return false;
            }
            if (card.cardType == CardType.Building && !CheckDistanceFromBuildings(position))
            {
                if (debugPlacementLogs) Debug.Log("[NCPS][Server] Invalid: too close to other buildings.");
                return false;
            }

            // Accept in unified permissive mode
            return true;
        }
        
        // 1. Check if position is within allowed placement areas
        if (!IsPositionInValidArea(position, playerFaction))
        {
            return false;
        }
        
        // 2. Check if position is not in invalid areas
        if (IsPositionInInvalidArea(position))
        {
            return false;
        }
        
        // 3. Check distance from towers
        if (!CheckDistanceFromTowers(position))
        {
            return false;
        }
        
        // 4. Check distance from other buildings (for building cards)
        if (card.cardType == CardType.Building && !CheckDistanceFromBuildings(position))
        {
            return false;
        }
        
        // 5. Check if position is on ground
        if (!IsPositionOnGround(position))
        {
            return false;
        }
        
        // 6. Check faction-specific placement rules
        if (!CheckFactionPlacementRules(position, playerFaction, clientId))
        {
            return false;
        }
        
        return true;
    }
    
    private bool ValidatePositionOnClient(Vector3 position, Card card, Unit.Faction playerFaction)
    {
        // Simpler client-side validation for immediate feedback
        // If unified mode with no explicit valid areas configured, default to permissive client-side preview:
        // allow unless we are clearly in an invalid area. Server will still do full checks.
        if (useUnifiedPlacementForAllPlayers && validPlacementAreas.Count == 0)
        {
            if (IsPositionInInvalidArea(position))
            {
                if (debugPlacementLogs)
                {
                    Debug.Log("[NCPS][Client] Invalid: non-placeable area while in permissive unified mode.");
                }
                return false;
            }
            return true;
        }

        // Basic area check
        if (!IsPositionInValidArea(position, playerFaction))
        {
            if (debugPlacementLogs)
            {
                Debug.Log($"[NCPS][Client] Invalid: not in valid area. unified={useUnifiedPlacementForAllPlayers}, validAreas={validPlacementAreas?.Count ?? 0}, groundMask={groundLayerMask.value}, friendlyMask={friendlyPlacementLayerMask.value}, enemyMask={enemyPlacementLayerMask.value}");
            }
            return false;
        }
        
        // Check if not in obviously invalid areas
        if (IsPositionInInvalidArea(position))
        {
            if (debugPlacementLogs)
            {
                Debug.Log("[NCPS][Client] Invalid: hit non-placeable or invalid collider under cursor.");
            }
            return false;
        }
        
        return true;
    }
    
    private bool IsPositionInValidArea(Vector3 position, Unit.Faction playerFaction)
    {
        // Unified placement: treat both friendly and enemy layers as valid zones
        int layerMask;
        if (useUnifiedPlacementForAllPlayers)
        {
            layerMask = friendlyPlacementLayerMask | enemyPlacementLayerMask; // union
        }
        else
        {
            // Legacy: use per-faction masks
            layerMask = (playerFaction == Unit.Faction.Player) ? friendlyPlacementLayerMask : enemyPlacementLayerMask;
        }

        // 1) Layer-mask based allow
        if (layerMask != 0)
        {
            if (Physics.Raycast(position + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 20f, layerMask))
            {
                return true;
            }
        }

        // 2) In unified mode, allow ground as a valid baseline when no explicit areas are configured
        if (useUnifiedPlacementForAllPlayers && validPlacementAreas.Count == 0 && groundLayerMask != 0)
        {
            if (Physics.Raycast(position + Vector3.up * 10f, Vector3.down, out RaycastHit groundHit, 20f, groundLayerMask))
            {
                return true;
            }
        }
        
        // Check collider-based validation
        foreach (Collider validArea in validPlacementAreas)
        {
            if (validArea != null && validArea.bounds.Contains(position))
            {
                return true;
            }
        }
        
        // 3) Fallback: if nothing defined at all, allow anywhere
        return validPlacementAreas.Count == 0 && layerMask == 0;
    }
    
    private bool IsPositionInInvalidArea(Vector3 position)
    {
        // Check non-placeable layer mask
        if (nonPlaceableLayerMask != 0)
        {
            if (Physics.Raycast(position + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 20f, nonPlaceableLayerMask))
            {
                // If ground is also considered non-placeable by mask but we have no explicit valid areas,
                // optionally allow it to avoid accidentally blocking everything.
                if (allowGroundEvenIfMarkedNonPlaceable && validPlacementAreas.Count == 0)
                {
                    int nl = nonPlaceableLayerMask.value;
                    int gl = groundLayerMask.value;
                    if ((nl & gl) != 0)
                    {
                        if (debugPlacementLogs)
                        {
                            Debug.Log("[NCPS][Client] Warning: Ground layer overlaps with nonPlaceableLayerMask. Allowing ground due to setting.");
                        }
                        return false;
                    }
                }
                return true;
            }
        }
        
        // Check invalid colliders
        foreach (Collider invalidArea in invalidPlacementAreas)
        {
            if (invalidArea != null && invalidArea.bounds.Contains(position))
            {
                return true;
            }
        }
        
        return false;
    }
    
    private bool CheckDistanceFromTowers(Vector3 position)
    {
        Tower[] towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
        foreach (Tower tower in towers)
        {
            if (tower != null)
            {
                float distance = Vector3.Distance(position, tower.transform.position);
                if (distance < minDistanceFromTowers)
                {
                    return false;
                }
            }
        }
        return true;
    }
    
    private bool CheckDistanceFromBuildings(Vector3 position)
    {
        Building[] buildings = FindObjectsByType<Building>(FindObjectsSortMode.None);
        foreach (Building building in buildings)
        {
            if (building != null)
            {
                float distance = Vector3.Distance(position, building.transform.position);
                if (distance < minDistanceBetweenBuildings)
                {
                    return false;
                }
            }
        }
        return true;
    }
    
    private bool IsPositionOnGround(Vector3 position)
    {
        // Check if there's ground beneath the position
        if (Physics.Raycast(position + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 10f, groundLayerMask))
        {
            return true;
        }
        return false;
    }
    
    private bool CheckFactionPlacementRules(Vector3 position, Unit.Faction playerFaction, ulong clientId)
    {
        // Relaxed: allow placement anywhere that passes area/ground/overlap checks.
        // Server will assign ownership/faction and gameplay systems enforce behavior.
        return true;
    }
    
    /// <summary>
    /// Show visual feedback for card placement
    /// </summary>
    public void ShowPlacementPreview(Vector3 position, Card card, Unit.Faction playerFaction, bool isValid)
    {
        // Preview is strictly local UX; do not gate on network ownership
        GameObject indicatorPrefab = isValid ? validPlacementIndicator : invalidPlacementIndicator;
        Color indicatorColor = isValid ? validColor : invalidColor;

        if (indicatorPrefab == null)
        {
            // No prefab assigned, nothing to show
            if (debugPlacementLogs)
            {
                Debug.LogWarning("[NCPS] No valid/invalid placement indicator prefab assigned. Assign prefabs in inspector to see placement markers.");
            }
            return;
        }

        // If indicator not present or prefab type changed (valid/invalid), recreate
        bool needsNewInstance = currentIndicator == null ||
                                (isValid && currentIndicator.name != validPlacementIndicator.name + "(Clone)") ||
                                (!isValid && currentIndicator.name != invalidPlacementIndicator.name + "(Clone)");

        if (needsNewInstance)
        {
            if (currentIndicator != null) Destroy(currentIndicator);
            currentIndicator = Instantiate(indicatorPrefab, position, Quaternion.identity);
        }
        else
        {
            // Just move existing indicator
            currentIndicator.transform.position = position;
        }

        // Update color if renderer available
        var renderer = currentIndicator.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = indicatorColor;
        }
    }
    
    /// <summary>
    /// Hide the placement preview
    /// </summary>
    public void HidePlacementPreview()
    {
        if (currentIndicator != null)
        {
            Destroy(currentIndicator);
            currentIndicator = null;
        }
    }
    
    /// <summary>
    /// Get the world position from screen point (for drag and drop)
    /// </summary>
    public Vector3 GetWorldPositionFromScreen(Vector2 screenPosition)
    {
        if (playerCamera == null) return Vector3.zero;
        
        Ray ray = playerCamera.ScreenPointToRay(screenPosition);
        
        // Raycast against ground
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, groundLayerMask))
        {
            return hit.point;
        }
        
        // Fallback: project to y=0 plane
        float distance = -ray.origin.y / ray.direction.y;
        return ray.origin + ray.direction * distance;
    }
    
    /// <summary>
    /// Public method to request card placement in multiplayer
    /// </summary>
    public void RequestCardPlacement(Vector3 position, Card card, Unit.Faction playerFaction)
    {
        if (card == null)
        {
            Debug.LogError("[NetworkCardPlacementSystem] Cannot place card - card is null");
            return;
        }
        
        ulong clientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0UL;

        // If networking isn't running, do a direct local spawn (offline/single-player)
        if (!IsNetworkReady())
        {
            CardSpawner spawner = FindFirstObjectByType<CardSpawner>();
            if (spawner != null)
            {
                StartCoroutine(spawner.SpawnUnitAtPosition(card, position, playerFaction));
                Debug.Log($"[NetworkCardPlacementSystem] Local placement: {card.cardName} at {position}");
            }
            return;
        }

        // If we are the server/host, execute placement immediately on server to avoid RPC dependency
        if (IsServer)
        {
            Debug.Log($"[NetworkCardPlacementSystem] Server-side direct placement for {card.cardName} at {position}");
            // Mirror server RPC logic inline
            var spawner = FindFirstObjectByType<CardSpawner>();
            if (spawner == null)
            {
                Debug.LogError("[NetworkCardPlacementSystem] No CardSpawner found on server; cannot place card.");
                return;
            }
            // Server determines faction as Player for host actions
            var serverFaction = Unit.Faction.Player;
            // Basic validation using server rules
            bool isValid = ValidatePositionOnServer(position, card, serverFaction, clientId);
            if (!isValid)
            {
                Debug.LogWarning("[NetworkCardPlacementSystem] Server-side validation failed for placement.");
                return;
            }
            spawner.SpawnAuthoritative(card.cardID, position, serverFaction, clientId);
            return;
        }

        // In multiplayer client, ask the server to validate and broadcast the spawn
        if (NetworkObject == null || !NetworkObject.IsSpawned)
        {
            // Try to find a spawned instance to route the RPC through (e.g., auto-spawned by server)
            var spawned = FindSpawnedRpcInstance();
            if (spawned != null)
            {
                spawned.RequestCardPlacementServerRpc(position, card.cardID, playerFaction, clientId);
                Debug.Log($"[NetworkCardPlacementSystem] Routed placement request via spawned instance for {card.cardName} at {position}");
                return;
            }
            else
            {
                Debug.LogError("[NetworkCardPlacementSystem] Cannot send ServerRpc: no spawned NetworkCardPlacementSystem found. Ensure the server spawned one and clients received it.");
                return;
            }
        }
        RequestCardPlacementServerRpc(position, card.cardID, playerFaction, clientId);
        Debug.Log($"[NetworkCardPlacementSystem] Requested placement: {card.cardName} at {position}");
    }

    // Finds any NetworkCardPlacementSystem in the scene with a spawned NetworkObject to use for RPCs
    private NetworkCardPlacementSystem FindSpawnedRpcInstance()
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
    
    /// <summary>
    /// Check if the placement system can handle network requests
    /// </summary>
    public bool IsNetworkReady()
    {
        return NetworkManager.Singleton != null && 
               NetworkManager.Singleton.IsListening && 
               (IsClient || IsServer);
    }
    
    /// <summary>
    /// Try to get a valid placement position from a ray (compatible with CardPlacementSystem interface)
    /// </summary>
    public bool TryGetPlacementPosition(Ray ray, Card card, out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;

        // Only require a valid card; a Ray was supplied by the caller so we don't depend on an internal camera here
        if (card == null)
            return false;
        
        // Raycast against ground to get the world position
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, groundLayerMask))
        {
            worldPosition = hit.point;
        }
        else
        {
            // Fallback: project to y=0 plane so placement isn't blocked by layer misconfig
            if (Mathf.Abs(ray.direction.y) > 0.0001f)
            {
                float t = -ray.origin.y / ray.direction.y;
                if (t > 0)
                {
                    worldPosition = ray.origin + ray.direction * t;
                }
            }
        }

        // If we got a position, validate it
        if (worldPosition != Vector3.zero)
        {
            ulong clientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
            return IsValidPlacementPosition(worldPosition, card, Unit.Faction.Player, clientId);
        }

        return false;
    }
    
    /// <summary>
    /// Request placement validation from server
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ValidatePlacementServerRpc(Vector3 position, string cardID, Unit.Faction playerFaction, ulong clientId)
    {
        // Find card data
        Card card = FindCardData(cardID);
        if (card == null)
        {
            SendValidationResultClientRpc(false, "Card not found", clientId);
            return;
        }
        
        // Server determines authoritative faction based on which client placed it
        Unit.Faction serverFaction = playerFaction;
        if (NetworkManager.Singleton != null)
        {
            serverFaction = (clientId == NetworkManager.ServerClientId) ? Unit.Faction.Player : Unit.Faction.Enemy;
        }

        // Validate position
        bool isValid = IsValidPlacementPosition(position, card, serverFaction, clientId);
        string message = isValid ? "Valid placement" : "Invalid placement";
        
        SendValidationResultClientRpc(isValid, message, clientId);
    }
    
    /// <summary>
    /// Request to place a card at the specified position
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestCardPlacementServerRpc(Vector3 position, string cardID, Unit.Faction playerFaction, ulong clientId)
    {
        Debug.Log($"[NetworkCardPlacementSystem] RequestCardPlacementServerRpc invoked. IsServer={IsServer}, IsClient={IsClient}, LocalClientId={(NetworkManager.Singleton!=null?NetworkManager.Singleton.LocalClientId:0)}, placingClient={clientId}");

        // Find card data
        Card card = FindCardData(cardID);
        if (card == null)
        {
            Debug.LogError($"[NetworkCardPlacementSystem] Card not found: {cardID}");
            NotifyPlacementResultClientRpc(false, "Card not found", clientId);
            return;
        }
        
        // Compute authoritative faction from placing client
        Unit.Faction serverFaction = playerFaction;
        if (NetworkManager.Singleton != null)
        {
            serverFaction = (clientId == NetworkManager.ServerClientId) ? Unit.Faction.Player : Unit.Faction.Enemy;
        }

        // Validate the placement using server-side validation
        bool isValid = ValidatePositionOnServer(position, card, serverFaction, clientId);
        
        if (isValid)
        {
            // Additional server-side security validation
            if (NetworkSecurityManager.Instance != null)
            {
                bool securityValid = NetworkSecurityManager.Instance.ValidateCardPlay(clientId, cardID, position, playerFaction);
                if (!securityValid)
                {
                    Debug.LogWarning($"[NetworkCardPlacementSystem] Security validation failed for client {clientId}");
                    NotifyPlacementResultClientRpc(false, "Security validation failed", clientId);
                    return;
                }
            }

            // Prefer authoritative spawn via CardSpawner on the server
            var spawner = FindFirstObjectByType<CardSpawner>();
            if (spawner != null)
            {
                // We are on the server here; spawn authoritatively with the mapped faction
                if (IsServer)
                {
                    spawner.SpawnAuthoritative(cardID, position, serverFaction, clientId);
                }
                else
                {
                    spawner.RequestSpawnServerRpc(position, cardID, serverFaction, clientId);
                }
            }
            else
            {
                // Fallback to broadcast instantiate if no spawner found (should not happen)
                SpawnCardForAllClientsClientRpc(position, cardID, serverFaction, clientId);
            }

            // Notify the requesting client of success
            NotifyPlacementResultClientRpc(true, "Card placed successfully", clientId);

            Debug.Log($"[NetworkCardPlacementSystem] Successfully placed card {cardID} for client {clientId} at {position} (faction={serverFaction})");
        }
        else
        {
            // Notify client of failure
            NotifyPlacementResultClientRpc(false, "Invalid placement position", clientId);
            Debug.Log($"[NetworkCardPlacementSystem] Invalid placement for client {clientId}: {cardID} at {position}");
        }
    }
    
    [ClientRpc]
    private void SendValidationResultClientRpc(bool isValid, string message, ulong targetClientId)
    {
        // Only process on the target client
        if (NetworkManager.Singleton.LocalClientId != targetClientId) return;
        
        // Handle validation result
        Debug.Log($"Placement validation: {isValid} - {message}");
        // You can add UI feedback here
    }
    
    /// <summary>
    /// Notify client about placement result
    /// </summary>
    [ClientRpc]
    private void NotifyPlacementResultClientRpc(bool success, string message, ulong targetClientId)
    {
        // Only process on the target client
        if (NetworkManager.Singleton.LocalClientId != targetClientId) return;
        
        if (success)
        {
            Debug.Log($"[NetworkCardPlacementSystem] Card placement successful: {message}");
            // You can add success UI feedback here (particles, sound, etc.)
        }
        else
        {
            Debug.LogWarning($"[NetworkCardPlacementSystem] Card placement failed: {message}");
            // You can add failure UI feedback here (shake animation, error sound, etc.)
        }
    }
    
    /// <summary>
    /// Spawn a card for all clients (called from server)
    /// </summary>
    [ClientRpc]
    public void SpawnCardForAllClientsClientRpc(Vector3 position, string cardID, Unit.Faction serverFaction, ulong placingClientId)
    {
        // Find the card data
        Card card = FindCardData(cardID);
        if (card == null)
        {
            Debug.LogError($"[NetworkCardPlacementSystem] Cannot spawn card - card data not found: {cardID}");
            return;
        }
        
        // Find the CardSpawner to handle the actual spawning
        CardSpawner spawner = FindFirstObjectByType<CardSpawner>();
        if (spawner == null)
        {
            // CardSpawner might not yet be active on this client (scene load timing). Retry briefly.
            StartCoroutine(EnsureSpawnerThenSpawn(card, position, serverFaction, placingClientId));
            return;
        }
        
        // Determine local faction and map opponent position if configured (client-side spawn path only)
        ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0UL;
        var localFaction = (placingClientId == localId) ? Unit.Faction.Player : Unit.Faction.Enemy;
        Vector3 spawnPos = MapOpponentPositionIfNeeded(position, placingClientId);
        StartCoroutine(spawner.SpawnUnitAtPosition(card, spawnPos, localFaction));
        
    // Show visual feedback for the placement (faction local to this client)
        ShowNetworkPlacementFeedback(spawnPos, card, localFaction, placingClientId);
        
        // Telemetry: record last enemy placement for status overlay
        if (NetworkManager.Singleton != null && placingClientId != NetworkManager.Singleton.LocalClientId)
        {
            LastEnemyPlacementOriginal = position;
            LastEnemyPlacementMapped = spawnPos;
            LastEnemyPlacementCardId = cardID;
            LastEnemyPlacementTime = Time.timeAsDouble;
        }

        Debug.Log($"[NetworkCardPlacementSystem] Spawned card {cardID} for client {placingClientId} at {spawnPos}");
    }

    // Retry locating a CardSpawner on the client for a short time, then spawn.
    private System.Collections.IEnumerator EnsureSpawnerThenSpawn(Card card, Vector3 position, Unit.Faction serverFaction, ulong placingClientId)
    {
        float timeout = 2f;
        float elapsed = 0f;
        CardSpawner spawner = null;
        while (elapsed < timeout && (spawner = FindFirstObjectByType<CardSpawner>()) == null)
        {
            yield return null; // wait one frame
            elapsed += Time.unscaledDeltaTime;
        }

        if (spawner == null)
        {
            Debug.LogError("[NetworkCardPlacementSystem] Cannot spawn card - no CardSpawner found in scene after waiting. Ensure CardSpawner exists on all clients in the battle scene.");
            yield break;
        }

        ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0UL;
        var localFaction = (placingClientId == localId) ? Unit.Faction.Player : Unit.Faction.Enemy;
        Vector3 spawnPos = MapOpponentPositionIfNeeded(position, placingClientId);
        yield return spawner.SpawnUnitAtPosition(card, spawnPos, localFaction);

        ShowNetworkPlacementFeedback(spawnPos, card, localFaction, placingClientId);
        Debug.Log($"[NetworkCardPlacementSystem] Delayed spawn completed for {card.cardName} at {spawnPos}");
    }
    
    /// <summary>
    /// Show visual feedback when a card is placed by any player
    /// </summary>
    private void ShowNetworkPlacementFeedback(Vector3 position, Card card, Unit.Faction faction, ulong placingClientId)
    {
        // You can add visual effects here like:
        // - Placement particles
        // - Screen shake
        // - UI notifications showing which player placed the card
        // - Sound effects
        
        // For now, just log the placement
        string playerName = placingClientId == NetworkManager.Singleton.LocalClientId ? "You" : $"Player {placingClientId}";
        Debug.Log($"[NetworkCardPlacementSystem] {playerName} placed {card.cardName} at {position}");
        
        // TODO: Add visual/audio feedback here
    }
    
    private Card FindCardData(string cardID)
    {
        if (string.IsNullOrEmpty(cardID)) return null;
        // Prefer DeckManager's catalog when available
        if (DeckManager.Instance != null && DeckManager.Instance.allCards != null)
        {
            foreach (var c in DeckManager.Instance.allCards)
            {
                if (c != null && c.cardID == cardID)
                    return c;
            }
        }
        // Fallback: search loaded ScriptableObjects (Editor/runtime)
        var all = Resources.FindObjectsOfTypeAll<Card>();
        foreach (var c in all)
        {
            if (c != null && c.cardID == cardID)
                return c;
        }
        Debug.LogWarning($"[NetworkCardPlacementSystem] Card data not found for id '{cardID}'. Ensure it exists in DeckManager.allCards or is loadable via Resources.");
        return null;
    }

    // Maps opponent placement position locally for client-side spawns (ClientRpc path).
    // Does not affect server-authoritative NetworkObjects.
    private Vector3 MapOpponentPositionIfNeeded(Vector3 original, ulong placingClientId)
    {
        if (!mapOpponentPositions) return original;
        if (NetworkManager.Singleton == null) return original;
        // Only map positions coming from the opponent (not our own placements)
        if (placingClientId == NetworkManager.Singleton.LocalClientId) return original;

        Vector3 pos = original;
        switch (opponentMappingMode)
        {
            case OpponentMappingMode.MirrorAcrossX0:
                pos = new Vector3(-pos.x, pos.y, pos.z);
                break;
            case OpponentMappingMode.MirrorAcrossZ0:
                pos = new Vector3(pos.x, pos.y, -pos.z);
                break;
            case OpponentMappingMode.MirrorAcrossCustomPlane:
                {
                    Vector3 n = customPlaneNormal.sqrMagnitude > 1e-6f ? customPlaneNormal.normalized : Vector3.up;
                    Vector3 P = customPlanePoint ? customPlanePoint.position : Vector3.zero;
                    Vector3 v = pos - P;
                    pos = pos - 2f * Vector3.Dot(v, n) * n;
                }
                break;
            case OpponentMappingMode.None:
            default:
                break;
        }
        return pos + opponentPositionOffset;
    }

    [ClientRpc]
    public void FinalizeSpawnClientRpc(ulong networkObjectId, string cardID, int level, Vector3 worldPos, bool useLeftPath, ulong placerClientId)
    {
        if (!NetworkManager.Singleton) return;
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out var netObj)) return;
        var go = netObj.gameObject;
        var card = FindCardData(cardID);
        if (card == null) return;

        // Compute local faction relative to this client
        ulong localId = NetworkManager.Singleton.LocalClientId;
        var localFaction = (placerClientId == localId) ? Unit.Faction.Player : Unit.Faction.Enemy;

        // If mapping is enabled and this is an opponent's placement, adjust position locally
        Vector3 adjustedPos = worldPos;
        if (placerClientId != localId)
        {
            adjustedPos = MapOpponentPositionIfNeeded(worldPos, placerClientId);
            if (adjustedPos != worldPos)
            {
                // Move the object locally to the mapped position
                go.transform.position = adjustedPos;
            }
            // Telemetry: record last enemy placement
            LastEnemyPlacementOriginal = worldPos;
            LastEnemyPlacementMapped = adjustedPos;
            LastEnemyPlacementCardId = cardID;
            LastEnemyPlacementTime = Time.timeAsDouble;
        }

        // Use CardSpawner to apply configuration (paths, towers, stats)
        var spawner = FindFirstObjectByType<CardSpawner>();
        if (spawner != null)
        {
            spawner.ConfigureSpawnedObject(go, card, localFaction, level, adjustedPos, useLeftPath);
        }
    }
}