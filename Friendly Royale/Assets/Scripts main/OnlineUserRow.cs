using System;
using UnityEngine;
using TMPro;

/// <summary>
/// Attach this to your user row prefab to explicitly map TMP_Text fields.
/// OnlineUsersPanel will prefer these references over name-based lookups.
/// </summary>
public class OnlineUserRow : MonoBehaviour
{
    [Header("Row Text Fields")]
    public TMP_Text usernameText;
    public TMP_Text trophyText;
    public TMP_Text lastSeenText;
    [Tooltip("Optional rank text field (e.g., '#1')")] public TMP_Text rankText;

    [Header("Colors")]
    [Tooltip("Text color when the player is currently online")] public Color onlineColor = Color.green;
    [Tooltip("Text color when the player is offline")] public Color offlineColor = Color.red;
    [Tooltip("Rank color for #1")] public Color rank1Color = new Color(1f, 0.84f, 0f); // gold
    [Tooltip("Rank color for #2")] public Color rank2Color = new Color(0.75f, 0.75f, 0.75f); // silver
    [Tooltip("Rank color for #3")] public Color rank3Color = new Color(0.8f, 0.5f, 0.2f); // bronze
    [Tooltip("Default rank color")] public Color rankDefaultColor = Color.white;

    [Header("Auto-Bind by Child Names")]
    [Tooltip("If true, this component will auto-find child TMP_Texts by exact name or keywords when fields are not assigned")]
    public bool autoBindByNames = true;
    [Tooltip("Exact child name for username TMP_Text (case-insensitive). Leave empty to use keywords.")]
    public string usernameChildName = "";
    [Tooltip("Exact child name for trophies TMP_Text (case-insensitive). Leave empty to use keywords.")]
    public string trophyChildName = "";
    [Tooltip("Exact child name for last-seen TMP_Text (case-insensitive). Leave empty to use keywords.")]
    public string lastSeenChildName = "";

    [Tooltip("Keyword hints used to locate the username text if exact name not provided")]
    public string[] usernameKeywords = new[] { "username", "name" };
    [Tooltip("Keyword hints used to locate the trophy text if exact name not provided")]
    public string[] trophyKeywords = new[] { "trophy", "trophies", "cup", "cups" };
    [Tooltip("Keyword hints used to locate the last-seen text if exact name not provided")]
    public string[] lastSeenKeywords = new[] { "last", "seen", "online" };

    [Header("Formatting")]
    [Tooltip("Append '(You)' to the username when this row is the local player")] public bool showYouSuffix = true;
    [Tooltip("Show 'Online now' vs friendly 'Xm ago' time for offline")] public bool showLastSeen = true;

    // cached state
    private int _rank = 0;

    /// <summary>
    /// Populate this row's UI fields.
    /// </summary>
    public void SetRow(string id, string username, int trophies, bool online, long lastSeenUnix, bool isMe)
    {
        // Ensure bindings are connected if requested
        if (autoBindByNames)
        {
            TryAutoBind();
        }

        if (usernameText != null)
        {
            usernameText.text = showYouSuffix && isMe ? ($"{username} (You)") : username;
            // Color by online state
            usernameText.color = online ? onlineColor : offlineColor;
        }
        if (trophyText != null)
        {
            trophyText.text = Mathf.Max(0, trophies).ToString();
        }
        if (lastSeenText != null)
        {
            if (showLastSeen)
            {
                lastSeenText.text = online ? "Online now" : FormatLastSeen(lastSeenUnix);
                lastSeenText.gameObject.SetActive(true);
            }
            else
            {
                // Hide if not showing
                lastSeenText.gameObject.SetActive(false);
            }
        }

        // Apply rank if available
        ApplyRankVisuals();
    }

    private void Awake()
    {
        if (autoBindByNames)
        {
            TryAutoBind();
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying && autoBindByNames)
        {
            TryAutoBind();
        }
    }
#endif

    private void TryAutoBind()
    {
        // Only attempt if any is null
        if (usernameText == null)
            usernameText = FindText(transform, usernameChildName, usernameKeywords);
        if (trophyText == null)
            trophyText = FindText(transform, trophyChildName, trophyKeywords);
        if (lastSeenText == null)
            lastSeenText = FindText(transform, lastSeenChildName, lastSeenKeywords);
        if (rankText == null)
            rankText = FindText(transform, "rank", new[] { "rank", "#" });
    }

    private TMP_Text FindText(Transform root, string exactName, string[] keywords)
    {
        if (root == null) return null;

        // 1) Try exact match if provided
        if (!string.IsNullOrWhiteSpace(exactName))
        {
            var found = FindTextByExactName(root, exactName);
            if (found != null) return found;
        }

        // 2) Heuristic search by keywords
        var texts = root.GetComponentsInChildren<TMP_Text>(true);
        if (texts != null)
        {
            foreach (var t in texts)
            {
                string n = t.name.ToLowerInvariant();
                if (keywords != null)
                {
                    for (int i = 0; i < keywords.Length; i++)
                    {
                        var kw = keywords[i];
                        if (string.IsNullOrWhiteSpace(kw)) continue;
                        if (n.Contains(kw.ToLowerInvariant()))
                            return t;
                    }
                }
            }
        }
        return null;
    }

    private TMP_Text FindTextByExactName(Transform root, string name)
    {
        if (root == null || string.IsNullOrWhiteSpace(name)) return null;
        string target = name.Trim();
        var q = new System.Collections.Generic.Queue<Transform>();
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

    /// <summary>
    /// Set the leaderboard rank. Call before or after SetRow; visuals update accordingly.
    /// </summary>
    public void SetRank(int rank)
    {
        _rank = Mathf.Max(0, rank);
        ApplyRankVisuals();
    }

    private void ApplyRankVisuals()
    {
        if (rankText != null)
        {
            if (_rank > 0)
            {
                rankText.gameObject.SetActive(true);
                rankText.text = "#" + _rank;
                // Color-code top 3
                if (_rank == 1) rankText.color = rank1Color;
                else if (_rank == 2) rankText.color = rank2Color;
                else if (_rank == 3) rankText.color = rank3Color;
                else rankText.color = rankDefaultColor;
            }
            else
            {
                // Hide if rank not provided
                rankText.gameObject.SetActive(false);
            }
        }
        else if (usernameText != null && _rank > 0)
        {
            // Prefix rank if we don't have a dedicated rank text
            if (!usernameText.text.StartsWith("#"))
                usernameText.text = "#" + _rank + " " + usernameText.text;
        }
    }
}
