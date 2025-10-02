using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages card placement validation, visual feedback, and placement areas.
/// Similar to Clash Royale's arena placement system with valid/invalid zones.
/// </summary>
public class CardPlacementSystem : MonoBehaviour
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
    [Tooltip("Bridge areas where placement might be restricted (add to invalid areas list for restrictions)")]
    public Collider[] bridgeAreas;

    [Header("Validation Settings")]
    [Tooltip("Layer mask for ground/walkable areas")]
    public LayerMask groundLayerMask = 1;
    
    [Tooltip("Layer mask for obstacles that block placement")]
    public LayerMask obstacleLayerMask = ~0;
    
    [Tooltip("Minimum distance from towers")]
    public float minDistanceFromTowers = 3f;
    
    [Tooltip("Minimum overlap distance between buildings (small value to prevent stacking)")]
    public float minDistanceBetweenBuildings = 1f;
    
    [Tooltip("Range restriction for player cards (from player side)")]
    public float maxPlacementRange = 15f;

    [Header("Visual Feedback")]
    [Tooltip("Prefab for valid placement indicator")]
    public GameObject validPlacementIndicator;
    
    [Tooltip("Prefab for invalid placement indicator")]
    public GameObject invalidPlacementIndicator;
    
    [Tooltip("Prefab for range circle indicator")]
    public GameObject rangeCircleIndicator;
    
    [Tooltip("Material for valid placement area overlay")]
    public Material validAreaMaterial;
    
    [Tooltip("Material for invalid placement area overlay")]
    public Material invalidAreaMaterial;



    // Visual feedback objects
    private GameObject currentPlacementIndicator;
    private GameObject currentRangeCircle;
    private List<GameObject> activeAreaOverlays = new List<GameObject>();
    
    // Current placement state
    private Card currentCard;
    private bool isPlacingCard = false;
    
    // References
    private Camera mainCamera;
    private CardSpawner cardSpawner;
    private LayerMask combinedObstacleMask;



    void Awake()
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            mainCamera = FindFirstObjectByType<Camera>();
        }
        
        cardSpawner = FindFirstObjectByType<CardSpawner>();
        combinedObstacleMask = obstacleLayerMask | (1 << LayerMask.NameToLayer("Water"));
        
        // Create default placement areas if no areas are set up
        if (validPlacementAreas.Count == 0 && friendlyPlacementLayerMask == 0)
        {
            CreateDefaultPlacementAreas();
        }
    }

    void Start()
    {
        // Initialize visual elements
        SetupVisualFeedback();
    }

    /// <summary>
    /// Called when a card starts being dragged
    /// </summary>
    public void BeginCardPlacement(Card card)
    {
        currentCard = card;
        isPlacingCard = true;
        
        ShowPlacementAreas(card);
        CreateRangeIndicator(card);
        
        Debug.Log($"[CardPlacementSystem] Beginning placement for {card.cardName} - indicators will lock to placeable areas only");
    }

    /// <summary>
    /// Called when card dragging ends
    /// </summary>
    public void EndCardPlacement()
    {
        currentCard = null;
        isPlacingCard = false;
        
        HidePlacementAreas();
        DestroyRangeIndicator();
        HidePlacementIndicator();
        
        Debug.Log("[CardPlacementSystem] Ended card placement - clearing all indicators");
    }

    /// <summary>
    /// Updates the placement preview at the given world position
    /// </summary>
    public void UpdatePlacementPreview(Vector3 worldPosition, bool isValid)
    {
        if (!isPlacingCard) return;
        
        // Check if position is on non-placeable layer - hide indicator if so
        bool isOnNonPlaceableLayer = IsInInvalidPlacementArea(worldPosition);
        
        if (isOnNonPlaceableLayer)
        {
            // Hide all indicators when on non-placeable areas
            HidePlacementIndicator();
            HideRangeIndicator();
            return;
        }
        
        // Show placement indicator when back on placeable ground
        ShowPlacementIndicator(worldPosition, isValid);
        
        // Show range circle for ranged units/buildings when back on placeable ground
        if (currentCard != null && (currentCard.baseRange > 1.5f || currentCard.cardType == CardType.Building))
        {
            // Make sure range circle is created if it doesn't exist
            if (currentRangeCircle == null)
            {
                CreateRangeIndicator(currentCard);
            }
            UpdateRangeIndicator(worldPosition);
        }
    }

    /// <summary>
    /// Attempts to get a placement position from a screen ray (locks to placeable areas only)
    /// </summary>
    public bool TryGetPlacementPosition(Ray ray, Card card, out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;
        
        // First try friendly placement layers (highest priority for locking)
        if (friendlyPlacementLayerMask != 0)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, friendlyPlacementLayerMask))
            {
                worldPosition = hit.point;
                return true;
            }
        }
        
        // Then try ground layers (but only if they're not non-placeable)
        if (Physics.Raycast(ray, out RaycastHit groundHit, Mathf.Infinity, groundLayerMask))
        {
            // Check if this ground position is on a non-placeable or enemy layer
            Vector3 testPos = groundHit.point;
            
            // Skip if it's on non-placeable layer
            if (nonPlaceableLayerMask != 0 && Physics.CheckSphere(testPos, 0.1f, nonPlaceableLayerMask))
            {
                return false; // Don't return position on non-placeable areas
            }
            
            // Skip if it's on enemy layer
            if (enemyPlacementLayerMask != 0 && Physics.CheckSphere(testPos, 0.1f, enemyPlacementLayerMask))
            {
                return false; // Don't return position on enemy areas
            }
            
            worldPosition = testPos;
            return true;
        }
        
        // Try valid placement area colliders
        foreach (Collider validArea in validPlacementAreas)
        {
            if (validArea != null && validArea.Raycast(ray, out RaycastHit areaHit, Mathf.Infinity))
            {
                worldPosition = areaHit.point;
                return true;
            }
        }
        
        return false; // No valid position found
    }
    
    /// <summary>
    /// Get position from ray and check if it's valid (separate methods)
    /// </summary>
    public bool TryGetValidPlacementPosition(Ray ray, Card card, out Vector3 worldPosition, out bool isValid)
    {
        isValid = false;
        worldPosition = Vector3.zero;
        
        // Try to get a position (this now locks to placeable areas only)
        if (TryGetPlacementPosition(ray, card, out worldPosition))
        {
            // Check if we should show indicator at this position
            if (!ShouldShowIndicatorAt(worldPosition))
            {
                return false; // Don't show anything on non-placeable areas
            }
            
            isValid = IsValidPlacementPosition(worldPosition, card);
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// Checks if a position is valid for placing the given card
    /// </summary>
    public bool IsValidPlacementPosition(Vector3 position, Card card)
    {
        if (card == null) return false;
        
        // FIRST: Check invalid placement areas (highest priority - overrides everything)
        if (IsInInvalidPlacementArea(position))
        {
            return false;
        }
        
        // Check if position is in player's placement area
        if (!IsInPlayerPlacementArea(position))
        {
            return false;
        }
        
        // Check range restrictions
        if (!IsWithinPlacementRange(position))
        {
            return false;
        }
        
        // Check for obstacles
        if (HasObstaclesAt(position, card))
        {
            return false;
        }
        
        // Check minimum distance from towers
        if (!IsValidDistanceFromTowers(position))
        {
            return false;
        }
        
        // Building-specific checks (only check for overlaps)
        if (card.cardType == CardType.Building)
        {
            return IsValidBuildingPlacement(position, card);
        }
        
        // Troop-specific checks
        if (card.cardType == CardType.Troop)
        {
            return IsValidTroopPlacement(position, card);
        }
        
        // Spell-specific checks
        if (card.cardType == CardType.Spell)
        {
            return IsValidSpellPlacement(position, card);
        }
        
        return true;
    }



    /// <summary>
    /// Check if position is in an invalid placement area
    /// </summary>
    private bool IsInInvalidPlacementArea(Vector3 position)
    {
        // Check non-placeable layer mask first (highest priority)
        if (nonPlaceableLayerMask != 0)
        {
            // Use sphere check for immediate detection
            if (Physics.CheckSphere(position, 0.1f, nonPlaceableLayerMask))
            {
                return true;
            }
            
            // Also raycast down to check surface
            Vector3 rayStart = position + Vector3.up * 1f;
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 2f, nonPlaceableLayerMask))
            {
                return true;
            }
        }
        
        // Check enemy placement layers (restricted for player)
        if (enemyPlacementLayerMask != 0)
        {
            if (Physics.CheckSphere(position, 0.1f, enemyPlacementLayerMask))
            {
                return true;
            }
            
            Vector3 rayStart = position + Vector3.up * 1f;
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 2f, enemyPlacementLayerMask))
            {
                return true;
            }
        }
        
        // Check invalid placement areas list
        foreach (Collider invalidArea in invalidPlacementAreas)
        {
            if (invalidArea != null && IsPositionInCollider(position, invalidArea))
            {
                Debug.Log($"[CardPlacementSystem] Position {position} is in invalid area: {invalidArea.name}");
                return true;
            }
        }
        
        // Check bridge areas if they should be invalid (legacy support)
        if (bridgeAreas != null)
        {
            foreach (Collider bridge in bridgeAreas)
            {
                if (bridge != null && IsPositionInCollider(position, bridge))
                {
                    Debug.Log($"[CardPlacementSystem] Position {position} is on bridge: {bridge.name}");
                    // For now, bridges are not automatically invalid - depends on your game design
                    // You can add bridges to invalidPlacementAreas list if needed
                }
            }
        }
        
        return false;
    }
    
    private bool IsInPlayerPlacementArea(Vector3 position)
    {
        bool foundValidArea = false;
        
        // Check layer-based placement areas
        if (friendlyPlacementLayerMask != 0)
        {
            // Raycast down to check if we're on a friendly placement layer
            Vector3 rayStart = position + Vector3.up * 2f;
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 5f, friendlyPlacementLayerMask))
            {
                Debug.Log($"[CardPlacementSystem] Position {position} is on friendly layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)}");
                foundValidArea = true;
            }
            
            // Also check with sphere for better detection
            if (!foundValidArea && Physics.CheckSphere(position, 0.2f, friendlyPlacementLayerMask))
            {
                Debug.Log($"[CardPlacementSystem] Position {position} is in friendly area (sphere check)");
                foundValidArea = true;
            }
        }
        
        // Check valid placement areas list
        if (!foundValidArea)
        {
            foreach (Collider validArea in validPlacementAreas)
            {
                if (validArea != null && IsPositionInCollider(position, validArea))
                {
                    Debug.Log($"[CardPlacementSystem] Position {position} is in valid area: {validArea.name}");
                    foundValidArea = true;
                    break;
                }
            }
        }
        
        // If any restrictions are defined, require explicit valid placement
        bool hasRestrictions = (nonPlaceableLayerMask != 0) || (enemyPlacementLayerMask != 0) || 
                              (friendlyPlacementLayerMask != 0) || (validPlacementAreas.Count > 0) || 
                              (invalidPlacementAreas.Count > 0);
        
        if (hasRestrictions && !foundValidArea)
        {
            Debug.Log($"[CardPlacementSystem] Position {position} REJECTED - not in any valid placement area");
            return false;
        }
        
        // Only allow placement everywhere if NO restrictions are defined at all
        if (!hasRestrictions)
        {
            Debug.Log($"[CardPlacementSystem] Position {position} allowed - no restrictions defined");
            return true;
        }
        
        return foundValidArea;
    }

    private bool IsWithinPlacementRange(Vector3 position)
    {
        // Find player's base/king tower
        Tower playerTower = FindPlayerKingTower();
        if (playerTower == null) return true; // No range restriction if no tower found
        
        // Add some buffer to the range to avoid edge cases
        float distance = Vector3.Distance(position, playerTower.transform.position);
        return distance <= (maxPlacementRange + 1f); // Add 1 unit buffer
    }

    private bool HasObstaclesAt(Vector3 position, Card card)
    {
        // Check for obstacles in a small radius around the position
        float checkRadius = card.cardType == CardType.Building ? 1.2f : 0.3f; // Smaller radius to avoid false positives
        
        Collider[] obstacles = Physics.OverlapSphere(position, checkRadius, combinedObstacleMask);
        
        // Filter out ground/placement surfaces
        foreach (Collider obstacle in obstacles)
        {
            // Skip if it's a placement area or ground
            if (validPlacementAreas.Contains(obstacle)) continue;
            if ((groundLayerMask.value & (1 << obstacle.gameObject.layer)) != 0) continue;
            if ((friendlyPlacementLayerMask.value & (1 << obstacle.gameObject.layer)) != 0) continue;
            
            // Found a real obstacle
            return true;
        }
        
        return false;
    }

    private bool IsValidDistanceFromTowers(Vector3 position)
    {
        Tower[] towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
        foreach (Tower tower in towers)
        {
            float distance = Vector3.Distance(position, tower.transform.position);
            if (distance < minDistanceFromTowers)
            {
                return false;
            }
        }
        return true;
    }

    private bool IsValidBuildingPlacement(Vector3 position, Card card)
    {
        // Only check for overlapping with other buildings - no spot restrictions
        Building[] buildings = FindObjectsByType<Building>(FindObjectsSortMode.None);
        foreach (Building building in buildings)
        {
            float distance = Vector3.Distance(position, building.transform.position);
            // Use a small overlap distance - just prevent direct overlap
            float overlapDistance = 1.0f;
            if (distance < overlapDistance)
            {
                return false;
            }
        }
        
        // Buildings can be placed anywhere valid - no spot restrictions
        return true;
    }

    private bool IsValidTroopPlacement(Vector3 position, Card card)
    {
        // Troops follow the same validation as other cards
        // All restrictions are handled by the main validation system
        return true;
    }

    private bool IsValidSpellPlacement(Vector3 position, Card card)
    {
        // Spells follow the same validation as other cards
        // All restrictions are handled by the main validation system
        return true;
    }

    private void ShowPlacementAreas(Card card)
    {
        // Show valid placement areas
        foreach (Collider validArea in validPlacementAreas)
        {
            if (validArea != null)
            {
                GameObject overlay = CreateAreaOverlay(validArea, validAreaMaterial);
                if (overlay != null)
                {
                    activeAreaOverlays.Add(overlay);
                }
            }
        }
        
        // Show invalid placement areas
        foreach (Collider invalidArea in invalidPlacementAreas)
        {
            if (invalidArea != null)
            {
                GameObject restrictedOverlay = CreateAreaOverlay(invalidArea, invalidAreaMaterial);
                if (restrictedOverlay != null)
                {
                    activeAreaOverlays.Add(restrictedOverlay);
                }
            }
        }
        

    }

    private void HidePlacementAreas()
    {
        foreach (GameObject overlay in activeAreaOverlays)
        {
            if (overlay != null)
            {
                Destroy(overlay);
            }
        }
        activeAreaOverlays.Clear();
    }

    private void ShowPlacementIndicator(Vector3 position, bool isValid)
    {
        // Remove old indicator
        HidePlacementIndicator();
        
        // Create new indicator
        GameObject prefab = isValid ? validPlacementIndicator : invalidPlacementIndicator;
        if (prefab != null)
        {
            currentPlacementIndicator = Instantiate(prefab, position, Quaternion.identity);
            
            // Position flat on the ground
            Vector3 pos = currentPlacementIndicator.transform.position;
            pos.y = 0.01f; // Just barely above ground to prevent z-fighting
            currentPlacementIndicator.transform.position = pos;
        }
    }

    private void HidePlacementIndicator()
    {
        if (currentPlacementIndicator != null)
        {
            Destroy(currentPlacementIndicator);
            currentPlacementIndicator = null;
        }
    }

    private void CreateRangeIndicator(Card card)
    {
        if (rangeCircleIndicator == null) return;
        if (card.baseRange <= 1.5f && card.cardType != CardType.Building) return;
        
        currentRangeCircle = Instantiate(rangeCircleIndicator);
        
        // Scale the range circle based on card's range
        float range = card.cardType == CardType.Building ? card.defenseAttackRange : card.baseRange;
        currentRangeCircle.transform.localScale = Vector3.one * range * 2f; // Diameter
        
        // Initially hide it
        currentRangeCircle.SetActive(false);
    }

    private void UpdateRangeIndicator(Vector3 position)
    {
        if (currentRangeCircle != null)
        {
            // Make the range indicator completely flat on the ground
            Vector3 flatPosition = position;
            flatPosition.y = 0f;
            currentRangeCircle.transform.position = flatPosition;
            currentRangeCircle.SetActive(true);
        }
    }

    private void HideRangeIndicator()
    {
        if (currentRangeCircle != null)
        {
            currentRangeCircle.SetActive(false);
        }
    }

    private void DestroyRangeIndicator()
    {
        if (currentRangeCircle != null)
        {
            Destroy(currentRangeCircle);
            currentRangeCircle = null;
        }
    }

    private GameObject CreateAreaOverlay(Collider area, Material material)
    {
        if (area == null || material == null) return null;
        
        // Create a simple quad overlay
        GameObject overlay = GameObject.CreatePrimitive(PrimitiveType.Quad);
        overlay.name = "PlacementAreaOverlay";
        
        // Remove collider (we don't need it for visual overlay)
        Destroy(overlay.GetComponent<Collider>());
        
        // Set material
        Renderer renderer = overlay.GetComponent<Renderer>();
        renderer.material = material;
        
        // Position and scale to match area bounds
        Bounds bounds = area.bounds;
        overlay.transform.position = new Vector3(bounds.center.x, bounds.min.y + 0.01f, bounds.center.z);
        overlay.transform.rotation = Quaternion.Euler(90, 0, 0); // Flat on ground
        overlay.transform.localScale = new Vector3(bounds.size.x, bounds.size.z, 1);
        
        return overlay;
    }

    private void CreateDefaultPlacementAreas()
    {
        // Create default player placement area
        GameObject playerArea = new GameObject("PlayerPlacementArea");
        playerArea.transform.SetParent(transform);
        
        BoxCollider playerCollider = playerArea.AddComponent<BoxCollider>();
        playerCollider.isTrigger = true;
        playerCollider.size = new Vector3(20f, 2f, 10f);
        playerCollider.center = new Vector3(0f, 1f, -5f); // Player side
        
        // Add to valid placement areas list
        validPlacementAreas.Add(playerCollider);
        
        Debug.Log("[CardPlacementSystem] Created default placement areas and added to valid list");
    }

    private void SetupVisualFeedback()
    {
        // Create default indicators if not assigned
        if (validPlacementIndicator == null)
        {
            validPlacementIndicator = CreateDefaultIndicator(Color.green);
        }
        
        if (invalidPlacementIndicator == null)
        {
            invalidPlacementIndicator = CreateDefaultIndicator(Color.red);
        }
        
        if (rangeCircleIndicator == null)
        {
            rangeCircleIndicator = CreateDefaultRangeCircle();
        }
    }

    private GameObject CreateDefaultIndicator(Color color)
    {
        GameObject indicator = new GameObject("PlacementIndicator");
        
        // Create a flat circle using a very thin cylinder
        GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.transform.SetParent(indicator.transform);
        cylinder.transform.localScale = new Vector3(2f, 0.01f, 2f); // Much thinner
        cylinder.transform.localPosition = Vector3.zero;
        
        // Remove collider
        Destroy(cylinder.GetComponent<Collider>());
        
        // Set color
        Renderer renderer = cylinder.GetComponent<Renderer>();
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = color;
        mat.SetFloat("_Mode", 2); // Fade mode
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
        
        Color transparentColor = color;
        transparentColor.a = 0.5f;
        mat.color = transparentColor;
        
        renderer.material = mat;
        
        // Don't save in scene
        indicator.hideFlags = HideFlags.DontSave;
        
        return indicator;
    }

    private GameObject CreateDefaultRangeCircle()
    {
        GameObject circle = new GameObject("RangeCircle");
        
        // Create a flat quad for the range circle
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.transform.SetParent(circle.transform);
        quad.transform.localPosition = Vector3.zero;
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // Rotate to lay flat
        quad.transform.localScale = Vector3.one; // Will be scaled by parent
        
        // Remove collider
        Destroy(quad.GetComponent<Collider>());
        
        // Create material for the circle
        Material rangeMat = new Material(Shader.Find("Sprites/Default"));
        rangeMat.color = new Color(1f, 1f, 0f, 0.3f); // Yellow with transparency
        rangeMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        rangeMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        rangeMat.SetInt("_ZWrite", 0);
        rangeMat.DisableKeyword("_ALPHATEST_ON");
        rangeMat.EnableKeyword("_ALPHABLEND_ON");
        rangeMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        rangeMat.renderQueue = 3000;
        
        // Apply material
        Renderer renderer = quad.GetComponent<Renderer>();
        renderer.material = rangeMat;
        
        circle.hideFlags = HideFlags.DontSave;
        return circle;
    }

    private Tower FindPlayerKingTower()
    {
        Tower[] towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
        foreach (Tower tower in towers)
        {
            // Assuming player towers have a specific faction or name pattern
            UnitHealth health = tower.GetComponent<UnitHealth>();
            if (health != null && health.GetComponent<Unit>()?.faction == Unit.Faction.Player)
            {
                return tower;
            }
        }
        
        // Fallback - find CardSpawner and use its player king tower reference
        if (cardSpawner != null && cardSpawner.playerKingTower != null)
        {
            return cardSpawner.playerKingTower;
        }
        
        return null;
    }
    
    /// <summary>
    /// Add a collider to the valid placement areas list
    /// </summary>
    public void AddValidPlacementArea(Collider area)
    {
        if (area != null && !validPlacementAreas.Contains(area))
        {
            validPlacementAreas.Add(area);
            Debug.Log($"[CardPlacementSystem] Added valid placement area: {area.name}");
        }
    }
    
    /// <summary>
    /// Add a collider to the invalid placement areas list
    /// </summary>
    public void AddInvalidPlacementArea(Collider area)
    {
        if (area != null && !invalidPlacementAreas.Contains(area))
        {
            invalidPlacementAreas.Add(area);
            Debug.Log($"[CardPlacementSystem] Added invalid placement area: {area.name}");
        }
    }
    
    /// <summary>
    /// Remove a collider from valid placement areas list
    /// </summary>
    public void RemoveValidPlacementArea(Collider area)
    {
        if (validPlacementAreas.Remove(area))
        {
            Debug.Log($"[CardPlacementSystem] Removed valid placement area: {area.name}");
        }
    }
    
    /// <summary>
    /// Remove a collider from invalid placement areas list
    /// </summary>
    public void RemoveInvalidPlacementArea(Collider area)
    {
        if (invalidPlacementAreas.Remove(area))
        {
            Debug.Log($"[CardPlacementSystem] Removed invalid placement area: {area.name}");
        }
    }
    
    /// <summary>
    /// Clear all placement areas
    /// </summary>
    public void ClearAllPlacementAreas()
    {
        validPlacementAreas.Clear();
        invalidPlacementAreas.Clear();
        Debug.Log("[CardPlacementSystem] Cleared all placement areas");
    }
    
    /// <summary>
    /// Check if a position is on a specific layer mask
    /// </summary>
    public bool IsPositionOnLayer(Vector3 position, LayerMask layerMask)
    {
        if (layerMask == 0) return false;
        
        // Raycast down to check the layer
        if (Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 1f, layerMask))
        {
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Check if a position is on a non-placeable layer
    /// </summary>
    public bool IsPositionOnNonPlaceableLayer(Vector3 position)
    {
        return IsPositionOnLayer(position, nonPlaceableLayerMask);
    }
    
    /// <summary>
    /// Set the non-placeable layer mask
    /// </summary>
    public void SetNonPlaceableLayerMask(LayerMask layerMask)
    {
        nonPlaceableLayerMask = layerMask;
        Debug.Log($"[CardPlacementSystem] Set non-placeable layer mask: {LayerMaskToString(layerMask)}");
    }
    
    /// <summary>
    /// Check if we should show placement indicator at this position
    /// </summary>
    public bool ShouldShowIndicatorAt(Vector3 position)
    {
        // Don't show indicator on non-placeable layers
        if (nonPlaceableLayerMask != 0 && Physics.CheckSphere(position, 0.1f, nonPlaceableLayerMask))
        {
            return false;
        }
        
        // Don't show indicator on enemy layers
        if (enemyPlacementLayerMask != 0 && Physics.CheckSphere(position, 0.1f, enemyPlacementLayerMask))
        {
            return false;
        }
        
        // Show indicator everywhere else
        return true;
    }
    
    /// <summary>
    /// Debug method to check why a position might be invalid
    /// </summary>
    public void DebugPlacementPosition(Vector3 position, Card card)
    {
        Debug.Log($"\n=== DEBUGGING PLACEMENT FOR POSITION {position} ===");
        Debug.Log($"Card: {(card != null ? card.cardName : "NULL")}");
        
        Debug.Log($"Should show indicator: {ShouldShowIndicatorAt(position)}");
        Debug.Log($"Is in invalid area: {IsInInvalidPlacementArea(position)}");
        Debug.Log($"Is in player area: {IsInPlayerPlacementArea(position)}");
        Debug.Log($"Is within range: {IsWithinPlacementRange(position)}");
        Debug.Log($"Has obstacles: {HasObstaclesAt(position, card)}");
        Debug.Log($"Valid distance from towers: {IsValidDistanceFromTowers(position)}");
        
        Debug.Log($"Final validation result: {IsValidPlacementPosition(position, card)}");
        Debug.Log($"================================\n");
    }
    
    /// <summary>
    /// Get info about current placement setup
    /// </summary>
    public string GetPlacementInfo()
    {
        return $"Placement Areas Info:\n" +
               $"Valid Areas: {validPlacementAreas.Count}\n" +
               $"Invalid Areas: {invalidPlacementAreas.Count}\n" +
               $"Friendly Layer Mask: {LayerMaskToString(friendlyPlacementLayerMask)}\n" +
               $"Enemy Layer Mask: {LayerMaskToString(enemyPlacementLayerMask)}\n" +
               $"Non-Placeable Layer Mask: {LayerMaskToString(nonPlaceableLayerMask)}";
    }
    
    /// <summary>
    /// Check if a position is inside a collider (more accurate than bounds.Contains)
    /// </summary>
    private bool IsPositionInCollider(Vector3 position, Collider collider)
    {
        if (collider == null) return false;
        
        // Use ClosestPoint for more accurate detection
        Vector3 closestPoint = collider.ClosestPoint(position);
        float distance = Vector3.Distance(position, closestPoint);
        
        // If distance is very small, we're inside or very close to the collider
        return distance < 0.1f;
    }
    
    /// <summary>
    /// Add multiple bridges at once
    /// </summary>
    public void SetBridgeAreas(params Collider[] bridges)
    {
        bridgeAreas = bridges;
        Debug.Log($"[CardPlacementSystem] Set {bridges.Length} bridge areas");
    }
    
    /// <summary>
    /// Convert LayerMask to readable string
    /// </summary>
    private string LayerMaskToString(LayerMask layerMask)
    {
        if (layerMask == 0) return "None";
        
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < 32; i++)
        {
            if ((layerMask.value & (1 << i)) != 0)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(LayerMask.LayerToName(i));
            }
        }
        return sb.Length > 0 ? sb.ToString() : "None";
    }

    /// <summary>
    /// Debug method to visualize placement areas in scene view
    /// </summary>
    void OnDrawGizmosSelected()
    {
        // Draw valid placement areas in green
        Gizmos.color = Color.green;
        foreach (Collider validArea in validPlacementAreas)
        {
            if (validArea != null)
            {
                Gizmos.DrawWireCube(validArea.bounds.center, validArea.bounds.size);
            }
        }
        
        // Draw invalid placement areas in red
        Gizmos.color = Color.red;
        foreach (Collider invalidArea in invalidPlacementAreas)
        {
            if (invalidArea != null)
            {
                Gizmos.DrawWireCube(invalidArea.bounds.center, invalidArea.bounds.size);
            }
        }
        

        
        // Draw layer mask info as text (if in editor)
        #if UNITY_EDITOR
        if (friendlyPlacementLayerMask != 0)
        {
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2f, 
                $"Friendly Layers: {LayerMaskToString(friendlyPlacementLayerMask)}");
        }
        if (enemyPlacementLayerMask != 0)
        {
            UnityEditor.Handles.Label(transform.position + Vector3.up * 3f, 
                $"Enemy Layers: {LayerMaskToString(enemyPlacementLayerMask)}");
        }
        if (nonPlaceableLayerMask != 0)
        {
            UnityEditor.Handles.Label(transform.position + Vector3.up * 4f, 
                $"Non-Placeable Layers: {LayerMaskToString(nonPlaceableLayerMask)}");
        }
        #endif
    }
}