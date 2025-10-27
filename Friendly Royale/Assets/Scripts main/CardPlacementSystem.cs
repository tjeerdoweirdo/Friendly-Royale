using UnityEngine;

/// <summary>
/// Simplified local placement UX that delegates raycasts, validation, preview, and spawning
/// to NetworkCardPlacementSystem. This removes side-specific zones and custom area lists.
/// </summary>
public class CardPlacementSystem : MonoBehaviour
{
    [Header("Integration")]
    [Tooltip("Placement/validation/spawn backend. Auto-assigned if left null.")]
    public NetworkCardPlacementSystem networkPlacement;

    [Header("Preview")]
    [Tooltip("Optional range circle prefab (purely visual)")]
    public GameObject rangeCircleIndicator;

    // State
    private Camera mainCamera;
    private Card currentCard;
    private bool isPlacingCard = false;
    private GameObject currentRangeCircle;

    void Awake()
    {
        mainCamera = Camera.main ?? FindFirstObjectByType<Camera>();
        if (networkPlacement == null)
        {
            networkPlacement = FindFirstObjectByType<NetworkCardPlacementSystem>();
        }
    }

    /// <summary>
    /// Called when a card starts being dragged
    /// </summary>
    public void BeginCardPlacement(Card card)
    {
        currentCard = card;
        isPlacingCard = true;
        CreateRangeIndicatorIfNeeded(card);
        Debug.Log($"[CardPlacementSystem] Begin placement for {card?.cardName}");
    }

    /// <summary>
    /// Called when card dragging ends
    /// </summary>
    public void EndCardPlacement()
    {
        isPlacingCard = false;
        currentCard = null;
        // Hide backend preview and local range
        if (networkPlacement != null) networkPlacement.HidePlacementPreview();
        DestroyRangeIndicator();
        Debug.Log("[CardPlacementSystem] End placement");
    }

    /// <summary>
    /// Try to compute a world placement position from a screen-space ray via backend
    /// </summary>
    public bool TryGetPlacementPosition(Ray ray, Card card, out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;
        if (networkPlacement == null) return false;
        return networkPlacement.TryGetPlacementPosition(ray, card, out worldPosition);
    }

    /// <summary>
    /// Try to get a world position and whether it's valid
    /// </summary>
    public bool TryGetValidPlacementPosition(Ray ray, Card card, out Vector3 worldPosition, out bool isValid)
    {
        worldPosition = Vector3.zero;
        isValid = false;
        if (networkPlacement == null) return false;
        if (!networkPlacement.TryGetPlacementPosition(ray, card, out worldPosition)) return false;

        ulong clientId = Unity.Netcode.NetworkManager.Singleton != null ? Unity.Netcode.NetworkManager.Singleton.LocalClientId : 0UL;
        isValid = networkPlacement.IsValidPlacementPosition(worldPosition, card, Unit.Faction.Player, clientId);
        return true;
    }

    /// <summary>
    /// Update the on-ground preview and optional range circle
    /// </summary>
    public void UpdatePlacementPreview(Vector3 worldPosition, bool isValid)
    {
        if (!isPlacingCard || networkPlacement == null || currentCard == null) return;
        networkPlacement.ShowPlacementPreview(worldPosition, currentCard, Unit.Faction.Player, isValid);
        UpdateRangeIndicator(worldPosition);
    }

    /// <summary>
    /// Place the current card at the given world position via backend (handles offline + network)
    /// </summary>
    public void PlaceCurrentCardAt(Vector3 worldPosition)
    {
        if (currentCard == null || networkPlacement == null) return;
        networkPlacement.RequestCardPlacement(worldPosition, currentCard, Unit.Faction.Player);
    }

    /// <summary>
    /// Convenience validator
    /// </summary>
    public bool IsValidPlacementPosition(Vector3 position, Card card)
    {
        if (networkPlacement == null || card == null) return false;
        ulong clientId = Unity.Netcode.NetworkManager.Singleton != null ? Unity.Netcode.NetworkManager.Singleton.LocalClientId : 0UL;
        return networkPlacement.IsValidPlacementPosition(position, card, Unit.Faction.Player, clientId);
    }

    private void CreateRangeIndicatorIfNeeded(Card card)
    {
        if (rangeCircleIndicator == null || card == null) return;
        if (card.baseRange <= 1.5f && card.cardType != CardType.Building) return;
        if (currentRangeCircle != null) return;

        currentRangeCircle = Instantiate(rangeCircleIndicator);
        float range = card.cardType == CardType.Building ? card.defenseAttackRange : card.baseRange;
        currentRangeCircle.transform.localScale = Vector3.one * range * 2f; // diameter
        currentRangeCircle.SetActive(true);
    }

    private void UpdateRangeIndicator(Vector3 position)
    {
        if (currentRangeCircle == null) return;
        var p = position; p.y = 0f;
        currentRangeCircle.transform.position = p;
    }

    private void DestroyRangeIndicator()
    {
        if (currentRangeCircle != null)
        {
            Destroy(currentRangeCircle);
            currentRangeCircle = null;
        }
    }
}