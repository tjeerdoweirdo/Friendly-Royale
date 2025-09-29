using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Bridge component that helps with unit navigation.
/// Provides debugging tools and path validation for bridges.
/// 
/// SETUP INSTRUCTIONS:
/// 1. Install AI Navigation package: Window → Package Manager → Unity Registry → "AI Navigation"
/// 2. Add NavMesh Surface component to this bridge GameObject
/// 3. Set Agent Type to match your units (usually Humanoid)
/// 4. Click Bake on the NavMesh Surface component
/// 5. Make sure bridge connects to main NavMesh (no gaps)
/// </summary>
public class Bridge : MonoBehaviour
{
    [Header("Bridge Settings")]
    [Tooltip("Agent type this bridge supports (should match your units)")]
    public int agentTypeID = 0;
    
    [Header("Connection Points")]
    [Tooltip("Points where units enter/exit the bridge (for debugging and validation)")]
    public Transform[] connectionPoints;
    
    [Header("Debug")]
    [Tooltip("Show bridge connection gizmos in scene view")]
    public bool showDebugGizmos = true;
    
    [Tooltip("Color for debug gizmos")]
    public Color debugColor = Color.cyan;
    
    [Header("Setup Instructions")]
    [TextArea(3, 6)]
    public string setupInstructions = "1. Install AI Navigation package\n2. Add NavMesh Surface component\n3. Set Agent Type to Humanoid\n4. Click Bake button\n5. Check for NavMesh gaps";

    void Start()
    {
        ValidateBridgeSetup();
        if (connectionPoints != null && connectionPoints.Length >= 2)
        {
            TestBridgePathfinding();
        }
    }

    /// <summary>
    /// Validate that the bridge has proper setup for navigation
    /// </summary>
    void ValidateBridgeSetup()
    {
        Debug.Log($"=== Bridge '{gameObject.name}' Validation ===");
        
        // Check for colliders
        Collider[] colliders = GetComponentsInChildren<Collider>();
        if (colliders.Length == 0)
        {
            Debug.LogWarning($"❌ Bridge '{gameObject.name}' has no colliders! Add colliders for NavMesh generation.");
        }
        else
        {
            Debug.Log($"✓ Bridge has {colliders.Length} collider(s)");
        }

        // Check for MeshRenderer (for geometry)
        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"❌ Bridge '{gameObject.name}' has no MeshRenderers! NavMesh needs geometry to bake.");
        }
        else
        {
            Debug.Log($"✓ Bridge has {renderers.Length} mesh renderer(s)");
        }

        // Check for NavMesh Surface (if AI Navigation package is installed)
        Component navSurface = GetComponent("NavMeshSurface");
        if (navSurface == null)
        {
            Debug.LogWarning($"❌ Bridge '{gameObject.name}' has no NavMesh Surface component! Install AI Navigation package and add NavMesh Surface component.");
            Debug.Log($"📋 Instructions:\n{setupInstructions}");
        }
        else
        {
            Debug.Log($"✓ Bridge has NavMesh Surface component");
        }

        // Check connection points
        if (connectionPoints == null || connectionPoints.Length < 2)
        {
            Debug.LogWarning($"❌ Bridge needs at least 2 connection points for testing. Create empty GameObjects at bridge entrances/exits.");
        }
        else
        {
            Debug.Log($"✓ Bridge has {connectionPoints.Length} connection points");
        }
    }

    /// <summary>
    /// Test if a unit can pathfind across the bridge
    /// </summary>
    [ContextMenu("Test Bridge Pathfinding")]
    public void TestBridgePathfinding()
    {
        if (connectionPoints == null || connectionPoints.Length < 2)
        {
            Debug.LogWarning("Need at least 2 connection points to test pathfinding");
            return;
        }

        NavMeshPath path = new NavMeshPath();
        Vector3 start = connectionPoints[0].position;
        Vector3 end = connectionPoints[connectionPoints.Length - 1].position;

        if (NavMesh.CalculatePath(start, end, NavMesh.AllAreas, path))
        {
            if (path.status == NavMeshPathStatus.PathComplete)
            {
                Debug.Log($"✓ Bridge pathfinding test PASSED! Path has {path.corners.Length} corners.");
            }
            else
            {
                Debug.LogWarning($"⚠ Bridge pathfinding test PARTIAL. Path status: {path.status}");
            }
        }
        else
        {
            Debug.LogError("✗ Bridge pathfinding test FAILED! No path found between connection points.");
        }
    }



    /// <summary>
    /// Force units to recalculate paths that cross this bridge
    /// </summary>
    public void RefreshUnitPaths()
    {
        Unit[] allUnits = FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (var unit in allUnits)
        {
            if (unit.agent != null && unit.agent.isActiveAndEnabled)
            {
                // Check if unit's path might cross this bridge
                Vector3 unitPos = unit.transform.position;
                Vector3 bridgePos = transform.position;
                float distanceToBridge = Vector3.Distance(unitPos, bridgePos);
                
                // If unit is near the bridge, refresh its path
                if (distanceToBridge < 20f) // Adjust range as needed
                {
                    Vector3 currentDestination = unit.agent.destination;
                    unit.agent.ResetPath();
                    unit.agent.SetDestination(currentDestination);
                }
            }
        }
        Debug.Log($"Refreshed paths for units near bridge '{gameObject.name}'");
    }

    void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;

        // Draw bridge bounds
        Gizmos.color = debugColor;
        Bounds bounds = GetBridgeBounds();
        Gizmos.DrawWireCube(bounds.center, bounds.size);

        // Draw connection points
        if (connectionPoints != null)
        {
            Gizmos.color = Color.green;
            foreach (var point in connectionPoints)
            {
                if (point != null)
                {
                    Gizmos.DrawWireSphere(point.position, 0.5f);
                    Gizmos.DrawLine(point.position, point.position + Vector3.up * 2f);
                }
            }
        }

        // Draw NavMesh bounds if available
        Component navSurface = GetComponent("NavMeshSurface");
        if (navSurface != null)
        {
            Gizmos.color = Color.blue;
            // Simple bounds representation since we can't access navMeshData directly
            Bounds simpleBounds = new Bounds(transform.position, bounds.size * 1.1f);
            Gizmos.DrawWireCube(simpleBounds.center, simpleBounds.size);
        }
    }

    Bounds GetBridgeBounds()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(transform.position, Vector3.one);

        Bounds bounds = renderers[0].bounds;
        foreach (var renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
        }
        return bounds;
    }

    void OnDrawGizmosSelected()
    {
        // Draw detailed debug info when selected
        if (connectionPoints != null && connectionPoints.Length > 1)
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i < connectionPoints.Length - 1; i++)
            {
                if (connectionPoints[i] != null && connectionPoints[i + 1] != null)
                {
                    Gizmos.DrawLine(connectionPoints[i].position, connectionPoints[i + 1].position);
                }
            }
        }
    }
}