using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

/// <summary>
/// Networked version of CardPlacementSystem that handles multiplayer card placement validation
/// and visual feedback with proper client-server architecture.
/// </summary>
public class NetworkCardPlacementSystem : NetworkBehaviour
{
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
    
    private bool ValidatePositionOnServer(Vector3 position, Card card, Unit.Faction playerFaction, ulong clientId)
    {
        // Comprehensive server-side validation
        
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
        
        // Basic area check
        if (!IsPositionInValidArea(position, playerFaction))
        {
            return false;
        }
        
        // Check if not in obviously invalid areas
        if (IsPositionInInvalidArea(position))
        {
            return false;
        }
        
        return true;
    }
    
    private bool IsPositionInValidArea(Vector3 position, Unit.Faction playerFaction)
    {
        // Check layer mask based validation
        int layerMask = (playerFaction == Unit.Faction.Player) ? friendlyPlacementLayerMask : enemyPlacementLayerMask;
        
        if (layerMask != 0)
        {
            // Raycast downward to check if we're over a valid layer
            if (Physics.Raycast(position + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 20f, layerMask))
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
        
        // If no specific valid areas are defined, allow placement anywhere (fallback)
        return validPlacementAreas.Count == 0 && layerMask == 0;
    }
    
    private bool IsPositionInInvalidArea(Vector3 position)
    {
        // Check non-placeable layer mask
        if (nonPlaceableLayerMask != 0)
        {
            if (Physics.Raycast(position + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 20f, nonPlaceableLayerMask))
            {
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
            Debug.LogError("[NetworkCardPlacementSystem] Cannot send ServerRpc: this component's NetworkObject is not spawned. Ensure it has a NetworkObject and the scene is loaded by NGO.");
        }
        RequestCardPlacementServerRpc(position, card.cardID, playerFaction, clientId);
        Debug.Log($"[NetworkCardPlacementSystem] Requested placement: {card.cardName} at {position}");
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
        
        if (card == null || playerCamera == null)
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
            Debug.LogError("[NetworkCardPlacementSystem] Cannot spawn card - no CardSpawner found in scene");
            return;
        }
        
        // Spawn the card at the specified position (local faction will be computed client-side if needed)
        ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0UL;
        var localFaction = (placingClientId == localId) ? Unit.Faction.Player : Unit.Faction.Enemy;
        StartCoroutine(spawner.SpawnUnitAtPosition(card, position, localFaction));
        
    // Show visual feedback for the placement (faction local to this client)
    ShowNetworkPlacementFeedback(position, card, localFaction, placingClientId);
        
        Debug.Log($"[NetworkCardPlacementSystem] Spawned card {cardID} for client {placingClientId} at {position}");
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

        // Use CardSpawner to apply configuration (paths, towers, stats)
        var spawner = FindFirstObjectByType<CardSpawner>();
        if (spawner != null)
        {
            spawner.ConfigureSpawnedObject(go, card, localFaction, level, worldPos, useLeftPath);
        }
    }
}