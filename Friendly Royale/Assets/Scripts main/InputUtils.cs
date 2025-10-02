using UnityEngine;

/// <summary>
/// Utility class for input-related helper functions for the drag-and-drop card system.
/// Provides cross-platform input handling for both mouse and touch.
/// </summary>
public static class InputUtils
{
    /// <summary>
    /// Get current input position (mouse or touch)
    /// </summary>
    public static Vector2 GetInputPosition()
    {
        #if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBGL
        return Input.mousePosition;
        #elif UNITY_MOBILE
        if (Input.touchCount > 0)
        {
            return Input.GetTouch(0).position;
        }
        return Vector2.zero;
        #else
        return Input.mousePosition;
        #endif
    }
    
    /// <summary>
    /// Check if input is currently pressed down
    /// </summary>
    public static bool IsInputPressed()
    {
        #if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBGL
        return Input.GetMouseButton(0);
        #elif UNITY_MOBILE
        return Input.touchCount > 0;
        #else
        return Input.GetMouseButton(0);
        #endif
    }
    
    /// <summary>
    /// Check if input was just pressed this frame
    /// </summary>
    public static bool IsInputDown()
    {
        #if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBGL
        return Input.GetMouseButtonDown(0);
        #elif UNITY_MOBILE
        return Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began;
        #else
        return Input.GetMouseButtonDown(0);
        #endif
    }
    
    /// <summary>
    /// Check if input was just released this frame
    /// </summary>
    public static bool IsInputUp()
    {
        #if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBGL
        return Input.GetMouseButtonUp(0);
        #elif UNITY_MOBILE
        return Input.touchCount > 0 && (Input.GetTouch(0).phase == TouchPhase.Ended || Input.GetTouch(0).phase == TouchPhase.Canceled);
        #else
        return Input.GetMouseButtonUp(0);
        #endif
    }
    
    /// <summary>
    /// Convert screen position to world position using raycast
    /// </summary>
    public static bool ScreenToWorldPosition(Vector2 screenPos, LayerMask groundMask, out Vector3 worldPos)
    {
        worldPos = Vector3.zero;
        
        Camera cam = Camera.main;
        if (cam == null) return false;
        
        Ray ray = cam.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, groundMask))
        {
            worldPos = hit.point;
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Get a ray from screen position
    /// </summary>
    public static Ray ScreenPointToRay(Vector2 screenPos)
    {
        Camera cam = Camera.main;
        if (cam == null) return new Ray();
        
        return cam.ScreenPointToRay(screenPos);
    }
    
    /// <summary>
    /// Check if a position is within the screen bounds
    /// </summary>
    public static bool IsPositionOnScreen(Vector2 screenPos)
    {
        return screenPos.x >= 0 && screenPos.x <= Screen.width && 
               screenPos.y >= 0 && screenPos.y <= Screen.height;
    }
    
    /// <summary>
    /// Calculate distance between two screen positions
    /// </summary>
    public static float ScreenDistance(Vector2 pos1, Vector2 pos2)
    {
        return Vector2.Distance(pos1, pos2);
    }
    
    /// <summary>
    /// Smooth damp for Vector2 positions (useful for smooth camera following)
    /// </summary>
    public static Vector2 SmoothDampVector2(Vector2 current, Vector2 target, ref Vector2 velocity, float smoothTime)
    {
        return Vector2.SmoothDamp(current, target, ref velocity, smoothTime);
    }
}