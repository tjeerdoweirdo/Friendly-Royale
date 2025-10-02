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
            playerCamera = FindObjectOfType<Camera>();
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
        Tower[] towers = FindObjectsOfType<Tower>();
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
        Building[] buildings = FindObjectsOfType<Building>();
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
        // TODO: Add faction-specific placement rules
        // For example, players can only place cards on their side of the arena
        
        // Simple rule: players can only place cards within their range
        if (playerFaction == Unit.Faction.Player)
        {
            // Find player's spawn area and check distance
            // This is a simplified check - you might want to make it more sophisticated
            return true; // Placeholder
        }
        
        return true;
    }
    
    /// <summary>
    /// Show visual feedback for card placement
    /// </summary>
    public void ShowPlacementPreview(Vector3 position, Card card, Unit.Faction playerFaction, bool isValid)
    {
        // Only show preview on the local client
        if (!IsOwner) return;
        
        if (currentIndicator != null)
        {
            Destroy(currentIndicator);
        }
        
        GameObject indicatorPrefab = isValid ? validPlacementIndicator : invalidPlacementIndicator;
        Color indicatorColor = isValid ? validColor : invalidColor;
        
        if (indicatorPrefab != null)
        {
            currentIndicator = Instantiate(indicatorPrefab, position, Quaternion.identity);
            
            // Set color
            Renderer renderer = currentIndicator.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = indicatorColor;
            }
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
        
        // Validate position
        bool isValid = IsValidPlacementPosition(position, card, playerFaction, clientId);
        string message = isValid ? "Valid placement" : "Invalid placement";
        
        SendValidationResultClientRpc(isValid, message, clientId);
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
    
    private Card FindCardData(string cardID)
    {
        // TODO: Implement proper card database lookup
        Card[] allCards = FindObjectsOfType<Card>();
        foreach (Card card in allCards)
        {
            if (card.cardID == cardID)
            {
                return card;
            }
        }
        return null;
    }
}