using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Setup guide and validator for the new drag-and-drop card placement system.
/// This script helps ensure all components are properly configured and provides
/// automatic setup for the drag-and-drop card system similar to Clash Royale.
/// 
/// USAGE:
/// 1. Add this component to any GameObject in your scene
/// 2. Click "Quick Setup All" in the context menu OR let it auto-run on Start
/// 3. Test by playing the scene and dragging cards from hand to battlefield
/// </summary>
public class DragDropSetupGuide : MonoBehaviour
{
    [Header("🔧 Setup Configuration")]
    [Tooltip("Automatically run setup when the scene starts")]
    public bool autoSetupOnStart = true;
    
    [Tooltip("Show detailed logs during setup process")]
    public bool verboseLogging = true;
    
    [Header("📋 Required Components (Auto-Found)")]
    [SerializeField] private HandUI handUI;
    [SerializeField] private CardSpawner cardSpawner;
    [SerializeField] private CardPlacementSystem placementSystem;
    [SerializeField] private NetworkCardPlacementSystem networkPlacement;
    
    [Header("🎨 Visual Settings")]
    [Tooltip("Prefab for drag preview (leave empty to auto-create)")]
    public GameObject dragPreviewPrefab;
    
    [Tooltip("Colors for placement indicators")]
    public Color validPlacementColor = Color.green;
    public Color invalidPlacementColor = Color.red;
    
    [Header("⚙️ Placement Settings (Unified)")]
    [Range(5f, 25f)]
    [Tooltip("Maximum distance from player tower to place cards")]
    public float maxPlacementRange = 15f;
    
    [Range(1f, 5f)]
    [Tooltip("Minimum distance from towers when placing")]
    public float minDistanceFromTowers = 3f;
    
    [Range(1f, 4f)]
    [Tooltip("Minimum distance between buildings")]
    public float minDistanceBetweenBuildings = 2f;

    [Header("🌐 Network Placement System")]
    [Tooltip("Use the same valid zones for both players (no special Player 2 zone)")]
    public bool useUnifiedPlacementForAllPlayers = true;
    [Tooltip("Map opponent placements locally when using client-side spawns (mirroring/plane)")]
    public bool mapOpponentPositions = false;
    public NetworkCardPlacementSystem.OpponentMappingMode opponentMappingMode = NetworkCardPlacementSystem.OpponentMappingMode.None;
    [Tooltip("Optional point on the plane used for custom plane mirror")] public Transform customPlanePoint;
    [Tooltip("Plane normal used for custom plane mirror")] public Vector3 customPlaneNormal = Vector3.forward;
    [Tooltip("Offset added after mapping opponent positions")] public Vector3 opponentPositionOffset = Vector3.zero;
    [Tooltip("Ground layers used by placement raycasts")] public LayerMask groundLayerMask = 1;
    
    [Header("🖱️ Drag Settings")]
    [Range(10f, 50f)]
    [Tooltip("Minimum pixels to drag before starting drag mode")]
    public float dragThreshold = 30f;
    
    [Range(1.1f, 1.5f)]
    [Tooltip("Scale multiplier when dragging cards")]
    public float dragScale = 1.2f;

    // Setup state
    private bool setupComplete = false;
    private int setupErrors = 0;
    private int setupWarnings = 0;

    void Start()
    {
        if (autoSetupOnStart && !setupComplete)
        {
            Log("🚀 Starting automatic drag-and-drop setup...");
            QuickSetupAll();
        }
    }

    #region Public Setup Methods

    /// <summary>
    /// Performs complete automatic setup of the drag-and-drop system
    /// </summary>
    [ContextMenu("🚀 Quick Setup All")]
    public void QuickSetupAll()
    {
        Log("=== DRAG-AND-DROP SETUP STARTING ===");
        setupErrors = 0;
        setupWarnings = 0;
        
        // Step 1: Find and validate components
        FindComponents();
        
    // Step 2: Create missing systems
    SetupPlacementSystem();
    SetupNetworkPlacementSystem();
        
        // Step 3: Create drag preview
        SetupDragPreview();
        
        // Step 4: Setup draggable cards
        SetupDraggableCards();
        
        // Step 5: Final validation
        ValidateCompleteSetup();
        
        // Summary
        if (setupErrors == 0)
        {
            Log("✅ SETUP COMPLETE! Drag-and-drop system is ready to use!");
            Log($"📊 Summary: {setupWarnings} warnings, {setupErrors} errors");
            setupComplete = true;
        }
        else
        {
            LogError($"❌ SETUP FAILED! {setupErrors} errors need to be fixed.");
        }
        
        Log("=== SETUP FINISHED ===");
    }

    /// <summary>
    /// Validates the current setup and reports any issues
    /// </summary>
    [ContextMenu("🔍 Validate Setup")]
    public void ValidateCompleteSetup()
    {
        Log("🔍 Validating drag-and-drop setup...");
        setupErrors = 0;
        setupWarnings = 0;
        
        // Find components
        FindComponents();
        
        // Check essential components
        CheckEssentialComponents();
        
        // Check drag components
        CheckDragComponents();
        
        // Check systems
        CheckGameSystems();
        
        // Report results
        if (setupErrors == 0 && setupWarnings == 0)
        {
            Log("✅ Perfect! All components are properly configured.");
        }
        else
        {
            Log($"📊 Validation complete: {setupWarnings} warnings, {setupErrors} errors");
        }
    }

    /// <summary>
    /// Shows detailed setup instructions in the console
    /// </summary>
    [ContextMenu("📖 Show Instructions")]
    public void ShowSetupInstructions()
    {
    string guide = @"
🎮 DRAG-AND-DROP CARD SYSTEM SETUP GUIDE

📋 QUICK START:
1. Add this script to any GameObject in your scene
2. Click '🚀 Quick Setup All' in the context menu
3. Play the scene and test dragging cards!

🔧 MANUAL SETUP STEPS:
1. Ensure HandUI exists with card slots assigned
2. Ensure CardSpawner exists (king towers will bind automatically after scene configure)
3. Ensure NetworkCardPlacementSystem exists (this guide can create it) and is on a GameObject with NetworkObject
4. Ensure main camera is tagged 'MainCamera'
5. Run 'Quick Setup All' to auto-configure everything

🎯 TESTING:
• Click cards: Works as before (backward compatible)
• Drag cards: Shows preview and placement indicators
• Green areas: Valid placement zones
• Red areas: Invalid placement zones
• Range circles: Show attack/effect ranges

⚙️ CUSTOMIZATION:
• Adjust placement settings in this component's inspector
• Configure unified placement and opponent mapping on NetworkCardPlacementSystem
• Modify drag sensitivity and visual feedback
• Create custom drag preview prefabs

🆘 TROUBLESHOOTING:
• Check console for detailed error messages
• Use '🔍 Validate Setup' to diagnose issues
• Ensure all required components exist in scene
• Verify card prefabs have proper components

✨ FEATURES ADDED:
✅ Clash Royale-style drag-and-drop
✅ Real-time placement validation
✅ Visual feedback and indicators
✅ Range preview circles
✅ Smooth animations
✅ Backward compatibility
✅ Cross-platform input support
";
        
        Debug.Log(guide);
    }

    #endregion

    #region Setup Implementation

    private void FindComponents()
    {
        Log("🔍 Finding components...");
        
        if (handUI == null) handUI = FindFirstObjectByType<HandUI>();
        if (cardSpawner == null) cardSpawner = FindFirstObjectByType<CardSpawner>();
        if (placementSystem == null) placementSystem = FindFirstObjectByType<CardPlacementSystem>();
        
        Log($"Found - HandUI: {handUI != null}, CardSpawner: {cardSpawner != null}, PlacementSystem: {placementSystem != null}");
    }

    private void SetupPlacementSystem()
    {
        if (placementSystem != null)
        {
            Log("✓ CardPlacementSystem already exists");
            ConfigurePlacementSystem();
            return;
        }

        Log("🔧 Creating CardPlacementSystem...");
        
        // Create placement system GameObject
        GameObject placementGO = new GameObject("CardPlacementSystem");
        placementGO.transform.SetParent(transform);
        placementSystem = placementGO.AddComponent<CardPlacementSystem>();
        
        ConfigurePlacementSystem();
        
        Log("✅ CardPlacementSystem created and configured");
    }

    private void ConfigurePlacementSystem()
    {
        if (placementSystem == null) return;
        // Link to backend
        if (networkPlacement == null)
        {
            networkPlacement = FindFirstObjectByType<NetworkCardPlacementSystem>();
        }
        placementSystem.networkPlacement = networkPlacement;
        Log("⚙️ CardPlacementSystem linked to NetworkCardPlacementSystem");
    }

    private void SetupNetworkPlacementSystem()
    {
        if (networkPlacement == null)
        {
            var nm = NetworkManager.Singleton;
            bool netActive = nm != null && nm.IsListening;
            bool isServer = netActive && nm.IsServer;

            if (netActive && !isServer)
            {
                // In online client: do not create a local instance. Server should spawn it.
                // Try to find one if it was spawned already.
                networkPlacement = FindFirstObjectByType<NetworkCardPlacementSystem>();
                if (networkPlacement == null)
                {
                    Log("⌛ Waiting for server to spawn NetworkCardPlacementSystem (client).");
                    return;
                }
            }
            else
            {
                // Offline or server: create (and spawn if server)
                Log("🔧 Creating NetworkCardPlacementSystem...");
                GameObject go = new GameObject("NetworkCardPlacementSystem");
                go.transform.SetParent(transform);
                networkPlacement = go.AddComponent<NetworkCardPlacementSystem>();
                var netObj = go.GetComponent<Unity.Netcode.NetworkObject>();
                if (netActive && isServer)
                {
                    if (netObj == null) netObj = go.AddComponent<Unity.Netcode.NetworkObject>();
                    if (!netObj.IsSpawned) netObj.Spawn();
                }
                else
                {
                    // Offline practice: network object not required, but safe to add
                    if (netObj == null) go.AddComponent<Unity.Netcode.NetworkObject>();
                }
            }
        }

        // Apply configuration
        networkPlacement.useUnifiedPlacementForAllPlayers = useUnifiedPlacementForAllPlayers;
        networkPlacement.mapOpponentPositions = mapOpponentPositions;
        networkPlacement.opponentMappingMode = opponentMappingMode;
        networkPlacement.customPlanePoint = customPlanePoint;
        networkPlacement.customPlaneNormal = customPlaneNormal;
        networkPlacement.opponentPositionOffset = opponentPositionOffset;

        // Ground mask: prefer user-provided; fallback to Default/Ground; then default layer
        LayerMask gm = groundLayerMask;
        if (gm.value == 0)
        {
            gm = LayerMask.GetMask("Default", "Ground");
            if (gm.value == 0) gm = 1;
        }
        networkPlacement.groundLayerMask = gm;

        // Range and distance helpers
        networkPlacement.maxPlacementRange = maxPlacementRange;
        networkPlacement.minDistanceFromTowers = minDistanceFromTowers;
        networkPlacement.minDistanceBetweenBuildings = minDistanceBetweenBuildings;

        // Ensure frontend is linked
        if (placementSystem != null)
        {
            placementSystem.networkPlacement = networkPlacement;
        }

        Log("✅ NetworkCardPlacementSystem created/configured");
    }

    private void SetupDragPreview()
    {
        if (dragPreviewPrefab != null)
        {
            Log("✓ Drag preview prefab already assigned");
            return;
        }

        Log("🎨 Creating default drag preview prefab...");
        
        // Create preview prefab
        GameObject previewPrefab = new GameObject("CardDragPreview");
        
        // Add RectTransform for UI
        RectTransform previewRect = previewPrefab.AddComponent<RectTransform>();
        previewRect.sizeDelta = new Vector2(100, 120);
        
        // Add CanvasGroup for alpha control
        previewPrefab.AddComponent<CanvasGroup>();
        
        // Add CardDragPreview script
        CardDragPreview previewScript = previewPrefab.AddComponent<CardDragPreview>();
        
        // Create background
        GameObject background = new GameObject("Background");
        background.transform.SetParent(previewPrefab.transform);
        
        Image bgImage = background.AddComponent<Image>();
        bgImage.color = new Color(0.1f, 0.1f, 0.2f, 0.9f);
        
        RectTransform bgRect = background.GetComponent<RectTransform>();
        bgRect.sizeDelta = new Vector2(100, 120);
        bgRect.anchoredPosition = Vector2.zero;
        
        // Create card icon
        GameObject icon = new GameObject("CardIcon");
        icon.transform.SetParent(previewPrefab.transform);
        
        Image iconImage = icon.AddComponent<Image>();
        iconImage.color = Color.white;
        
        RectTransform iconRect = icon.GetComponent<RectTransform>();
        iconRect.sizeDelta = new Vector2(70, 70);
        iconRect.anchoredPosition = new Vector2(0, 15);
        
        // Create cost text
        GameObject costGO = new GameObject("CostText");
        costGO.transform.SetParent(previewPrefab.transform);
        
        TextMeshProUGUI costText = costGO.AddComponent<TextMeshProUGUI>();
        costText.text = "0";
        costText.fontSize = 18;
        costText.color = Color.yellow;
        costText.alignment = TextAlignmentOptions.Center;
        costText.fontStyle = FontStyles.Bold;
        
        RectTransform costRect = costGO.GetComponent<RectTransform>();
        costRect.sizeDelta = new Vector2(40, 25);
        costRect.anchoredPosition = new Vector2(0, -40);
        
        // Link to script
        previewScript.cardIcon = iconImage;
        previewScript.costText = costText;
        previewScript.backgroundImage = bgImage;
        
        // Save reference
        dragPreviewPrefab = previewPrefab;
        
        // Try to save as prefab asset in editor
        #if UNITY_EDITOR
        try
        {
            string prefabPath = "Assets/Prefabs/";
            if (!System.IO.Directory.Exists(prefabPath))
            {
                System.IO.Directory.CreateDirectory(prefabPath);
            }
            
            string fullPath = prefabPath + "CardDragPreview.prefab";
            dragPreviewPrefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(previewPrefab, fullPath);
            
            // Destroy the scene instance since we now have a prefab
            DestroyImmediate(previewPrefab);
            
            Log($"💾 Drag preview prefab saved to {fullPath}");
        }
        catch (System.Exception e)
        {
            LogWarning($"Could not save prefab: {e.Message}");
        }
        #endif
        
        Log("✅ Drag preview created");
    }

    private void SetupDraggableCards()
    {
        if (handUI == null)
        {
            LogError("❌ Cannot setup draggable cards - HandUI not found!");
            return;
        }

        if (handUI.cardSlots == null || handUI.cardSlots.Count == 0)
        {
            LogError("❌ HandUI card slots not assigned!");
            return;
        }

        Log($"🎮 Setting up {handUI.cardSlots.Count} draggable card slots...");
        
        int setupCount = 0;
        int skippedCount = 0;
        
        foreach (Button cardSlot in handUI.cardSlots)
        {
            if (cardSlot == null) continue;
            
            DraggableCard existing = cardSlot.GetComponent<DraggableCard>();
            if (existing != null)
            {
                skippedCount++;
                // Update existing settings
                UpdateDraggableCardSettings(existing);
                continue;
            }
            
            // Add new DraggableCard component
            DraggableCard draggable = cardSlot.gameObject.AddComponent<DraggableCard>();
            UpdateDraggableCardSettings(draggable);
            
            setupCount++;
        }
        
        Log($"✅ Draggable cards setup: {setupCount} added, {skippedCount} already existed");
    }

    private void UpdateDraggableCardSettings(DraggableCard draggable)
    {
        if (draggable == null) return;
        
        draggable.dragThreshold = dragThreshold;
        draggable.dragScale = dragScale;
        draggable.validPlacementColor = validPlacementColor;
        draggable.invalidPlacementColor = invalidPlacementColor;
        
        if (dragPreviewPrefab != null)
        {
            draggable.dragPreviewPrefab = dragPreviewPrefab;
        }
    }

    #endregion

    #region Validation

    private void CheckEssentialComponents()
    {
        // HandUI
        if (handUI == null)
        {
            LogError("❌ HandUI not found! Please ensure a HandUI component exists in the scene.");
        }
        else
        {
            Log("✓ HandUI found");
            
            if (handUI.cardSlots == null || handUI.cardSlots.Count == 0)
            {
                LogError("❌ HandUI card slots not assigned!");
            }
            else
            {
                Log($"✓ HandUI has {handUI.cardSlots.Count} card slots");
            }
        }
        
        // CardSpawner
        if (cardSpawner == null)
        {
            LogError("❌ CardSpawner not found! Please ensure a CardSpawner component exists in the scene.");
        }
        else
        {
            Log("✓ CardSpawner found");
            
            if (cardSpawner.playerKingTower == null)
                LogWarning("⚠️ CardSpawner.playerKingTower not assigned");
            if (cardSpawner.enemyKingTower == null)
                LogWarning("⚠️ CardSpawner.enemyKingTower not assigned");
        }
        
        // Main Camera
        if (Camera.main == null)
        {
            LogError("❌ No main camera found! Please tag your camera as 'MainCamera'.");
        }
        else
        {
            Log("✓ Main camera found");
        }
    }

    private void CheckDragComponents()
    {
        // CardPlacementSystem
        if (placementSystem == null)
        {
            LogError("❌ CardPlacementSystem not found!");
        }
        else
        {
            Log("✓ CardPlacementSystem found");
        }
        
        // Drag preview
        if (dragPreviewPrefab == null)
        {
            LogWarning("⚠️ No drag preview prefab assigned");
        }
        else
        {
            Log("✓ Drag preview prefab assigned");
        }
        
        // Check draggable cards
        if (handUI != null && handUI.cardSlots != null)
        {
            int draggableCount = 0;
            foreach (Button slot in handUI.cardSlots)
            {
                if (slot != null && slot.GetComponent<DraggableCard>() != null)
                    draggableCount++;
            }
            
            if (draggableCount == 0)
            {
                LogWarning("⚠️ No DraggableCard components found on card slots");
            }
            else
            {
                Log($"✓ {draggableCount} draggable card slots configured");
            }
        }
    }

    private void CheckGameSystems()
    {
        // CoinSystem
        if (CoinSystem.Instance == null)
        {
            LogWarning("⚠️ CoinSystem.Instance not found - cards may not be playable");
        }
        else
        {
            Log("✓ CoinSystem found");
        }
        
        // DeckManager
        if (DeckManager.Instance == null)
        {
            LogWarning("⚠️ DeckManager.Instance not found - card playing may not work");
        }
        else
        {
            Log("✓ DeckManager found");
        }
    }

    #endregion

    #region Utility Methods

    private void Log(string message)
    {
        if (verboseLogging)
        {
            Debug.Log($"[DragDropSetup] {message}");
        }
    }

    private void LogWarning(string message)
    {
        setupWarnings++;
        Debug.LogWarning($"[DragDropSetup] {message}");
    }

    private void LogError(string message)
    {
        setupErrors++;
        Debug.LogError($"[DragDropSetup] {message}");
    }

    #endregion

    #region Editor Helpers

    #if UNITY_EDITOR
    [ContextMenu("🧹 Clean Old Components")]
    public void CleanOldComponents()
    {
        if (handUI?.cardSlots != null)
        {
            int cleaned = 0;
            foreach (Button slot in handUI.cardSlots)
            {
                if (slot != null)
                {
                    DraggableCard[] draggables = slot.GetComponents<DraggableCard>();
                    for (int i = 1; i < draggables.Length; i++) // Keep first, remove duplicates
                    {
                        DestroyImmediate(draggables[i]);
                        cleaned++;
                    }
                }
            }
            Log($"🧹 Cleaned {cleaned} duplicate DraggableCard components");
        }
    }
    
    [ContextMenu("📊 Generate Setup Report")]
    public void GenerateSetupReport()
    {
        string report = $@"
📊 DRAG-DROP SETUP REPORT
Generated: {System.DateTime.Now}

🔧 CONFIGURATION:
• Max Placement Range: {maxPlacementRange}m
• Min Distance From Towers: {minDistanceFromTowers}m
• Min Distance Between Buildings: {minDistanceBetweenBuildings}m
• Drag Threshold: {dragThreshold} pixels
• Drag Scale: {dragScale}x

📋 COMPONENTS STATUS:
• HandUI: {(handUI != null ? "✓ Found" : "❌ Missing")}
• CardSpawner: {(cardSpawner != null ? "✓ Found" : "❌ Missing")}
• PlacementSystem: {(placementSystem != null ? "✓ Found" : "❌ Missing")}
• Drag Preview: {(dragPreviewPrefab != null ? "✓ Assigned" : "❌ Missing")}
• Main Camera: {(Camera.main != null ? "✓ Found" : "❌ Missing")}

🎮 CARD SLOTS:
{(handUI?.cardSlots != null ? $"• Total Slots: {handUI.cardSlots.Count}" : "• No card slots found")}
{(handUI?.cardSlots != null ? $"• Draggable Slots: {CountDraggableSlots()}" : "")}

🔄 SYSTEMS:
• CoinSystem: {(CoinSystem.Instance != null ? "✓ Active" : "❌ Missing")}
• DeckManager: {(DeckManager.Instance != null ? "✓ Active" : "❌ Missing")}

📈 RECOMMENDATIONS:
{GetRecommendations()}
";
        
        Debug.Log(report);
    }
    
    private int CountDraggableSlots()
    {
        if (handUI?.cardSlots == null) return 0;
        
        int count = 0;
        foreach (Button slot in handUI.cardSlots)
        {
            if (slot != null && slot.GetComponent<DraggableCard>() != null)
                count++;
        }
        return count;
    }
    
    private string GetRecommendations()
    {
        var recommendations = new System.Collections.Generic.List<string>();
        
        if (handUI == null) recommendations.Add("• Add HandUI component to scene");
        if (cardSpawner == null) recommendations.Add("• Add CardSpawner component to scene");
        if (placementSystem == null) recommendations.Add("• Run 'Setup Placement System'");
        if (dragPreviewPrefab == null) recommendations.Add("• Create drag preview prefab");
        if (Camera.main == null) recommendations.Add("• Tag main camera as 'MainCamera'");
        
        if (recommendations.Count == 0)
        {
            return "• Setup looks good! ✅";
        }
        
        return string.Join("\n", recommendations);
    }
    #endif

    #endregion
}