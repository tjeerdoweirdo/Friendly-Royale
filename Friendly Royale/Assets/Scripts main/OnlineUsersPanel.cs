using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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

    [Header("Row Prefab Loading")]
    [Tooltip("Optional Resources path to the row prefab (e.g., 'UI/UserRow'). Used if userRowPrefab is not assigned.")]
    public string userRowPrefabResourcesPath = "";

    [Header("Row Field Mapping")]
    [Tooltip("Child name of TMP_Text for username in the row prefab (exact, case-insensitive). Leave empty to auto-detect by name contains 'username'.")]
    public string usernameChildName = "";
    [Tooltip("Child name of TMP_Text for trophies in the row prefab (exact, case-insensitive). Leave empty to auto-detect by name contains 'trophy'.")]
    public string trophyChildName = "";
    [Tooltip("Optional: Child name of TMP_Text for last seen in the row prefab (exact, case-insensitive). Leave empty to auto-detect by name contains 'last' or 'seen'.")]
    public string lastSeenChildName = "";

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

    [Header("History")]
    [Tooltip("Include previously seen players even if offline")] public bool includeOfflineHistory = true;
    [Tooltip("Max historical users to keep on disk")] public int maxHistoryEntries = 2000;
    [Tooltip("Always include the current player in the list/history")] public bool includeCurrentPlayer = true;

    [Serializable]
    private class UserSeen
    {
        public string id;
        public string username;
        public int trophies;
        public long lastSeenUnix; // UTC seconds
    }

    [Serializable]
    private class HistoryData
    {
        public List<UserSeen> users = new List<UserSeen>();
    }

    private readonly Dictionary<string, UserSeen> _history = new Dictionary<string, UserSeen>();
    private string HistoryFilePath => Path.Combine(Application.persistentDataPath, "online_users_history.json");

    // internal state
    private List<Lobby> _lastResults = null;
    private Coroutine _loop;
    private Coroutine _slideCoroutine;
    private bool _isPanelVisible = false;
    private GameObject _cachedRowPrefab = null;

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

        // Load local history
        LoadHistory();

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

        // Persist history
        SaveHistory();
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

        // Always proceed; even with no lobbies we'll still show history/current user
        if (results == null)
        {
            results = new List<Lobby>();
        }

        _lastResults = results;
        // Collect unique currently-online players across lobbies
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

                // Update local history last-seen for this player
                if (!_history.TryGetValue(p.Id, out var seen))
                {
                    seen = new UserSeen { id = p.Id, username = username, trophies = Mathf.Max(0, trophies), lastSeenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
                    _history[p.Id] = seen;
                }
                else
                {
                    seen.username = string.IsNullOrWhiteSpace(username) ? seen.username : username;
                    seen.trophies = Mathf.Max(seen.trophies, Mathf.Max(0, trophies));
                    seen.lastSeenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }
            }
        }

        // Build combined list: online players plus historical (if enabled)
        // Ensure the local player's entry (if online) uses authoritative local trophies/username
        if (includeCurrentPlayer)
        {
            var meInfoForOnline = ResolveCurrentUser();
            if (!string.IsNullOrEmpty(meInfoForOnline.id) && uniquePlayers.ContainsKey(meInfoForOnline.id))
            {
                uniquePlayers[meInfoForOnline.id] = (meInfoForOnline.username, meInfoForOnline.trophies);
            }
        }

    var combined = new List<(string id, string username, int trophies, long lastSeen, bool online)>();
    var addedIds = new HashSet<string>();

        // Add all online entries
        foreach (var kv in uniquePlayers)
        {
            var hs = _history.ContainsKey(kv.Key) ? _history[kv.Key] : null;
            long lastSeen = hs != null ? hs.lastSeenUnix : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!string.IsNullOrEmpty(kv.Key) && addedIds.Add(kv.Key))
            {
                combined.Add((kv.Key, kv.Value.username, kv.Value.trophies, lastSeen, true));
            }
        }

        // Ensure current player is present in history and list
        if (includeCurrentPlayer)
        {
            var meInfo = ResolveCurrentUser();
            string myId = meInfo.id;
            if (!string.IsNullOrEmpty(myId))
            {
                // Update history
                var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (!_history.TryGetValue(myId, out var me))
                {
                    me = new UserSeen { id = myId, username = meInfo.username, trophies = meInfo.trophies, lastSeenUnix = nowUnix };
                    _history[myId] = me;
                }
                else
                {
                    me.username = string.IsNullOrWhiteSpace(meInfo.username) ? me.username : meInfo.username;
                    me.trophies = Mathf.Max(me.trophies, meInfo.trophies);
                    me.lastSeenUnix = nowUnix;
                }

                // Add to combined list if not already from online set
                if (!uniquePlayers.ContainsKey(myId) && addedIds.Add(myId))
                {
                    combined.Add((myId, me.username, me.trophies, me.lastSeenUnix, false));
                }
            }
        }

        if (includeOfflineHistory)
        {
            foreach (var kv in _history)
            {
                if (uniquePlayers.ContainsKey(kv.Key)) continue; // already added as online
                if (string.IsNullOrEmpty(kv.Key)) continue;
                if (!addedIds.Add(kv.Key)) continue; // avoid duplicates (e.g., current user already added)
                combined.Add((kv.Key, kv.Value.username, kv.Value.trophies, kv.Value.lastSeenUnix, false));
            }
        }

        // If nothing at all, show empty state
        if (combined.Count == 0)
        {
            if (emptyText != null)
            {
                emptyText.text = "No players found";
                emptyText.gameObject.SetActive(true);
            }
            yield break;
        }

        // Sort: online first; online by trophies desc; offline by lastSeen desc
        combined.Sort((a, b) =>
        {
            if (a.online != b.online) return b.online.CompareTo(a.online);
            if (a.online && b.online) return b.trophies.CompareTo(a.trophies);
            return b.lastSeen.CompareTo(a.lastSeen);
        });

        // Ensure we have a content parent; try to auto-find if not assigned
        EnsureContentParent();

        foreach (var entry in combined)
        {
            if (contentParent == null)
            {
                Debug.LogWarning("[OnlineUsersPanel] contentParent not assigned; cannot render rows");
                break;
            }
            GameObject row;
            var prefab = ResolveRowPrefab();
            if (prefab != null)
            {
                row = Instantiate(prefab, contentParent);
            }
            else
            {
                // Fallback: create a basic text row
                row = new GameObject("UserRow");
                row.transform.SetParent(contentParent, false);
                var text = row.AddComponent<TextMeshProUGUI>();
                text.fontSize = 24f;
                text.text = FormatFallbackRow(entry.username, entry.trophies, entry.online, entry.lastSeen);
                continue;
            }

            // Prefer OnlineUserRow binding to populate; fallback to explicit mapping
            var binding = row.GetComponent<OnlineUserRow>();
            string myId = ResolveCurrentUser().id;
            bool isMe = !string.IsNullOrEmpty(myId) && entry.id == myId;
            if (binding != null)
            {
                binding.SetRow(entry.id, entry.username, entry.trophies, entry.online, entry.lastSeen, isMe);
                continue;
            }

            // Fallback mapping by configured names/heuristics
            TMP_Text usernameText = ResolveRowText(row.transform, usernameChildName, new string[]{"username"});
            TMP_Text trophyText = ResolveRowText(row.transform, trophyChildName, new string[]{"trophy","trophies"});
            TMP_Text lastSeenText = ResolveRowText(row.transform, lastSeenChildName, new string[]{"last","seen"});
            if (usernameText != null) usernameText.text = isMe ? ($"{entry.username} (You)") : entry.username;
            if (trophyText != null) trophyText.text = entry.trophies.ToString();
            if (lastSeenText != null) lastSeenText.text = entry.online ? "Online now" : FormatLastSeen(entry.lastSeen);
        }

        // Persist updated history
        TrimAndSaveHistory();
    }

    // Try to locate a reasonable content parent automatically
    private void EnsureContentParent()
    {
        if (contentParent != null) return;
        Transform root = null;
        if (panelRectTransform != null) root = panelRectTransform;
        else if (panel != null) root = panel.transform;
        if (root == null) root = this.transform;

        // Prefer ScrollRect.content if present
        var scroll = root.GetComponentInChildren<ScrollRect>(true);
        if (scroll != null && scroll.content != null)
        {
            contentParent = scroll.content;
            return;
        }
        // Next, any object with a VerticalLayoutGroup/HorizontalLayoutGroup
        var v = root.GetComponentInChildren<VerticalLayoutGroup>(true);
        if (v != null)
        {
            contentParent = v.transform;
            return;
        }
        var h = root.GetComponentInChildren<HorizontalLayoutGroup>(true);
        if (h != null)
        {
            contentParent = h.transform;
            return;
        }
        // Lastly, a child named "Content"
        var trs = root.GetComponentsInChildren<Transform>(true);
        foreach (var t in trs)
        {
            if (string.Equals(t.name, "Content", StringComparison.OrdinalIgnoreCase))
            {
                contentParent = t;
                return;
            }
        }
    }

    private string FormatFallbackRow(string username, int trophies, bool online, long lastSeen)
    {
        string seen = online ? "Online now" : FormatLastSeen(lastSeen);
        return $"{username}  -  {trophies}🏆  ({seen})";
    }

    private string FormatLastSeen(long lastSeenUnix)
    {
        try
        {
            var last = DateTimeOffset.FromUnixTimeSeconds(lastSeenUnix).UtcDateTime;
            var now = DateTime.UtcNow;
            var span = now - last;
            if (span.TotalSeconds < 30) return "just now";
            if (span.TotalMinutes < 60) return $"{Mathf.RoundToInt((float)span.TotalMinutes)}m ago";
            if (span.TotalHours < 24) return $"{Mathf.RoundToInt((float)span.TotalHours)}h ago";
            if (span.TotalDays < 7) return $"{Mathf.RoundToInt((float)span.TotalDays)}d ago";
            return last.ToString("yyyy-MM-dd");
        }
        catch { return "unknown"; }
    }

    private void LoadHistory()
    {
        _history.Clear();
        try
        {
            if (!File.Exists(HistoryFilePath)) return;
            var json = File.ReadAllText(HistoryFilePath);
            if (string.IsNullOrWhiteSpace(json)) return;
            var data = JsonUtility.FromJson<HistoryData>(json);
            if (data?.users == null) return;
            foreach (var u in data.users)
            {
                if (u == null || string.IsNullOrEmpty(u.id)) continue;
                _history[u.id] = u;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[OnlineUsersPanel] Failed to load history: {e.Message}");
        }
    }

    private void TrimAndSaveHistory()
    {
        try
        {
            // Trim if exceeding limit (keep most recent)
            if (_history.Count > maxHistoryEntries)
            {
                var list = new List<UserSeen>(_history.Values);
                list.Sort((a, b) => b.lastSeenUnix.CompareTo(a.lastSeenUnix));
                _history.Clear();
                int keep = Mathf.Min(maxHistoryEntries, list.Count);
                for (int i = 0; i < keep; i++)
                {
                    var u = list[i];
                    _history[u.id] = u;
                }
            }
            SaveHistory();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[OnlineUsersPanel] Failed to save history: {e.Message}");
        }
    }

    private void SaveHistory()
    {
        try
        {
            var data = new HistoryData { users = new List<UserSeen>(_history.Values) };
            var json = JsonUtility.ToJson(data);
            File.WriteAllText(HistoryFilePath, json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[OnlineUsersPanel] Failed to persist history: {e.Message}");
        }
    }

    // --- Row prefab resolution ---
    private GameObject ResolveRowPrefab()
    {
        if (userRowPrefab != null) return userRowPrefab;
        if (_cachedRowPrefab != null) return _cachedRowPrefab;
        // Try explicit path first
        if (!string.IsNullOrWhiteSpace(userRowPrefabResourcesPath))
        {
            try
            {
                var loaded = Resources.Load<GameObject>(userRowPrefabResourcesPath);
                if (loaded != null)
                {
                    _cachedRowPrefab = loaded;
                    return _cachedRowPrefab;
                }
            }
            catch { /* ignore */ }
        }
        // Try a few sensible defaults under Resources
        string[] guesses = new[] { "UI/UserRow", "Prefabs/UserRow", "UserRow" };
        foreach (var path in guesses)
        {
            try
            {
                var loaded = Resources.Load<GameObject>(path);
                if (loaded != null)
                {
                    _cachedRowPrefab = loaded;
                    return _cachedRowPrefab;
                }
            }
            catch { /* ignore */ }
        }
        return null;
    }

    // Resolve current user identity even if Authentication is unavailable
    private (string id, string username, int trophies) ResolveCurrentUser()
    {
        string id = null;
        try { id = AuthenticationService.Instance.PlayerId; }
        catch { /* ignored */ }

        if (string.IsNullOrEmpty(id))
        {
            // Use a persistent local id
            id = PlayerPrefs.GetString("LocalUserId", string.Empty);
            if (string.IsNullOrEmpty(id))
            {
                id = System.Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString("LocalUserId", id);
                PlayerPrefs.Save();
            }
        }

        // Prefer PlayerProgress (authoritative) for username/trophies
        string myUsername = null;
        int myTrophies = 0;
        try
        {
            var pp = PlayerProgress.Instance;
            if (pp != null)
            {
                myUsername = pp.GetUsername();
                myTrophies = pp.GetTrophies();
            }
        }
        catch { /* ignore */ }

        // Username from prefs fallbacks
        if (string.IsNullOrWhiteSpace(myUsername))
        {
            myUsername = PlayerPrefs.GetString("Username", string.Empty);
            if (string.IsNullOrWhiteSpace(myUsername))
            {
                myUsername = PlayerPrefs.GetString("LocalPlayerUsername", string.Empty);
            }
        }
        if (string.IsNullOrWhiteSpace(myUsername))
        {
            // derive short tag from id
            string shortId = id.Length > 6 ? id.Substring(0, 6) : id;
            myUsername = $"Player_{shortId}";
        }
        if (myTrophies <= 0)
        {
            myTrophies = PlayerPrefs.GetInt("Trophies", 0);
        }
        return (id, myUsername, Mathf.Max(0, myTrophies));
    }

    // --- Row field resolution helpers ---
    private TMP_Text ResolveRowText(Transform root, string preferredName, string[] keywordsLower)
    {
        TMP_Text t = null;
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            t = FindTextByExactName(root, preferredName);
            if (t != null) return t;
        }
        // fallback heuristic: first TMP_Text whose name contains any keyword
        var texts = root.GetComponentsInChildren<TMP_Text>(true);
        foreach (var txt in texts)
        {
            string n = txt.name.ToLower();
            for (int i = 0; i < keywordsLower.Length; i++)
            {
                if (n.Contains(keywordsLower[i])) return txt;
            }
        }
        return null;
    }

    private TMP_Text FindTextByExactName(Transform root, string name)
    {
        if (root == null || string.IsNullOrWhiteSpace(name)) return null;
        string target = name.Trim();
        var q = new Queue<Transform>();
        q.Enqueue(root);
        while (q.Count > 0)
        {
            var t = q.Dequeue();
            if (string.Equals(t.name, target, StringComparison.OrdinalIgnoreCase))
            {
                var text = t.GetComponent<TMP_Text>();
                if (text != null) return text;
            }
            for (int i = 0; i < t.childCount; i++) q.Enqueue(t.GetChild(i));
        }
        return null;
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
