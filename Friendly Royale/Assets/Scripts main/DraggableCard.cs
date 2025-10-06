using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using TMPro;

/// <summary>
/// Makes a card UI element draggable with visual feedback.
/// Handles drag detection, visual preview, and communicates with placement system.
/// Similar to Clash Royale's card dragging behavior.
/// </summary>
public class DraggableCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("Drag Settings")]
    [Tooltip("Minimum drag distance before starting drag mode")]
    public float dragThreshold = 30f;
    
    [Tooltip("Scale multiplier when dragging")]
    public float dragScale = 1.2f;
    
    [Tooltip("How fast the card scales when starting/ending drag")]
    public float scaleAnimationSpeed = 8f;

    [Header("Visual Feedback")]
    [Tooltip("Prefab for the drag preview (appears at mouse position)")]
    public GameObject dragPreviewPrefab;
    
    [Tooltip("Color tint when card can be placed")]
    public Color validPlacementColor = Color.green;
    
    [Tooltip("Color tint when card cannot be placed")]
    public Color invalidPlacementColor = Color.red;

    // References
    public Card cardData { get; private set; }
    public int slotIndex { get; private set; }
    public HandUI handUI { get; private set; }
    
    // Internal state
    private bool isDragging = false;
    private bool isPointerDown = false;
    private Vector2 startDragPosition;
    private Vector2 currentDragPosition;
    private GameObject dragPreview;
    private Canvas dragCanvas;
    private CanvasGroup canvasGroup;
    private RectTransform rectTransform;
    private Vector3 originalScale;
    private Vector3 originalPosition;
    private Transform originalParent;
    
    // Placement system references
    private CardPlacementSystem placementSystem;
    private Camera mainCamera;
    
    // Animation
    private Coroutine scaleAnimation;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        
        originalScale = transform.localScale;
        originalPosition = transform.localPosition;
        originalParent = transform.parent;
        
        // Find or create drag canvas
        dragCanvas = FindDragCanvas();
        
        // Find placement system
        placementSystem = FindFirstObjectByType<CardPlacementSystem>();
        if (placementSystem == null)
        {
            Debug.LogWarning("[DraggableCard] No CardPlacementSystem found in scene!");
        }
        
        // Find main camera
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogWarning("[DraggableCard] No main camera found!");
        }
    }

    /// <summary>
    /// Initialize this draggable card with its data and references
    /// </summary>
    public void Initialize(Card card, int index, HandUI ui)
    {
        cardData = card;
        slotIndex = index;
        handUI = ui;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!CanDrag()) return;
        
        isPointerDown = true;
        startDragPosition = eventData.position;
        currentDragPosition = eventData.position;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPointerDown = false;
        
        if (!isDragging)
        {
            // This was a click, not a drag - handle as normal card click
            if (handUI != null)
            {
                handUI.OnCardClicked(slotIndex);
            }
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!CanDrag() || !isPointerDown) return;
        
        // Check if we've dragged far enough to start drag mode
        float dragDistance = Vector2.Distance(startDragPosition, eventData.position);
        if (dragDistance < dragThreshold) return;
        
        StartDragMode(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging) return;
        
        currentDragPosition = eventData.position;
        UpdateDragPreview();
        UpdatePlacementValidation();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!isDragging) return;
        
        EndDragMode(eventData);
    }

    private bool CanDrag()
    {
        if (cardData == null) return false;
        
        // Check if player has enough coins
        if (CoinSystem.Instance == null) return false;
        if (CoinSystem.Instance.currentCoins < cardData.coinCost) return false;
        
        // Building placement is now handled by the main validation system
        // No special building restrictions - they follow the same rules as other cards
        
        return true;
    }

    private void StartDragMode(PointerEventData eventData)
    {
        isDragging = true;
        isPointerDown = false;
        
        // Visual feedback - scale up the original card
        AnimateScale(originalScale * dragScale);
        
        // Reduce opacity of original card
        canvasGroup.alpha = 0.6f;
        
        // Create drag preview
        CreateDragPreview();
        
        // Move to drag canvas to render on top
        transform.SetParent(dragCanvas.transform, true);
        
        // Notify placement system
        if (placementSystem != null)
        {
            placementSystem.BeginCardPlacement(cardData);
        }
        
        Debug.Log($"[DraggableCard] Started dragging card: {cardData.cardName}");
    }

    private void EndDragMode(PointerEventData eventData)
    {
        Vector3 worldPos = Vector3.zero;
        bool validPlacement = false;
        
        // Check if we're over a valid placement area
        if (mainCamera != null)
        {
            Ray ray = mainCamera.ScreenPointToRay(eventData.position);
            
            // Try NetworkCardPlacementSystem first, then fall back to CardPlacementSystem
            NetworkCardPlacementSystem networkPlacement = NetworkCardPlacementSystem.Instance;
            if (networkPlacement != null)
            {
                validPlacement = networkPlacement.TryGetPlacementPosition(ray, cardData, out worldPos);
            }
            else if (placementSystem != null)
            {
                validPlacement = placementSystem.TryGetPlacementPosition(ray, cardData, out worldPos);
            }
        }
        
        if (validPlacement)
        {
            // Place the card
            PlaceCard(worldPos);
        }
        else
        {
            // Invalid placement - return card to hand with animation
            ReturnToHand();
        }
        
        // Cleanup
        isDragging = false;
        DestroyDragPreview();
        
        // Notify placement system
        NetworkCardPlacementSystem networkPlacementCleanup = NetworkCardPlacementSystem.Instance;
        if (networkPlacementCleanup != null)
        {
            networkPlacementCleanup.HidePlacementPreview();
        }
        else if (placementSystem != null)
        {
            placementSystem.EndCardPlacement();
        }
    }

    private void CreateDragPreview()
    {
        if (dragPreviewPrefab != null && dragCanvas != null)
        {
            dragPreview = Instantiate(dragPreviewPrefab, dragCanvas.transform);
            
            // Copy card visual data to preview
            UpdateDragPreviewVisuals();
        }
    }

    private void UpdateDragPreviewVisuals()
    {
        if (dragPreview == null || cardData == null) return;
        
        // Use CardDragPreview component if available
        CardDragPreview preview = dragPreview.GetComponent<CardDragPreview>();
        if (preview != null)
        {
            preview.SetCard(cardData);
        }
        else
        {
            // Fallback to basic setup
            Image previewIcon = dragPreview.GetComponentInChildren<Image>();
            if (previewIcon != null && cardData.icon != null)
            {
                previewIcon.sprite = cardData.icon;
            }
            
            TMP_Text costText = dragPreview.GetComponentInChildren<TMP_Text>();
            if (costText != null)
            {
                costText.text = cardData.coinCost.ToString();
            }
        }
    }

    private void UpdateDragPreview()
    {
        if (dragPreview == null) return;
        
        // Position preview at mouse position
        Vector2 localPos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            dragCanvas.transform as RectTransform,
            currentDragPosition,
            dragCanvas.worldCamera,
            out localPos
        );
        
        dragPreview.transform.localPosition = localPos;
    }

    private void UpdatePlacementValidation()
    {
        if (mainCamera == null || dragPreview == null) return;
        
        // Check if current position is valid for placement
        Ray ray = mainCamera.ScreenPointToRay(currentDragPosition);
        Vector3 worldPos = Vector3.zero;
        bool isValid = false;
        
        // Try NetworkCardPlacementSystem first, then fall back to CardPlacementSystem
        NetworkCardPlacementSystem networkPlacement = NetworkCardPlacementSystem.Instance;
        if (networkPlacement != null)
        {
            isValid = networkPlacement.TryGetPlacementPosition(ray, cardData, out worldPos);
        }
        else if (placementSystem != null)
        {
            isValid = placementSystem.TryGetPlacementPosition(ray, cardData, out worldPos);
        }
        
        // Update preview color based on validity
        CardDragPreview preview = dragPreview.GetComponent<CardDragPreview>();
        if (preview != null)
        {
            preview.SetValidityTint(isValid);
        }
        else
        {
            // Fallback to basic color change
            Image previewImage = dragPreview.GetComponent<Image>();
            if (previewImage != null)
            {
                previewImage.color = isValid ? validPlacementColor : invalidPlacementColor;
            }
        }
        
        // Update placement indicators in the world (use NetworkCardPlacementSystem if available)
        if (networkPlacement != null)
        {
            networkPlacement.ShowPlacementPreview(worldPos, cardData, Unit.Faction.Player, isValid);
        }
        else if (placementSystem != null)
        {
            placementSystem.UpdatePlacementPreview(worldPos, isValid);
        }
    }

    private void PlaceCard(Vector3 worldPosition)
    {
        if (handUI == null || cardData == null) return;
        
        // Check and spend coins
        if (CoinSystem.Instance != null && CoinSystem.Instance.currentCoins >= cardData.coinCost)
        {
            bool paid = CoinSystem.Instance.SpendCoins(cardData.coinCost);
            if (paid)
            {
                // Play the card through DeckManager
                if (DeckManager.Instance != null)
                {
                    DeckManager.Instance.PlayCard(cardData);
                }
                
                // Use NetworkCardPlacementSystem for proper multiplayer support
                NetworkCardPlacementSystem networkPlacement = NetworkCardPlacementSystem.Instance;
                if (networkPlacement != null)
                {
                    networkPlacement.RequestCardPlacement(worldPosition, cardData, Unit.Faction.Player);
                }
                else
                {
                    // Fallback to direct spawner for offline mode
                    CardSpawner spawner = FindFirstObjectByType<CardSpawner>();
                    if (spawner != null)
                    {
                        StartCoroutine(spawner.SpawnUnitAtPosition(cardData, worldPosition, Unit.Faction.Player));
                    }
                    else
                    {
                        Debug.LogWarning("[DraggableCard] No CardSpawner or NetworkCardPlacementSystem found for placement!");
                    }
                }
                
                // Animate card disappearing
                AnimateCardPlaced();
                
                Debug.Log($"[DraggableCard] Placed card {cardData.cardName} at {worldPosition}");
            }
            else
            {
                ReturnToHand();
            }
        }
        else
        {
            ReturnToHand();
        }
    }

    private void ReturnToHand()
    {
        // Animate return to original position
        StartCoroutine(AnimateReturnToHand());
    }

    private void AnimateCardPlaced()
    {
        // Fade out and scale down
        StartCoroutine(AnimatePlacementFeedback());
    }

    private IEnumerator AnimateReturnToHand()
    {
        float duration = 0.3f;
        float elapsed = 0f;
        
        Vector3 startPos = transform.position;
        Vector3 startScale = transform.localScale;
        
        // Calculate target position in original parent
        transform.SetParent(originalParent, true);
        Vector3 targetPos = originalParent.TransformPoint(originalPosition);
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            t = Mathf.SmoothStep(0f, 1f, t); // Smooth animation curve
            
            transform.position = Vector3.Lerp(startPos, targetPos, t);
            transform.localScale = Vector3.Lerp(startScale, originalScale, t);
            canvasGroup.alpha = Mathf.Lerp(0.6f, 1f, t);
            
            yield return null;
        }
        
        // Ensure exact final values
        transform.localPosition = originalPosition;
        transform.localScale = originalScale;
        canvasGroup.alpha = 1f;
    }

    private IEnumerator AnimatePlacementFeedback()
    {
        float duration = 0.4f;
        float elapsed = 0f;
        
        Vector3 startScale = transform.localScale;
        float startAlpha = canvasGroup.alpha;
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            
            // Scale down and fade out
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
            
            yield return null;
        }
        
        // Reset for next use (card will be cycled)
        transform.localScale = originalScale;
        canvasGroup.alpha = 1f;
        transform.SetParent(originalParent, false);
        transform.localPosition = originalPosition;
    }

    private void AnimateScale(Vector3 targetScale)
    {
        if (scaleAnimation != null)
        {
            StopCoroutine(scaleAnimation);
        }
        scaleAnimation = StartCoroutine(AnimateScaleCoroutine(targetScale));
    }

    private IEnumerator AnimateScaleCoroutine(Vector3 targetScale)
    {
        Vector3 startScale = transform.localScale;
        float elapsed = 0f;
        float duration = 1f / scaleAnimationSpeed;
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            t = Mathf.SmoothStep(0f, 1f, t);
            
            transform.localScale = Vector3.Lerp(startScale, targetScale, t);
            yield return null;
        }
        
        transform.localScale = targetScale;
        scaleAnimation = null;
    }

    private void DestroyDragPreview()
    {
        if (dragPreview != null)
        {
            Destroy(dragPreview);
            dragPreview = null;
        }
    }

    private Canvas FindDragCanvas()
    {
        // Look for existing drag canvas
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (Canvas canvas in canvases)
        {
            if (canvas.name.Contains("Drag") || canvas.sortingOrder > 100)
            {
                return canvas;
            }
        }
        
        // Create new drag canvas if none found
        GameObject dragCanvasGO = new GameObject("DragCanvas");
        Canvas canvas2 = dragCanvasGO.AddComponent<Canvas>();
        canvas2.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas2.sortingOrder = 1000; // Render on top
        
        CanvasScaler scaler = dragCanvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        
        dragCanvasGO.AddComponent<GraphicRaycaster>();
        
        return canvas2;
    }

    void OnDestroy()
    {
        DestroyDragPreview();
        if (scaleAnimation != null)
        {
            StopCoroutine(scaleAnimation);
        }
    }
}