using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Handles per-player setup: assigning camera, UI canvas, and configuring CardPlacementSystem side.
/// Attach to a player prefab with NetworkObject.
/// </summary>
public class NetworkPlayerSetup : NetworkBehaviour
{
    [Header("References (Optional Auto-Find)")]
    public Camera playerCamera;
    public Canvas playerUICanvas;
    public CardPlacementSystem placementSystem; // local-only reference (not on prefab, found at runtime)

    [Header("Side Configuration")] 
    [Tooltip("If true, this instance forces Player1 side (override network index)")] public bool forcePlayer1;
    [Tooltip("If true, this instance forces Player2 side (override network index)")] public bool forcePlayer2;

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

        // Determine side
        CardPlacementSystem.PlayerSide side = CardPlacementSystem.PlayerSide.Player1;
        if (forcePlayer2) side = CardPlacementSystem.PlayerSide.Player2;
        else if (!forcePlayer1)
        {
            // Basic heuristic: first owner -> Player1, others -> Player2
            side = (NetworkManager.Singleton.ConnectedClientsList.Count > 1 && OwnerClientId != NetworkManager.ServerClientId)
                ? CardPlacementSystem.PlayerSide.Player2
                : CardPlacementSystem.PlayerSide.Player1;
        }

        // Find placement system (could be singleton-like or scene object)
        if (placementSystem == null)
        {
            placementSystem = FindFirstObjectByType<CardPlacementSystem>();
        }
        if (placementSystem != null)
        {
            placementSystem.SetLocalPlayerSide(side);
        }

        Debug.Log($"[NetworkPlayerSetup] Local player initialized as {side} (ClientId={OwnerClientId})");
    }
}
