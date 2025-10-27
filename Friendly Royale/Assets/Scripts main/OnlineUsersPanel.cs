using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

/// <summary>
/// Displays a scrolling list of currently online players (from public lobbies) with their trophy counts.
/// Attach to a Main Menu panel and wire up the references.
/// </summary>
public class OnlineUsersPanel : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Optional: The panel to show/hide")] public GameObject panel;
    [Tooltip("Parent transform to place rows under (e.g., a Vertical Layout Group content)")] public Transform contentParent;
    [Tooltip("Prefab with two TMP_Texts: usernameText and trophyText")]
    public GameObject userRowPrefab;
    [Tooltip("Shown when there are no online users to display")] public TMP_Text emptyText;

    [Header("Sliding Panel Animation")]
    [Tooltip("CanvasGroup controlling visibility/interactivity of the panel")] public CanvasGroup panelCanvasGroup;
    [Tooltip("RectTransform of the panel being animated")] public RectTransform panelRectTransform;
    [Tooltip("Seconds to animate the slide")] public float slideDuration = 0.5f;
    [Tooltip("Anchored position when hidden (off-screen)")] public Vector2 hiddenPosition = new Vector2(-600, 0);
    [Tooltip("Anchored position when shown (on-screen)")] public Vector2 shownPosition = new Vector2(0, 0);

    [Header("Refresh")]
    public float refreshIntervalSeconds = 10f;
    public int queryLobbyCount = 25;

    [Header("Input")]
    [Tooltip("Enable hotkey to toggle the panel")] public bool enableKeyToggle = true;
    [Tooltip("Key used to open/close the panel")] public KeyCode toggleKey = KeyCode.O;

    // internal state
    private List<Lobby> _lastResults = null;
    private Coroutine _loop;
    private Coroutine _slideCoroutine;
    private bool _isPanelVisible = false;

    private async void OnEnable()
    {
        // Ensure initial hidden state, matching Settingspanel behavior
        HidePanelImmediate();
        if (panelRectTransform != null)
        {
            panelRectTransform.anchoredPosition = hiddenPosition;
        }

        // Ensure Unity Services are ready
        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[OnlineUsersPanel] Unity Services initialization failed: {e.Message}");
        }

        if (_loop == null)
        {
            _loop = StartCoroutine(RefreshLoop());
        }
    }

    private void OnDisable()
    {
        if (_loop != null)
        {
            StopCoroutine(_loop);
            _loop = null;
        }
        if (_slideCoroutine != null)
        {
            StopCoroutine(_slideCoroutine);
            _slideCoroutine = null;
        }
    }

    private void Update()
    {
        if (!enableKeyToggle) return;
        if (toggleKey == KeyCode.None) return;
        if (IsTypingInInput()) return; // avoid toggling while typing in fields

        if (Input.GetKeyDown(toggleKey))
        {
            TogglePanelSlide();
        }
    }

    private IEnumerator RefreshLoop()
    {
        while (true)
        {
            yield return RefreshNow();
            yield return new WaitForSeconds(Mathf.Max(2f, refreshIntervalSeconds));
        }
    }

    public IEnumerator RefreshNow()
    {
        // Clear previous entries
        if (contentParent != null)
        {
            for (int i = contentParent.childCount - 1; i >= 0; i--)
            {
                Destroy(contentParent.GetChild(i).gameObject);
            }
        }

        if (emptyText != null) emptyText.gameObject.SetActive(false);

        // Query public lobbies and aggregate players
        QueryLobbiesOptions opts = new QueryLobbiesOptions
        {
            Count = Mathf.Clamp(queryLobbyCount, 5, 100),
            Filters = new List<QueryFilter>
            {
                new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GE)
            }
        };

        var task = LobbyService.Instance.QueryLobbiesAsync(opts);
        yield return new WaitUntil(() => task.IsCompleted);

        List<Lobby> results = null;
        if (task.Exception != null || task.Result == null)
        {
            Debug.LogWarning($"[OnlineUsersPanel] Query failed (phase 1): {task.Exception?.GetBaseException().Message}");
        }
        else
        {
            results = task.Result.Results;
            Debug.Log($"[OnlineUsersPanel] Query (phase 1) returned {results?.Count ?? 0} lobbies");
        }

        // Fallback: try a broader query with no filters if nothing found
        if (results == null || results.Count == 0)
        {
            var fallbackTask = LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
            {
                Count = Mathf.Clamp(queryLobbyCount * 2, 10, 100)
            });
            yield return new WaitUntil(() => fallbackTask.IsCompleted);
            if (fallbackTask.Exception != null || fallbackTask.Result == null)
            {
                Debug.LogWarning($"[OnlineUsersPanel] Query failed (fallback): {fallbackTask.Exception?.GetBaseException().Message}");
            }
            else
            {
                results = fallbackTask.Result.Results;
                Debug.Log($"[OnlineUsersPanel] Query (fallback) returned {results?.Count ?? 0} lobbies");
            }
        }

        if (results == null || results.Count == 0)
        {
            if (emptyText != null)
            {
                emptyText.text = "No online players found";
                emptyText.gameObject.SetActive(true);
            }
            yield break;
        }

        _lastResults = results;
        // Collect unique players across lobbies
        var uniquePlayers = new Dictionary<string, (string username, int trophies)>();
        foreach (var lobby in results)
        {
            if (lobby?.Players == null) continue;
            foreach (var p in lobby.Players)
            {
                if (string.IsNullOrEmpty(p?.Id)) continue;
                string username = p.Data != null && p.Data.ContainsKey("username") ? p.Data["username"].Value : ("Player_" + p.Id.Substring(0, Mathf.Min(6, p.Id.Length)));
                int trophies = 0;
                if (p.Data != null && p.Data.ContainsKey("trophies"))
                {
                    int.TryParse(p.Data["trophies"].Value, out trophies);
                }
                // Last write wins; fine for this UI
                uniquePlayers[p.Id] = (username, Mathf.Max(0, trophies));
            }
        }

        // Populate UI
        if (uniquePlayers.Count == 0)
        {
            if (emptyText != null)
            {
                emptyText.text = "No online players found";
                emptyText.gameObject.SetActive(true);
            }
            yield break;
        }

        // Sort by trophies desc
        var list = new List<(string id, string username, int trophies)>();
        foreach (var kv in uniquePlayers)
        {
            list.Add((kv.Key, kv.Value.username, kv.Value.trophies));
        }
        list.Sort((a, b) => b.trophies.CompareTo(a.trophies));

        foreach (var entry in list)
        {
            if (contentParent == null)
            {
                Debug.LogWarning("[OnlineUsersPanel] contentParent not assigned; cannot render rows");
                break;
            }
            GameObject row;
            if (userRowPrefab != null)
            {
                row = Instantiate(userRowPrefab, contentParent);
            }
            else
            {
                // Fallback: create a basic text row
                row = new GameObject("UserRow");
                row.transform.SetParent(contentParent, false);
                var text = row.AddComponent<TextMeshProUGUI>();
                text.fontSize = 24f;
                text.text = $"{entry.username}  -  {entry.trophies}🏆";
                continue;
            }

            // Try to find username and trophy text fields on the prefab
            TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>(true);
            TMP_Text usernameText = null;
            TMP_Text trophyText = null;
            foreach (var t in texts)
            {
                if (t.name.ToLower().Contains("username")) usernameText = t;
                else if (t.name.ToLower().Contains("trophy") || t.name.ToLower().Contains("trophies")) trophyText = t;
            }
            if (usernameText != null) usernameText.text = entry.username;
            if (trophyText != null) trophyText.text = entry.trophies.ToString();
        }
    }

    // --- Sliding API (match Settingspanel behavior) ---
    public void TogglePanelSlide()
    {
        if (_slideCoroutine != null)
            StopCoroutine(_slideCoroutine);
        if (_isPanelVisible)
            _slideCoroutine = StartCoroutine(SlidePanel(hiddenPosition, false));
        else
            _slideCoroutine = StartCoroutine(SlidePanel(shownPosition, true));
    }

    public void ShowPanel()
    {
        if (_isPanelVisible) return;
        if (_slideCoroutine != null) StopCoroutine(_slideCoroutine);
        _slideCoroutine = StartCoroutine(SlidePanel(shownPosition, true));
    }

    public void HidePanel()
    {
        if (!_isPanelVisible) return;
        if (_slideCoroutine != null) StopCoroutine(_slideCoroutine);
        _slideCoroutine = StartCoroutine(SlidePanel(hiddenPosition, false));
    }

    public void HidePanelImmediate()
    {
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = 0f;
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
        }
        if (panelRectTransform != null)
        {
            panelRectTransform.anchoredPosition = hiddenPosition;
        }
        _isPanelVisible = false;
    }

    private IEnumerator SlidePanel(Vector2 targetPosition, bool show)
    {
        if (panelRectTransform == null || panelCanvasGroup == null)
            yield break;

        float elapsed = 0f;
        Vector2 startPos = panelRectTransform.anchoredPosition;

        if (show)
        {
            panelCanvasGroup.alpha = 1f;
            panelCanvasGroup.interactable = true;
            panelCanvasGroup.blocksRaycasts = true;
        }

        while (elapsed < slideDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            panelRectTransform.anchoredPosition = Vector2.Lerp(startPos, targetPosition, Mathf.Clamp01(elapsed / slideDuration));
            yield return null;
        }

        panelRectTransform.anchoredPosition = targetPosition;
        _isPanelVisible = show;

        if (!show)
        {
            panelCanvasGroup.alpha = 0f;
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
        }

        _slideCoroutine = null;
    }

    // --- Helpers ---
    private bool IsTypingInInput()
    {
        var go = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (go == null) return false;
        // If any TMP_InputField or legacy InputField is focused, consider user typing
        return go.GetComponent<TMP_InputField>() != null || go.GetComponent<InputField>() != null;
    }
}
