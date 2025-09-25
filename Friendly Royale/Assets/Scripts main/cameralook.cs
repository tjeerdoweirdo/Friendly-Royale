using UnityEngine;

/// <summary>
/// Makes the health bar always face the active camera in the scene.
/// Attach this to the health bar Canvas (world-space).
/// </summary>
public class HealthBarBillboard : MonoBehaviour
{
    private Camera mainCamera;

    void Start()
    {
        // Automatically find the main camera in the scene
        if (Camera.main != null)
        {
            mainCamera = Camera.main;
        }
        else
        {
            // If no camera is tagged as MainCamera, just grab any camera
            Camera[] cams = FindObjectsOfType<Camera>();
            if (cams.Length > 0) mainCamera = cams[0];
        }
    }

    void LateUpdate()
    {
        if (mainCamera == null) return;

        // Rotate to face the camera, then turn 180 degrees
        Vector3 dir = transform.position - mainCamera.transform.position;
        transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0, 180, 0);
    }
}
