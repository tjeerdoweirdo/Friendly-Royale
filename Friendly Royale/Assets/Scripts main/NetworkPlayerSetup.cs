using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Handles per-player setup: assigning camera and UI canvas. Placement uses unified Player1 rules per client,
/// so there's no per-player side to set anymore (both clients feel like Player1).
/// Attach to a player prefab with NetworkObject.
/// </summary>
public class NetworkPlayerSetup : NetworkBehaviour
{
    [Header("References (Optional Auto-Find)")]
    public Camera playerCamera;
    public Canvas playerUICanvas;
    public CardPlacementSystem placementSystem; // local-only reference (not on prefab, found at runtime)

    [Header("Side Configuration (Optional Fallback)")] 
    [Tooltip("If true, writes PlayerPrefs LocalPlayerIsPlayer1=1 as a fallback override for systems that still read it.")]
    public bool forcePlayer1;
    [Tooltip("If true, writes PlayerPrefs LocalPlayerIsPlayer1=0 as a fallback override for systems that still read it.")]
    public bool forcePlayer2;

    private bool initialized = false;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            // Disable local-only components for remote players
            if (playerCamera != null) playerCamera.gameObject.SetActive(false);
            if (playerUICanvas != null) playerUICanvas.gameObject.SetActive(false);
            return;
        }

        InitializeLocal();
    }

    private void InitializeLocal()
    {
        if (initialized) return;
        initialized = true;

        // Auto-find camera & UI if missing
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>(true);
            if (playerCamera == null)
            {
                playerCamera = Camera.main; // fallback
            }
        }
        if (playerUICanvas == null)
        {
            playerUICanvas = GetComponentInChildren<Canvas>(true);
        }

        if (playerCamera != null) playerCamera.gameObject.SetActive(true);
        if (playerUICanvas != null) playerUICanvas.gameObject.SetActive(true);

        // Optional: record a fallback preference for legacy systems
        if (forcePlayer1 && !forcePlayer2)
        {
            PlayerPrefs.SetInt("LocalPlayerIsPlayer1", 1);
        }
        else if (forcePlayer2 && !forcePlayer1)
        {
            PlayerPrefs.SetInt("LocalPlayerIsPlayer1", 0);
        }

        // No explicit side configuration needed: TowerSceneAutoConfigurator assigns per-client sides by camera proximity.
        if (placementSystem == null)
        {
            placementSystem = FindFirstObjectByType<CardPlacementSystem>();
        }

        Debug.Log($"[NetworkPlayerSetup] Local player initialized (ClientId={OwnerClientId}). Unified placement is active; no per-side setup required.");
    }
}
